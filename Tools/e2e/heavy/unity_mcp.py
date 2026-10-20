"""Вызовы Unity через штатный MCP-прокси своего worktree (Tools/agents/unity_mcp_proxy.py, stdio).

Прокси сам направляет вызовы linked worktree в worker своей RUNNING аренды и отказывает без неё;
heavy об аренде не знает. Повторяются только наблюдения (PlayLaunch.Status, PlayModeTestStand.Operation):
их продвижение принадлежит EditorApplication.update, повтор ничего не меняет. Мутация отправляется один раз;
сбой при ней — неизвестный исход, без повтора.
"""
import asyncio
from datetime import timedelta
import json
import time

LAUNCH = "VrBattlegrounds.EditorTools.TestStand.PlayLaunch"
STAND = "VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand"
SESSION_READ_SECONDS = 180


class UnityCallError(RuntimeError):
    """Вызов Unity не дал ответа; effect_unknown — мутация могла выполниться."""

    def __init__(self, message, effect_unknown=False):
        super().__init__(message)
        self.effect_unknown = effect_unknown


def literal(value):
    """C#-литерал строки с JSON значения."""
    return json.dumps(json.dumps(value, separators=(",", ":"), ensure_ascii=False), ensure_ascii=False)


def unpack(reply):
    if getattr(reply, "isError", False):
        raise RuntimeError("MCP отказал: " + " ".join(c.text for c in reply.content if hasattr(c, "text"))[:1200])
    texts = [c.text for c in reply.content if hasattr(c, "text")]
    if not texts:
        raise RuntimeError("Нет ответа MCP")
    value = json.loads(texts[0])
    for _ in range(10):
        if isinstance(value, str):
            try:
                value = json.loads(value)
                continue
            except json.JSONDecodeError:
                return value
        if not isinstance(value, dict):
            return value
        if value.get("success") is False:
            raise RuntimeError(str(value.get("error") or value.get("message") or "success=false без текста")[:1200])
        if "result" in value:
            value = value["result"]
        elif "data" in value:
            value = value["data"]
        else:
            return value
    raise RuntimeError("Неизвестная обёртка MCP")


def unpack_envelope(reply):
    """Ответ инструмента MCP for Unity без спуска в поля данных: снимает только конверты
    {"result": {...}} и {"success", "data"}. У job get_test_job своё поле result (итог тестов) — его не трогаем."""
    if getattr(reply, "isError", False):
        raise RuntimeError("MCP отказал: " + " ".join(c.text for c in reply.content if hasattr(c, "text"))[:1200])
    texts = [c.text for c in reply.content if hasattr(c, "text")]
    if not texts:
        raise RuntimeError("Нет ответа MCP")
    value = json.loads(texts[0])
    for _ in range(5):
        if isinstance(value, dict) and set(value) == {"result"}:
            value = value["result"]
        elif isinstance(value, dict) and "success" in value:
            if value.get("success") is False:
                raise RuntimeError(str(value.get("error") or value.get("message") or "success=false без текста")[:1200])
            value = value.get("data")
        else:
            return value
    return value


def is_observation(expression):
    return expression == LAUNCH + ".Status()" or (
        expression.startswith(STAND + ".Operation(") and expression.endswith(")"))


class UnityMcp:
    def __init__(self, project_root, trace, call_timeout=45, observation_budget=90):
        self.project_root = project_root
        self.trace = trace
        self.call_timeout = call_timeout
        self.observation_budget = observation_budget
        self._stack = None
        self._session = None

    async def __aenter__(self):
        from contextlib import AsyncExitStack
        # MCP SDK нужен только живому прогону (heavy.py ставит его через uv); чистые функции импортируются без него.
        from mcp import ClientSession, StdioServerParameters
        from mcp.client.stdio import stdio_client
        self._stack = AsyncExitStack()
        server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"],
                                       cwd=str(self.project_root))
        reader, writer = await self._stack.enter_async_context(stdio_client(server))
        self._session = await self._stack.enter_async_context(
            # Срок чтения сессии — по самому долгому вызову (get_test_job во время EditMode), сроки вызовов — свои.
            ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=SESSION_READ_SECONDS)))
        await asyncio.wait_for(self._session.initialize(), self.call_timeout)
        return self

    async def __aexit__(self, *exc):
        await self._stack.aclose()

    async def code(self, expression):
        """execute_code: повторяется только наблюдение Status/Operation."""
        return await self.tool("execute_code", {"action": "execute", "code": "return " + expression + ";"},
                               observation=is_observation(expression), label=expression, unpacker=unpack)

    async def tool(self, name, arguments, observation=False, label=None, unpacker=unpack_envelope,
                   call_timeout=None, budget=None):
        """Любой инструмент MCP; observation=True — чтение, которое можно повторить в пределах budget."""
        item = {"tool": name, "expression": (label or json.dumps(arguments, ensure_ascii=False))[:400],
                "observation": observation, "started": time.time(), "attempts": 0}
        deadline = time.monotonic() + (budget or self.observation_budget)
        try:
            while True:
                item["attempts"] += 1
                try:
                    reply = await asyncio.wait_for(self._session.call_tool(name, arguments),
                                                   call_timeout or self.call_timeout)
                    value = unpacker(reply)
                    item["ok"] = True
                    return value
                except Exception as error:
                    item["lastError"] = type(error).__name__ + ": " + str(error)[:600]
                    if not observation or time.monotonic() + 2 > deadline:
                        raise UnityCallError(item["lastError"], effect_unknown=not observation) from None
                    await asyncio.sleep(2)
        finally:
            item["seconds"] = round(time.time() - item["started"], 2)
            self.trace(item)

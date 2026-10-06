# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.20,<2"]
# ///
"""MCP-обёртка Unity для агентов: каждый вызов инструмента сначала решает брокер редактора.

Запуск: uv run --quiet Tools/agents/unity_mcp_proxy.py (stdio). Агентом считается worktree,
в котором лежит этот скрипт. Решение — `editor-broker.py mcp-gate`, см. editor_broker/mcp_gate.py.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
from pathlib import Path
import subprocess
import sys

import anyio
import mcp.types as types
from mcp.client.session import ClientSession
from mcp.client.streamable_http import streamablehttp_client
from mcp.server.lowlevel import Server
from mcp.server.lowlevel.helper_types import ReadResourceContents
from mcp.server.stdio import stdio_server

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from editor_broker.mcp_gate import classify  # noqa: E402  только stdlib, без побочных эффектов
from editor_broker.service import default_state_dir  # noqa: E402


class Upstream:
    """Соединение с общим сервером MCP живёт в отдельной задаче и переподключается после сбоя."""

    def __init__(self, url):
        self.url = url
        self.session = None
        self.error = "ещё не подключено"
        self._broken = anyio.Event()

    async def run(self):
        while True:
            try:
                async with streamablehttp_client(self.url) as (read, write, _):
                    async with ClientSession(read, write) as session:
                        await session.initialize()
                        self.session, self.error = session, ""
                        await self._broken.wait()
            except Exception as error:  # noqa: BLE001 — сервер Unity MCP может быть не запущен
                self.error = str(error) or type(error).__name__
            self.session = None
            self._broken = anyio.Event()
            await anyio.sleep(1)

    async def get(self, timeout=20):
        with anyio.move_on_after(timeout):
            while self.session is None:
                await anyio.sleep(0.1)
        if self.session is None:
            raise RuntimeError(f"Сервер Unity MCP недоступен ({self.url}): {self.error}")
        return self.session

    def reset(self):
        self._broken.set()


def _error(text):
    return types.CallToolResult(content=[types.TextContent(type="text", text=text)], isError=True)


class Gate:
    def __init__(self, agent_root):
        self.agent_root = agent_root
        self.enabled = (default_state_dir(agent_root) / "config.json").is_file()

    def decide(self, tool, arguments, pinned):
        if not self.enabled:
            return {"allow": True, "unity_instance": None}
        payload = json.dumps({"tool": tool, "arguments": arguments, "pinned": pinned},
                             ensure_ascii=False).encode("utf-8")
        command = [sys.executable, str(HERE / "editor-broker.py"), "--repo", str(self.agent_root),
                   "mcp-gate", "--agent-root", str(self.agent_root)]
        try:
            done = subprocess.run(command, input=payload, capture_output=True, timeout=60)
            if done.returncode == 2 and b"mcp-gate" in done.stderr and not done.stdout.strip():
                # Закреплённая runtime-копия брокера старше mcp-gate: работаем как прямой MCP,
                # проверка включится после обновления runtime без перезапуска агентов.
                print("unity_mcp_proxy: runtime брокера без mcp-gate, вызовы без проверки",
                      file=sys.stderr)
                return {"allow": True, "unity_instance": None}
            reply = json.loads(done.stdout.decode("utf-8"))
        except (OSError, subprocess.TimeoutExpired, ValueError) as error:
            reply = {"ok": False, "error": "брокер не ответил: " + str(error)}
        if reply.get("ok"):
            return reply["result"]
        reason = "Брокер отказал: " + str(reply.get("error", "неизвестная ошибка"))
        # Недоступный брокер не мешает смотреть консоль и сцену, но изменение не пропускает.
        if classify(tool, arguments) == "read":
            return {"allow": True, "unity_instance": None}
        return {"allow": False, "reason": reason}


def build(upstream, gate):
    server = Server("unityMCP", instructions=(
        "Unity MCP через брокер редактора: чтение доступно всегда; изменение Unity — только в своей "
        "аренде (checkpoint → request → claim → begin), вызов тогда автоматически идёт в worker."))
    state = {"pinned": None}

    @server.list_tools()
    async def list_tools():
        session = await upstream.get()
        tools, cursor = [], None
        while True:
            page = await session.list_tools(cursor=cursor)
            tools.extend(page.tools)
            cursor = page.nextCursor
            if not cursor:
                return tools

    @server.call_tool(validate_input=False)
    async def call_tool(name, arguments):
        arguments = dict(arguments or {})
        decision = await anyio.to_thread.run_sync(gate.decide, name, arguments, state["pinned"])
        if not decision.get("allow"):
            return _error(decision.get("reason", "Брокер отказал"))
        if decision.get("unity_instance"):
            arguments["unity_instance"] = decision["unity_instance"]
        session = await upstream.get()
        try:
            result = await session.call_tool(name, arguments)
        except Exception as error:  # noqa: BLE001
            upstream.reset()
            # Изменяющий вызов не повторяем: неизвестно, успел ли Unity его выполнить.
            return _error(f"Вызов {name} прерван ({error}); соединение будет восстановлено. "
                          "Проверьте состояние Unity перед повтором.")
        if name == "set_active_instance" and not result.isError:
            state["pinned"] = arguments.get("instance")
        return result

    @server.list_resources()
    async def list_resources():
        return (await (await upstream.get()).list_resources()).resources

    @server.list_resource_templates()
    async def list_resource_templates():
        return (await (await upstream.get()).list_resource_templates()).resourceTemplates

    @server.read_resource()
    async def read_resource(uri):
        result = await (await upstream.get()).read_resource(uri)
        # SDK сам кодирует bytes в base64: blob раскодируем, чтобы не закодировать дважды.
        return [ReadResourceContents(content=item.text if isinstance(item, types.TextResourceContents)
                                     else base64.b64decode(item.blob), mime_type=item.mimeType)
                for item in result.contents]

    return server


async def main_async(args):
    agent_root = Path(args.repo).resolve()
    upstream = Upstream(args.url)
    server = build(upstream, Gate(agent_root))
    async with anyio.create_task_group() as group:
        group.start_soon(upstream.run)
        async with stdio_server() as (read, write):
            await server.run(read, write, server.create_initialization_options())
        group.cancel_scope.cancel()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=os.environ.get("UNITY_MCP_URL", "http://127.0.0.1:8080/mcp"))
    parser.add_argument("--repo", default=str(HERE.parents[1]), help="Worktree агента")
    anyio.run(main_async, parser.parse_args())


if __name__ == "__main__":
    main()

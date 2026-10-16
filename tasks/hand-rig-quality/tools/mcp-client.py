"""Временный клиент задачи к штатному Unity MCP-прокси; общую инфраструктуру не меняет."""
from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[3]
REPORTS = ROOT / "tasks/hand-rig-quality/reports/resume-2026-10-14"


def find_value(value, key):
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = find_value(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_value(child, key)
            if found is not None:
                return found
    return None


def decoded(value):
    """Текстовые MCP-блоки с JSON тоже входят в поиск результата; raw сохраняется отдельно."""
    if isinstance(value, dict):
        result = {k: decoded(v) for k, v in value.items()}
        if value.get("type") == "text" and isinstance(value.get("text"), str):
            try:
                result["decoded"] = decoded(json.loads(value["text"]))
            except ValueError:
                pass
        return result
    if isinstance(value, list):
        return [decoded(v) for v in value]
    return value


async def run(args):
    if args.tool:
        if not args.ticket or not args.token:
            raise ValueError("Для Unity-инструмента обязательны ticket и token своей RUNNING аренды")
        guard = subprocess.run(
            [sys.executable, "-B", "-X", "utf8", "Tools/agents/editor-broker.py", "guard",
             "--ticket", str(args.ticket), "--token", args.token],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=False,
        )
        verdict = json.loads(guard.stdout)
        if guard.returncode or not verdict.get("ok"):
            raise RuntimeError("Broker guard отклонил вызов: " + guard.stdout[-1800:])
        if args.apply:
            zone = subprocess.run(
                [sys.executable, "-B", "-X", "utf8", "Tools/agents/coordination.py", "zone-check",
                 "--task", "hand-rig-quality", "--stage", "grip-save-validation"],
                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=False,
            )
            scope = json.loads(zone.stdout)
            requirements = scope.get("result", {}).get("stages", [{}])[0].get("requirements", [])
            approved = {r.get("owner") for r in requirements if r.get("satisfied", {}).get("via") == "ack"}
            if zone.returncode or not scope.get("ok") or not {"avatar-ik", "weapon-system"}.issubset(approved):
                raise RuntimeError("Apply запрещён: свежие ACK обеих зон не подтверждены")

    params = json.loads(Path(args.params_file).read_text(encoding="utf-8")) if args.params_file else {}
    if args.code_file:
        if args.tool != "execute_code":
            raise ValueError("code-file разрешён только для execute_code")
        code = Path(args.code_file).read_text(encoding="utf-8")
        if args.apply:
            marker = "const bool applyAcceptedPilot = false;"
            if code.count(marker) != 1:
                raise ValueError("Apply marker не единственный")
            code = code.replace(marker, "const bool applyAcceptedPilot = true;")
        params.update(action="execute", code=code)
    if args.job_id:
        if args.tool != "get_test_job":
            raise ValueError("job-id разрешён только для get_test_job")
        params.update(job_id=args.job_id, wait_timeout=45, include_details=True, include_failed_tests=True)

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    report_path = REPORTS / f"mcp-{args.tool or 'discovery'}-{stamp}.json"
    server = StdioServerParameters(
        command="C:/Users/asker/.local/bin/uv.exe",
        args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"],
        cwd=str(ROOT),
    )
    # Не повторять tools/call после неизвестного исхода: отчёт запроса сохраняется до отправки.
    request_path = REPORTS / f"mcp-{args.tool or 'discovery'}-{stamp}-request.json"
    request_path.write_text(json.dumps({"ticket": args.ticket, "tool": args.tool,
                                       "arguments": params}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    async with stdio_client(server) as streams:
        async with ClientSession(*streams) as session:
            init = await session.initialize()
            if args.tool:
                # Выбор только в контексте этого клиента и только по собственной guard binding.
                selected = await session.call_tool("set_active_instance", arguments={"instance": verdict["result"]["unity_instance"]})
                selection = decoded(selected.model_dump(mode="json", by_alias=True, exclude_none=True))
                if selected.isError or find_value(selection, "success") is False or find_value(selection, "error"):
                    raise RuntimeError("Не удалось выбрать worker instance в своём MCP-клиенте")
            state = await session.read_resource("mcpforunity://editor/state") if args.tool else None
            if args.tool:
                response = await session.call_tool(args.tool, arguments=params)
            else:
                response = await session.list_tools()
            raw = response.model_dump(mode="json", by_alias=True, exclude_none=True)
            report_path.write_text(json.dumps({"initialize": init.model_dump(mode="json"),
                                               "editor_state": state.model_dump(mode="json") if state else None,
                                               "response": raw}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            parsed = decoded(raw)
            summary = {"report": str(report_path), "request": str(request_path),
                       "tool": args.tool, "isError": raw.get("isError", False)}
            if not args.tool:
                summary["tools"] = [t.name for t in response.tools]
            else:
                for key in ("job_id", "status", "summary", "passed", "numericChecksPassed", "scope", "applyExecuted", "handCount", "currentJobId", "normalizerCompiled", "error"):
                    value = find_value(parsed, key)
                    if value is not None:
                        summary[key] = value
                summary["failures"] = (find_value(parsed, "failures") or [])[:10]
                summary["result_message"] = str(find_value(parsed, "message") or "")[:1400]
            print(json.dumps(summary, ensure_ascii=False), flush=True)
            return bool(summary.get("error") or summary.get("isError") or summary.get("passed") is False or summary.get("numericChecksPassed") is False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tool", choices=("execute_code", "run_tests", "get_test_job", "read_console"))
    parser.add_argument("--params-file")
    parser.add_argument("--code-file")
    parser.add_argument("--job-id")
    parser.add_argument("--apply", action="store_true", help="Только после свежих zone-ack обеих зон")
    parser.add_argument("--ticket", type=int)
    parser.add_argument("--token")
    args = parser.parse_args()
    if args.apply and not args.code_file:
        parser.error("--apply требует --code-file")
    raise SystemExit(1 if asyncio.run(run(args)) else 0)


if __name__ == "__main__":
    main()

"""Один вызов через штатный прокси worktree; мутации только в собственной RUNNING-аренде."""
import argparse
import asyncio
from datetime import timedelta
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[3]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tool", default="execute_code")
    parser.add_argument("--code-file", type=Path)
    parser.add_argument("--params-file", type=Path)
    parser.add_argument("--instance", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args()
    if bool(args.code_file) == bool(args.params_file):
        parser.error("нужен ровно один из --code-file / --params-file")
    if args.code_file and args.tool != "execute_code":
        parser.error("--code-file предназначен только для execute_code")

    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    sys.path.insert(0, str(ROOT / "Tools" / "TestStand"))
    from worker_probe import unpack

    params = ({"action": "execute", "code": args.code_file.read_text(encoding="utf-8")}
              if args.code_file else json.loads(args.params_file.read_text(encoding="utf-8")))
    report = {"tool": args.tool, "started": time.time(), "worktree": str(ROOT),
              "instance": args.instance, "toolSucceeded": False, "callStarted": False}
    args.output.parent.mkdir(parents=True, exist_ok=True)

    def save():
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    async def run():
        server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(ROOT))
        async with stdio_client(server) as (reader, writer):
            async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=args.timeout)) as session:
                await session.initialize()
                unpack(await session.call_tool("set_active_instance", {"instance": args.instance}))
                report["callStarted"] = True
                save()
                # Единственная отправка: после таймаута сначала inspect durable/native state.
                reply = await session.call_tool(args.tool, params)
                report["response"] = reply.model_dump(mode="json")
                save()
                report["result"] = unpack(reply)
                report["toolSucceeded"] = True

    save()
    try:
        asyncio.run(asyncio.wait_for(run(), timeout=args.timeout + 30))
    except Exception as error:
        report["error"] = type(error).__name__ + ": " + str(error)
        # Ответ с ошибкой/timeout не доказывает, что Unity не успела выполнить действие.
        # Консервативно требуем inspect и при транспортном, и при серверном отказе после отправки.
        report["effectUnknown"] = report["callStarted"]
    finally:
        report["finished"] = time.time()
        save()
    print(json.dumps({"toolSucceeded": report["toolSucceeded"], "error": report.get("error"),
                      "effectUnknown": report.get("effectUnknown", False), "report": str(args.output)}, ensure_ascii=False))
    return 0 if report["toolSucceeded"] else 1


if __name__ == "__main__":
    raise SystemExit(main())

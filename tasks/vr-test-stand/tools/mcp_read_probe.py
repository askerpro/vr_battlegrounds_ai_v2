"""Проверка MCP initialize и чтения состояния worker без Unity-мутаций."""
import asyncio
from datetime import timedelta
import json
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "tasks/vr-test-stand/reports/mcp-read-probe.json"


async def probe(report):
    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(ROOT))
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=30)) as session:
            report["initialize"] = (await session.initialize()).model_dump(mode="json")
            report["tools"] = [t.name for t in (await session.list_tools()).tools]
            resources = (await session.list_resources()).resources
            report["resources"] = [r.model_dump(mode="json") for r in resources]
            report["reads"] = {}
            for resource in resources:
                uri = str(resource.uri)
                if "instances" in uri or "editor/state" in uri:
                    report["reads"][uri] = (await session.read_resource(uri)).model_dump(mode="json")
            report["passed"] = bool(report["reads"])


async def main():
    report = {"passed": False, "worktree": str(ROOT)}
    try:
        await asyncio.wait_for(probe(report), timeout=60)
    except Exception as error:
        def describe(exception):
            children = getattr(exception, "exceptions", None)
            if children:
                return "; ".join(describe(child) for child in children)
            return type(exception).__name__ + ": " + str(exception)
        report["error"] = describe(error)
    finally:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "toolCount": len(report.get("tools", [])),
                      "reads": report.get("reads", {}), "error": report.get("error"), "report": str(OUTPUT)}, ensure_ascii=False))
    return report["passed"]


if __name__ == "__main__":
    raise SystemExit(0 if asyncio.run(main()) else 1)

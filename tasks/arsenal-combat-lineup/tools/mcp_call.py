# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp"]
# ///
"""Вызов инструмента Unity MCP через штатный прокси своего worktree (как Tools/e2e/heavy).

Нужен, когда MCP-сервер сессии агента запущен не из worktree задачи: прокси сам
направляет вызов в worker своей RUNNING аренды и отказывает без неё.

uv run tasks/arsenal-combat-lineup/tools/mcp_call.py <tool> <args.json> [--out <файл>] [--timeout 120]
Картинки из ответа (base64 PNG) сохраняются рядом с --out: <out>.<n>.png.
"""
import argparse
import asyncio
import base64
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


async def main(args):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    arguments = json.loads(Path(args.args).read_text(encoding="utf-8"))
    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(ROOT))
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer, read_timeout_seconds=float(args.timeout + 30)) as session:
            await asyncio.wait_for(session.initialize(), 60)
            reply = await asyncio.wait_for(session.call_tool(args.tool, arguments), args.timeout)
    texts, images = [], 0
    out = Path(args.out) if args.out else None
    for item in reply.content:
        if getattr(item, "type", "") == "image" and out:
            images += 1
            out.with_suffix(f".{images}.png").write_bytes(base64.b64decode(item.data))
        elif hasattr(item, "text"):
            texts.append(item.text)
    text = "\n".join(texts)
    if out:
        out.write_text(text, encoding="utf-8")
    print(("ERROR " if getattr(reply, "isError", False) else "") + text[: args.limit])
    if images:
        print(f"images: {images}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("tool")
    parser.add_argument("args")
    parser.add_argument("--out")
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--limit", type=int, default=6000)
    sys.stdout.reconfigure(encoding="utf-8")
    asyncio.run(main(parser.parse_args()))

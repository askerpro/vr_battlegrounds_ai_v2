"""Вызов Unity MCP через штатный прокси этого worktree (тот же клиент, что у heavy).

Прокси направляет вызовы linked worktree в worker своей RUNNING аренды и отказывает без неё.
Запуск: uv run --quiet --no-project --with "mcp>=1.20,<2" python -X utf8 tasks/lobby-decor-builders/tools/unity_call.py
        --code-file <тело C# метода с return>  |  --tool <имя> --args-file <json>
Полный ответ пишется в --out (JSON), в stdout — первые 4000 символов.
"""
import argparse
import asyncio
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "e2e"))
from heavy.unity_mcp import UnityMcp, unpack, unpack_envelope  # noqa: E402


async def main(args):
    trace = []
    async with UnityMcp(ROOT, trace.append, call_timeout=args.timeout) as unity:
        if args.code_file:
            code = Path(args.code_file).read_text(encoding="utf-8")
            value = await unity.tool("execute_code", {"action": "execute", "code": code,
                                                      "timeout_seconds": max(30, min(300, args.timeout))},
                                     label=args.code_file, unpacker=unpack, call_timeout=args.timeout + 15)
        else:
            arguments = json.loads(Path(args.args_file).read_text(encoding="utf-8")) if args.args_file else {}
            value = await unity.tool(args.tool, arguments, unpacker=unpack_envelope, call_timeout=args.timeout)
    text = json.dumps({"value": value, "trace": trace}, ensure_ascii=False, indent=1, default=str)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
    print(text[:4000])


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--code-file")
    parser.add_argument("--tool")
    parser.add_argument("--args-file")
    parser.add_argument("--out")
    parser.add_argument("--timeout", type=int, default=120)
    parsed = parser.parse_args()
    if not parsed.code_file and not parsed.tool:
        parser.error("нужен --code-file или --tool")
    asyncio.run(main(parsed))

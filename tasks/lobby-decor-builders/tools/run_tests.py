"""Узкий прогон EditMode через штатный MCP-прокси этого worktree (в worker своей аренды), по образцу heavy.

Запуск: uv run --quiet --no-project --with "mcp>=1.20,<2" python -X utf8 tasks/lobby-decor-builders/tools/run_tests.py
        --group <regex> [--group ...] --out tasks/lobby-decor-builders/reports/<файл>.json
В stdout — summary и не прошедшие тесты (state, первая строка message); полный ответ — в --out.
"""
import argparse
import asyncio
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "e2e"))
from heavy.unity_mcp import UnityMcp  # noqa: E402


async def main(args):
    trace = []
    async with UnityMcp(ROOT, trace.append) as unity:
        started = await unity.tool("run_tests", {"mode": "EditMode", "include_failed_tests": True,
                                                 "group_names": args.group, "init_timeout": 120000})
        job_id = (started or {}).get("job_id")
        if not job_id:
            raise SystemExit("run_tests без job_id: " + json.dumps(started, ensure_ascii=False)[:400])
        deadline = time.monotonic() + args.budget
        job = None
        while time.monotonic() < deadline:
            job = await unity.tool("get_test_job", {"job_id": job_id, "include_failed_tests": True, "wait_timeout": 10},
                                   observation=True, call_timeout=150, budget=600)
            if (job or {}).get("status") not in (None, "running"):
                break
            await asyncio.sleep(1)
    Path(args.out).write_text(json.dumps({"job": job, "trace": trace}, ensure_ascii=False, indent=1, default=str), encoding="utf-8")
    result = (job or {}).get("result") or {}
    print("status:", (job or {}).get("status"), "summary:", json.dumps(result.get("summary"), ensure_ascii=False))
    for row in result.get("results") or []:
        message = (str(row.get("message") or "").strip().splitlines() or [""])[0]
        print("-", row.get("state"), row.get("fullName") or row.get("name"), "—", message[:300])


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--group", action="append", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--budget", type=int, default=1200)
    asyncio.run(main(parser.parse_args()))

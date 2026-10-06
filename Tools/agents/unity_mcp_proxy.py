#!/usr/bin/env python3
"""Заглушка MCP-прокси Unity: запускает unity_mcp_proxy.py из runtime брокера для этого worktree.

Подключается в .mcp.json / .codex/config.toml. Прокси на каждый вызов спрашивает брокер
(см. docs/unity-mcp-proxy.md в F:/UnityProjects/agent-infra); stdio передаётся процессу как есть.
"""
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from broker_runtime import require  # noqa: E402

if __name__ == "__main__":
    try:
        script = require() / "unity_mcp_proxy.py"
    except RuntimeError as error:
        print("unity_mcp_proxy: " + str(error), file=sys.stderr)
        raise SystemExit(1)
    worktree = subprocess.check_output(["git", "-C", str(HERE), "rev-parse", "--show-toplevel"], text=True).strip()
    raise SystemExit(subprocess.call(["uv", "run", "--quiet", str(script), "--repo", worktree, *sys.argv[1:]]))

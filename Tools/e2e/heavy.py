"""Heavy-регрессия перед вливанием: python Tools/e2e/heavy.py --profile editor --suite default.

Профилю editor нужен MCP SDK; без него скрипт перезапускает себя через uv (как пробы стенда).
Код возврата: 0 — можно вливать, 1 — найдена регрессия, 2 — вердикт не получен.
"""
import os
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def needs_mcp(argv):
    profile = "editor"
    for index, value in enumerate(argv):
        if value == "--profile" and index + 1 < len(argv):
            profile = argv[index + 1]
        elif value.startswith("--profile="):
            profile = value.split("=", 1)[1]
    return profile == "editor" and not any(a in ("-h", "--help") for a in argv)


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")
    argv = sys.argv[1:]
    if needs_mcp(argv):
        try:
            import mcp  # noqa: F401
        except ImportError:
            if os.environ.get("VRBG_HEAVY_UV"):
                print("heavy: пакет mcp недоступен даже под uv", file=sys.stderr)
                raise SystemExit(2)
            environment = dict(os.environ, VRBG_HEAVY_UV="1", PYTHONIOENCODING="utf-8")
            command = ["uv", "run", "--quiet", "--no-project", "--with", "mcp>=1.20,<2",
                       "python", "-X", "utf8", str(Path(__file__).resolve()), *argv]
            try:
                raise SystemExit(subprocess.run(command, env=environment).returncode)
            except OSError as error:
                print("heavy: не запустился uv для MCP SDK: " + str(error), file=sys.stderr)
                raise SystemExit(2)
    sys.path.insert(0, str(HERE))
    from heavy.cli import main
    raise SystemExit(main(argv))

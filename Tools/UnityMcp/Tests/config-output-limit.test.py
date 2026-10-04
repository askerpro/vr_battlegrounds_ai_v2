"""Проверка TOML и области действия резервного лимита, без запуска агента."""
from pathlib import Path
import tomllib

root = Path(__file__).resolve().parents[3]
with (root / ".codex/config.toml").open("rb") as stream:
    config = tomllib.load(stream)
assert config["tool_output_token_limit"] == 4000
assert config["mcp_servers"]["blender"]["command"] == "uvx"
assert config["mcp_servers"]["blender"]["args"] == ["blender-mcp"]
assert "tool_output_token_limit" not in config["mcp_servers"]["blender"]
print("PASS project TOML: root tool_output_token_limit=4000; Blender config preserved")

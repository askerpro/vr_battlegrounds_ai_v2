"""Проверка профиля и загрузочных документов без запуска Unity или изменения проекта."""
import argparse
import json
from pathlib import Path
import tomllib

parser = argparse.ArgumentParser()
parser.add_argument("--profile", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parent
template = root / "avatar-grip-expert.config.toml"
profile = args.profile or template
data = tomllib.loads(profile.read_text(encoding="utf-8-sig"))
assert set(data) == {"developer_instructions"}, "Профиль должен менять только предметные инструкции"
assert "avatar-grip-expert" in data["developer_instructions"]
assert "AGENTS.md" in data["developer_instructions"]
for name in ("avatar-grip-expert.md", "analysis.md", "sources.md", "handoff.md", "launch.md"):
    assert (root / name).is_file(), name
if args.profile:
    assert profile.read_bytes() == template.read_bytes(), "Установленный профиль отличается от шаблона"
print(json.dumps({"status": "PASS", "scope": "TOML syntax, role/KB/handoff presence and profile content only", "profile": str(profile)}, ensure_ascii=False))

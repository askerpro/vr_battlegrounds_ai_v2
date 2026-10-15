"""Разовая LF-нормализация только файлов, изменённых при возобновлении bots-fix."""
import json
from pathlib import Path
from time import time_ns

ROOT = Path(__file__).resolve().parents[3]
BACKUP_ROOT = ROOT / "tasks/bots-fix/reports/resume-2026-10-15/eol-backup" / str(time_ns())
PATHS = (
    "experts/bots/backlog.md",
    "tasks/bots-fix/Readme.md",
    "tasks/bots-fix/Details.md",
    "tasks/bots-fix/plan.json",
    "tasks/bots-fix/changelog/2026-10-14-codex-resume.md",
    "tasks/bots-fix/changelog/2026-10-15-recovery664.md",
    "tasks/bots-fix/changelog/2026-10-15-worker666.md",
    "tasks/bots-fix/tools/cc1-round1/analyze.py",
    "tasks/bots-fix/tools/cc1-round1/b4-runtime-queue.cs",
    "tasks/bots-fix/tools/cc1-round1/b2-test-receipt.cs",
    "tasks/bots-fix/tools/cc1-round1/b7-tests-direct.cs",
    "tasks/bots-fix/tools/cc1-round1/b8-precache-snapshot.cs",
    "tasks/bots-fix/tools/cc1-round1/package.json",
    "tasks/bots-fix/tools/cc1-round1/verify-analyzer.py",
    "tasks/bots-fix/tools/normalize-resume-lf.py",
    "tasks/bots-fix/tools/cc1-round2-input.py",
    "tasks/bots-fix/tools/cc1-build-component-input.py",
)

changed = []
for relative in PATHS:
    path = ROOT / relative
    original = path.read_bytes()
    original.decode("utf-8")
    normalized = original.replace(b"\r\n", b"\n")
    if b"\r" in normalized:
        raise ValueError("Неожиданный одиночный CR: " + relative)
    if normalized == original:
        continue
    backup = BACKUP_ROOT / relative
    backup.parent.mkdir(parents=True, exist_ok=True)
    backup.write_bytes(original)
    if path.read_bytes() != original:
        raise RuntimeError("Файл изменился после чтения: " + relative)
    path.write_bytes(normalized)
    changed.append(relative)
print(json.dumps({"checked": len(PATHS), "changed": changed, "scope": "только собственные изменённые файлы"}, ensure_ascii=False))

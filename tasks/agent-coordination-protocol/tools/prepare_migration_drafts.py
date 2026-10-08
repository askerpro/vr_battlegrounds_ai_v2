"""Черновики миграции в reports: чужие worktree и ACK не изменяются."""
import json
from pathlib import Path
import subprocess

reports = Path(__file__).resolve().parent.parent / "reports"
root = reports.parents[2]
shared = root / ".agent-state/coordination"
base = json.loads((root / ".agent-state/editor-broker/config.json").read_text(encoding="utf-8"))["baseline_sha"]
roster = {
    "arsenal-generator": ("arsenal-generator", "claude"),
    "weapon-system": ("shotgun-per-shell", "claude"),
    "bots-fix": ("bots-fix", "other"),
    "map-runtime-bootstrap": ("map-runtime-bootstrap", "claude"),
    "legs-ik": (None, "claude"),
    "hand-rig-quality": ("hands-rig-quality", "codex"),
    "haptics": ("haptics", "claude"),
    "vr-test-stand": ("vr-test-stand", "codex"),
}
draft_dir = reports / "migration-drafts"
draft_dir.mkdir(exist_ok=True)
status = json.loads((reports / "maintenance-broker-status.json").read_text(encoding="utf-8"))["result"]
inventory = []
for task, (folder, client) in roster.items():
    legacy = (shared / (task + ".md")).read_text(encoding="utf-8")
    worktree = Path("F:/CodexWorktrees") / folder / "Vr_Battlegrounds_ai" if folder else None
    request_owner = task
    if worktree:
        for ticket in status.get("tickets", []):
            if str(ticket.get("agent_root", "")).replace("\\", "/").casefold() == worktree.as_posix().casefold():
                request_owner = ticket["owner"]
    doc_path = "tasks/" + task + "/Readme.md"
    spec = {
        "task_id": task, "owner": request_owner, "client": client,
        "worktree": str(worktree) if worktree else "OWNER_MUST_CHOOSE_LINKED_WORKTREE",
        "base_sha": base, "doc_path": doc_path, "title": "Миграция плана " + task,
        "stages": [{"id": "migration-review", "writes": [doc_path], "after": [], "needs": []}],
    }
    fence = chr(96) * 3
    text = "# " + task + " — черновик миграции\n\n"
    text += "Подготовлен контроллером 2026-10-08. Это НЕ подтверждение владельца, НЕ полный зарегистрированный план и НЕ архитектурный ACK.\n\n"
    text += "Владелец сверяет worktree, owner, client и актуальную базу; переносит сюда мотивацию, архитектуру, прогресс и все известные будущие этапы.\n"
    text += "Структурированный блок ниже разрешает только подготовку собственного документа. До реализации добавить полный план, области, зависимости и используемые контракты.\n"
    text += "Оставлять source-этапы без известных контрактов нельзя. Старые документы статуса заменить ссылками на этот единственный документ.\n"
    text += "После собственного review: register --expected-revision 0 (или текущая ревизия), затем явные contract-ack владельцем и затронутыми потребителями.\n"
    text += "Промежуточные данные — tasks/" + task + "/reports/.\n\n"
    text += fence + "coordination\n" + json.dumps(spec, ensure_ascii=False, indent=2) + "\n" + fence + "\n\n"
    text += "## Исходный опубликованный план для сверки\n\n" + legacy
    (draft_dir / (task + ".md")).write_text(text, encoding="utf-8")
    inventory.append({"task_id": task, "owner_candidate": request_owner, "client_candidate": client,
                      "worktree": str(worktree) if worktree else None,
                      "canonical_document_exists": bool(worktree and (worktree / doc_path).exists()),
                      "draft": str(draft_dir / (task + ".md")), "registered": False,
                      "owner_review_required": True})
(reports / "migration-inventory.json").write_text(json.dumps({"base": base, "active": inventory,
                                                               "excluded_completed": ["calibration-owner"]},
                                                              ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"drafts": len(inventory), "excluded_completed": ["calibration-owner"],
                  "missing_worktree": [x["task_id"] for x in inventory if not x["worktree"]],
                  "existing_canonical_documents": [x["task_id"] for x in inventory if x["canonical_document_exists"]],
                  "ack_created": 0}, ensure_ascii=False))

"""EditMode в heavy: полный прогон через MCP run_tests/get_test_job и поимённое сравнение красных
с Tools/e2e/editmode-known-red.json (решение пользователя 2026-10-20).

Новый красный тест, которого нет в списке, роняет вердикт. Известный красный, ставший зелёным, —
подсказка убрать его из списка. Итоговый result.results get_test_job с include_failed_tests содержит все
не прошедшие тесты (лимит 25 — только у progress.failures_so_far).

Предупреждение — Inconclusive с маркером «[Warn:<область>]» (NUnit 3.5 в UTF 1.6 без Assert.Warn): вердикт не
роняет, но попадает в warnings и заметки verdict.json, чтобы агент перечислил его в отчёте. Inconclusive без маркера
(отказ окружения «[TestEnvironment]» и т.п.) — тоже заметка: summary MCP его не считает.
"""
import asyncio
from datetime import date
import json
from pathlib import Path
import time

from . import verdict as V

KNOWN_RED = Path(__file__).resolve().parents[1] / "editmode-known-red.json"
POLL_WAIT_SECONDS = 10
CALL_SECONDS = 150
OBSERVATION_BUDGET_SECONDS = 600
MAX_LISTED = 15
WARN_MARKER = "[Warn:"


class KnownRedError(ValueError):
    """Файл известных красных не прочитан."""


def load_known_red(path=KNOWN_RED):
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise KnownRedError(Path(path).name + ": " + str(error)) from None
    if data.get("version") != 1 or not isinstance(data.get("tests"), list):
        raise KnownRedError(Path(path).name + ": нужны version 1 и список tests")
    known = {}
    for index, entry in enumerate(data["tests"]):
        name = entry.get("name") if isinstance(entry, dict) else None
        if not isinstance(name, str) or not name.strip():
            raise KnownRedError(Path(path).name + ", tests[" + str(index) + "]: пустой name")
        if name in known:
            raise KnownRedError(Path(path).name + ": тест повторяется: " + name)
        known[name] = str(entry.get("reason") or "")
    return known


def is_red(state):
    """Красный — Failed и его разновидности (Failed:Error и т.п.); Skipped/Ignored/Inconclusive не красные."""
    return str(state or "").lower().startswith("failed")


def is_inconclusive(state):
    return str(state or "").lower().startswith("inconclusive")


def compare(result, known):
    """result — result из get_test_job (summary + results не прошедших). Возвращает запись EditMode."""
    summary = (result or {}).get("summary") or {}
    results = (result or {}).get("results")
    record = {"summary": {k: summary.get(k) for k in ("total", "passed", "failed", "skipped", "durationSeconds")}}
    if not result or results is None or not summary.get("total"):
        record.update(verdict=V.INCONCLUSIVE, reasons=["результат EditMode не получен или пуст"])
        return record
    red = {r.get("fullName") or r.get("name"): r for r in results if is_red(r.get("state"))}
    not_passed = {r.get("fullName") or r.get("name") for r in results}
    new_red = sorted(n for n in red if n not in known)
    fixed = sorted(n for n in known if n not in not_passed)
    record.update(red=sorted(red), newRed=[{"name": n, "message": str(red[n].get("message") or "").strip()[:300]}
                                           for n in new_red],
                  stillRed=sorted(n for n in red if n in known), fixed=fixed)
    reasons = []
    if len(red) != (summary.get("failed") or 0):
        reasons.append("в summary failed=" + str(summary.get("failed")) + ", а красных в списке " + str(len(red)))
    for item in record["newRed"][:MAX_LISTED]:
        reasons.append("новый красный: " + item["name"] + (" — " + item["message"].splitlines()[0] if item["message"] else ""))
    if len(new_red) > MAX_LISTED:
        reasons.append("… ещё новых красных: " + str(len(new_red) - MAX_LISTED))
    notes = []
    inconclusive = [r for r in results if is_inconclusive(r.get("state"))]
    warned = [r for r in inconclusive if WARN_MARKER in str(r.get("message") or "")]
    record["warnings"] = [{"name": r.get("fullName") or r.get("name"), "message": str(r.get("message") or "").strip()[:300]}
                          for r in warned]
    for item in record["warnings"][:MAX_LISTED]:
        notes.append("предупреждение: " + item["name"] + " — " + (item["message"].splitlines() or [""])[0])
    if len(warned) > MAX_LISTED:
        notes.append("… ещё предупреждений: " + str(len(warned) - MAX_LISTED))
    other = sorted(r.get("fullName") or r.get("name") for r in inconclusive if r not in warned)
    if other:
        notes.append("Inconclusive без маркера (" + str(len(other)) + "): " + ", ".join(other[:5]) + (" …" if len(other) > 5 else ""))
    if fixed:
        notes.append("известные красные не красные в этом прогоне (" + str(len(fixed)) + ", зелёные или удалены) — "
                     "убрать из Tools/e2e/editmode-known-red.json: "
                     + ", ".join(fixed[:5]) + (" …" if len(fixed) > 5 else ""))
    record["notes"] = notes
    record["verdict"] = V.FAIL if new_red else V.INCONCLUSIVE if reasons else V.PASS
    record["reasons"] = reasons
    return record


def candidate_known_red(record, known, base):
    """Файл известных красных по этому прогону: для обновления списка на чистой origin/dev."""
    tests = [{"name": n, "reason": known.get(n) or "красный на " + str(base)[:12]} for n in record.get("red", [])]
    return {"version": 1, "updated": date.today().isoformat(), "baseSha": base, "tests": tests}


async def run(unity, log, init_timeout_ms=120000, budget_seconds=2400):
    """Полный EditMode; возвращает (result get_test_job или None, служебная запись)."""
    info = {}
    started = await unity.tool("run_tests", {"mode": "EditMode", "include_failed_tests": True,
                                             "init_timeout": init_timeout_ms})
    job_id = (started or {}).get("job_id")
    info["jobId"] = job_id
    if not job_id:
        info["error"] = "run_tests не вернул job_id: " + json.dumps(started, ensure_ascii=False)[:300]
        return None, info
    log("EditMode: job " + job_id)
    deadline = time.monotonic() + budget_seconds
    last_note = 0
    while time.monotonic() < deadline:
        # Пока идут тесты, главный поток Unity занят и ответ приходит за десятки секунд; reload EditMode-тестов
        # прокси пережидает до 120 с — отсюда длинные срок вызова и бюджет повторов чтения.
        job = await unity.tool("get_test_job", {"job_id": job_id, "include_failed_tests": True,
                                                "wait_timeout": POLL_WAIT_SECONDS}, observation=True,
                               call_timeout=CALL_SECONDS, budget=OBSERVATION_BUDGET_SECONDS)
        status = (job or {}).get("status")
        progress = (job or {}).get("progress") or {}
        if status and status != "running":
            info.update(status=status, error=job.get("error"), finishedUnixMs=job.get("finished_unix_ms"),
                        startedUnixMs=job.get("started_unix_ms"))
            return job.get("result"), info
        if time.monotonic() - last_note > 60:
            log("  EditMode: " + str(progress.get("completed")) + "/" + str(progress.get("total"))
                + (", blocked: " + str(progress.get("blocked_reason")) if progress.get("blocked_reason") else ""))
            last_note = time.monotonic()
        await asyncio.sleep(1)
    info["error"] = "EditMode не закончился за " + str(budget_seconds) + " с"
    return None, info

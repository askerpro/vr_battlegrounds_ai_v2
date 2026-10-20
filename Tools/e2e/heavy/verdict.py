"""Вердикт heavy-прогона: результаты участников, логи и сводка «можно вливать / нельзя».

Классификация logIssues повторяет Tools/e2e/E2ELogVerdict.psm1 (контракт quest-device-run r5): шум сценария
(knownNoise результата), затем шум каркаса Tools/e2e/known-noise.json с учётом scope; kind=defect роняет прогон.
Дополнительно считаются строки архивов логов участников за окно прогона.
"""
import json
from pathlib import Path
import re

KNOWN_NOISE = Path(__file__).resolve().parents[1] / "known-noise.json"
NATIVE_MARKERS = ("Crash!!!", "Fatal signal")
# Только строки-сообщения исключений («[Namespace.]XxxException: текст»): в логе Editor у каждой записи полный
# стек, и имена типов в сигнатурах кадров (System.Action`2<System.Exception, …>) не считаются.
_MESSAGE = r"^\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*{}(?::|\s*$)"
COUNTERS = {
    "uxrComponentNotFound": re.compile(_MESSAGE.format("UxrComponentNotFoundException")),
    "nullReference": re.compile(_MESSAGE.format("NullReferenceException")),
    "exception": re.compile(_MESSAGE.format(r"[A-Za-z_][A-Za-z0-9_]*Exception")),
}
# Счётчики, которые роняют прогон при строке вне известного шума.
GATED_COUNTERS = ("uxrComponentNotFound", "nullReference")
MAX_EXAMPLES = 10

PASS, FAIL, INCONCLUSIVE = "pass", "fail", "inconclusive"
EXIT = {"mergeable": 0, "blocked": 1, "inconclusive": 2}


class NoiseError(ValueError):
    """Список шума нельзя применить: битый файл, пустой id/reason, неверный regex."""


def _rule(where, entry, source, scope="any"):
    rule_id = str(entry.get("id") or "").strip()
    pattern = entry.get("regex") or ""
    reason = str(entry.get("reason") or "").strip()
    if not rule_id:
        raise NoiseError(where + ": пустой id")
    if not pattern:
        raise NoiseError("'" + rule_id + "' (" + where + "): пустой regex")
    if not reason:
        raise NoiseError("'" + rule_id + "' (" + where + "): пустой reason")
    try:
        compiled = re.compile(pattern)
    except re.error as error:
        raise NoiseError("'" + rule_id + "' (" + where + "): неверный regex: " + str(error)) from None
    kind = entry.get("kind") or "noise"
    if kind not in ("noise", "defect"):
        raise NoiseError("'" + rule_id + "' (" + where + "): kind " + kind + " — допустимо noise, defect")
    return {"id": rule_id, "regex": compiled, "scope": scope, "source": source, "kind": kind,
            "owner": entry.get("owner"), "ticket": entry.get("ticket")}


def load_known_noise(path=KNOWN_NOISE):
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise NoiseError(Path(path).name + ": " + str(error)) from None
    rules, ids = [], set()
    for index, entry in enumerate(data.get("entries") or []):
        where = Path(path).name + ", entries[" + str(index) + "]"
        scope = entry.get("scope") or "any"
        if scope not in ("any", "server", "client"):
            raise NoiseError(where + ": scope " + scope + " — допустимо any, server, client")
        rule = _rule(where, entry, "framework", scope)
        if rule["kind"] == "defect" and not (rule["owner"] and rule["ticket"]):
            raise NoiseError("'" + rule["id"] + "' (" + where + "): у известного дефекта нужны owner и ticket")
        if rule["id"] in ids:
            raise NoiseError("'" + rule["id"] + "' (" + where + "): id повторяется")
        ids.add(rule["id"])
        rules.append(rule)
    return rules


def _in_scope(scope, process):
    return scope == "any" or (scope == "server" and process == "server") or (scope == "client" and process.startswith("client"))


def _match(text, process, scenario_rules, framework):
    for rule in scenario_rules:
        if rule["regex"].search(text):
            return rule
    for rule in framework:
        if _in_scope(rule["scope"], process) and rule["regex"].search(text):
            return rule
    return None


def classify_log_issues(processes, framework):
    """processes: [(метка server|client-N, результат участника или None)]."""
    report = {"unexpected": [], "defects": [], "known": [], "dropped": 0, "notes": [], "perProcess": []}
    for label, data in processes:
        entry = {"process": label, "captured": False, "issues": 0, "unexpected": 0, "defects": 0, "known": 0, "dropped": 0}
        report["perProcess"].append(entry)
        if data is None:
            report["notes"].append(label + ": результата нет — ошибки лога не собраны")
            continue
        if "logIssues" not in data:
            report["notes"].append(label + ": в результате нет logIssues — ошибки лога не собраны")
            continue
        entry["captured"] = True
        scenario_rules = [_rule(label + " knownNoise[" + str(i) + "]", n, "scenario")
                          for i, n in enumerate(data.get("knownNoise") or [])]
        for issue in data.get("logIssues") or []:
            text = str(issue.get("message") or "") + "\n" + str(issue.get("stack") or "")
            record = {"process": label, "type": issue.get("type"), "message": issue.get("message"),
                      "count": int(issue.get("count") or 1), "firstAtSeconds": issue.get("firstAtSeconds")}
            hit = _match(text, label, scenario_rules, framework)
            entry["issues"] += 1
            if hit and hit["kind"] == "defect":
                record.update(id=hit["id"], owner=hit["owner"], ticket=hit["ticket"])
                report["defects"].append(record)
                entry["defects"] += 1
            elif hit:
                record.update(id=hit["id"], source=hit["source"])
                report["known"].append(record)
                entry["known"] += 1
            else:
                report["unexpected"].append(record)
                entry["unexpected"] += 1
        entry["dropped"] = int(data.get("logIssuesDropped") or 0)
        report["dropped"] += entry["dropped"]
    return report


def scan_log(path, process, framework):
    """Счётчики строк архива лога участника; строки известного шума (kind=noise) не роняют прогон."""
    counts = {name: 0 for name in COUNTERS}
    gated = {name: 0 for name in GATED_COUNTERS}
    examples, native = [], []
    with open(path, encoding="utf-8", errors="replace") as handle:
        for number, line in enumerate(handle, 1):
            for marker in NATIVE_MARKERS:
                if marker in line:
                    native.append({"line": number, "marker": marker, "text": line.strip()[:300]})
            hits = [name for name, pattern in COUNTERS.items() if pattern.search(line)]
            for name in hits:
                counts[name] += 1
            gated_hits = [name for name in hits if name in GATED_COUNTERS]
            if not gated_hits:
                continue
            rule = _match(line, process, [], framework)
            if rule and rule["kind"] == "noise":
                continue
            for name in gated_hits:
                gated[name] += 1
            if len(examples) < MAX_EXAMPLES:
                examples.append({"line": number, "counter": gated_hits[0], "text": line.strip()[:300],
                                 "defect": rule["id"] if rule else None})
    return {"process": process, "path": str(path), "counts": counts, "gated": gated, "examples": examples, "native": native}


def evaluate_participant(result, scenario, role_kind):
    """Итог одного участника: PASS, FAIL или INCONCLUSIVE и причины."""
    if result is None:
        return INCONCLUSIVE, ["файла результата нет"]
    reasons = []
    if result.get("scenario") != scenario:
        reasons.append("сценарий в результате " + str(result.get("scenario")))
    role = str(result.get("role") or "")
    if not (role == "server" if role_kind == "server" else role.startswith("client-")):
        reasons.append("роль в результате " + role)
    status = result.get("status")
    if status == "started":
        return INCONCLUSIVE, ["сценарий не завершён (status=started)"] + reasons
    checks = result.get("checks") or []
    failed = [c for c in checks if not c.get("passed")]
    if status != "completed":
        reasons.append("status=" + str(status) + (": " + str(result.get("summary")) if result.get("summary") else ""))
    if not checks:
        reasons.append("нет ни одной проверки")
    for check in failed[:MAX_EXAMPLES]:
        reasons.append("проверка «" + str(check.get("name")) + "»: " + str(check.get("detail") or "")[:300])
    if not result.get("passed") and not reasons:
        reasons.append("passed=false")
    return (FAIL, reasons) if reasons else (PASS, [])


def scenario_verdict(record):
    """record: launch (ok/error), participants [{role, verdict, reasons}], logs {classified, scans, archived}."""
    reasons, verdicts = [], []
    if record.get("launchError"):
        verdicts.append(INCONCLUSIVE)
        reasons.append(record["launchError"])
    for participant in record.get("participants", []):
        verdicts.append(participant["verdict"])
        reasons.extend(participant["role"] + ": " + r for r in participant["reasons"])
    logs = record.get("logs") or {}
    classified = logs.get("classified")
    if classified:
        if classified["unexpected"]:
            verdicts.append(FAIL)
            reasons.append("неожиданных ошибок лога: " + str(len(classified["unexpected"])))
        if classified["defects"]:
            verdicts.append(FAIL)
            reasons.append("известных дефектов (ещё не исправлены): " + str(len(classified["defects"])))
        if classified["dropped"]:
            verdicts.append(FAIL)
            reasons.append("ошибок сверх лимита захвата: " + str(classified["dropped"]))
    for scan in logs.get("scans", []):
        for name, count in scan["gated"].items():
            if count:
                verdicts.append(FAIL)
                reasons.append(scan["process"] + ": " + name + " " + str(count) + " строк в логе")
        if scan["native"]:
            verdicts.append(FAIL)
            reasons.append(scan["process"] + ": нативное падение в логе")
    if not record.get("launchError") and not logs.get("archived"):
        verdicts.append(INCONCLUSIVE)
        reasons.append("архивы логов участников не получены")
    if record.get("cleanupError"):
        verdicts.append(INCONCLUSIVE)
        reasons.append("уборка стенда: " + record["cleanupError"])
    if not verdicts:
        verdicts.append(INCONCLUSIVE)
        reasons.append("нет результатов участников")
    verdict = FAIL if FAIL in verdicts else INCONCLUSIVE if INCONCLUSIVE in verdicts else PASS
    return verdict, reasons


def aggregate(checks, scenarios):
    """checks: [{name, verdict, detail}]; scenarios: [{name, verdict, reasons}] → итог и код возврата."""
    verdicts = [c["verdict"] for c in checks] + [s["verdict"] for s in scenarios]
    if not scenarios:
        verdicts.append(INCONCLUSIVE)
    overall = "blocked" if FAIL in verdicts else "inconclusive" if INCONCLUSIVE in verdicts else "mergeable"
    return overall, EXIT[overall]


TITLES = {"mergeable": "МОЖНО ВЛИВАТЬ", "blocked": "НЕЛЬЗЯ ВЛИВАТЬ: найдена регрессия",
          "inconclusive": "НЕЛЬЗЯ ВЛИВАТЬ: вердикт не получен"}
MARKS = {PASS: "[ok]  ", FAIL: "[FAIL]", INCONCLUSIVE: "[??]  "}


def render_text(report):
    lines = ["HEAVY " + report["profile"] + "/" + report["suite"] + ": " + TITLES[report["verdict"]]
             + " (код " + str(report["exitCode"]) + ")"]
    for check in report["checks"]:
        lines.append("  " + MARKS[check["verdict"]] + " " + check["name"] + (": " + check["detail"] if check.get("detail") else ""))
    for scenario in report["scenarios"]:
        head = "  " + MARKS[scenario["verdict"]] + " " + scenario["name"] + " " + scenario.get("topology", "")
        if scenario.get("summary"):
            head += " — " + scenario["summary"]
        lines.append(head)
        for reason in scenario.get("reasons", [])[:MAX_EXAMPLES]:
            lines.append("           " + reason)
        for note in scenario.get("notes", []):
            lines.append("           заметка: " + note)
    lines.append("Отчёт: " + report["reportPath"])
    return "\n".join(lines)

"""Профиль windows: e2e-плеер Windows из выделенного сборочного checkout и сценарии через Tools/e2e/Run-E2E.ps1.

Ревизия — закоммиченный HEAD ветки (полный SHA). Сборочный checkout переключает и собирает только
Tools/release/Build-Game.ps1 (-Targets e2e -Ref <SHA>): один владелец его состояния. Плеер той же ревизии
(Build/e2e/build.json sourceSha) переиспользуется без пересборки. Занятый checkout (Unity или сборка на нём)
heavy не трогает: перепроверяет каждые 5 мин, не дольше часа (Docs/release.md «Агенту: сборка в выделенном checkout»).
"""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import time

from . import verdict as V
from .freshness import GAME_PREFIXES

DEFAULT_CHECKOUT = Path("F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai")
DEFAULT_ANDROID = Path("F:/CodexWorktrees/build-test-android/Vr_Battlegrounds_ai")
BUSY_POLL_SECONDS = 300
BUSY_BUDGET_SECONDS = 3600
FOREIGN_BUILD_QUIET_SECONDS = 600
BUILD_TIMEOUT_SECONDS = 90 * 60
RUN_GRACE_SECONDS = 240  # Сверх -Timeout сценария: старт сервера (90 с), клиенты, сбор результатов.
POWERSHELL = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass"]


def norm(path):
    return str(path).replace("\\", "/").rstrip("/").casefold()


def uncommitted_game_changes(git):
    """Незакоммиченные изменения игровых путей: сборка берёт коммит, их в плеере не будет."""
    status = git("status", "--porcelain", "--untracked-files=all", "--", *[p.rstrip("/") for p in GAME_PREFIXES])
    return [line[3:] for line in status.stdout.splitlines() if line.strip()]


def read_build(checkout):
    """Build/e2e/build.json сборочного checkout: sourceSha, успех профиля e2e, appVersion."""
    path = Path(checkout) / "Build" / "e2e" / "build.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None
    records = [p for p in data.get("profiles") or [] if p.get("profile") == "e2e"]
    record = records[0] if len(records) == 1 else {}
    return {"sourceSha": data.get("sourceSha"), "success": bool(record.get("success")) and
            (record.get("terminal") or {}).get("result") == "Succeeded",
            "appVersion": (record.get("actual") or {}).get("appVersion"),
            "ended": (record.get("terminal") or {}).get("endedUtc"), "path": str(path)}


def busy_reasons(checkout, processes, own_pid=None):
    """Чем занят сборочный checkout: процессы Unity/сборки на нём и UnityLockfile."""
    reasons = []
    target = norm(checkout)
    for process in processes:
        line = norm(process.get("CommandLine") or "")
        if process.get("ProcessId") == own_pid or target not in line:
            continue
        name = str(process.get("Name") or "")
        if (name.lower() in ("unity.exe", "vrbattlegrounds.exe", "vrbattlegroundsserver.exe")
                or "build-game.ps1" in line or "run-queste2e.ps1" in line or "run-e2e.ps1" in line):
            reasons.append(name + " pid " + str(process.get("ProcessId")))
    lock = Path(checkout) / "Temp" / "UnityLockfile"
    if lock.exists():
        try:
            with open(lock, "a"):
                pass
        except OSError:
            reasons.append("Temp/UnityLockfile открыт на запись")
    return reasons


def list_processes():
    command = ("$ErrorActionPreference='Stop'; @(Get-CimInstance Win32_Process -Filter \"Name='Unity.exe' or "
               "Name='powershell.exe' or Name='pwsh.exe' or Name='VrBattlegrounds.exe' or Name='VrBattlegroundsServer.exe'\" "
               "| Select-Object ProcessId,Name,@{n='CommandLine';e={[string]$_.CommandLine + ' ' + [string]$_.ExecutablePath}}) "
               "| ConvertTo-Json -Compress")
    result = subprocess.run(POWERSHELL + ["-Command", command], capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=60)
    if result.returncode or not result.stdout.strip():
        return []
    data = json.loads(result.stdout)
    return data if isinstance(data, list) else [data]


def fresh_foreign_build(checkout, sha, now=None):
    """Чужая сборка закончилась только что: владелец, вероятно, сейчас запустит её плеер — не перезаписываем."""
    current = read_build(checkout)
    if not current or current["sourceSha"] == sha or not current.get("ended"):
        return None
    try:
        ended = datetime.fromisoformat(str(current["ended"]).replace("Z", "+00:00"))
    except ValueError:
        return None
    age = ((now or datetime.now(timezone.utc)) - ended).total_seconds()
    if age < FOREIGN_BUILD_QUIET_SECONDS:
        return "свежая чужая сборка " + str(current["sourceSha"])[:12] + " (" + str(round(age / 60)) + " мин назад)"
    return None


def wait_free(checkout, sha, log):
    deadline = time.monotonic() + BUSY_BUDGET_SECONDS
    while True:
        reasons = busy_reasons(checkout, list_processes(), os.getpid())
        fresh = fresh_foreign_build(checkout, sha)
        if fresh:
            reasons.append(fresh)
        if not reasons:
            return None
        if time.monotonic() + BUSY_POLL_SECONDS > deadline:
            return "сборочный checkout занят больше часа: " + ", ".join(reasons)
        log("  Сборочный checkout занят (" + ", ".join(reasons) + "), перепроверка через 5 мин")
        time.sleep(BUSY_POLL_SECONDS)


def build(project_root, checkout, android, sha, outdir, log):
    """Build-Game -Targets e2e -Ref <sha>: (verdict, detail)."""
    script = Path(project_root) / "Tools" / "release" / "Build-Game.ps1"
    command = POWERSHELL + ["-File", str(script), "-Targets", "e2e", "-Config", "Test", "-Win64Worktree", str(checkout),
                            "-AndroidWorktree", str(android), "-Ref", sha]
    log("  Сборка e2e-плеера: Build-Game -Targets e2e -Ref " + sha[:12])
    started = time.monotonic()
    with open(outdir / "build-game.log", "w", encoding="utf-8") as handle:
        try:
            code = subprocess.run(command, stdout=handle, stderr=subprocess.STDOUT, timeout=BUILD_TIMEOUT_SECONDS).returncode
        except subprocess.TimeoutExpired:
            return V.INCONCLUSIVE, "Build-Game не закончился за " + str(BUILD_TIMEOUT_SECONDS // 60) + " мин"
    seconds = round(time.monotonic() - started)
    if code == 1:
        return V.FAIL, "сборка e2e-плеера упала (Build-Game код 1, " + str(seconds) + " с) — build-game.log"
    if code != 0:
        return V.INCONCLUSIVE, "Build-Game не запустил сборку (код " + str(code) + ") — build-game.log"
    return V.PASS, "собран за " + str(seconds) + " с"


def ensure_player(project_root, checkout, android, sha, outdir, log, extra):
    """Плеер ревизии sha в checkout: переиспользовать или собрать. Возвращает проверку."""
    name = "e2e-плеер Windows собран на HEAD ветки"
    current = read_build(checkout)
    extra["playerBefore"] = current
    if current and current["sourceSha"] == sha and current["success"]:
        return {"name": name, "verdict": V.PASS, "detail": "уже собран на " + sha[:12] + " — без пересборки"}
    busy = wait_free(checkout, sha, log)
    if busy:
        return {"name": name, "verdict": V.INCONCLUSIVE, "detail": busy}
    verdict, detail = build(project_root, checkout, android, sha, outdir, log)
    after = read_build(checkout)
    extra["playerAfter"] = after
    if verdict == V.PASS and not (after and after["sourceSha"] == sha and after["success"]):
        return {"name": name, "verdict": V.INCONCLUSIVE, "detail": "build.json не подтверждает сборку " + sha[:12]}
    return {"name": name, "verdict": verdict, "detail": detail}


def run_e2e_command(project_root, scenario, player, results_dir):
    command = POWERSHELL + ["-File", str(Path(project_root) / "Tools" / "e2e" / "Run-E2E.ps1"),
                            "-Scenario", scenario.name, "-Map", scenario.scene_name, "-Clients", str(scenario.clients),
                            "-Timeout", str(scenario.scenario_timeout), "-Player", str(player), "-SkipStaleCheck",
                            "-ResultsDir", str(results_dir)]
    if scenario.late_client_delay:
        command += ["-LateClientDelay", str(scenario.late_client_delay)]
    if scenario.args:
        command += ["-Arg", ",".join(k + "=" + v for k, v in scenario.args)]
    return command


def revision_reasons(results, sha12):
    """appVersion каждого процесса — 0.1.0+<sha12>: прогон шёл на собранной ревизии."""
    reasons = []
    for role, data in results:
        version = str((data or {}).get("appVersion") or "")
        if data is not None and not version.endswith("+" + sha12):
            reasons.append(role + ": appVersion " + (version or "нет") + " не ревизия " + sha12)
    return reasons


def run_scenario(project_root, scenario, player, sha12, outdir, framework, log):
    results_dir = outdir / scenario.name
    record = {"name": scenario.name, "topology": scenario.topology, "participants": [], "logs": {}}
    command = run_e2e_command(project_root, scenario, player, results_dir)
    record["command"] = " ".join(command)
    log("  " + scenario.name + " " + scenario.topology + ": Run-E2E")
    started = time.monotonic()
    with open(outdir / (scenario.name + ".run-e2e.log"), "w", encoding="utf-8") as handle:
        try:
            code = subprocess.run(command, stdout=handle, stderr=subprocess.STDOUT,
                                  timeout=scenario.scenario_timeout + RUN_GRACE_SECONDS).returncode
        except subprocess.TimeoutExpired:
            code = None
    record["runE2eExitCode"] = code
    record["scenarioSeconds"] = round(time.monotonic() - started, 1)
    if code is None:
        record["launchError"] = "Run-E2E не закончился за " + str(scenario.scenario_timeout + RUN_GRACE_SECONDS) + " с"
    results = []
    for index in range(scenario.clients + 1):
        role = "server" if index == 0 else "client-" + str(index)
        path = results_dir / (role + ".json")
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig")) if path.is_file() else None
        except ValueError:
            data = None
        results.append((role, data))
        verdict, reasons = V.evaluate_participant(data, scenario.name, "server" if index == 0 else "client")
        checks = (data or {}).get("checks") or []
        record["participants"].append({"participant": role, "role": role, "verdict": verdict, "reasons": reasons,
                                       "result": str(path), "checksPassed": sum(1 for c in checks if c.get("passed")),
                                       "checksTotal": len(checks)})
    wrong = revision_reasons(results, sha12)
    if wrong and not record.get("launchError"):
        record["launchError"] = "плеер не той ревизии: " + "; ".join(wrong)
    scans = [V.scan_log(p, p.stem if p.stem.startswith("client") else "server", framework)
             for p in sorted(results_dir.glob("*.log"))] if results_dir.is_dir() else []
    record["logs"] = {"archived": len(scans), "scans": scans,
                      "classified": V.classify_log_issues(results, framework)}
    if code == 2 and not record.get("launchError") and all(d is None for _, d in results):
        record["launchError"] = "Run-E2E: прогон не запустился (код 2) — " + scenario.name + ".run-e2e.log"
    return record


def run(project_root, git, scenarios, outdir, log, framework, checkout=None, android=None):
    """Возвращает (checks, scenario_records, extra) как editor_profile.run."""
    checkout = Path(checkout or DEFAULT_CHECKOUT)
    android = Path(android or DEFAULT_ANDROID)
    checks, records, extra = [], [], {"buildCheckout": str(checkout)}
    sha = git("rev-parse", "HEAD").stdout.strip()
    pending = uncommitted_game_changes(git)
    name = "Игровые изменения закоммичены (сборка берёт коммит)"
    if pending:
        checks.append({"name": name, "verdict": V.INCONCLUSIVE, "detail": str(len(pending)) + " путей, например "
                       + ", ".join(pending[:5]) + " — закоммить и повтори"})
        return checks, records, extra
    checks.append({"name": name, "verdict": V.PASS, "detail": "HEAD " + sha[:12]})
    if not checkout.is_dir():
        checks.append({"name": "Сборочный checkout найден", "verdict": V.INCONCLUSIVE, "detail": str(checkout)})
        return checks, records, extra
    check = ensure_player(project_root, checkout, android, sha, outdir, log, extra)
    checks.append(check)
    if check["verdict"] != V.PASS:
        return checks, records, extra
    player = checkout / "Build" / "e2e" / "VrBattlegrounds.exe"
    for scenario in scenarios:
        log("Сценарий " + scenario.name + " (" + scenario.topology + ")")
        records.append(run_scenario(project_root, scenario, player, sha[:12], outdir, framework, log))
    return checks, records, extra

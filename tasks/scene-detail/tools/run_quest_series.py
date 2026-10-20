"""Последовательные прогоны штатного Run-QuestE2E по готовым RunSpec bundle (prepare-quest-run).

Перед каждым прогоном: переподключение adb по Wi-Fi (после сна сессия бывает offline) и KEYCODE_WAKEUP —
runner сам будит шлем, но до него проверяет устройство. Аргументы берутся из launch-args.json bundle,
вывод runner — в <bundle>.runner.txt, итог — в progress JSON (дописывается после каждого прогона).
"""
import argparse
import json
import re
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

ADB = "C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
PWSH = "C:/Users/asker/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe"


def _adb(args, timeout):
    """adb с таймаутом; зависший connect к спящему шлему не роняет серию, а считается неудачной попыткой."""
    try:
        return subprocess.run([ADB] + args, capture_output=True, text=True, timeout=timeout).stdout
    except subprocess.TimeoutExpired:
        return ""


def reconnect(serial, attempts=20):
    for _ in range(attempts):
        _adb(["disconnect", serial], 15)
        _adb(["connect", serial], 20)
        if re.search(re.escape(serial) + r"\s+device\b", _adb(["devices"], 15)):
            _adb(["-s", serial, "shell", "input keyevent KEYCODE_WAKEUP"], 20)
            return True
        time.sleep(6)
    return False


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--serial", required=True)
    p.add_argument("--progress", required=True)
    p.add_argument("--timeout", type=int, default=300)
    p.add_argument("--pause-guardian", action="store_true", help="debug.oculus.guardian_pause=1 на серию с возвратом")
    p.add_argument("--head-pose", default=None, help="передать runner -HeadPose (off — человек в шлеме)")
    p.add_argument("--keep-going", action="store_true", help="не останавливать серию на не-GREEN (визуальная проверка)")
    p.add_argument("--apk", default=None, help="другой путь к тому же APK (runner сверяет его с inventory manifest)")
    p.add_argument("bundles", nargs="+", help="папки RunSpec bundle в порядке прогона")
    args = p.parse_args()
    repo = Path(__file__).resolve().parents[3]
    progress_path = Path(args.progress)
    progress = json.loads(progress_path.read_text(encoding="utf-8")) if progress_path.exists() else []
    done = {row["bundle"] for row in progress if row.get("verdict") == "GREEN"}
    guardian = None
    try:
        if args.pause_guardian:
            # Решение пользователя 2026-10-20: без человека шлем после пробуждения показывает диалог Guardian;
            # на время серии — отладочная пауза Guardian (debug.*, сбрасывается и перезагрузкой), затем возврат.
            if not reconnect(args.serial):
                raise RuntimeError("шлем недоступен по adb для паузы Guardian")
            guardian = {"prop": "debug.oculus.guardian_pause",
                        "before": _adb(["-s", args.serial, "shell", "getprop debug.oculus.guardian_pause"], 20).strip()}
            _adb(["-s", args.serial, "shell", "setprop debug.oculus.guardian_pause 1"], 20)
            guardian["during"] = _adb(["-s", args.serial, "shell", "getprop debug.oculus.guardian_pause"], 20).strip()
            print(json.dumps({"guardian": guardian}), flush=True)
        run_series(args, repo, progress_path, progress, done)
    finally:
        if guardian is not None:
            value = guardian["before"] if guardian["before"] else "''"
            _adb(["-s", args.serial, "shell", "setprop debug.oculus.guardian_pause " + value], 20)
            guardian["after"] = _adb(["-s", args.serial, "shell", "getprop debug.oculus.guardian_pause"], 20).strip()
            guardian["restored"] = guardian["after"] == guardian["before"]
            (progress_path.parent / "guardian-pause.json").write_text(json.dumps(guardian, indent=1) + "\n",
                                                                       encoding="utf-8", newline="\n")
            print(json.dumps({"guardian": guardian}), flush=True)
        # Правило пользователя 2026-10-20: после тестов шлем — в сон (между прогонами серии он не спит).
        _adb(["-s", args.serial, "shell", "input keyevent KEYCODE_SLEEP"], 20)
        print(json.dumps({"sleepSent": True}), flush=True)


def run_series(args, repo, progress_path, progress, done):
    for bundle in args.bundles:
        bundle_path = Path(bundle)
        if str(bundle_path) in done:
            continue
        launch = json.loads((bundle_path / "launch-args.json").read_text(encoding="utf-8"))
        row = {"bundle": str(bundle_path), "started": datetime.now(timezone.utc).isoformat(), "args": launch["Arg"]}
        if not reconnect(args.serial):
            row.update(verdict="NO-DEVICE")
            progress.append(row)
            progress_path.write_text(json.dumps(progress, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
            print(json.dumps(row, ensure_ascii=False), flush=True)
            break
        argv = [PWSH, "-NoProfile", "-File", str(repo / "Tools/e2e/Run-QuestE2E.ps1"), "-Scenario", "perf-stress",
                "-Map", "Lobby", "-Timeout", str(args.timeout), "-Serial", args.serial, "-Adb", ADB,
                "-ServerPlayer", launch["ServerPlayer"], "-Apk", args.apk or launch["Apk"], "-RunSpec", launch["RunSpec"],
                "-Arg", ",".join(launch["Arg"]), "-StopLink", "-KeepAwake"]
        if args.head_pose:
            argv += ["-HeadPose", args.head_pose]
        log_path = bundle_path.parent / (bundle_path.name + ".runner.txt")
        with log_path.open("w", encoding="utf-8", newline="\n") as log:
            code = subprocess.run(argv, cwd=repo, stdout=log, stderr=subprocess.STDOUT,
                                  timeout=args.timeout + 600).returncode
        text = log_path.read_text(encoding="utf-8", errors="replace")
        verdict = re.search(r"ИТОГ:\s*(\S+)", text)
        folder = re.search(r"Папка прогона:\s*(.+)", text)
        refused = re.search(r"ОТКАЗ:\s*(.+)", text)
        row.update(exit=code, verdict=verdict.group(1) if verdict else ("REFUSED" if refused else "UNKNOWN"),
                   reason=refused.group(1).strip() if refused else None,
                   resultDir=folder.group(1).strip() if folder else None,
                   ended=datetime.now(timezone.utc).isoformat(), runnerLog=str(log_path))
        progress.append(row)
        progress_path.write_text(json.dumps(progress, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
        print(json.dumps({k: row[k] for k in ("bundle", "verdict", "exit", "resultDir")}, ensure_ascii=False), flush=True)
        if row["verdict"] != "GREEN" and not args.keep_going:
            break


if __name__ == "__main__":
    main()

"""Разведка draw-трассы ovrgpuprofiler на Quest (одноразовый discovery, не collector).

Порядок как у штатного collector: detailed включается только при подтверждённо
незапущенном приложении, захваты идут в заданной фазе perf-stress (по logcat),
в finally режим возвращается в исходный и перепроверяется. Сохраняет raw-вывод
каждого варианта команды и receipt.json с argv/exit/временем; формат не толкует.
"""
import argparse
import json
import re
import subprocess
import threading
import time
from pathlib import Path

PROFILER = "/system_ext/bin/ovrgpuprofiler"
DEFAULT_ADB = ("C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data/PlaybackEngines/"
               "AndroidPlayer/SDK/platform-tools/adb.exe")
VARIANTS = [
    ("draw-trace", ["-v", "-x", "-t"]),
    ("draw-trace-metrics-s", ["-v", "-x", "-t", "-s", "1,27,28,29,34,39"]),
    ("draw-trace-metrics-eq", ["-v", "-x=1,27,28,29,34,39", "-t"]),
]


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--serial", required=True)
    p.add_argument("--package", required=True)
    p.add_argument("--phase", required=True, help="точное имя фазы perf-stress")
    p.add_argument("--out", required=True)
    p.add_argument("--adb", default=DEFAULT_ADB)
    p.add_argument("--settle", type=float, default=3.0)
    p.add_argument("--max-wait", type=float, default=240.0)
    p.add_argument("--command-timeout", type=float, default=60.0)
    args = p.parse_args()
    out = Path(args.out)
    raw = out / "raw"
    raw.mkdir(parents=True, exist_ok=True)
    adb = [args.adb, "-s", args.serial]
    receipt = {"schemaVersion": 1, "kind": "vrb.quest-draw-discovery", "phase": args.phase,
               "commands": [], "captures": [], "reasons": [],
               "modeOwnership": {"initiallyEnabled": None, "enabledByScript": False, "restored": None}}
    t0 = time.monotonic()

    def run(name, argv, ok=(0,)):
        start = time.monotonic() - t0
        r = subprocess.run(adb + ["shell"] + argv, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=args.command_timeout)
        (raw / (name + ".txt")).write_text(r.stdout + r.stderr, encoding="utf-8", newline="\n")
        receipt["commands"].append({"name": name, "argv": argv, "exit": r.returncode,
                                    "start": round(start, 3), "end": round(time.monotonic() - t0, 3)})
        if r.returncode not in ok:
            raise RuntimeError("%s exit %d" % (name, r.returncode))
        return r.stdout + r.stderr

    def detailed(text):
        if "is enabled" in text:
            return True
        if "is disabled" in text:
            return False
        raise RuntimeError("непонятный ответ -i: %r" % text[:200])

    log = None
    phase_seen = threading.Event()
    phase_line = re.compile(r"=== фаза «" + re.escape(args.phase) + r"»")
    try:
        receipt["modeOwnership"]["initiallyEnabled"] = detailed(run("mode-initial", [PROFILER, "-i"]))
        if not receipt["modeOwnership"]["initiallyEnabled"]:
            pid = run("application-pid", ["pidof", args.package], ok=(0, 1))
            if pid.strip():
                raise RuntimeError("приложение уже запущено — detailed не включаю")
            run("mode-enable", [PROFILER, "-e", args.package])
            receipt["modeOwnership"]["enabledByScript"] = True
            if not detailed(run("mode-after-enable", [PROFILER, "-i"])):
                raise RuntimeError("detailed enable не подтверждён")
        log = subprocess.Popen(adb + ["logcat", "-T", "1", "-v", "brief", "Unity:I", "*:S"],
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                               encoding="utf-8", errors="replace")
        log_file = (raw / "logcat.txt").open("w", encoding="utf-8", newline="\n")

        def reader():
            for line in log.stdout:
                log_file.write(line)
                if phase_line.search(line):
                    receipt["phaseSeenAt"] = round(time.monotonic() - t0, 3)
                    phase_seen.set()
            log_file.close()

        threading.Thread(target=reader, daemon=True).start()
        print(json.dumps({"status": "waiting-phase", "detailed": True}, ensure_ascii=False), flush=True)
        if not phase_seen.wait(args.max_wait):
            raise RuntimeError("фаза не наступила за max-wait")
        time.sleep(args.settle)
        for name, flags in VARIANTS:
            try:
                text = run(name, [PROFILER] + flags, ok=tuple(range(0, 256)))
                receipt["captures"].append({"name": name, "flags": flags,
                                            "exit": receipt["commands"][-1]["exit"],
                                            "lines": text.count("\n"), "bytes": len(text.encode("utf-8"))})
            except subprocess.TimeoutExpired:
                receipt["captures"].append({"name": name, "flags": flags, "exit": None, "timeout": True})
    except (RuntimeError, OSError, subprocess.SubprocessError) as exc:
        receipt["reasons"].append(str(exc))
    finally:
        if log is not None:
            log.terminate()
            try:
                log.wait(timeout=5)
            except subprocess.TimeoutExpired:
                log.kill()
        if receipt["modeOwnership"]["enabledByScript"]:
            try:
                run("mode-restore", [PROFILER, "-d"])
                receipt["modeOwnership"]["restored"] = not detailed(run("mode-after-restore", [PROFILER, "-i"]))
            except (RuntimeError, OSError, subprocess.SubprocessError) as exc:
                receipt["modeOwnership"]["restored"] = False
                receipt["reasons"].append("restore: %s" % exc)
        (out / "receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=1) + "\n",
                                          encoding="utf-8", newline="\n")
        print(json.dumps({"captures": receipt["captures"], "reasons": receipt["reasons"],
                          "modeOwnership": receipt["modeOwnership"]}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()

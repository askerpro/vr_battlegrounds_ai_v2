"""Разбивка времени GPU на Quest во время прогона perf-stress (задача perf-stress-scenario).

Слушает logcat приложения; при начале фазы стресс-теста («=== фаза «база»», «=== фаза «куклы: по карте»»)
ждёт успокоения и снимает ovrgpuprofiler:
  --pass realtime  — метрики реального времени (-r), без детального режима: загрузка GPU, вершины против
                     фрагментов, простои текстур/памяти. Эти числа годятся для оценки.
  --pass detailed  — детальный режим драйвера (-e до запуска приложения): трасса стадий рендера (-t) и
                     drawcall-ов (-x -t). Режим сам нагружает GPU — числа только для долей, не для бюджета.
Прогон запускает отдельно Run-QuestE2E.ps1 (этот скрипт adb-приложение не запускает и настроек не меняет,
кроме детального режима профайлера, который в конце выключается).

Пример:
  python -X utf8 Tools/Perf/quest_gpu_breakdown.py --pass realtime --out <отчёты задачи>/reports/gpu/<run>
"""
import argparse
import os
import subprocess
import sys
import time

ADB_DEFAULT = r"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
PACKAGE = "com.UnityTechnologies.com.unity.template.urpblank"
PROFILER = "/system_ext/bin/ovrgpuprofiler"
PHASES = {"база": "base", "куклы: по карте": "puppets-map", "куклы: арена": "puppets-arena", "куклы": "puppets"}

# 4 util, 3 bus, 7 vertex fetch stall, 8 texture fetch stall, 11 stalled on sysmem, 17 shaders busy,
# 18 shaders stalled, 22 texture pipes busy, 25 % time vertices, 32 % time fragments,
# 26 vertices/s, 33 fragments/s, 70 avg polygon area, 65 prims trivially rejected, 80 LRZ pixels killed
REALTIME_METRICS = "4,3,7,8,11,17,18,22,25,32,26,33,70,65,80"


def adb(args, adb_path, timeout=None):
    return subprocess.run([adb_path] + args, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout)


def capture_realtime(adb_path, seconds, path):
    # -r печатает раз в секунду до прерывания: читаем seconds строк блоков и обрываем.
    with open(path, "w", encoding="utf-8") as out:
        proc = subprocess.Popen([adb_path, "shell", PROFILER, f"--realtime={REALTIME_METRICS}"],
                                stdout=out, stderr=subprocess.STDOUT)
        time.sleep(seconds)
        proc.terminate()
        try:
            proc.wait(5)
        except subprocess.TimeoutExpired:
            proc.kill()


# Метрики трассы стадий/drawcall-ов (список -x -m / -m -t): 1 Clocks, 27 % Time Shading Fragments,
# 28 Fragments Shaded, 3 % Texture Fetch Stall, 20 % Time Shading Vertices. Без списка -x отвечает
# «No metrics to track!».
TRACE_METRICS = "1,27,28,3,20"


def capture_detailed(adb_path, path_prefix):
    stage = adb(["shell", PROFILER, "-v", "-t"], adb_path, timeout=120)
    open(path_prefix + "-renderstage.txt", "w", encoding="utf-8").write(stage.stdout + stage.stderr)
    variants = {
        "drawcalls": ["-v", "-x", f"--renderstage-metrics={TRACE_METRICS}", "-t"],
        "drawcalls-s": ["-x", "-s", TRACE_METRICS, "-t"],
        "renderstage-metrics": ["-v", f"--renderstage-metrics={TRACE_METRICS}", "-t"],
    }
    for name, flags in variants.items():
        r = adb(["shell", PROFILER] + flags, adb_path, timeout=180)
        open(f"{path_prefix}-{name}.txt", "w", encoding="utf-8").write(r.stdout + r.stderr)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pass", dest="mode", choices=["realtime", "detailed"], required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--adb", default=ADB_DEFAULT)
    ap.add_argument("--settle", type=float, default=10.0, help="с после начала фазы до замера")
    ap.add_argument("--seconds", type=float, default=30.0, help="длительность realtime-замера")
    ap.add_argument("--max-wait", type=float, default=900.0)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "detailed":
        r = adb(["shell", PROFILER, "-e", PACKAGE], args.adb, timeout=30)
        print("detailed on:", (r.stdout + r.stderr).strip()[:200], flush=True)

    seen = set()
    start = time.time()

    def lines():
        # -T 1: только новые строки — иначе буфер logcat отдаст фазы прошлого прогона. Дирижёр делает
        # `logcat -c`, на котором поток logcat обрывается, — переподключаемся до конца прогона.
        while time.time() - start < args.max_wait:
            proc = subprocess.Popen([args.adb, "logcat", "-T", "1", "-v", "brief", "Unity:I", "*:S"],
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                    encoding="utf-8", errors="replace")
            holder["log"] = proc
            for item in proc.stdout:
                yield item
            proc.wait()
            time.sleep(0.5)

    holder = {}
    try:
        for line in lines():
            if time.time() - start > args.max_wait:
                print("timeout waiting for phases", flush=True)
                break
            if "=== итог прогона" in line:
                print("run finished", flush=True)
                break
            if "=== фаза «" not in line:
                continue
            name = line.split("=== фаза «", 1)[1].split("»", 1)[0]
            key = PHASES.get(name)
            if key is None or key in seen:
                continue
            seen.add(key)
            print(f"phase {name} -> capture {args.mode} in {args.settle:.0f} s", flush=True)
            time.sleep(args.settle)
            prefix = os.path.join(args.out, key)
            if args.mode == "realtime":
                capture_realtime(args.adb, args.seconds, prefix + "-realtime.txt")
            else:
                capture_detailed(args.adb, prefix)
            print(f"captured {key}", flush=True)
    finally:
        if holder.get("log") is not None:
            holder["log"].terminate()
        if args.mode == "detailed":
            r = adb(["shell", PROFILER, "-d"], args.adb, timeout=30)
            print("detailed off:", (r.stdout + r.stderr).strip()[:200], flush=True)
    print("phases captured:", sorted(seen), flush=True)
    return 0 if seen else 1


if __name__ == "__main__":
    sys.exit(main())

"""Raw GPU evidence Quest: runtime-каталоги, границы фаз и partial receipt.

Приложение запускается отдельно. Legacy logcat не доказывает workload.ready.
Exit0 означает полноту запрошенного сбора, а не performance PASS.
"""
import argparse
import json
from pathlib import Path
import sys

from gpu_diagnostics.collector import ADB_DEFAULT, LEGACY_PHASES, PACKAGE, Config, collect, parse_phases


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pass", dest="mode", choices=("realtime", "detailed"), required=True)
    parser.add_argument("--out", type=Path, required=True, help="новый каталог evidence")
    parser.add_argument("--phases", help="JSON список точных имён фаз либо путь JSON; без флага — четыре legacy фазы")
    parser.add_argument("--serial", help="точный ADB serial; передаётся всем device-командам")
    parser.add_argument("--adb", default=ADB_DEFAULT)
    parser.add_argument("--package", default=PACKAGE)
    parser.add_argument("--metrics", help="точные runtime имена через запятую; неизвестное имя — отказ")
    # draw этого collector не реализован; per-draw на шлеме — адаптер Meta RenderDoc,
    # см. Tools/Perf/README.md, раздел «Per-draw захват Meta RenderDoc на Quest».
    parser.add_argument("--capture-kind", choices=("stage", "draw"), default="stage",
                        help="один detailed capture; draw требует проверенного adapter (пока отсутствует)")
    parser.add_argument("--enable-detailed", action="store_true",
                        help="включить только выключенный режим до запуска приложения; свой режим восстановится")
    parser.add_argument("--settle", type=float, default=2.0)
    parser.add_argument("--seconds", type=float, default=5.0, help="длительность realtime")
    parser.add_argument("--max-wait", type=float, default=900.0, help="общий wall timeout, включая молчание logcat")
    parser.add_argument("--capture-timeout", type=float, default=120.0)
    parser.add_argument("--command-timeout", type=float, default=30.0)
    args = parser.parse_args(argv)
    try:
        phases = parse_phases(args.phases if args.phases is not None else LEGACY_PHASES)
        metrics = [name.strip() for name in args.metrics.split(",")] if args.metrics is not None else None
        options = vars(args).copy()
        options["phases"], options["metrics"] = phases, metrics
        receipt = collect(Config(**options))
    except (ValueError, OSError) as exc:
        print(str(exc), file=sys.stderr)
        return 2
    print(json.dumps({"complete": receipt["complete"], "missing": receipt["missing"],
                      "quality": receipt["quality"], "reasons": receipt["reasons"],
                      "receipt": str(args.out / "capture.json")}, ensure_ascii=False))
    return receipt["exitCode"]


if __name__ == "__main__":
    sys.exit(main())

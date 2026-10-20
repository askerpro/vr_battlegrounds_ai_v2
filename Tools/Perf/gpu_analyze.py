"""Проверка, импорт и offline A/B отчёт GPU package; Unity/ADB не требуются."""
import argparse
import json
import sys
from pathlib import Path

from gpu_diagnostics.model import PackageError, analyze_run, compare_runs, import_summary, load_run
from gpu_diagnostics.report import render_report
from gpu_diagnostics.scene_data import index_scene, query_scene
from gpu_diagnostics.import_runtime import import_runtime_bundle
from gpu_diagnostics.compare_series import compare_series
from gpu_diagnostics.run_spec import RENDER_DIAGNOSTICS, prepare_windows_run, prepare_quest_run
from gpu_diagnostics.replay_data import query_replay
from gpu_diagnostics.replay_scene import join_replay_scene
from gpu_diagnostics.compare_replay import compare_replay


def emit(value):
    print(json.dumps(value, ensure_ascii=False, allow_nan=False))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate", help="Проверить один нормализованный package")
    validate.add_argument("run", type=Path)
    importer = commands.add_parser("import-summary", help="Импорт старого summary без псевдосэмплов")
    importer.add_argument("source", type=Path)
    importer.add_argument("--out", type=Path, required=True)
    runtime = commands.add_parser("import-runtime", help="Импорт raw runtime observations без выдуманных GPU-интервалов")
    runtime.add_argument("source", type=Path)
    runtime.add_argument("--out", type=Path, required=True)
    replay = commands.add_parser("replay-query", help="Дорогие GPU replay-команды, проходы и связанные ресурсы порциями")
    replay.add_argument("source", type=Path)
    replay.add_argument("--table", choices=("events", "resources"), default="events")
    replay.add_argument("--event-id", type=int)
    replay.add_argument("--resource-id")
    replay.add_argument("--offset", type=int, default=0)
    replay.add_argument("--limit", type=int, default=25)
    replay.add_argument("--full", action="store_true", help="Также вернуть полные API events каждой команды")
    replay.add_argument("--joins", type=Path, help="Пакет кандидатов из join-replay-scene")
    join = commands.add_parser("join-replay-scene", help="Связать capture с кандидатами assets через scene index и identity того же процесса")
    join.add_argument("source", type=Path)
    join.add_argument("--scene-index", type=Path, required=True)
    join.add_argument("--runtime", type=Path, required=True)
    join.add_argument("--out", type=Path, required=True)
    replay_compare = commands.add_parser("compare-replay", help="Сравнить состав indexed GPU-команд пары LOD capture")
    replay_compare.add_argument("a", type=Path)
    replay_compare.add_argument("b", type=Path)
    replay_compare.add_argument("--joins-a", type=Path, required=True)
    replay_compare.add_argument("--joins-b", type=Path, required=True)
    replay_compare.add_argument("--out", type=Path, required=True)
    compare = commands.add_parser("compare", help="Сравнить два package и записать JSON")
    compare.add_argument("a", type=Path)
    compare.add_argument("b", type=Path)
    compare.add_argument("--out", type=Path, required=True)
    compare.add_argument("--allow-condition", action="append", default=[], metavar="KEY")
    compare.add_argument("--html", action="store_true", help="Дополнительно создать прежний HTML")
    series = commands.add_parser("compare-series", help="Повторные LOD A/B и A/A без псевдоповторов из API polls")
    series.add_argument("plan", type=Path)
    series.add_argument("--out", type=Path, required=True)
    prepare = commands.add_parser("prepare-windows-run", help="Подготовить RunSpec для существующего Windows E2E launcher")
    prepare.add_argument("--build", type=Path, required=True)
    prepare.add_argument("--views", type=Path, required=True)
    prepare.add_argument("--out", type=Path, required=True)
    prepare.add_argument("--variant", choices=("lod-a", "lod-b"), required=True)
    identity = prepare.add_mutually_exclusive_group(required=True)
    identity.add_argument("--fingerprint")
    identity.add_argument("--discovery", action="store_true")
    prepare.add_argument("--pair-id", required=True)
    prepare.add_argument("--repetition", type=int, default=1)
    prepare.add_argument("--phase-seconds", type=float, default=10)
    prepare.add_argument("--view", help="Один существующий view для короткого discovery; отсутствие — полный views file")
    prepare.add_argument("--capture-kind", choices=("none", "draw"), default="none", help="draw создаёт instrumented RunSpec для RenderDoc actuator")
    prepare.add_argument("--render-diag", choices=RENDER_DIAGNOSTICS, default=None,
                         help="Диагностический вариант рендера (PerfRenderDiagnosticControl)")
    scene = commands.add_parser("inspect-scene", help="Индекс JSON/JSONL из scene.json; без выдуманных GPU-таймингов")
    scene.add_argument("scene", type=Path)
    scene.add_argument("--out", type=Path, required=True)
    query = commands.add_parser("scene-query", help="Поиск и чтение записей индекса ограниченными порциями")
    query.add_argument("package", type=Path)
    query.add_argument("--id", dest="identity")
    query.add_argument("--search")
    query.add_argument("--kind", choices=("object", "renderer", "resource", "lod", "light"))
    query.add_argument("--full", action="store_true", help="Вернуть исходные поля выбранных записей")
    links = commands.add_parser("scene-links", help="Входящие и исходящие связи по точному ID")
    links.add_argument("package", type=Path)
    links.add_argument("identity")
    for command in (query, links):
        command.add_argument("--offset", type=int, default=0)
        command.add_argument("--limit", type=int, default=25)
    quest = commands.add_parser("prepare-quest-run", help="Immutable входы прежнего Quest E2E из совпадающих Windows/Quest builder profiles")
    quest.add_argument("--server-build", type=Path, required=True)
    quest.add_argument("--quest-build", type=Path, required=True)
    quest.add_argument("--views", type=Path, required=True)
    quest.add_argument("--out", type=Path, required=True)
    quest.add_argument("--variant", choices=("lod-a", "lod-b"), required=True)
    quality = quest.add_mutually_exclusive_group(required=True)
    quality.add_argument("--fingerprint")
    quality.add_argument("--discovery", action="store_true")
    quest.add_argument("--pair-id", required=True)
    quest.add_argument("--repetition", type=int, default=1)
    quest.add_argument("--phase-seconds", type=float, default=10)
    quest.add_argument("--view")
    quest.add_argument("--capture-kind", choices=("none", "stage", "draw"), default="none")
    quest.add_argument("--render-diag", choices=RENDER_DIAGNOSTICS, default=None,
                       help="Диагностический вариант рендера (PerfRenderDiagnosticControl); none — явный контроль")
    quest.add_argument("--head-pose", choices=("eye", "off"), default="eye",
                       help="off — реальный трекинг для визуальной проверки человеком в шлеме")
    args = parser.parse_args(argv)
    try:
        if args.command == "prepare-quest-run":
            emit(prepare_quest_run(args.server_build, args.quest_build, args.views, args.out, args.variant,
                                  args.fingerprint, args.discovery, args.pair_id, args.repetition,
                                  args.phase_seconds, args.view, args.capture_kind, args.render_diag,
                                  args.head_pose))
            return 0
        if args.command == "compare-replay":
            result = compare_replay(args.a, args.b, args.joins_a, args.joins_b, args.out)
            emit(result)
            return 2 if result["status"] == "incomparable" else 0
        if args.command == "join-replay-scene":
            emit(join_replay_scene(args.source, args.scene_index, args.runtime, args.out))
            return 0
        if args.command == "replay-query":
            emit(query_replay(args.source, args.table, args.event_id, args.resource_id, args.offset, args.limit, args.full, args.joins))
            return 0
        if args.command == "prepare-windows-run":
            emit(prepare_windows_run(args.build, args.views, args.out, args.variant, args.fingerprint,
                                     args.discovery, args.pair_id, args.repetition, args.phase_seconds, args.view, args.capture_kind,
                                     args.render_diag))
            return 0
        if args.command == "inspect-scene":
            emit(index_scene(args.scene, args.out))
            return 0
        if args.command in {"scene-query", "scene-links"}:
            emit(query_scene(args.package, identity=args.identity,
                             search=getattr(args, "search", None), kind=getattr(args, "kind", None),
                             offset=args.offset, limit=args.limit, full=getattr(args, "full", False),
                             links=args.command == "scene-links"))
            return 0
        if args.command == "validate":
            result = analyze_run(load_run(args.run))
            emit({"status": result["status"], "runId": result["runId"], "reasons": result["reasons"],
                  "metrics": {key: {k: m.get(k) for k in ("count", "p50", "p95", "sourceSummaryOnly", "observationDistribution")}
                              for key, m in result["metrics"].items()}})
            return 2 if result["status"] == "invalid" else 0
        if args.command == "import-runtime":
            emit({"status": "limited", "packages": import_runtime_bundle(args.source, args.out),
                  "reason": "API observations; GPU source frame/interval неизвестны"})
            return 0
        if args.command == "compare-series":
            if args.out.exists():
                raise PackageError("Каталог series уже существует")
            spec = json.loads(args.plan.read_text(encoding="utf-8"))
            if not isinstance(spec, dict) or spec.get("schemaVersion") != 1:
                raise PackageError("series plan: требуется schemaVersion1")
            def paths(rows):
                if not isinstance(rows, list) or any(not isinstance(row, dict) or
                    any(not isinstance(row.get(key), str) or not row[key] for key in ("a", "b", "order")) for row in rows):
                    raise PackageError("series pairs: требуются a/b paths и order")
                return [dict(row, **{key: (args.plan.parent / row[key]).resolve() for key in ("a", "b")}) for row in rows]
            result = compare_series(paths(spec.get("pairs")), paths(spec.get("aa")), spec.get("allowedSettings", ["/sceneDetail/farStart"]))
            args.out.mkdir(parents=True)
            target = args.out / "series.json"
            target.write_text(json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")
            emit({"status": result["status"], "groups": len(result["groups"]), "report": str(target.resolve())})
            return 0
        if args.command == "import-summary":
            paths = import_summary(args.source, args.out)
            emit({"status": "limited", "packages": [str(path) for path in paths],
                  "reason": "summary-only: сырые samples и verified window отсутствуют"})
            return 0
        if args.out.resolve() in {args.a.resolve(), args.b.resolve()}:
            raise PackageError("Выходной каталог сравнения совпадает с исходным package")
        try:
            result = compare_runs(load_run(args.a), load_run(args.b), args.allow_condition)
        except (PackageError, OSError, ValueError) as error:
            result = {"schemaVersion": 1, "status": "invalid", "reasons": [str(error)],
                      "conditionsDiff": [], "allowedConditions": args.allow_condition,
                      "a": {"path": str(args.a.resolve())}, "b": {"path": str(args.b.resolve())},
                      "metrics": {}, "quest2": "notMeasured"}
        args.out.mkdir(parents=True, exist_ok=True)
        (args.out / "comparison.json").write_text(json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n",
                                                  encoding="utf-8", newline="\n")
        output = {"status": result["status"], "reasons": result["reasons"],
                  "comparison": str((args.out / "comparison.json").resolve())}
        if args.html:
            output["report"] = str(render_report(result, args.out).resolve())
        emit(output)
        return 2 if result["status"] in {"invalid", "incomparable"} else 0
    except (PackageError, OSError, ValueError) as error:
        emit({"status": "invalid", "reason": str(error)})
        return 2


if __name__ == "__main__":
    sys.exit(main())

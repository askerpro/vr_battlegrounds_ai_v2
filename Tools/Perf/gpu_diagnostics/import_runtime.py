"""Импорт реального runtime bundle; API observations не выдаются за GPU source samples."""
import csv
import hashlib
import io
import json
import os
from pathlib import Path

from .model import CONDITIONS, PackageError, _json, _local_path, _number, _require


METRICS = {
    "delta_ms": ("ms", "app", "Unity deltaTime"),
    "uxr_ms": ("ms", "app", "PerfFrameRecorder UXR"),
    "unity_cpu_ms": ("ms", "app", "FrameTimingManager CPU"),
    "unity_gpu_ms": ("ms", "app", "FrameTimingManager GPU"),
    "app_cpu_ms": ("ms", "app", "Unity.XR.Oculus.Stats.PerfMetrics.AppCPUTime"),
    "app_gpu_ms": ("ms", "app", "Unity.XR.Oculus.Stats.PerfMetrics.AppGPUTime"),
    "compositor_gpu_ms": ("ms", "compositor", "Unity.XR.Oculus.Stats.PerfMetrics.CompositorGPUTime"),
    "gpu_util_percent": ("%", "device", "Unity.XR.Oculus.Stats.PerfMetrics.GPUUtilization"),
    "cpu_util_percent": ("%", "device", "Unity.XR.Oculus.Stats.PerfMetrics.CPUUtilization"),
    "cpu_level": ("level", "device", "OculusPerfStats CPU level"),
    "gpu_level": ("level", "device", "OculusPerfStats GPU level"),
}


def _sha(raw):
    return hashlib.sha256(raw).hexdigest()


def _write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def import_runtime_bundle(source: Path, out: Path) -> list:
    source, out = source.resolve(), out.resolve()
    # Штатный harvest вкладывает role/process и может превысить Windows MAX_PATH.
    # Extended path сохраняет исходный комплект и проверки хешей без переноса файлов.
    if os.name == "nt" and not str(source).startswith("\\\\?\\"):
        absolute = str(source)
        source = Path("\\\\?\\UNC\\" + absolute[2:] if absolute.startswith("\\\\") else "\\\\?\\" + absolute)
    _require(not out.exists(), "Выходной каталог уже существует")
    manifest = _json(source / "source.json")
    runtime = _json(source / "runtime.json")
    _require(manifest.get("kind") == "vrb.perf-runtime-observations" and manifest.get("schemaVersion") == 1,
             "Неизвестный runtime source format")
    _require(runtime.get("schemaVersion") == 1 and runtime.get("runId") == manifest.get("runId"), "runtime/source runId mismatch")
    _require(manifest.get("sourceIntervalsKnown") is False, "Импортер поддерживает только observations; native samples требуют своего доказательства")
    _require(runtime.get("requestedMode", "live") == manifest.get("mode"), "requested/recorder mode mismatch; нельзя объявить instrumented/replay live")
    frequency = manifest.get("tickFrequency")
    _require(type(frequency) is int and frequency > 0, "Неверная tickFrequency")
    _require(runtime.get("clockId") == "process:" + runtime.get("processInstanceId", "") + ":stopwatch", "clock/process mismatch")
    raw = {"source.json": (source / "source.json").read_bytes(), "runtime.json": (source / "runtime.json").read_bytes()}
    if runtime.get("qualityCanonicalPath") is not None:
        path = _local_path(source, runtime["qualityCanonicalPath"], label="qualityCanonicalPath")
        raw["quality-canonical.json"] = path.read_bytes()
        if runtime.get("qualityFingerprint") is not None:
            _require(_sha(raw["quality-canonical.json"]) == runtime["qualityFingerprint"], "quality fingerprint mismatch")
    if runtime.get("sceneIdentitiesPath") is not None:
        identity_path = _local_path(source, runtime["sceneIdentitiesPath"], label="sceneIdentitiesPath")
        raw["scene-identities.json"] = identity_path.read_bytes()
    if source.parent.parent.name == "processes" and source.parent.parent.parent.name == "source":
        stage = source.parents[3]
        for name, hash_key in (("run-spec.json", "specSha256"), ("build.json", "buildManifestSha256")):
            staged = stage / name
            if staged.is_file():
                raw[name] = staged.read_bytes()
                _require(_sha(raw[name]) == runtime.get(hash_key), name + " hash mismatch")
    for key in ("frames", "phases"):
        path = _local_path(source, manifest.get(key + "Path"), label=key + "Path")
        raw[key + ".csv"] = path.read_bytes()
        _require(_sha(raw[key + ".csv"]) == manifest.get(key + "Sha256"), key + " SHA256 mismatch")
    events_path = source.parent / "events.jsonl"
    events = []
    actual_path = source.parent / "actual.json"
    actual = _json(actual_path) if actual_path.is_file() else {}
    if actual:
        _require(actual.get("runId") == manifest["runId"] and actual.get("processInstanceId") == runtime["processInstanceId"],
                 "actual identity mismatch")
        raw["actual.json"] = actual_path.read_bytes()
    if events_path.is_file():
        raw["events.jsonl"] = events_path.read_bytes()
        for line in raw["events.jsonl"].decode("utf-8").splitlines():
            event = json.loads(line)
            _require(event.get("runId") == manifest["runId"] and event.get("processInstanceId") == runtime["processInstanceId"],
                     "events identity mismatch")
            _require(event.get("tickFrequency") == frequency, "events clock frequency mismatch")
            events.append(event)
    phases = list(csv.DictReader(io.StringIO(raw["phases.csv"].decode("utf-8"))))
    frames = list(csv.DictReader(io.StringIO(raw["frames.csv"].decode("utf-8"))))
    _require(len(phases) == manifest.get("phaseObservationCount") and len(frames) == manifest.get("frameCount"), "runtime row count mismatch")
    generations = {}
    for phase in phases:
        generation, ticks = int(phase["phaseGeneration"]), int(phase["monotonicTicks"])
        _require(generation >= 1 and ticks >= 0 and phase["transition"] in ("begin", "end"), "Неверная phase observation")
        target = generations.setdefault(generation, {})
        _require(phase["transition"] not in target, "Повторная phase transition")
        target[phase["transition"]] = phase
    frame_groups, previous_ticks, previous_frame = {}, -1, -1
    for frame in frames:
        ticks, number = int(frame["observationTicks"]), int(frame["observationFrame"])
        _require(ticks > previous_ticks and number > previous_frame, "Frames не упорядочены")
        _require(all(frame[key] == "null" for key in ("ovrSourceFrame", "sourceStart", "sourceEnd")), "Неожиданный source interval")
        previous_ticks, previous_frame = ticks, number
        frame_groups.setdefault(int(frame["candidatePhaseGeneration"]), []).append(frame)
    definitions = {name: {"unit": unit, "scope": scope, "source": origin, "cadence": "frame",
                          "aggregation": "unknown", "observationCadence": "poll per Unity frame; unique source cadence unknown"}
                   for name, (unit, scope, origin) in METRICS.items()}
    packages = []
    run_spec = json.loads(raw["run-spec.json"].decode("utf-8")) if "run-spec.json" in raw else None
    minimum_window = run_spec["capture"]["minimumWindowSeconds"] if run_spec else None
    if minimum_window is not None:
        _number(minimum_window, "minimumWindowSeconds", nonnegative=True)
    for generation, phase in sorted(generations.items()):
        begin, end = phase.get("begin"), phase.get("end")
        _require(begin is not None, "Phase end без begin")
        if begin["measured"] != "true":
            continue
        start_ticks = int(begin["monotonicTicks"])
        end_ticks = int(end["monotonicTicks"]) if end else None
        if end:
            _require(end_ticks >= start_ticks and end["phaseId"] == begin["phaseId"], "Phase bounds/identity mismatch")
        phase_events = [event for event in events if event.get("phaseGeneration") == generation]
        candidates = [event for event in phase_events if event.get("kind") == "conditions.observed"]
        ready_events = [event for event in phase_events if event.get("kind") == "workload.ready"]
        ready = ready_events[0] if len(ready_events) == 1 else None
        if ready:
            receipt = ready["payload"]["conditionsEventId"]
            candidates = [event for event in candidates if event.get("eventId") == receipt]
            _require(len(candidates) == 1, "ready conditions receipt отсутствует/неоднозначен")
            _require(any(event.get("eventId") == ready["payload"]["viewEventId"] and event.get("kind") == "view.observed"
                         for event in phase_events), "ready view receipt отсутствует")
        evidence = candidates[-1]["payload"] if candidates else {}
        values = evidence.get("values", {})
        extra_conditions = ("qualityFingerprintMethod", "applicabilityPolicy", "runtimePlatform", "xrEnabled", "puppetCount",
                            "clutterCount", "renderDiagnostic")
        conditions = {key: values.get(key, {}).get("value") for key in (*CONDITIONS, *extra_conditions)}
        conditions.update(phaseId=begin["phaseId"], role=runtime.get("role"))
        limitations = ["API observations, не verified source-frame samples: interval/frame/latency неизвестны",
                       "candidatePhaseGeneration относится ко времени чтения, не к доказанной фазе GPU",
                       "Распределения API polls не являются количеством уникальных исходных кадров",
                       "Recorder mode=" + str(manifest["mode"]) + "; detailed GPU mode/overhead этим bundle не проверены"]
        if runtime.get("renderDiagnosticRestore") not in (None, "restored", "pending"):
            limitations.append("render_diag restore: " + str(runtime.get("renderDiagnosticRestore")))
        if runtime.get("discoveryOnly") is True:
            limitations.append("Discovery-only: fingerprint наблюдается впервые; это не принятый GPU/A/B замер")
        if not end:
            limitations.append("Фактический phase.end отсутствует")
        if not manifest.get("complete"):
            limitations.append("Source recorder неполный: см. drop counts/stopReason")
        if not ready:
            limitations.append("Нет принятого workload.ready этой generation")
        selected_start, selected_end = start_ticks, end_ticks
        if ready:
            selected_start = max(start_ticks, int(ready["monotonicTicks"]))
            invalidations = [event for event in phase_events if event.get("kind") == "workload.invalidated"
                             and event["payload"].get("readyId") == ready["payload"]["readyId"]]
            if invalidations:
                invalidated_at = min(int(event["monotonicTicks"]) for event in invalidations)
                selected_end = min(selected_end, invalidated_at) if selected_end is not None else invalidated_at
            limitations.append("Выборка ограничена локальными ready/invalidation; это не GPU source window")
        destination = out / f"phase-{generation:04d}"
        (destination / "raw").mkdir(parents=True)
        for name, content in raw.items():
            (destination / "raw" / name).write_bytes(content)
        observations_path = destination / "observations.csv"
        with observations_path.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.writer(stream, lineterminator="\n")
            writer.writerow(("metric", "observationTicks", "value", "sourceFrameId", "sourceStart", "sourceEnd"))
            for frame in frame_groups.get(generation, []):
                ticks = int(frame["observationTicks"])
                _require(ticks >= start_ticks and (end_ticks is None or ticks <= end_ticks), "Observation вне локальной фазы")
                if ticks < selected_start or (selected_end is not None and ticks > selected_end):
                    continue
                for metric in METRICS:
                    value = frame.get(metric)
                    if value is None or value == "null":
                        continue
                    _number(float(value), metric, nonnegative=True)
                    writer.writerow((metric, ticks, value, "null", "null", "null"))
        (destination / "samples.csv").write_text("metric,start,end,value\n", encoding="utf-8", newline="\n")
        normalized = {"schemaVersion": 1, "runId": manifest["runId"] + "-g" + str(generation),
                      "parentRunId": manifest["runId"],
                      "startedUtc": actual.get("startedUtc"),
                      "provenance": {key: runtime.get(key) for key in ("sourceSha", "specSha256", "buildManifestSha256")},
                      "variant": manifest["variant"], "mode": manifest["mode"],
                      "source": {"kind": manifest["kind"], "path": "raw/source.json", "sha256": _sha(raw["source.json"])},
                      "conditions": conditions, "metrics": definitions, "window": None, "summary": [], "limitations": limitations,
                      "observations": {"path": "observations.csv", "sha256": _sha(observations_path.read_bytes()),
                                       "clock": runtime["clockId"], "tickFrequency": frequency,
                                       "readyEventId": ready.get("eventId") if ready else None,
                                       "localWindowClosed": selected_end is not None,
                                       "sourceComplete": manifest.get("complete") is True,
                                       "localWindowSeconds": (selected_end - selected_start) / frequency if selected_end is not None else None,
                                       "minimumWindowSeconds": minimum_window,
                                       "timeMeaning": "observation", "sourceIntervalsKnown": False}}
        _write_json(destination / "quality.json", {"schemaVersion": 1, "snapshot": runtime.get("quality"),
                                                   "conditionEvidence": values, "phaseClosed": end is not None,
                                                   "sourcePhaseGeneration": generation})
        _write_json(destination / "events.json", {"schemaVersion": 1,
            "events": [dict(event, id=event["eventId"]) for event in phase_events]})
        _write_json(destination / "run.json", normalized)
        packages.append(str(destination))
    _require(packages, "Нет measured phase observations; нормализованные прогоны не созданы")
    _write_json(out / "import.json", {"schemaVersion": 1, "status": "limited", "packages": packages,
                                      "rawHashes": {name: _sha(value) for name, value in raw.items()}})
    return packages

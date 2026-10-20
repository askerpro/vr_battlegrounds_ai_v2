"""Проверка происхождения и сопоставимости офлайн-измерений GPU.

Raw и summary остаются разными источниками. Процентили raw используют линейную
интерполяцию между соседними порядковыми статистиками; summary не пересчитываются.
"""

import copy
import csv
import hashlib
import json
import math
from pathlib import Path, PurePosixPath, PureWindowsPath
import re


CONDITIONS = (
    "deviceModel", "deviceId", "graphicsApi", "developmentBuild", "sceneId", "role",
    "viewId", "phaseId", "workloadId", "refreshHz", "eyeResolution", "ffr", "cpuLevel",
    "gpuLevel", "qualityHash",
)
FIXED_CONDITIONS = frozenset((
    "deviceModel", "deviceId", "graphicsApi", "developmentBuild", "role", "viewId",
    "phaseId", "mode", "runtimePlatform", "xrEnabled", "applicabilityPolicy",
))
PROVENANCE = ("unit", "source", "scope", "cadence", "aggregation")


class PackageError(ValueError):
    """Пакет не соответствует контракту нормализованных измерений."""


def _require(ok, message):
    if not ok:
        raise PackageError(message)


def _text(value):
    return isinstance(value, str) and bool(value.strip())


def _number(value, label, nonnegative=False):
    _require(type(value) in (int, float), f"{label}: требуется число")
    try:
        finite = math.isfinite(value)
    except OverflowError:
        finite = False
    _require(finite, f"{label}: NaN/Inf или непредставимое число недопустимы")
    _require(not nonnegative or value >= 0, f"{label}: отрицательное значение")
    return value


def _finite_json(value, label):
    if isinstance(value, float):
        _number(value, label)
    elif isinstance(value, dict):
        for key, item in value.items():
            _finite_json(item, f"{label}.{key}")
    elif isinstance(value, list):
        for index, item in enumerate(value):
            _finite_json(item, f"{label}[{index}]")


def _object(value, label):
    _require(isinstance(value, dict), f"{label}: требуется object")
    return value


def _list(value, label):
    _require(isinstance(value, list), f"{label}: требуется array")
    return value


def _json(path, raw=None):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            _require(key not in result, f"{path.name}: повторное поле {key}")
            result[key] = value
        return result

    try:
        content = path.read_text(encoding="utf-8-sig") if raw is None else raw.decode("utf-8-sig")
        value = json.loads(content, object_pairs_hook=unique)
    except (OSError, UnicodeError, ValueError) as exc:
        raise PackageError(f"Не удалось прочитать {path}: {exc}") from exc
    _finite_json(value, path.name)
    return _object(value, path.name)


def _schema(value, label):
    _object(value, label)
    _require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1,
             f"{label}: поддерживается только schemaVersion=1")


def _resolution(value):
    """Проверяет DTO размеров, сохраняя отличие mono/stereo от legacy-пары."""
    if _unknown(value):
        return

    def dimension(item, label):
        _require(type(item) is int and item > 0, f"{label}: требуется положительный int")
        _number(item, label)

    if isinstance(value, list):
        _require(len(value) == 2, "conditions.eyeResolution: legacy list содержит ровно width,height")
        for item in value:
            dimension(item, "conditions.eyeResolution")
        return
    _object(value, "conditions.eyeResolution")
    _require(set(value) <= {"mode", "width", "height", "eyes"},
             "conditions.eyeResolution: неизвестные поля DTO")
    _require(value.get("mode") in ("mono", "stereo"), "conditions.eyeResolution.mode: требуется mono/stereo")
    for key in ("width", "height"):
        dimension(value.get(key), f"conditions.eyeResolution.{key}")
    if "eyes" in value:
        eyes = _list(value["eyes"], "conditions.eyeResolution.eyes")
        _require(bool(eyes), "conditions.eyeResolution.eyes: требуется непустой список")
        for eye in eyes:
            _object(eye, "conditions.eyeResolution.eyes item")
            _require(set(eye) == {"width", "height"}, "conditions.eyeResolution.eyes: требуются width,height")
            for key in ("width", "height"):
                dimension(eye[key], f"conditions.eyeResolution.eyes.{key}")


def _manifest(value):
    _schema(value, "run.json")
    for key in ("runId", "variant"):
        _require(_text(value.get(key)), f"run.json.{key}: требуется непустая строка")
    _require(value.get("mode") in ("live", "instrumented", "replay"), "run.json.mode: неизвестный режим")
    source = _object(value.get("source"), "source")
    for key in ("kind", "path"):
        _require(_text(source.get(key)), f"source.{key}: требуется строка")
    _require(isinstance(source.get("sha256"), str) and
             re.fullmatch(r"[0-9a-fA-F]{64}", source["sha256"]) is not None,
             "source.sha256: требуется SHA256")
    conditions = _object(value.get("conditions"), "conditions")
    for key in ("deviceModel", "deviceId", "graphicsApi", "sceneId", "role", "viewId",
                "phaseId", "workloadId", "qualityHash"):
        if not _unknown(conditions.get(key)):
            _require(_text(conditions[key]), f"conditions.{key}: требуется строка/null")
    if not _unknown(conditions.get("developmentBuild")):
        _require(type(conditions["developmentBuild"]) is bool, "conditions.developmentBuild: требуется bool/null")
    for key in ("refreshHz", "ffr", "cpuLevel", "gpuLevel"):
        if not _unknown(conditions.get(key)):
            _number(conditions[key], f"conditions.{key}", nonnegative=True)
            if key == "refreshHz":
                _require(conditions[key] > 0, "conditions.refreshHz: требуется частота > 0")
    _resolution(conditions.get("eyeResolution"))
    metrics = _object(value.get("metrics"), "metrics")
    for metric_id, metric in metrics.items():
        _require(_text(metric_id), "metrics: пустой id")
        _object(metric, f"metrics.{metric_id}")
        for key in ("unit", "source", "scope"):
            _require(_text(metric.get(key)), f"metrics.{metric_id}.{key}: требуется строка")
        _require(metric.get("cadence") in ("frame", "interval"), f"{metric_id}: неизвестный cadence")
        _require(metric.get("aggregation") in ("exclusive", "inclusive", "overlap", "unknown"),
                 f"{metric_id}: неизвестная aggregation")
        if metric_id == "app_gpu_ms":
            _require((metric["unit"], metric["scope"], metric["cadence"]) == ("ms", "app", "frame"),
                     "app_gpu_ms: требуются ms/app/frame")
    _require("window" in value, "run.json.window: требуется поле, допускается null")
    window = value["window"]
    if window is not None:
        _object(window, "window")
        for key in ("start", "end", "uncertaintySeconds"):
            _number(window.get(key), f"window.{key}", nonnegative=key == "uncertaintySeconds")
        _require(window["start"] <= window["end"], "window: start > end")
        _require(_text(window.get("clock")), "window.clock: требуется объявленный clock")
        _require(type(window.get("verified")) is bool, "window.verified: требуется bool")
    for record in _list(value.get("summary"), "summary"):
        _object(record, "summary item")
        _require(record.get("metric") in metrics, "summary: незарегистрированная metric")
        _require(type(record.get("count")) is int and record["count"] >= 0, "summary.count: требуется int >= 0")
        for key in ("p50", "p95", "min", "max"):
            if record.get(key) is not None:
                _number(record[key], f"summary.{key}", nonnegative=True)
        for key in ("phaseId", "viewId"):
            _require(key in record and (record[key] is None or _text(record[key])), f"summary.{key}: требуется id/null")
        _ordered_stats(record, "summary")
    limitations = _list(value.get("limitations"), "limitations")
    _require(all(isinstance(item, str) for item in limitations), "limitations: требуются строки")


def _ordered_stats(record, label):
    previous = None
    for key in ("min", "p50", "p95", "max"):
        value = record.get(key)
        if value is not None:
            _require(previous is None or previous <= value, f"{label}: нарушен порядок статистик")
            previous = value


def _samples(path, metrics):
    result, previous = [], {}
    try:
        with path.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            _require(reader.fieldnames == ["metric", "start", "end", "value"], "samples.csv: неверный заголовок")
            for index, record in enumerate(reader, 2):
                label = f"samples.csv:{index}"
                _require(None not in record and None not in record.values(), f"{label}: неверное число колонок")
                metric = record["metric"]
                _require(metric in metrics, f"{label}: незарегистрированная metric {metric}")
                row = {"metric": metric}
                for key in ("start", "end", "value"):
                    try:
                        row[key] = float(record[key])
                    except (ValueError, TypeError) as exc:
                        raise PackageError(f"{label}.{key}: требуется число") from exc
                    _number(row[key], f"{label}.{key}", nonnegative=key == "value")
                _require(row["start"] <= row["end"], f"{label}: start > end")
                old = previous.get(metric)
                _require(old is None or (old[0] <= row["start"] and old[1] <= row["end"]),
                         f"{label}: неупорядоченные интервалы {metric}")
                previous[metric] = (row["start"], row["end"])
                result.append(row)
    except (OSError, UnicodeError, csv.Error) as exc:
        raise PackageError(f"Не удалось прочитать {path}: {exc}") from exc
    return result


def _local_path(root, raw, label="rawPath"):
    _require(_text(raw), f"{label}: требуется относительный локальный файл")
    windows, posix = PureWindowsPath(raw), PurePosixPath(raw)
    _require(not windows.drive and not windows.root and not posix.is_absolute() and
             ".." not in windows.parts and ".." not in posix.parts and ":" not in raw,
             f"{label} вне package: {raw}")
    resolved = (root / Path(*windows.parts)).resolve()
    _require(resolved.is_relative_to(root.resolve()), f"{label} вне package: {raw}")
    return resolved


def _source_path(root, raw):
    """Абсолютный legacy provenance показываем, но не читаем за пределами пакета."""
    windows, posix = PureWindowsPath(raw), PurePosixPath(raw)
    _require(".." not in windows.parts and ".." not in posix.parts,
             f"source.path: traversal недопустим: {raw}")
    if windows.is_absolute() or posix.is_absolute():
        return None
    return _local_path(root, raw, label="source.path")


def _source_integrity(root, source):
    result = {"status": "unverified", "path": source["path"],
              "expectedSha256": source["sha256"], "actualSha256": None, "reason": None}
    path = _source_path(root, source["path"])
    if path is None:
        result["reason"] = "source: legacy absolute path не проверяется; требуется относительный файл внутри package"
    elif not path.is_file():
        result["reason"] = "source: локальный evidence файл отсутствует"
    else:
        try:
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            result["actualSha256"] = digest.hexdigest()
        except OSError as exc:
            result["reason"] = f"source: evidence файл недоступен: {exc}"
        else:
            if result["actualSha256"] == source["sha256"].lower():
                result["status"] = "verified"
            else:
                result.update(status="mismatch", reason="source: SHA256 локального evidence не совпадает с manifest")
    return result


def _optional(root, filename, lists):
    path = root / filename
    if not path.exists():
        return None
    _require(path.resolve().is_relative_to(root.resolve()), f"{filename}: файл вне package")
    value = _json(path)
    _schema(value, filename)
    for key in lists:
        _list(value.get(key), f"{filename}.{key}")
    return value


def _identities(items, label, require_path=False):
    identities = set()
    for item in items:
        _object(item, label)
        identity = item.get("id")
        _require(_text(identity) and identity not in identities, f"{label}: пустой/повторный id")
        identities.add(identity)
        if require_path:
            _require(_text(item.get("path")), f"{label}.path: требуется строка")


def load_run(path: Path) -> dict:
    """Читает пакет целиком; ошибки входа не превращает в успешные измерения."""
    root = Path(path).resolve()
    _require(root.is_dir(), f"Каталог package отсутствует: {root}")
    for filename in ("run.json", "samples.csv"):
        _require((root / filename).resolve().is_relative_to(root), f"{filename}: файл вне package")
    manifest = _json(root / "run.json")
    _manifest(manifest)
    _source_path(root, manifest["source"]["path"])
    result = {"path": str(root), "manifest": manifest,
              "samples": _samples(root / "samples.csv", manifest["metrics"])}
    result["observations"] = _load_observations(root, manifest)
    for key, lists in (("scene", ("objects", "resources", "lights")), ("events", ("events",)),
                       ("attribution", ("groups", "observations")), ("capabilities", ()), ("quality", ())):
        result[key] = _optional(root, f"{key}.json", lists)
    if result["scene"]:
        for key in ("objects", "resources", "lights"):
            _identities(result["scene"][key], f"scene.{key}", require_path=True)
        for item in result["scene"]["objects"]:
            if "resourceIds" in item:
                _require(all(_text(identity) for identity in _list(item["resourceIds"], "scene.object.resourceIds")),
                         "scene.object.resourceIds: требуются ids")
    if result["events"]:
        _identities(result["events"]["events"], "events")
    if result["attribution"]:
        _identities(result["attribution"]["groups"], "attribution.groups")
        comparison_keys = set()
        for group in result["attribution"]["groups"]:
            if "comparisonKey" in group:
                key = group["comparisonKey"]
                _require(_text(key), "attribution.comparisonKey: требуется непустая строка")
                _require(key not in comparison_keys, f"attribution.comparisonKey: повторный ключ {key}")
                comparison_keys.add(key)
            for key in ("name", "scope", "unit", "metric", "source"):
                _require(_text(group.get(key)), f"attribution.{key}: требуется строка")
            _require(group.get("confidence") in ("exact", "group", "candidate", "unresolved"),
                     "attribution.confidence: неизвестное значение")
            if group.get("value") is not None:
                _number(group["value"], "attribution.value", nonnegative=True)
            for key in ("contributors", "resourceIds", "eventIds"):
                _require(all(_text(item) for item in _list(group.get(key), f"attribution.{key}")),
                         f"attribution.{key}: требуются ids")
            if group.get("rawPath") is not None:
                _local_path(root, group["rawPath"])
        for observation in result["attribution"]["observations"]:
            _object(observation, "attribution.observations item")
            for key in ("observation", "confidence", "nextExperiment", "owner"):
                _require(isinstance(observation.get(key), str), f"attribution.observations.{key}: требуется строка")
    return result


def _load_observations(root, manifest):
    definition = manifest.get("observations")
    if definition is None:
        return []
    _object(definition, "observations")
    _require(definition.get("timeMeaning") == "observation" and definition.get("sourceIntervalsKnown") is False,
             "observations: время чтения нельзя объявлять исходным GPU интервалом")
    _require(_text(definition.get("clock")) and type(definition.get("tickFrequency")) is int
             and definition["tickFrequency"] > 0, "observations: требуется clock/tickFrequency")
    _require(type(definition.get("localWindowClosed")) is bool and type(definition.get("sourceComplete")) is bool,
             "observations: требуется явная полнота source/local window")
    for key in ("localWindowSeconds", "minimumWindowSeconds"):
        if definition.get(key) is not None:
            _number(definition[key], "observations." + key, nonnegative=True)
    _require(_text(definition.get("path")), "observations.path: требуется относительный путь")
    path = _local_path(root, definition["path"], label="observations.path")
    _require(hashlib.sha256(path.read_bytes()).hexdigest() == definition.get("sha256"),
             "observations: SHA256 mismatch")
    rows, last = [], {}
    with path.open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream)
        _require(reader.fieldnames == ["metric", "observationTicks", "value", "sourceFrameId", "sourceStart", "sourceEnd"],
                 "observations.csv: неверный заголовок")
        for number, row in enumerate(reader, 2):
            metric = row["metric"]
            _require(metric in manifest["metrics"], f"observations.csv:{number}: неизвестная metric")
            _require(all(row[key] == "null" for key in ("sourceFrameId", "sourceStart", "sourceEnd")),
                     "observations: source frame/interval должны оставаться null")
            ticks = int(row["observationTicks"])
            _require(ticks >= 0 and ticks > last.get(metric, -1), "observations: неупорядоченные ticks")
            last[metric] = ticks
            value = _number(float(row["value"]), "observation.value", nonnegative=True)
            rows.append({"metric": metric, "observationTicks": ticks, "value": value})
    return rows


def _unknown(value):
    return value is None or (isinstance(value, str) and value.strip().lower() in ("", "unknown"))


def _percentile(values, quantile):
    if not values:
        return None
    position = (len(values) - 1) * quantile
    left = int(position)
    right = min(left + 1, len(values) - 1)
    return values[left] + (values[right] - values[left]) * (position - left)


def _not_applicable(run, key):
    """Только явно записанная Windows mono policy; Quest unknown не превращается в N/A."""
    conditions = run.get("conditions") or {}
    evidence = (run.get("quality") or {}).get("conditionEvidence") or {}
    item = evidence.get(key) or {}
    resolution = conditions.get("eyeResolution")
    return (key in ("ffr", "cpuLevel", "gpuLevel") and conditions.get(key) is None
            and conditions.get("applicabilityPolicy") == "vrb-gpu-platform-v1"
            and conditions.get("runtimePlatform") == "WindowsPlayer" and conditions.get("xrEnabled") is False
            and isinstance(resolution, dict) and resolution.get("mode") == "mono"
            and item.get("value") is None and item.get("source") == "runtime-observation"
            and item.get("reason") == "not-applicable: rendered Windows client with XR disabled")


def _attribution(run, reasons):
    value = copy.deepcopy(run.get("attribution"))
    if value is None:
        return None
    scene = run.get("scene") or {}
    known = {
        "contributors": {item["id"] for item in scene.get("objects", [])},
        "resourceIds": {item["id"] for item in scene.get("resources", [])},
        "eventIds": {item["id"] for item in (run.get("events") or {}).get("events", [])},
    }
    for group in value["groups"]:
        missing = [item for key, ids in known.items() for item in group[key] if item not in ids]
        if group["metric"] not in run["manifest"]["metrics"]:
            missing.append(group["metric"])
        if missing:
            group["confidence"] = "unresolved"
            group["unresolvedIds"] = missing
            reasons.append(f"Группа {group['id']}: неизвестные IDs, связь unresolved")
        raw = group.get("rawPath")
        group["rawAvailable"] = raw is not None and _local_path(Path(run["path"]), raw).is_file()
        if raw and not group["rawAvailable"]:
            reasons.append(f"Группа {group['id']}: raw-файл отсутствует")
    return value


def analyze_run(run: dict) -> dict:
    """Анализирует проверенный load_run пакет, сохраняя ограничения доказательств."""
    _object(run, "run")
    manifest = _object(run.get("manifest"), "run.manifest")
    _manifest(manifest)
    conditions = copy.deepcopy(manifest["conditions"])
    reasons, limited, invalid = list(manifest["limitations"]), False, False
    if reasons:
        limited = True
    source_integrity = _source_integrity(Path(run["path"]), manifest["source"])
    if source_integrity["reason"]:
        reasons.append(source_integrity["reason"])
    if source_integrity["status"] == "mismatch":
        invalid = True
    elif source_integrity["status"] != "verified":
        limited = True
    missing = [key for key in CONDITIONS if _unknown(conditions.get(key)) and not _not_applicable(run, key)]
    if missing:
        reasons.append("Неизвестные условия: " + ", ".join(missing))
        limited = True
    if isinstance(conditions.get("eyeResolution"), list):
        reasons.append("eyeResolution: legacy размеры, нет mono/stereo mode")
        limited = True
    window = manifest["window"]
    if window is None or not window["verified"]:
        reasons.append("Нет подтверждённого окна/clock измерения")
        limited = True
    metrics = {}
    has_observations = False
    for metric_id, provenance in manifest["metrics"].items():
        rows = [row for row in run["samples"] if row["metric"] == metric_id]
        if window is not None:
            uncertainty = window["uncertaintySeconds"]
            outside = [row for row in rows if row["start"] - uncertainty < window["start"] or
                       row["end"] + uncertainty > window["end"]]
            if outside:
                reasons.append(f"{metric_id}: {len(outside)} samples вне window с учётом uncertainty")
                invalid = True
        excluded = [row for row in rows if metric_id == "app_gpu_ms" and row["value"] == 0]
        samples = [{key: row[key] for key in ("start", "end", "value")} for row in rows
                   if not (metric_id == "app_gpu_ms" and row["value"] == 0)]
        if excluded:
            reasons.append(f"{metric_id}: исключено {len(excluded)} нулевых значений; 0 не подтверждает замер")
            limited = True
        values = sorted(row["value"] for row in samples)
        metric = dict(provenance, count=len(values), p50=_percentile(values, 0.5),
                      p95=_percentile(values, 0.95), min=min(values) if values else None,
                      max=max(values) if values else None, samples=samples, rawCount=len(samples),
                      excludedCount=len(excluded), sourceSummaryOnly=False,
                      availability="raw" if values else "unavailable")
        summaries = [item for item in manifest["summary"] if item["metric"] == metric_id and
                     item["phaseId"] == conditions.get("phaseId") and item["viewId"] == conditions.get("viewId")]
        metric["sourceSummary"] = copy.deepcopy(summaries)
        # Нулевой реальный замер нельзя заменить более красивым старым summary.
        if not rows and len(summaries) == 1:
            summary = summaries[0]
            if summary["count"] > 0:
                for key in ("count", "p50", "p95", "min", "max"):
                    metric[key] = summary.get(key)
                metric["sourceSummaryOnly"] = True
                metric["availability"] = "summaryOnly"
                if metric_id == "app_gpu_ms" and metric["p50"] == 0:
                    metric.update(p50=None, p95=None, min=None, max=None, availability="unavailable")
                    reasons.append("app_gpu_ms: summary с p50=0 не подтверждает замер")
                reasons.append(f"{metric_id}: только source summary; raw samples отсутствуют")
                limited = True
        elif not rows and len(summaries) > 1:
            reasons.append(f"{metric_id}: неоднозначные summaries не объединены")
            limited = True
        elif not samples:
            reasons.append(f"{metric_id}: измерения недоступны")
            limited = True
        metrics[metric_id] = metric
        observations = [row for row in run.get("observations", []) if row["metric"] == metric_id]
        observed_values = sorted(row["value"] for row in observations if not
                                 (metric_id in ("app_gpu_ms", "unity_gpu_ms", "compositor_gpu_ms") and row["value"] == 0))
        if observations:
            has_observations = has_observations or bool(observed_values)
            limited = True
            metric["observationDistribution"] = {
                "count": len(observed_values), "p50": _percentile(observed_values, .5), "p95": _percentile(observed_values, .95),
                "min": min(observed_values) if observed_values else None, "max": max(observed_values) if observed_values else None,
                "countMeaning": "API polls, not unique source frames", "timeMeaning": "observation",
                "uniqueSourceFrameCount": None, "sourceIntervalsKnown": False,
                "localReadyWindow": bool(manifest["observations"].get("readyEventId")) and
                                    manifest["observations"].get("sourceComplete") is True and
                                    manifest["observations"].get("localWindowClosed") is True and
                                    type(manifest["observations"].get("minimumWindowSeconds")) in (int, float) and
                                    type(manifest["observations"].get("localWindowSeconds")) in (int, float) and
                                    manifest["observations"]["localWindowSeconds"] >= manifest["observations"]["minimumWindowSeconds"],
                "excludedZeroCount": len(observations) - len(observed_values)}
            reasons.append(f"{metric_id}: распределение API observations; source frame/interval/latency неизвестны")
    if not has_observations and not any(metric["count"] and metric["p50"] is not None for metric in metrics.values()):
        reasons.append("Нет доступных raw или подходящих summary данных")
        invalid = True
    attribution = _attribution(run, reasons)
    if attribution and any(group["confidence"] == "unresolved" or
                           (group.get("rawPath") and not group["rawAvailable"])
                           for group in attribution["groups"]):
        limited = True
    return {"runId": manifest["runId"], "variant": manifest["variant"], "mode": manifest["mode"],
            "conditions": conditions, "status": "invalid" if invalid else "limited" if limited else "valid",
            "reasons": reasons, "metrics": metrics, "scene": copy.deepcopy(run.get("scene")),
            "attribution": attribution, "path": run["path"], "window": copy.deepcopy(window),
            "source": copy.deepcopy(manifest["source"]),
            "sourceIntegrity": source_integrity,
            "sourceSummaryOnly": any(item["sourceSummaryOnly"] for item in metrics.values()),
            "capabilities": copy.deepcopy(run.get("capabilities")), "quality": copy.deepcopy(run.get("quality")),
            "events": copy.deepcopy(run.get("events")),
            "availability": {key: run.get(key) is not None for key in
                             ("scene", "events", "attribution", "capabilities", "quality")}}


def compare_runs(a: dict, b: dict, allow_conditions=()) -> dict:
    """Сравнивает p50 одной пары; не утверждает значимость и не суммирует группы."""
    allowed = sorted(set(allow_conditions))
    _require(all(_text(key) for key in allowed), "allow_conditions: требуются имена условий")
    _require(not FIXED_CONDITIONS.intersection(allowed),
             "allow_conditions: нельзя обходить identity/API/developmentBuild/role/view/phase/mode")
    left, right = analyze_run(a), analyze_run(b)
    reasons = ["Одна пара: нет A/A noise / повторов; статистическая значимость не установлена"]
    differences, metrics, observation_metrics = {}, {}, {}
    invalid = left["status"] == "invalid" or right["status"] == "invalid"
    limited = left["status"] == "limited" or right["status"] == "limited"
    incomparable = False
    if left["runId"] == right["runId"] or Path(left["path"]).resolve() == Path(right["path"]).resolve():
        reasons.append("Повторное использование одного прогона/path/runId: это не независимая пара A/B")
        incomparable = True
    for side, result in (("A", left), ("B", right)):
        reasons.extend(f"{side}: {reason}" for reason in result["reasons"])
    if left["mode"] != right["mode"]:
        differences["mode"] = {"a": left["mode"], "b": right["mode"], "allowed": False}
        reasons.append("Несовпадение mode")
        incomparable = True
    for key in sorted(set(CONDITIONS) | set(left["conditions"]) | set(right["conditions"])):
        av, bv = left["conditions"].get(key), right["conditions"].get(key)
        if _not_applicable(left, key) and _not_applicable(right, key):
            continue
        if _unknown(av) or _unknown(bv):
            differences[key] = {"a": av, "b": bv, "allowed": key in allowed, "known": False}
            reasons.append(f"Условие {key}: неизвестно, сопоставимость не подтверждена")
            limited = True
        elif av != bv:
            differences[key] = {"a": av, "b": bv, "allowed": key in allowed, "known": True}
            if key in allowed:
                reasons.append(f"Явно разрешено изменение условия {key}")
            else:
                reasons.append(f"Несовпадение условия {key}")
                incomparable = True
    for metric_id in sorted(set(left["metrics"]) | set(right["metrics"])):
        am, bm = left["metrics"].get(metric_id), right["metrics"].get(metric_id)
        definition = am or bm
        mismatch = am is not None and bm is not None and any(am[key] != bm[key] for key in PROVENANCE)
        if mismatch:
            incomparable = True
            reasons.append(f"{metric_id}: несовпадение provenance (unit/source/scope/cadence/aggregation)")
        av, bv = am["p50"] if am else None, bm["p50"] if bm else None
        if av is None or bv is None:
            limited = True
            reasons.append(f"{metric_id}: данные отсутствуют у одной из сторон")
        delta = bv - av if av is not None and bv is not None and not mismatch else None
        percentage = 100 * (delta / av) if delta is not None and av != 0 else None
        if percentage is not None and not math.isfinite(percentage):
            percentage = None
            limited = True
            reasons.append(f"{metric_id}: deltaPercent не представима конечным числом")
        metrics[metric_id] = {"a": av, "b": bv, "delta": delta,
            "deltaPercent": percentage,
            "unit": definition["unit"], "scope": definition["scope"],
            "source": definition["source"], "cadence": definition["cadence"],
            "aggregation": definition["aggregation"]}
        ao = am.get("observationDistribution") if am else None
        bo = bm.get("observationDistribution") if bm else None
        if ao is not None or bo is not None:
            ap = ao.get("p50") if ao else None
            bp = bo.get("p50") if bo else None
            observation_metrics[metric_id] = {
                "a": ao, "b": bo, "deltaP50": bp - ap if ap is not None and bp is not None and not mismatch and
                    ao.get("localReadyWindow") is True and bo.get("localReadyWindow") is True else None,
                "unit": definition["unit"], "source": definition["source"], "scope": definition["scope"],
                "evidence": "observationDistribution", "sourceIntervalsKnown": False,
                "reason": "API polling distribution; not verified source-frame GPU timing"}
    status = "invalid" if invalid else "incomparable" if incomparable else "limited" if limited else "comparable"
    if status in ("invalid", "incomparable"):
        # Паспорт/числа источников остаются видимыми, недостоверной дельты нет.
        for metric in metrics.values():
            metric.update(delta=None, deltaPercent=None)
        for metric in observation_metrics.values():
            metric["deltaP50"] = None
    return {"schemaVersion": 1, "status": status, "reasons": reasons,
            "conditionsDiff": differences, "allowedConditions": allowed, "a": left, "b": right,
            "metrics": metrics, "observationMetrics": observation_metrics, "quest2": "notMeasured"}


def _legacy_metric(key):
    aliases = {"ovr_gpu": "app_gpu_ms", "ovr_comp_gpu": "compositor_gpu_ms", "cpu": "cpu_ms"}
    metric_id = aliases.get(key, f"legacy_{key}")
    if key.endswith("_util"):
        unit = "%"
    elif key.endswith("_lvl"):
        unit = "level"
    elif key.endswith("_mb"):
        unit = "MiB"
    elif key == "tris_k":
        unit = "kTriangles"
    elif key == "setpass":
        unit = "count"
    elif key in ("dt", "cpu", "cpu_main", "cpu_render", "gpu", "ovr_cpu", "ovr_gpu", "ovr_comp_gpu",
                 "uxr", "uxr_update", "uxr_track", "uxr_manip", "uxr_anim", "uxr_post", "gc_collect",
                 "present_wait", "physics", "scripts", "scripts_late", "anim", "anim_end", "skinning",
                 "particles", "render"):
        unit = "ms"
    else:
        unit = "unknown"
    scope = "compositor" if key == "ovr_comp_gpu" else "device" if key.endswith(("_util", "_lvl")) else "app"
    return metric_id, {"unit": unit, "source": f"legacy-summary-v1:PerfFrameRecorder.{key}",
        "scope": scope, "cadence": "frame", "aggregation": "unknown"}


def import_summary(source: Path, out: Path) -> list[Path]:
    """Импортирует каждую legacy-фазу отдельно, без восстановления мнимых кадров."""
    source, out = Path(source).resolve(), Path(out).resolve()
    try:
        source_bytes = source.read_bytes()
    except OSError as exc:
        raise PackageError(f"Не удалось прочитать {source}: {exc}") from exc
    data = _json(source, raw=source_bytes)
    phases = _list(data.get("phases"), "legacy.phases")
    _require(bool(phases), "legacy.phases: нет фаз")
    digest = hashlib.sha256(source_bytes).hexdigest()
    prepared = []
    run_id = data.get("startedAt") if _text(data.get("startedAt")) else source.stem
    for index, phase in enumerate(phases):
        _object(phase, "legacy.phase")
        phase_id = phase.get("name")
        _require(_text(phase_id), "legacy.phase.name: требуется имя фазы")
        conditions = dict.fromkeys(CONDITIONS)
        for target, key in (("deviceModel", "device"), ("developmentBuild", "developmentBuild"),
                            ("sceneId", "scene"), ("role", "role"), ("refreshHz", "refreshRate")):
            conditions[target] = data.get(key)
        conditions["phaseId"] = phase_id
        # Читаем только явно сохранённые условия, без вывода из среднего уровня/имени пути.
        for key in CONDITIONS:
            if key in data:
                conditions[key] = data[key]
            if key in phase:
                conditions[key] = phase[key]
        metrics, summaries = {}, []
        for record in _list(phase.get("metrics"), "legacy.phase.metrics"):
            _object(record, "legacy.metric")
            key = record.get("key")
            _require(_text(key), "legacy.metric.key: требуется имя")
            metric_id, definition = _legacy_metric(key)
            _require(metric_id not in metrics, f"legacy: повторная metric {key}")
            metrics[metric_id] = definition
            stats = _object(record.get("summary"), "legacy.metric.summary")
            summaries.append({"metric": metric_id, "count": stats.get("count"), "p50": stats.get("p50"),
                "p95": stats.get("p95"), "min": stats.get("min"), "max": stats.get("max"),
                "phaseId": conditions["phaseId"], "viewId": conditions["viewId"]})
        manifest = {"schemaVersion": 1, "runId": f"{run_id}:phase-{index + 1}",
            "variant": "legacy-summary", "mode": "live",
            "source": {"kind": "legacy-summary-v1", "path": "raw/summary.json", "sha256": digest,
                       "originalPath": str(source)},
            "conditions": conditions, "metrics": metrics, "window": None, "summary": summaries,
            "limitations": ["Только legacy summary, без raw samples; sourceSummaryOnly=true",
                "Clock, окно и покадровая свежесть исходных значений не подтверждены",
                "mode=live — классификация legacy StressTest; detailed/instrumentation status неизвестен"]}
        _manifest(manifest)
        # Нумерация не зависит от пользовательского имени и безопасна на Windows.
        destination = out / f"phase-{index + 1:02d}"
        _require(not destination.exists(), f"Импорт не перезаписывает существующий каталог: {destination}")
        prepared.append((destination, manifest))
    # Валидация всех фаз предшествует записи первой; исходный файл не изменяется.
    paths = []
    try:
        for destination, manifest in prepared:
            destination.mkdir(parents=True)
            (destination / "raw").mkdir()
            (destination / "raw" / "summary.json").write_bytes(source_bytes)
            (destination / "run.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2,
                                                            allow_nan=False) + "\n", encoding="utf-8", newline="\n")
            (destination / "samples.csv").write_text("metric,start,end,value\n", encoding="utf-8", newline="\n")
            paths.append(destination)
    except OSError as exc:
        raise PackageError(f"Не удалось сохранить импорт: {exc}") from exc
    return paths

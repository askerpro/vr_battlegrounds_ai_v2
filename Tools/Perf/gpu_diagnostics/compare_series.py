"""Сравнение независимых прогонов; тысячи API polls не заменяют повторные A/B."""
import copy
import json
from collections import defaultdict
from datetime import datetime

from .model import _percentile, _require, compare_runs, load_run


def _changes(a, b, pointer=""):
    if isinstance(a, dict) and isinstance(b, dict):
        result = []
        for key in sorted(set(a) | set(b)):
            escaped = key.replace("~", "~0").replace("/", "~1")
            result.extend(_changes(a.get(key), b.get(key), pointer + "/" + escaped))
        return result
    return [] if a == b else [{"path": pointer, "a": a, "b": b}]


def _primary(run):
    snapshot = copy.deepcopy((run.get("quality") or {}).get("snapshot"))
    if isinstance(snapshot, dict) and isinstance(snapshot.get("sceneDetail"), dict):
        # Эти поля — следствие применения правила, не дополнительные параметры сценария.
        snapshot["sceneDetail"].pop("groups", None)
        snapshot["sceneDetail"].pop("rendererEnabled", None)
    return snapshot


def _started(run):
    value = run["manifest"].get("startedUtc")
    if not isinstance(value, str):
        return None
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return result if result.tzinfo is not None else None


def compare_series(pairs, aa, allowed_settings=("/sceneDetail/farStart",)):
    """Пары содержат a/b Path и order; результаты группируются по виду/фазе/устройству."""
    _require(isinstance(pairs, list) and pairs and isinstance(aa, list), "series: требуются непустые pairs и массив aa")
    _require(isinstance(allowed_settings, (list, tuple)) and all(isinstance(path, str) and path.startswith("/") for path in allowed_settings),
             "allowedSettings: требуются JSON pointers")
    groups, receipts = defaultdict(lambda: {"ab": [], "aa": [], "roots": set(), "reused": False}), []
    for category, collection in (("ab", pairs), ("aa", aa)):
        for pair in collection:
            left, right = load_run(pair["a"]), load_run(pair["b"])
            result = compare_runs(left, right, ("qualityHash",) if category == "ab" else ())
            reasons = []
            a_settings, b_settings = _primary(left), _primary(right)
            settings_diff = _changes(a_settings, b_settings)
            paths = {row["path"] for row in settings_diff}
            if a_settings is None or b_settings is None:
                reasons.append("Нет actual quality snapshots")
            if category == "ab" and paths != set(allowed_settings):
                reasons.append("Actual settings diff не равен единственной разрешённой правке")
            if category == "aa" and paths:
                reasons.append("A/A имеет разные actual settings")
            for key in ("sourceSha", "buildManifestSha256"):
                av = left["manifest"].get("provenance", {}).get(key)
                bv = right["manifest"].get("provenance", {}).get(key)
                if av is None or bv is None or av != bv:
                    reasons.append(key + ": для LOD-only требуется одна подтверждённая сборка")
            at, bt = _started(left), _started(right)
            order = "AB" if at is not None and bt is not None and at < bt else "BA" if at is not None and bt is not None and bt < at else "unknown"
            if pair.get("order") not in ("AB", "BA") or order != pair.get("order"):
                reasons.append("Declared order не подтверждён startedUtc процессов")
            roots = [run["manifest"].get("parentRunId", run["manifest"]["runId"]) for run in (left, right)]
            if roots[0] == roots[1]:
                reasons.append("Обе стороны относятся к одному parent run")
            if result["status"] in ("invalid", "incomparable"):
                reasons.append("Пара не сопоставима: " + result["status"])
            receipt = {"kind": category, "a": str(pair["a"]), "b": str(pair["b"]), "order": order,
                       "eligible": not reasons, "reasons": reasons, "actualSettingsDiff": settings_diff,
                       "comparisonStatus": result["status"]}
            receipts.append(receipt)
            if reasons:
                continue
            identity = {key: result["a"]["conditions"].get(key) for key in
                        ("deviceId", "deviceModel", "graphicsApi", "developmentBuild", "role", "viewId", "phaseId", "workloadId")}
            # Пары с разными диагностическими вариантами рендера не смешиваются в одной группе.
            identity["renderDiagnostic"] = [result[side]["conditions"].get("renderDiagnostic") for side in ("a", "b")]
            for basis, metrics, delta_key in (("sourceSamples", result["metrics"], "delta"),
                                               ("apiObservations", result.get("observationMetrics", {}), "deltaP50")):
                for metric_id, metric in metrics.items():
                    delta = metric.get(delta_key)
                    if delta is None:
                        continue
                    key = json.dumps({"identity": identity, "basis": basis, "metric": metric_id,
                                      "unit": metric["unit"], "scope": metric["scope"], "source": metric["source"]}, sort_keys=True)
                    group = groups[key]
                    # Один run участвует один раз в данном виде/фазе/метрике, включая A/A.
                    if group["roots"].intersection(roots):
                        group["reused"] = True
                    group["roots"].update(roots)
                    group[category].append({"delta": delta, "order": order, "roots": roots,
                                            "aPrimarySettings": a_settings, "bPrimarySettings": b_settings})
    output = []
    for key, group in sorted(groups.items()):
        ab = group["ab"]
        deltas = sorted(row["delta"] for row in ab)
        noise = sorted(abs(row["delta"]) for row in group["aa"])
        orders = {row["order"] for row in ab}
        settings_consistent = bool(ab) and all(row["aPrimarySettings"] == ab[0]["aPrimarySettings"] and
                                               row["bPrimarySettings"] == ab[0]["bPrimarySettings"] for row in ab)
        repeated = len(ab) >= 3 and orders == {"AB", "BA"} and bool(noise) and not group["reused"] and settings_consistent
        median = _percentile(deltas, .5)
        output.append({**json.loads(key), "pairedRunCount": len(ab), "aaPairCount": len(noise),
                       "counterbalanced": orders == {"AB", "BA"}, "reusedParentRun": group["reused"],
                       "actualSettingsConsistent": settings_consistent, "repetitionCriteriaMet": repeated,
                       "pairedDeltas": deltas, "medianDelta": median, "p95PairedDelta": _percentile(deltas, .95),
                       "aaAbsoluteDeltas": noise, "maxObservedAaDelta": max(noise) if noise else None,
                       "exceedsObservedAaRange": abs(median) > max(noise) if repeated and median is not None else None,
                       "significance": "not established; A/A range is a diagnostic heuristic"})
    return {"schemaVersion": 1, "status": "limited", "allowedSettings": list(allowed_settings),
            "pairs": receipts, "groups": output, "quest2": "notMeasured",
            "reason": "Between-run evidence; API observations retain unknown source frame/interval; GPU/scene acceptance separate"}

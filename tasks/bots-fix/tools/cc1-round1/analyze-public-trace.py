"""Отдельная offline-проекция публичных Blaze фактов; исторические verdict не читаются.

Запуск: python -I analyze-public-trace.py ACTUAL_REPORTS_ROOT --output NEW.json
Выход создаётся исключительно в существующем каталоге, без перезаписи.
"""

import argparse
import hashlib
import json
import math
from collections import Counter, defaultdict
from pathlib import Path


MOVEMENT = {"Move", "Back", "Left", "Right"}
METHODS = {"Play", "MoveTo", "Path", "NonRoot", "SetState"}
SUBJECT_COLUMNS = {"subjectBodyId", "subjectRoot", "subjectFeet", "subjectCamera"}
THRESHOLD_M = 0.02


def numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def vector(value):
    return isinstance(value, list) and len(value) == 3 and all(numeric(v) for v in value)


def xz(a, b):
    return math.hypot(a[0] - b[0], a[2] - b[2]) if vector(a) and vector(b) else None


def identity(event):
    return tuple(event.get(k) for k in ("OwnerBodyId", "ProxyId", "ProxyGeneration"))


def point(event):
    sample = event.get("sample") or {}
    return {"sequence": event.get("Sequence"), "frame": sample.get("Frame"),
            "gameTime": sample.get("Time"), "position": sample.get("position"),
            "state": sample.get("state"), "currentHash": sample.get("CurrentHash"),
            "currentNt": sample.get("CurrentNt"), "nextHash": sample.get("NextHash"),
            "nextNt": sample.get("NextNt"), "previousRequestedAnimation": sample.get("PreviousRequestedAnimation")}


def read_json(path):
    data = path.read_bytes()
    return json.loads(data.decode("utf-8-sig")), {"path": str(path.resolve()), "sha256": hashlib.sha256(data).hexdigest()}


def subject_samples(rows):
    valid = [r for r in rows if r.get("subjectBodyId") is not None and
             all(vector(r.get(k)) for k in ("subjectRoot", "subjectFeet", "subjectCamera"))]
    result = {"status": "UNKNOWN", "validFrames": len(valid),
              "meaning": "subjectFeet — BotBody.Feet, не bone feet; только сохранённые post-UXR кадры с proxy"}
    if not valid:
        result["reason"] = "Нет совместных subjectBodyId/root/feet/camera; target trace не используется как subject."
        return result
    result["status"] = "MEASURED_SAMPLES_ONLY"
    result["first"] = {k: valid[0].get(k) for k in ("frame", "subjectBodyId", "subjectRoot", "subjectFeet", "subjectCamera")}
    result["last"] = {k: valid[-1].get(k) for k in ("frame", "subjectBodyId", "subjectRoot", "subjectFeet", "subjectCamera")}
    same_body = [r for r in valid if r["subjectBodyId"] == valid[0]["subjectBodyId"]]
    result["maxRootXZFromFirstSampleM"] = max(xz(r["subjectRoot"], valid[0]["subjectRoot"]) for r in same_body)
    result["maxFeetXZFromFirstSampleM"] = max(xz(r["subjectFeet"], valid[0]["subjectFeet"]) for r in same_body)
    result["bodyIds"] = sorted({r["subjectBodyId"] for r in valid})
    result["causalTransferFromProxy"] = "UNKNOWN: sampled displacement не доказывает авторство/порядок writers тела."
    return result


def first_crossing(request, events, rows, frame_times):
    """Ищет endpoint >2cm от position при запросе, а не любой следующий ненулевой шаг."""
    base = request.get("sample") or {}
    origin = base.get("position")
    result = {"thresholdMetersStrictlyGreaterThan": THRESHOLD_M, "positionAtRequest": origin,
              "latency": "UNKNOWN", "latencyReason": "Snapshot фиксирует endpoint; время начала физического движения не измерено.",
              "publicCallback": None, "postUXRProxy": None}
    if not vector(origin):
        result["reason"] = "Нет валидного positionAtRequest."
        return result
    for event in events:
        distance = xz(origin, (event.get("sample") or {}).get("position"))
        if distance is not None and distance > THRESHOLD_M:
            result["publicCallback"] = {**point(event), "kind": event.get("kind"), "callId": event.get("CallId"),
                                        "distanceFromRequestXZ_M": distance}
            break
    for row in rows:
        distance = xz(origin, [row.get("x"), 0, row.get("z")])
        if distance is not None and distance > THRESHOLD_M:
            result["postUXRProxy"] = {"frame": row.get("frame"), "traceT": row.get("t"),
                                      "positionXZ": [row.get("x"), row.get("z")], "distanceFromRequestXZ_M": distance,
                                      "sameFramePublicGameTime": frame_times.get(row.get("frame"))}
            break
    result["evidence"] = "MEASURED_PROXY_THRESHOLD_CROSSING" if result["publicCallback"] or result["postUXRProxy"] else "UNKNOWN_NO_THRESHOLD_CROSSING_OBSERVED"
    return result


def project_proxy(events, rows):
    reasons, pairs, missing = [], [], []
    pending = {}
    kinds = Counter(e.get("kind") for e in events)
    starts = [e for e in events if e.get("kind") == "ProxyStarted"]
    if any(value is None for value in identity(events[0])):
        reasons.append("proxy identity incomplete")
    if len(starts) != 1:
        reasons.append("ProxyStarted count != 1")
    if kinds["CoverageLost"]:
        reasons.append("CoverageLost")
    sequences = [e.get("Sequence") for e in events]
    if any(not isinstance(v, int) for v in sequences) or any(b != a + 1 for a, b in zip(sequences, sequences[1:])):
        reasons.append("Sequence отсутствует/непрерывен не на всём recorded interval")
    plays = []
    for event in events:
        kind = event.get("kind", "")
        suffix = "Before" if kind.endswith("Before") else "After" if kind.endswith("After") else None
        method = kind[:-len(suffix)] if suffix else None
        if method not in METHODS:
            continue
        key = (method, event.get("CallId"))
        if suffix == "Before":
            if key in pending:
                missing.append({"method": method, "callId": key[1], "reason": "duplicateBefore"})
            pending[key] = event
            if method == "Play":
                plays.append(event)
            continue
        before = pending.pop(key, None)
        if before is None:
            missing.append({"method": method, "callId": key[1], "reason": "missingBefore"})
            continue
        sample = before.get("sample") or {}
        call = before.get("call") or {}
        pair = {"method": method, "callId": key[1], "before": point(before), "after": point(event),
                "call": call, "result": event.get("Result"), "caller": before.get("Caller"),
                "publicCallbackXZ_M": xz(sample.get("position"), (event.get("sample") or {}).get("position"))}
        if method == "Play":
            pair["playClass"] = ("sameRequestNoOp" if call.get("Animation") == sample.get("PreviousRequestedAnimation")
                                 else "overplay" if call.get("Overplay") else "ordinaryChange")
        pairs.append(pair)
    missing.extend({"method": method, "callId": call_id, "reason": "missingAfter"} for method, call_id in pending)
    if missing:
        reasons.append("missing/duplicate before-after pair")
    frame_times = {(e.get("sample") or {}).get("Frame"): (e.get("sample") or {}).get("Time") for e in events}
    frame_list = [r.get("frame") for r in rows if isinstance(r.get("frame"), int)]
    if rows and len(frame_list) != len(rows):
        reasons.append("invalid trace frame")
    # Строка без proxy ID не присваивается другому поколению по приблизительному времени.
    first_frame = (events[0].get("sample") or {}).get("Frame") if events else None
    last_frame = (events[-1].get("sample") or {}).get("Frame") if events else None
    own_rows = [r for r in rows if isinstance(r.get("frame"), int) and isinstance(first_frame, int) and
                isinstance(last_frame, int) and first_frame <= r["frame"] <= last_frame]
    moving = [p for p in plays if (p.get("call") or {}).get("Animation") in MOVEMENT]
    first = moving[0] if moving else None
    episodes = []
    for index, play in enumerate(plays):
        call = play.get("call") or {}
        if call.get("Animation") not in MOVEMENT or call.get("Animation") == (play.get("sample") or {}).get("PreviousRequestedAnimation"):
            continue
        end = next((p for p in plays[index + 1:] if (p.get("call") or {}).get("Animation") != call.get("Animation")), None)
        start_seq, start_frame = play.get("Sequence"), (play.get("sample") or {}).get("Frame")
        end_seq = end.get("Sequence") if end else math.inf
        end_frame = (end.get("sample") or {}).get("Frame") if end else math.inf
        if not all(numeric(v) for v in (start_seq, start_frame)) or (end is not None and not all(numeric(v) for v in (end_seq, end_frame))):
            reasons.append("episode coordinate missing")
            continue
        window_events = [e for e in events if numeric(e.get("Sequence")) and start_seq <= e["Sequence"] < end_seq]
        window_rows = [r for r in own_rows if start_frame <= r["frame"] < end_frame]
        episodes.append({"requestCallId": play.get("CallId"), "animation": call.get("Animation"), "request": point(play),
                         "endedByDifferentRequest": {"callId": end.get("CallId"), "animation": (end.get("call") or {}).get("Animation"),
                                                     **point(end)} if end else None,
                         "stepEvidence": first_crossing(play, window_events, window_rows, frame_times),
                         "subject": subject_samples(window_rows),
                         "stateChanges": [{"callId": p["callId"], "before": p["before"], "after": p["after"],
                                           "requestedState": p["call"].get("requestedState"), "caller": p["caller"]}
                                          for p in pairs if p["method"] == "SetState" and numeric(p["before"]["sequence"]) and start_seq <= p["before"]["sequence"] < end_seq]})
    first_idle = next((p for p in plays if (p.get("call") or {}).get("Animation") == "Idle"), None)
    first_trace = own_rows[0] if own_rows else None
    return {"identity": dict(zip(("ownerBodyId", "proxyId", "generation"), identity(events[0]))),
            "coverageReasons": sorted(set(reasons)), "eventKinds": dict(kinds), "pairCount": len(pairs), "missingPairs": missing,
            "playClasses": dict(Counter(p["playClass"] for p in pairs if "playClass" in p)),
            "initialAnimatorAtProxyStarted": point(starts[0]) if starts else None,
            "initialAnimatorNameAtProxyStarted": "UNKNOWN: raw содержит hash, без таблицы hash→имя; initial state не explicit Play.",
            "firstNamedAnimatorTraceObservation": first_trace,
            "firstExplicitPlayIdle": {"callId": first_idle.get("CallId"), **point(first_idle)} if first_idle else None,
            "firstMovementRequest": {"callId": first.get("CallId"), "animation": (first.get("call") or {}).get("Animation"), **point(first)} if first else None,
            "firstDisplacementAfterFirstRequest": first_crossing(first, [e for e in events if e.get("Sequence", -1) >= first.get("Sequence", 0)],
                                                                 [r for r in own_rows if r["frame"] >= first["sample"]["Frame"]], frame_times) if first else None,
            "firstDisplacementAttribution": "UNKNOWN: поиск после первого запроса допускает последующие superseding requests; не причинная latency.",
            "traceWindow": {"rows": len(own_rows), "firstFrame": min(frame_list) if frame_list else None,
                            "lastFrame": max(frame_list) if frame_list else None, "wholeLifecycle": "UNKNOWN"},
            "subject": subject_samples(own_rows), "movementEpisodes": episodes, "publicCalls": pairs}


def project_file(path):
    data, source = read_json(path)
    trace_path = path.with_name("cc1-trace.json")
    rows, sources, reasons = [], [source], []
    if trace_path.exists():
        trace, trace_source = read_json(trace_path)
        sources.append(trace_source)
        columns = trace.get("columns", [])
        for values in trace.get("rows", []):
            if not isinstance(values, list) or len(values) != len(columns):
                reasons.append("trace row/columns length mismatch")
                continue
            rows.append(dict(zip(columns, values)))
        body_columns = SUBJECT_COLUMNS.issubset(columns)
        trace_scope = {k: trace.get(k) for k in ("scope", "limit", "boundedWindow")}
    else:
        reasons.append("cc1-trace missing")
        body_columns, trace_scope = False, {}
    groups = defaultdict(list)
    events = data.get("events", [])
    for event in events:
        groups[identity(event)].append(event)
    if data.get("coverageComplete") is not True:
        reasons.append("source coverageComplete != true")
    if data.get("dropped") != 0:
        reasons.append("dropped missing/nonzero")
    if not events:
        reasons.append("no public events/ProxyStarted")
    proxies = [project_proxy(items, rows if len(groups) == 1 else []) for items in groups.values()]
    if len(groups) > 1:
        reasons.append("multiple proxies: cc1-trace lacks proxy identity, assignment UNKNOWN")
    reasons.extend(r for proxy in proxies for r in proxy["coverageReasons"])
    return {"sources": sources, "sourceFlags": {k: data.get(k) for k in ("limit", "dropped", "coverageComplete", "scope")},
            "publicCoverage": "COMPLETE_DECLARED_RECORDED_INTERVAL" if not reasons else "INCOMPLETE",
            "coverageReasons": sorted(set(reasons)), "listenerContinuity": "UNKNOWN: HasListeners=false может пропускать Emit до Sequence++.",
            "traceScope": trace_scope, "subjectColumnsPresent": body_columns, "proxies": proxies}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("actual_reports_root", type=Path, help="Корень только фактических новых raw; без исторической базы сравнения")
    parser.add_argument("--output", required=True, type=Path, help="Новый .json в уже существующем каталоге")
    args = parser.parse_args()
    root, output = args.actual_reports_root.resolve(), args.output.resolve()
    if not root.is_dir():
        parser.error("actual_reports_root не является каталогом")
    if output.exists() or not output.parent.is_dir() or output.suffix.lower() != ".json":
        parser.error("Нужен НЕ существующий .json в существующем каталоге; перезапись запрещена")
    # Защита от случайного запуска по историческому receive679 или общему reports.
    if any("679" in part for part in root.parts) or root.name == "reports":
        parser.error("Передайте отдельный корень свежих raw; historical679/общий reports запрещены")
    paths = sorted(root.rglob("cc1-blaze-calls.json"))
    if not paths:
        parser.error("cc1-blaze-calls.json не найдены")
    files = []
    for path in paths:
        if any("679" in part for part in path.relative_to(root).parts):
            continue
        files.append(project_file(path))
    if not files:
        parser.error("Нет свежих raw после исключения historical679")
    result = {"schema": "cc1-public-trace-projection@1", "status": "DERIVED_FACTS_ONLY", "actualReportsRoot": str(root),
              "globalVerdict": "UNKNOWN: projection не принимает gameplay и не меняет legacy gates.",
              "limits": ["Private gate/Agent.Move не измерены.", "Root/Feet доступны только в совместном post-UXR sample; Feet — BotBody.Feet.",
                         "Ghost/noProxy frames, пропуски/bounded окна, teardown и физический Quest не покрыты.",
                         "Ни один отсутствующий замер не считается нулём или PASS."], "files": files}
    payload = json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n"
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(payload)
    proxies = [p for f in files for p in f["proxies"]]
    print(json.dumps({"status": result["status"], "files": len(files), "proxies": len(proxies),
                      "pairs": sum(p["pairCount"] for p in proxies), "output": str(output)}, ensure_ascii=True))


if __name__ == "__main__":
    main()

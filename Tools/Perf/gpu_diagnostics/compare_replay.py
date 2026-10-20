"""Парная разница indexed GPU-команд; не превращает replay в normal GPU budget."""
import hashlib
import json
from collections import defaultdict
from pathlib import Path

from .compare_series import _changes
from .model import CONDITIONS, _not_applicable, _percentile, _require, _unknown
from .replay_data import _read, query_replay


def compare_replay(a: Path, b: Path, joins_a: Path, joins_b: Path, out: Path):
    _require(not out.exists(), "Каталог replay comparison уже существует")
    passports = []
    groups = []
    join_manifests = []
    for source, joins in ((a, joins_a), (b, joins_b)):
        checked = query_replay(source, limit=1, joins=joins)
        def snapshot(path, expected):
            # Проверяется именно использованный набор байтов, не последующее состояние файла.
            return _read(path, expected)[0]

        join_hashes = checked["observedJoinFileSha256"]
        replay_hashes = checked["observedFileSha256"]
        join = json.loads(snapshot(joins / "manifest.json", join_hashes["manifest.json"]))
        _require("primaryQuality" in join and "actualConditions" in join, "Нужен актуальный join package с quality/conditions")
        join_manifests.append(join)
        candidates = {row["gpuResourceId"]: row for row in
                      (json.loads(line) for line in snapshot(joins / "candidates.jsonl", join_hashes["candidates.jsonl"]).splitlines())}
        resources = {row["id"]: row for row in
                     (json.loads(line) for line in snapshot(source / "resources.jsonl", replay_hashes["resources.jsonl"]).splitlines())}
        actions = {row["eventId"]: row for row in
                   (json.loads(line) for line in snapshot(source / "actions.jsonl", replay_hashes["actions.jsonl"]).splitlines())}
        grouped = defaultdict(lambda: {"drawCount": 0, "submittedIndexedElements": 0, "durations": [], "events": []})
        for line in snapshot(source / "bindings.jsonl", replay_hashes["bindings.jsonl"]).splitlines():
            binding = json.loads(line)
            action = actions[binding["eventId"]]
            identity = (binding.get("indexBuffer") or {}).get("resourceId")
            if identity is None or not action.get("indexed") or not action.get("work") or action.get("dispatch"):
                continue
            candidates_for_mesh = [row for row in candidates[identity]["candidates"] if row["type"] == "Mesh"]
            name = resources[identity]["name"]
            key = candidates_for_mesh[0]["authoringId"] if len(candidates_for_mesh) == 1 else "resource-name:" + name
            group = grouped[key]
            group.update(name=name, candidateIds=[row["authoringId"] for row in candidates_for_mesh],
                         mappingConfidence="uniqueAssetCandidate" if len(candidates_for_mesh) == 1 else "resourceNameOnly")
            group["drawCount"] += 1
            # RenderDoc1.46: numInstances=0 допустим у команды без ActionFlags::Instanced.
            # Настоящий instanced draw с нулём instances остаётся нулевым.
            instances = action["instanceCount"] if action["flags"] & 0x020000 else 1
            group["submittedIndexedElements"] += action["elementCount"] * instances
            group["events"].append(action["eventId"])
            if action["gpuReplaySeconds"] is not None:
                group["durations"].append(action["gpuReplaySeconds"] * 1000)
        for group in grouped.values():
            values = sorted(group.pop("durations"))
            group["individualDrawReplayMilliseconds"] = {"count": len(values), "p50": _percentile(values, .5),
                                                         "p95": _percentile(values, .95), "max": max(values) if values else None}
        groups.append(grouped)
        passports.append(checked)
    left, right = join_manifests
    quality_diff = _changes(left["primaryQuality"], right["primaryQuality"])
    fixed_keys = tuple(key for key in CONDITIONS if key != "qualityHash") + ("runtimePlatform", "xrEnabled", "applicabilityPolicy", "puppetCount", "clutterCount")
    conditions_diff = _changes({key: left["actualConditions"].get(key) for key in fixed_keys},
                               {key: right["actualConditions"].get(key) for key in fixed_keys})
    reasons = []
    for label, side in (("A", left), ("B", right)):
        passport = {"conditions": side["actualConditions"], "quality": {"conditionEvidence": side["conditionEvidence"]}}
        unknown = [key for key in CONDITIONS if key != "qualityHash" and _unknown(side["actualConditions"].get(key))
                   and not _not_applicable(passport, key)]
        if unknown:
            reasons.append(label + ": неизвестны условия " + ",".join(unknown))
    if any(left[key] != right[key] for key in ("sourceSha", "sceneSourceSha256", "scenePath", "tableSha256")):
        reasons.append("Source/scene/build identity не совпали")
    if left["runId"] == right["runId"] or passports[0]["sourceCaptureSha256"] == passports[1]["sourceCaptureSha256"]:
        reasons.append("Повторно использован тот же run/capture")
    if conditions_diff:
        reasons.append("Фактические условия различаются")
    if any(row["path"] != "/sceneDetail/farStart" for row in quality_diff):
        reasons.append("Помимо farStart изменились другие primary quality settings")
    signatures = []
    for passport in passports:
        raw_lines = passport.get("selectedReplayGpuEvidence", {}).get("rawLogLines", [])
        signature = raw_lines[0].split("Running replay on ", 1)[-1] if len(raw_lines) == 1 else None
        signatures.append(signature)
        if passport.get("replayApiProperties", {}).get("degraded") is not False:
            reasons.append("Replay hardware/degraded status не подтверждён")
    if signatures[0] is None or signatures[0] != signatures[1]:
        reasons.append("Selected replay GPU/driver не подтверждены одинаковыми log-derived свидетельствами")
    rows = []
    for key in sorted(set(groups[0]) | set(groups[1])):
        av, bv = groups[0].get(key), groups[1].get(key)
        delta = (bv["submittedIndexedElements"] if bv else 0) - (av["submittedIndexedElements"] if av else 0)
        changed = av is None or bv is None or delta != 0 or av["drawCount"] != bv["drawCount"]
        rows.append({"groupKey": key, "name": (av or bv)["name"], "geometryChanged": changed,
                     "a": av, "b": bv, "submittedIndexedElementsDelta": delta})
    out.mkdir(parents=True)
    raw = "".join(json.dumps(row, ensure_ascii=False, allow_nan=False) + "\n" for row in rows).encode("utf-8")
    (out / "groups.jsonl").write_bytes(raw)
    result = {"schemaVersion": 1, "status": "incomparable" if reasons else "limited",
              "timingDomain": "renderdoc-replay", "reasons": reasons, "actualSettingsDiff": quality_diff,
              "conditionsDiff": conditions_diff, "groupCount": len(rows),
              "geometryChangedGroupCount": sum(row["geometryChanged"] for row in rows),
              "submittedIndexedElementsDelta": sum(row["submittedIndexedElementsDelta"] for row in rows),
              "gpuFrameTimeDeltaMilliseconds": None, "groupsSha256": hashlib.sha256(raw).hexdigest(),
              "a": {"path": str(a.resolve()), "runId": left["runId"], "captureSha256": passports[0]["sourceCaptureSha256"],
                    "replayFileSha256": passports[0]["observedFileSha256"], "joinFileSha256": passports[0]["observedJoinFileSha256"]},
              "b": {"path": str(b.resolve()), "runId": right["runId"], "captureSha256": passports[1]["sourceCaptureSha256"],
                    "replayFileSha256": passports[1]["observedFileSha256"], "joinFileSha256": passports[1]["observedJoinFileSha256"]},
              "limitations": ["One instrumented capture pair; no independent A/A noise or repeated pairs",
                              "Group names/assets are candidate associations, not exact draw-to-object identity",
                              "Individual draw duration distributions are descriptive; their difference is not an optimisation benefit",
                              "No GPU timings are summed; submitted index elements are command metadata, not unique or visible geometry",
                              "Normal live GPU/Quest budget remains a separate measurement"]}
    (out / "comparison.json").write_text(json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {key: result[key] for key in ("status", "groupCount", "geometryChangedGroupCount", "submittedIndexedElementsDelta", "reasons")}

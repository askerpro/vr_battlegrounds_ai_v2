"""Ограниченные запросы к фактическому RenderDoc replay, без выдуманных Unity joins."""
import hashlib
import json
import math
from collections import defaultdict
from pathlib import Path

from .model import _require


def _read(path, expected=None):
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    _require(expected is None or digest == expected, "Replay snapshot SHA256 mismatch: " + str(path))
    return raw, digest


def query_replay(source: Path, table="events", event_id=None, resource_id=None,
                 offset=0, limit=25, full=False, joins=None):
    _require(type(offset) is int and offset >= 0 and type(limit) is int and 1 <= limit <= 100,
             "Требуются offset>=0 и limit1..100")
    root = source.resolve()
    raw, digest = _read(root / "capture.json")
    manifest = json.loads(raw)
    _require(manifest.get("schemaVersion") == 1 and manifest.get("kind") == "vrb.renderdoc-replay"
             and manifest.get("complete") is True and manifest.get("timingDomain") == "renderdoc-replay"
             and manifest.get("unit") == "seconds", "Нужен полный native RenderDoc replay export v1")
    hashes = {"capture.json": digest}

    def records(name, key):
        raw, hashes[name] = _read(root / name)
        rows = [json.loads(line) for line in raw.decode("utf-8").splitlines() if line.strip()]
        _require(all(isinstance(row, dict) and key in row for row in rows), name + ": нет ключа записи")
        indexed = {row[key]: row for row in rows}
        _require(len(indexed) == len(rows), name + ": повторный ID")
        return indexed

    actions = records("actions.jsonl", "eventId")
    resources = records("resources.jsonl", "id")
    bindings = records("bindings.jsonl", "eventId")
    joined = {}
    join_hashes = {}
    if joins is not None:
        join_manifest_raw, join_hashes["manifest.json"] = _read(joins / "manifest.json")
        join_manifest = json.loads(join_manifest_raw)
        join_raw, join_digest = _read(joins / "candidates.jsonl")
        join_hashes["candidates.jsonl"] = join_digest
        _require(join_manifest.get("kind") == "vrb.replay-scene-candidates" and join_manifest.get("schemaVersion") == 1
                 and join_digest == join_manifest.get("candidatesSha256"), "Неверный candidates package/hash")
        _require(all(join_manifest.get("replayFileSha256", {}).get(name) == digest for name, digest in hashes.items()),
                 "Candidates относятся к другому replay snapshot")
        join_rows = [json.loads(line) for line in join_raw.splitlines()]
        joined = {row["gpuResourceId"]: row for row in join_rows}
        _require(len(joined) == len(join_rows) == join_manifest.get("resourceCount") and set(joined) == set(resources),
                 "Candidates: повторные/потерянные/неизвестные resource IDs")

    def candidates(identity):
        row = joined.get(identity)
        if row is None or full:
            return row
        preview = []
        for candidate in row["candidates"][:3]:
            item = dict(candidate)
            locations = item.get("relatedLocations", [])
            item["relatedLocationCount"] = len(locations)
            item["relatedLocations"] = locations[:5]
            preview.append(item)
        return {"confidence": row["confidence"], "candidateCount": row["candidateCount"],
                "candidates": preview, "completeDetails": "--table resources --resource-id <id> --full"}
    _require(len(actions) == manifest.get("actionCount") and len(resources) == manifest.get("resourceCount")
             and len(bindings) == manifest.get("workEventCount"), "Счётчики manifest не совпали с JSONL")
    _require(set(bindings) <= set(actions), "Binding не связан с action")
    usage = defaultdict(dict)
    reverse = defaultdict(set)
    properties = defaultdict(lambda: defaultdict(set))

    def reference(event, identity, role):
        if identity is None:
            return
        _require(identity in resources, "Неизвестный capture resource: " + str(identity))
        usage[event].setdefault(identity, set()).add(role)
        reverse[identity].add(event)

    def ancestry(event):
        rows, seen = [], {event}
        parent = actions[event].get("parentEventId")
        while parent is not None:
            _require(parent in actions and parent not in seen, "Action tree: потерян parent или цикл")
            seen.add(parent)
            row = actions[parent]
            rows.append({"eventId": parent, "name": row.get("customName")})
            parent = row.get("parentEventId")
        return list(reversed(rows))

    for event, action in actions.items():
        duration = action.get("gpuReplaySeconds")
        _require(duration is None or type(duration) in (int, float) and math.isfinite(duration)
                 and duration >= 0, "Некорректный replay duration")
        ancestry(event)
        for key in ("copySource", "copyDestination", "depthOut"):
            reference(event, action.get(key), key)
        binding = bindings.get(event, {})
        reference(event, (binding.get("indexBuffer") or {}).get("resourceId"), "indexBuffer")
        for buffer in binding.get("vertexBuffers", []):
            reference(event, buffer.get("resourceId"), "vertexBuffer")
        for target in binding.get("outputTargets", []):
            reference(event, target, "outputTarget")
        reference(event, binding.get("depthTarget"), "depthTarget")
        for stage in binding.get("stages", []):
            prefix = "stage:" + str(stage.get("stageCode")) + ":"
            reference(event, stage.get("shaderId"), prefix + "shader")
            for key in ("readOnlyResources", "readWriteResources"):
                for identity in stage.get(key, []):
                    reference(event, identity, prefix + key)
            for key in ("readOnlyBindings", "readWriteBindings"):
                for descriptor in stage.get(key, []):
                    identity = descriptor.get("resourceId")
                    reference(event, identity, prefix + key)
                    if identity is not None and descriptor.get("shaderProperty"):
                        properties[event][identity].add(descriptor["shaderProperty"])
    _require(event_id is None or event_id in actions, "Event ID не найден")
    _require(resource_id is None or resource_id in resources, "Resource ID не найден")
    if table == "events":
        selected = [row for event, row in actions.items()
                    if (event_id is None or event == event_id)
                    and (resource_id is None or event in reverse[resource_id])]
        selected.sort(key=lambda row: (row.get("gpuReplaySeconds") is None,
                                      -(row.get("gpuReplaySeconds") or 0), row["eventId"]))

        def decorate(row):
            event = row["eventId"]
            data = dict(row)
            if not full:
                data.pop("apiEvents", None)
            if full:
                data["bindings"] = bindings.get(event)
            return dict(data, apiEventCount=len(row.get("apiEvents", [])),
                        gpuReplayMilliseconds=None if row.get("gpuReplaySeconds") is None
                        else row["gpuReplaySeconds"] * 1000, ancestry=ancestry(event),
                        resources=[{"id": identity, "name": resources[identity].get("name"),
                                    "typeCode": resources[identity].get("typeCode"), "roles": sorted(roles),
                                    "descriptor": resources[identity].get("descriptor"),
                                    "boundShaderProperties": sorted(properties[event][identity]),
                                    "sceneCandidates": candidates(identity)}
                                   for identity, roles in sorted(usage[event].items())])
    else:
        _require(table == "resources", "Неизвестная таблица")
        selected = [row for identity, row in resources.items()
                    if (resource_id is None or identity == resource_id)
                    and (event_id is None or identity in usage[event_id])]
        selected.sort(key=lambda row: (-len(reverse[row["id"]]), row["id"]))

        def decorate(row):
            events = sorted(reverse[row["id"]])
            return dict(row, usageEventCount=len(events), usageEventIds=events[:25],
                        usageHasMore=len(events) > 25, sceneCandidates=candidates(row["id"]),
                        costAttribution="unresolved; binding is not resource cost")
    capture = Path(manifest["sourceCapture"])
    capture_hash = None
    if capture.is_file():
        with capture.open("rb") as stream:
            capture_hash = hashlib.file_digest(stream, "sha256").hexdigest()
        if joins is not None:
            _require(capture_hash == join_manifest.get("sourceCaptureSha256"), "Candidates: capture SHA256 mismatch")
    if (root / "replay.log").is_file():
        _, hashes["replay.log"] = _read(root / "replay.log")
    return {"schemaVersion": 1, "status": "limited", "timingDomain": "renderdoc-replay",
            "source": str(root), "observedFileSha256": hashes, "sourceCaptureSha256": capture_hash,
            "observedJoinFileSha256": join_hashes,
            "sourceCaptureAvailable": capture.is_file(), "table": table,
            "replayApiProperties": manifest.get("replayApiProperties"),
            "selectedReplayGpuEvidence": manifest.get("selectedReplayGpuEvidence"),
            "coverage": {"actionCount": len(actions), "resourceCount": len(resources),
                         "timedWorkCount": sum(row.get("work") is True and row.get("gpuReplaySeconds") is not None
                                               for row in actions.values()),
                         "untimedWorkCount": sum(row.get("work") is True and row.get("gpuReplaySeconds") is None
                                                 for row in actions.values())},
            "total": len(selected), "offset": offset, "limit": limit,
            "nextOffset": offset + limit if offset + limit < len(selected) else None,
            "rows": [decorate(row) for row in selected[offset:offset + limit]],
            "limitations": manifest.get("limitations", []),
            "ordering": "events: descending individual replay duration; resources: binding event count",
            "aggregation": "none; parent/child, resources and overlapping work are not summed"}

"""Кандидаты Unity assets для GPU ресурсов с проверкой сцены и runtime process identity."""
import hashlib
import json
import os
import re
from collections import defaultdict
from pathlib import Path

from .model import _require
from .compare_series import _primary
from .replay_data import query_replay


def _physical(path):
    path = path.resolve()
    if os.name == "nt" and not str(path).startswith("\\\\?\\"):
        return Path("\\\\?\\UNC\\" + str(path)[2:] if str(path).startswith("\\\\") else "\\\\?\\" + str(path))
    return path


def join_replay_scene(source: Path, scene_index: Path, runtime: Path, out: Path):
    _require(not out.exists(), "Выходной каталог candidates уже существует")
    checked = query_replay(source, table="resources", limit=1)
    hashes = {}

    def read(path, expected=None):
        raw = _physical(path).read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        _require(expected is None or digest == expected, "SHA256 mismatch: " + str(path))
        hashes[str(path.resolve())] = digest
        return raw

    native = json.loads(read(source / "capture.json", checked["observedFileSha256"]["capture.json"]))
    receipt = json.loads(read(Path(native["sourceCapture"]).parent / "evidence.json"))
    runtime_info = json.loads(read(runtime / "runtime.json"))
    _require(receipt.get("status") == "captured" and receipt.get("capture", {}).get("sha256") == checked["sourceCaptureSha256"],
             "Capture receipt не подтверждает исходный .rdc")
    _require(receipt.get("runId") == runtime_info.get("runId") and receipt.get("processInstanceId") == runtime_info.get("processInstanceId")
             and runtime_info.get("requestedMode") == "instrumented", "Capture и runtime принадлежат разным run/process/mode")
    _require(runtime_info.get("sceneIdentitiesPath") == "scene-identities.json", "Runtime не содержит build identity")
    identities = json.loads(read(runtime / "scene-identities.json"))
    ready = json.loads(read(Path(native["sourceCapture"]).parent / "ready.json"))
    _require(ready.get("eventId") == receipt.get("readyEventId") and ready.get("runId") == runtime_info["runId"], "Capture ready receipt mismatch")
    observed = [json.loads(line) for line in read(runtime.parent / "events.jsonl").splitlines()]
    condition_events = [event for event in observed if event.get("eventId") == ready["payload"]["conditionsEventId"]
                        and event.get("kind") == "conditions.observed" and event.get("runId") == runtime_info["runId"]
                        and event.get("processInstanceId") == runtime_info["processInstanceId"]]
    _require(len(condition_events) == 1, "Нет единственного conditions receipt того же процесса")
    condition_evidence = condition_events[0]["payload"]["values"]
    quality = json.loads(read(runtime / "quality-canonical.json", runtime_info["qualityFingerprint"]))
    _require(ready["payload"]["actualFingerprint"] == runtime_info["qualityFingerprint"], "Capture quality fingerprint mismatch")
    index_manifest = json.loads(read(scene_index / "manifest.json"))
    _require(index_manifest.get("schemaVersion") == 1 and index_manifest.get("kind") == "vrb.scene-index", "Нужен scene-index v1")
    files = index_manifest["files"]
    summary = json.loads(read(scene_index / "summary.json", files["summary.json"]["sha256"]))
    scene = summary["source"]
    tables = [row for row in identities["tables"] if row["scenePath"] == scene["scenePath"]
              and row["sceneSourceSha256"] == scene["sceneFile"]["sha256"]]
    _require(len(tables) == 1, "Structural scene hash не совпал с единственной build identity table")
    table = tables[0]
    runtime_by_id = defaultdict(list)
    for row in identities["references"]:
        if row["scenePath"] == table["scenePath"] and row["tableSha256"] == table["tableSha256"]:
            runtime_by_id[row["authoringId"]].append(row)
    present = set(runtime_by_id)
    entities, by_name, incoming = {}, defaultdict(list), defaultdict(list)
    folded_sources = defaultdict(set)

    def build_copy_candidate(identity):
        match = re.fullmatch(r"GlobalObjectId_V1-2-([0-9a-f]{32})-([0-9]+)-([0-9]+)", identity)
        if not match or match[1] != table["sceneGuid"]:
            return None
        # Наблюдаемое преобразование native Unity, не публичный API identity-контракт.
        folded = (int(match[2]) ^ int(match[3])) & 0x7fffffffffffffff
        return "GlobalObjectId_V1-2-" + match[1] + "-" + str(folded) + "-0"
    for line in read(scene_index / "entities.jsonl", files["entities.jsonl"]["sha256"]).splitlines():
        row = json.loads(line)
        entities[row["id"]] = {key: row.get(key) for key in ("id", "name", "path", "type", "kind")}
        folded = build_copy_candidate(row["id"])
        if folded is not None:
            folded_sources[folded].add(row["id"])
        if row["kind"] == "resource" and row["id"] in present:
            by_name[row.get("name")].append(row)
    for line in read(scene_index / "relations.jsonl", files["relations.jsonl"]["sha256"]).splitlines():
        relation = json.loads(line)
        incoming[relation["targetId"]].append(relation)
    resources = [json.loads(line) for line in read(source / "resources.jsonl", checked["observedFileSha256"]["resources.jsonl"]).splitlines()]
    actions = {row["eventId"]: row for row in (json.loads(line) for line in read(source / "actions.jsonl", checked["observedFileSha256"]["actions.jsonl"]).splitlines())}
    uses = defaultdict(list)
    for line in read(source / "bindings.jsonl", checked["observedFileSha256"]["bindings.jsonl"]).splitlines():
        binding = json.loads(line)
        index_id = (binding.get("indexBuffer") or {}).get("resourceId")
        uses[index_id].append(actions[binding["eventId"]])
        for buffer in binding.get("vertexBuffers", []):
            uses[buffer.get("resourceId")].append(actions[binding["eventId"]])
    rows = []

    def runtime_match(entity):
        identity = entity["id"]
        key = identity if identity in present else build_copy_candidate(identity)
        matches = runtime_by_id.get(key, [])
        if len(matches) != 1 or matches[0]["runtimeType"] != entity["type"] or (key in folded_sources and len(folded_sources[key]) != 1):
            return {"status": "unresolved", "buildCopyId": None}
        return {"status": "serializedId" if identity == key else "prefabUnpackFoldCandidate",
                "buildCopyId": key, "runtimeInstanceId": matches[0]["runtimeInstanceId"],
                "runtimeInstanceIdScope": "same capture process only"}
    for resource in resources:
        candidates = []
        for entity in by_name[resource.get("name")]:
            kind, code = entity.get("type", ""), resource["typeCode"]
            compatible = code == 5 and kind == "Mesh" or code == 10 and kind == "Shader" or code == 4 and kind in ("Texture2D", "Texture3D", "Cubemap", "RenderTexture", "Texture2DArray", "CubemapArray")
            if not compatible:
                continue
            evidence = ["exact resource name/type match", "serialized authoring ID present in same capture process", "same authored scene SHA256"]
            mesh = entity.get("data", {})
            submeshes = {row.get("indexCount") for row in mesh.get("submeshes", [])}
            matching_events = sorted({row["eventId"] for row in uses[resource["id"]]
                                      if row.get("indexed") and row.get("elementCount") in submeshes})
            if matching_events:
                evidence.append("indexed draw count matches authored shared mesh submesh")
            locations = []
            for relation in incoming[entity["id"]]:
                linked = entities.get(relation["sourceId"])
                if linked:
                    locations.append(dict(linked, relation=relation["relation"], runtimeIdentityMatch=runtime_match(linked)))
            candidates.append({"authoringId": entity["id"], "assetPath": entity["path"], "name": entity["name"],
                               "type": kind, "confidence": "candidate", "evidence": evidence,
                               "matchingSubmeshCountEvents": matching_events, "relatedLocations": locations})
        rows.append({"gpuResourceId": resource["id"], "gpuResourceName": resource.get("name"),
                     "confidence": "candidate" if candidates else "unresolved", "candidateCount": len(candidates),
                     "candidates": candidates, "gpuResourceCostMilliseconds": None})
    # Проверенный snapshot ресурсов не должен поменяться во время построения связей.
    for filename, digest in checked["observedFileSha256"].items():
        read(source / filename, digest)
    out.mkdir(parents=True)
    target = out / "candidates.jsonl"
    target.write_text("".join(json.dumps(row, ensure_ascii=False, allow_nan=False) + "\n" for row in rows), encoding="utf-8", newline="\n")
    result = {"schemaVersion": 1, "kind": "vrb.replay-scene-candidates", "status": "limited",
              "runId": runtime_info["runId"], "processInstanceId": runtime_info["processInstanceId"],
              "sourceSha": runtime_info["sourceSha"], "scenePath": table["scenePath"],
              "qualityFingerprint": runtime_info["qualityFingerprint"],
              "primaryQuality": _primary({"quality": {"snapshot": quality}}),
              "actualConditions": {key: value.get("value") for key, value in condition_evidence.items()},
              "conditionEvidence": condition_evidence, "phaseMapping": receipt["phaseMapping"],
              "sceneSourceSha256": table["sceneSourceSha256"], "tableSha256": table["tableSha256"],
              "runtimeIdentityUnresolvedCount": table["unresolvedCount"], "sourceFileSha256": hashes,
              "prefabFoldCollisionKeyCount": sum(len(values) > 1 for values in folded_sources.values()),
              "replayFileSha256": checked["observedFileSha256"], "sourceCaptureSha256": checked["sourceCaptureSha256"],
              "resourceCount": len(rows), "resourcesWithCandidates": sum(bool(row["candidates"]) for row in rows),
              "candidatesSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
              "limitations": ["Names and geometry metadata are candidate evidence, not exact GPU-to-Unity joins",
                              "Build-copy prefab ID reconciliation uses an observed fold with uniqueness/type guards, not a public Unity contract",
                              "Authored asset metadata may differ from platform imports; scene hash alone does not prove imported asset bytes",
                              "Static batching, instancing and skinning may combine or transform resources",
                              "Phase mapping remains the capture receipt's candidate correlation",
                              "Resource relation is not per-texture/per-light/per-mesh exclusive GPU cost"]}
    (out / "manifest.json").write_text(json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {"status": "limited", "manifest": str((out / "manifest.json").resolve()),
            "resourcesWithCandidates": result["resourcesWithCandidates"], "resourceCount": len(rows)}

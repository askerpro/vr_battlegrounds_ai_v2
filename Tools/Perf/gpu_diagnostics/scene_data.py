"""Машиночитаемый индекс сцены и ограниченные выборки для анализа агентом."""
import hashlib
import json
import math
from collections import Counter
from pathlib import Path

from .model import PackageError


def _json(value):
    return json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(",", ":"))


def _require(condition, reason):
    if not condition:
        raise PackageError(reason)


def _id(value):
    return value.get("id") if isinstance(value, dict) else value


def _sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _positive(value):
    return type(value) in (int, float) and math.isfinite(value) and value > 0


def index_scene(source: Path, out: Path) -> dict:
    """Сохраняет исходник, полные записи, граф ссылок и структурные приоритеты."""
    source, out = source.resolve(), out.resolve()
    _require(not out.exists(), f"Каталог уже существует; укажите новый: {out}")
    raw = source.read_bytes()
    scene = json.loads(raw)
    _require(isinstance(scene, dict) and type(scene.get("schemaVersion")) is int
             and scene["schemaVersion"] == 1, "scene.json: требуется schemaVersion=1")
    entities, links, identities = [], [], set()

    def entity(kind, row, pointer, owner=None):
        _require(isinstance(row, dict), f"{pointer}: требуется object")
        identity = row.get("identity") or {}
        identity_id = row.get("id") or identity.get("id")
        _require(isinstance(identity_id, str) and identity_id and identity_id not in identities,
                 f"{pointer}: пустой или повторный ID: {identity_id}")
        identities.add(identity_id)
        entities.append({"kind": kind, "id": identity_id,
                         "path": row.get("path") or identity.get("path"),
                         "name": row.get("name") or identity.get("name"),
                         "type": row.get("type") or identity.get("type"),
                         "sourcePointer": pointer, "data": row})
        if owner:
            link(owner, identity_id, "containsRenderer", pointer)
        return identity_id

    def link(start, end, relation, pointer):
        start, end = _id(start), _id(end)
        if end is None:
            return
        _require(isinstance(start, str) and isinstance(end, str),
                 f"{pointer}: ссылка требует строковый ID")
        links.append({"sourceId": start, "targetId": end, "relation": relation,
                      "sourcePointer": pointer})

    for section, kind in (("objects", "object"), ("resources", "resource"),
                          ("lodGroups", "lod"), ("lights", "light")):
        rows = scene.get(section, [])
        _require(isinstance(rows, list), f"{section}: требуется array")
        for index, row in enumerate(rows):
            pointer = f"/{section}/{index}"
            identity = entity(kind, row, pointer)
            if kind == "object":
                link(identity, row.get("parent"), "parent", pointer + "/parent")
                for number, renderer in enumerate(row.get("renderers", [])):
                    ref = pointer + f"/renderers/{number}"
                    renderer_id = entity("renderer", renderer, ref, identity)
                    link(renderer_id, renderer.get("mesh"), "mesh", ref + "/mesh")
                    for slot_index, slot in enumerate(renderer.get("materialSlots", [])):
                        link(renderer_id, slot.get("material"), "materialSlot",
                             ref + f"/materialSlots/{slot_index}/material")
            elif kind == "resource":
                link(identity, row.get("shader"), "shader", pointer + "/shader")
                for number, binding in enumerate(row.get("textureBindings", [])):
                    link(identity, binding.get("texture"), "textureBinding",
                         pointer + f"/textureBindings/{number}/texture")
            elif kind == "lod":
                link(identity, row.get("object"), "object", pointer + "/object")
                for mode in ("authored", "original", "effective"):
                    for number, level in enumerate(row.get(mode) or []):
                        if level is None:
                            continue
                        for renderer_index, renderer in enumerate(level.get("renderers") or []):
                            link(identity, renderer, f"{mode}LodRenderer",
                                 pointer + f"/{mode}/{number}/renderers/{renderer_index}")
            elif kind == "light":
                link(identity, row.get("object"), "object", pointer + "/object")
                link(identity, row.get("cookie"), "cookie", pointer + "/cookie")

    incoming = Counter()
    for edge in links:
        edge["resolved"] = edge["sourceId"] in identities and edge["targetId"] in identities
        incoming[edge["targetId"]] += 1
    ranked = {}

    def candidates(key, rows, meaning, next_step):
        rows.sort(key=lambda row: (-row["value"], row["id"]))
        ranked[key] = {"evidence": "structuralCandidate", "gpuTimeMs": None,
                       "meaning": meaning, "nextStep": next_step,
                       "totalCandidates": len(rows), "limit": 25, "items": rows[:25]}

    geometry, textures, materials, shaders = [], [], [], []
    for record in entities:
        if record["kind"] != "resource":
            continue
        row = record["data"]
        item = {key: record[key] for key in ("id", "path", "name", "sourcePointer")}
        item["incomingReferences"] = incoming[record["id"]]
        if record["type"] == "Mesh" and _positive(row.get("vertexCount")):
            geometry.append({**item, "value": row["vertexCount"], "unit": "vertices",
                             "indexCountsBySubmesh": row.get("submeshes")})
        if _positive(row.get("width")) and _positive(row.get("height")):
            textures.append({**item, "value": row["width"] * row["height"],
                             "unit": "baseLevelTexelsPerSurface", "width": row["width"],
                             "height": row["height"], "depthOrLayers": row.get("depthOrLayers"),
                             "graphicsFormat": row.get("graphicsFormat"),
                             "activeMipmapLimit": row.get("activeMipmapLimit"),
                             "residentBytes": None})
        if record["type"] == "Material" and _positive(row.get("passCount")):
            materials.append({**item, "value": row["passCount"], "unit": "declaredPasses"})
        if record["type"] == "Shader":
            shaders.append({**item, "value": incoming[record["id"]], "unit": "materialReferences"})
    candidates("meshVertexCount", geometry,
               "Весь shared mesh; LOD уровни и экземпляры не складываются в visible triangles.",
               "Связать renderer и LOD, затем проверить actual draw и изолированный LOD A/B.")
    candidates("textureBaseArea", textures,
               "Ширина × высота поверхности mip0; не размер памяти и не GPU-время.",
               "Проверить material bindings, sampled mip/формат и контролируемый texture A/B.")
    candidates("materialDeclaredPasses", materials,
               "Декларированные passes; выполнение и стоимость не наблюдались.",
               "Сопоставить материал с реально выполненными passes GPU capture.")
    candidates("shaderMaterialReferences", shaders,
               "Количество ссылок материалов; не число draw и не сложность шейдера.",
               "Выбрать capture event/variant и проверить стоимость соответствующего прохода.")
    lights = [record for record in entities if record["kind"] == "light"]
    summary = {"schemaVersion": 1, "kind": "vrb.scene-analysis", "status": "structural-only",
               "source": scene.get("source"), "captureId": scene.get("captureId"),
               "counts": dict(sorted(Counter(row["kind"] for row in entities).items())),
               "resourceTypes": dict(sorted(Counter(row["type"] for row in entities
                                                     if row["kind"] == "resource").items())),
               "relations": len(links), "unresolvedRelations": sum(not row["resolved"] for row in links),
               "lightInventory": dict(sorted(Counter(
                   f'{row["data"].get("lightmapBakeType")}/{row["data"].get("shadows")}'
                   for row in lights).items())),
               "candidates": ranked,
               "unknown": ["GPU timing", "actual draw/pass", "camera visibility/selected LOD",
                           "resident texture bytes", "shadow casters/atlas cost"],
               "sceneDetail": scene.get("sceneDetail"), "observations": scene.get("observations")}
    # Сначала проверяем сериализацию: NaN и Infinity не должны попасть в пакет.
    summary_text = _json(summary)
    for row in entities:
        _json(row)
    out.mkdir(parents=True)
    (out / "raw").mkdir()
    (out / "raw" / "scene.json").write_bytes(raw)
    for name, rows in (("entities.jsonl", entities), ("relations.jsonl", links)):
        with (out / name).open("w", encoding="utf-8", newline="\n") as stream:
            for row in rows:
                stream.write(_json(row) + "\n")
    (out / "summary.json").write_text(summary_text + "\n", encoding="utf-8", newline="\n")
    manifest = {"schemaVersion": 1, "kind": "vrb.scene-index", "status": "structural-only",
                "captureId": scene.get("captureId"),
                "sourceSha256": hashlib.sha256(raw).hexdigest(),
                "files": {name: {"sha256": _sha(out / name), "bytes": (out / name).stat().st_size}
                          for name in ("raw/scene.json", "entities.jsonl", "relations.jsonl", "summary.json")}}
    (out / "manifest.json").write_text(_json(manifest) + "\n", encoding="utf-8", newline="\n")
    return {"status": "structural-only", "package": str(out), "counts": summary["counts"],
            "relations": len(links), "unresolvedRelations": summary["unresolvedRelations"]}


def query_scene(package: Path, *, identity=None, search=None, kind=None, offset=0,
                limit=25, full=False, links=False) -> dict:
    """Пагинация без потери записей; целостность читаемого файла проверяется до ответа."""
    _require(offset >= 0 and 1 <= limit <= 100, "offset >= 0; limit от1 до100")
    _require(not links or identity, "Для scene-links требуется точный ID")
    manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8"))
    _require(manifest.get("kind") == "vrb.scene-index" and manifest.get("schemaVersion") == 1,
             "Неизвестный scene-index manifest")
    filename = "relations.jsonl" if links else "entities.jsonl"
    path = package / filename
    evidence = manifest.get("files", {}).get(filename, {})
    _require(_sha(path) == evidence.get("sha256"), f"SHA256 mismatch: {filename}")
    items, total = [], 0
    with path.open(encoding="utf-8") as stream:
        for line in stream:
            row = json.loads(line)
            if links:
                if identity not in (row["sourceId"], row["targetId"]):
                    continue
            else:
                if identity is not None and row["id"] != identity:
                    continue
                if kind and row["kind"] != kind:
                    continue
                if search and search.casefold() not in " ".join(str(row.get(key) or "")
                                                               for key in ("id", "path", "name", "type")).casefold():
                    continue
            if offset <= total < offset + limit:
                items.append(row if full or links else {key: value for key, value in row.items() if key != "data"})
            total += 1
    return {"schemaVersion": 1, "status": "structural-only", "sourceSha256": manifest["sourceSha256"],
            "fileSha256": evidence["sha256"], "items": items, "total": total, "offset": offset,
            "limit": limit, "nextOffset": offset + limit if offset + limit < total else None}

"""Выбирает полный вариант URP и проверяет GUID/пути до записи в игровой Assets."""

import argparse
import collections
import hashlib
import json
import pathlib
import re
import uuid


def digest(path):
    value = hashlib.sha256()
    with pathlib.Path(path).open("rb") as source:
        for chunk in iter(lambda: source.read(1048576), b""):
            value.update(chunk)
    return value.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", type=pathlib.Path)
    args = parser.parse_args()
    stage = args.stage.resolve()
    excluded_roots = json.loads(pathlib.Path(__file__).with_name("catalog-policy.json").read_text(encoding="utf-8"))["excludedAssetRoots"]
    game = pathlib.Path.cwd().resolve()
    packages = json.loads((stage / "resolved-packages.json").read_text(encoding="utf-8"))
    if len(packages) != 13:
        raise ValueError("Подготовлены не все 13 паков")
    existing = {}
    for meta in (game / "Assets").rglob("*.meta"):
        match = re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="replace"), re.MULTILINE)
        if match:
            existing[match[1]] = pathlib.Path(str(meta)[:-5])
    chosen = {}
    destinations = {}
    cache_file = stage / "import-plan.json"
    previous = json.loads(cache_file.read_text(encoding="utf-8")) if cache_file.exists() else {}
    hashes = {item["source"]: item["sha256"] for item in previous.get("files", [])}
    report = {"packages": [], "files": [], "reused": [], "excluded": [], "conflicts": [], "guid_remaps": []}
    for package_index, package in enumerate(packages):
        original = package["original"]
        selected = package["urp"][0] if package["urp"] else original
        base_models = sum(a["path"].lower().endswith(".fbx") for a in original["assets"])
        selected_models = sum(a["path"].lower().endswith(".fbx") for a in selected["assets"])
        if selected_models < base_models:
            raise ValueError("URP-версия потеряла модели: " + original["package"])
        info = {"package": original["package"], "variant": "URP" if package["urp"] else "original",
                "models": selected_models, "prefabs": 0, "new_assets": 0}
        remap = {}
        for asset in selected["assets"]:
            guid = asset["guid"]
            if "asset" in asset and asset["path"].lower().endswith(".prefab") and guid in chosen:
                remap[guid] = uuid.uuid5(uuid.NAMESPACE_URL, "VRBattlegrounds/Environment/" + pathlib.Path(original["package"]).name + "/" + guid).hex
                report["guid_remaps"].append({"package": original["package"], "path": asset["path"], "old": guid, "new": remap[guid]})
        for asset in selected["assets"]:
            if "asset" not in asset:
                continue
            path = asset["path"]
            lower = path.lower()
            if any(lower.startswith(root.lower()) for root in excluded_roots):
                report["excluded"].append({"path": path, "reason": "Пак исключён политикой каталога"})
                continue
            if "/tutorialinfo/" in lower or lower.endswith((".cs", ".unitypackage", ".wlt")) or "hdrpdefaultresources" in lower or pathlib.PurePosixPath(lower).name == "readme.asset":
                report["excluded"].append({"path": path, "reason": "Демосервис, другой pipeline или вложенный архив"})
                continue
            guid = remap.get(asset["guid"], asset["guid"])
            source = asset["asset"]["source"]
            meta_source = asset.get("asset.meta", {}).get("source")
            # Переписываем только создаваемую копию. Исходный пак и его распаковка неизменны.
            if remap:
                serialized = {".prefab", ".mat", ".asset", ".unity", ".lighting", ".terrainlayer", ".shadergraph", ".shadersubgraph", ".preset"}
                if pathlib.PurePosixPath(path).suffix.lower() in serialized:
                    data = pathlib.Path(source).read_bytes()
                    if b"\0" in data[:64]:
                        # Бинарные TerrainData не должны ссылаться на конфликтующие префабы.
                        # Не делаем непроверенную правку формата Unity SerializedFile.
                        for old in remap:
                            raw = bytes.fromhex(old)
                            words_le = b"".join(raw[i:i + 4][::-1] for i in range(0, 16, 4))
                            if raw in data or words_le in data or old.encode() in data:
                                raise ValueError("Бинарный ассет с конфликтующей ссылкой требует отдельной обработки: " + path)
                    else:
                        text = data.decode("utf-8-sig")
                        patched = re.sub(r"[0-9a-fA-F]{32}", lambda m: remap.get(m[0].lower(), m[0]), text)
                        if patched != text:
                            target = stage / "ready" / str(package_index) / path
                            target.parent.mkdir(parents=True, exist_ok=True)
                            target.write_text(patched, encoding="utf-8")
                            source = str(target)
                if meta_source:
                    text = pathlib.Path(meta_source).read_text(encoding="utf-8-sig")
                    patched = re.sub(r"[0-9a-fA-F]{32}", lambda m: remap.get(m[0].lower(), m[0]), text)
                    if patched != text:
                        target = stage / "ready" / str(package_index) / (path + ".meta")
                        target.parent.mkdir(parents=True, exist_ok=True)
                        target.write_text(patched, encoding="utf-8")
                        meta_source = str(target)
            sha = hashes[source] if source in hashes and stage / "ready" not in pathlib.Path(source).parents else digest(source)
            key = lower
            if guid in existing:
                destination = existing[guid]
                if destination.is_file() and digest(destination) == sha:
                    report["reused"].append({"path": path, "existing": str(destination)})
                else:
                    report["conflicts"].append({"path": path, "existing": str(destination), "reason": "GUID уже есть с другим содержимым"})
                continue
            if guid in chosen:
                previous = chosen[guid]
                if previous["sha256"] != sha:
                    report["conflicts"].append({"path": path, "previous": previous["path"], "reason": "Один GUID у разных данных паков"})
                else:
                    report["reused"].append({"path": path, "existing": previous["path"]})
                continue
            destination = game.joinpath(*pathlib.PurePosixPath(path).parts)
            if key in destinations or destination.exists() or pathlib.Path(str(destination) + ".meta").exists():
                report["conflicts"].append({"path": path, "reason": "Путь уже занят другим GUID"})
                continue
            item = {"guid": guid, "path": path, "source": source,
                    "meta": meta_source, "sha256": sha,
                    "package": original["package"]}
            if item["meta"] is None:
                report["conflicts"].append({"path": path, "reason": "Нет исходного meta"})
                continue
            chosen[guid] = item
            destinations[key] = item
            info["new_assets"] += 1
            info["prefabs"] += lower.endswith(".prefab")
        report["packages"].append(info)
    report["files"] = list(chosen.values())
    report["extensions"] = dict(collections.Counter(pathlib.PurePosixPath(item["path"]).suffix.lower() for item in report["files"]))
    report["bytes"] = sum(pathlib.Path(item["source"]).stat().st_size for item in report["files"])
    (stage / "import-plan.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"files": len(report["files"]), "reused": len(report["reused"]),
                      "conflicts": len(report["conflicts"]), "gb": round(report["bytes"] / 1073741824, 2),
                      "guid_remaps": len(report["guid_remaps"]), "extensions": report["extensions"]}, ensure_ascii=False))
    if report["conflicts"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()

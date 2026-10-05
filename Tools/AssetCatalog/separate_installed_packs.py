"""Проверенная копия установленного окружения и очистка игры по Unity-манифесту."""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import time

PACK = "Assets/env_packs"
REVIEW = "Assets/Scenes/ExcludedFromIndex/IndustrialCandidateReview"


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def checked_path(root, relative):
    part = pathlib.PurePosixPath(relative)
    if part.is_absolute() or ".." in part.parts or ":" in relative or "\\" in relative:
        raise ValueError("Небезопасный относительный путь: " + relative)
    target = root.joinpath(*part.parts)
    if root.resolve() not in target.resolve().parents:
        raise ValueError("Путь вышел за разрешённый корень: " + relative)
    for parent in [target, *target.parents]:
        if parent == root:
            break
        if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
            raise ValueError("Reparse point в пути: " + str(parent))
    return target


def include_tree(game, relative, mapping, prefix=""):
    source = checked_path(game, relative)
    if source.is_file():
        mapping[relative] = prefix + relative
    elif source.is_dir():
        for file in source.rglob("*"):
            if file.is_file() and "__pycache__" not in file.parts:
                rel = file.relative_to(game).as_posix()
                checked_path(game, rel)
                mapping[rel] = prefix + rel
    meta = pathlib.Path(str(source) + ".meta")
    if meta.is_file():
        mapping[relative + ".meta"] = prefix + relative + ".meta"


def include_asset(game, relative, mapping, prefix=""):
    include_tree(game, relative, mapping, prefix)
    parent = pathlib.PurePosixPath(relative).parent
    while str(parent) != "Assets" and str(parent) != ".":
        meta = str(parent) + ".meta"
        if (game / meta).is_file():
            mapping[meta] = prefix + meta
        parent = parent.parent


def stage(game, destination, report):
    started = time.perf_counter()
    if destination.exists() or destination == game or game in destination.parents or destination in game.parents:
        raise ValueError("Назначение должно быть новым отдельным каталогом вне игры")
    if destination.parent.is_symlink() or (hasattr(destination.parent, "is_junction") and destination.parent.is_junction()):
        raise ValueError("Родитель назначения является reparse point")
    retention = json.loads((report / "retention-unity.json").read_text(encoding="utf-8-sig"))
    pipeline = json.loads((report / "pipeline.json").read_text(encoding="utf-8-sig"))
    keep = {entry["path"] for entry in retention["retained"]}
    mapping = {}
    include_tree(game, PACK, mapping)
    include_tree(game, REVIEW + ".unity", mapping, "Archive/")
    include_tree(game, REVIEW, mapping, "Archive/")
    for path in retention["reviewDependencies"]:
        if not path.startswith(PACK + "/"):
            include_asset(game, path, mapping, "Archive/")
    for path in pipeline["dependencies"]:
        include_asset(game, path, mapping)
    include_tree(game, "Tools/AssetCatalog", mapping, "Archive/")
    # Версии и теги нужны демо; игровые сцены, XR и менеджеры не переносятся.
    for path in ["ProjectSettings/ProjectVersion.txt", "ProjectSettings/TagManager.asset"]:
        mapping[path] = path
    destination.mkdir()
    records = []
    for index, (relative, target_relative) in enumerate(sorted(mapping.items())):
        source = checked_path(game, relative)
        target = checked_path(destination, target_relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        expected = digest(source)
        shutil.copy2(source, target)
        if digest(target) != expected or digest(source) != expected:
            raise ValueError("Содержимое изменилось или копия повреждена: " + relative)
        records.append({"source": relative, "destination": target_relative,
                        "sha256": expected, "bytes": source.stat().st_size})
        if index % 1500 == 0:
            print(json.dumps({"copied": index + 1, "total": len(mapping)}), flush=True)
    packages = destination / "Packages"
    packages.mkdir()
    (packages / "manifest.json").write_text(json.dumps({"dependencies": {
        "com.unity.render-pipelines.universal": "17.4.0", "com.unity.shadergraph": "17.4.0",
        "com.unity.modules.terrain": "1.0.0", "com.unity.modules.physics": "1.0.0",
        "com.unity.modules.audio": "1.0.0", "com.unity.modules.animation": "1.0.0",
        "com.unity.modules.particlesystem": "1.0.0", "com.unity.modules.ui": "1.0.0",
        "com.unity.modules.imgui": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0"
    }}, indent=2), encoding="utf-8")
    (destination / "ProjectSettings/GraphicsSettings.asset").write_text(
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!30 &1\nGraphicsSettings:\n"
        "  m_ObjectHideFlags: 0\n  serializedVersion: 16\n"
        "  m_CustomRenderPipeline: {fileID: 11400000, guid: " + pipeline["ids"][0] + ", type: 2}\n"
        "  m_RenderPipelineGlobalSettingsMap:\n"
        "    UnityEngine.Rendering.Universal.UniversalRenderPipeline: {fileID: 11400000, guid: " + pipeline["ids"][1] + ", type: 2}\n",
        encoding="utf-8")
    (destination / "README.md").write_text(
        "# Каталог окружения\n\nUnity 6000.4.1f1 / URP 17.4.0. Открывать как отдельный проект.\n\n"
        "Полные установленные паки и авторские демосцены: Assets/env_packs. GUID и настройки импорта сохранены.\n\n"
        "Archive содержит прежний собственный IndustrialCandidateReview, его внешние зависимости и копию инструментов. "
        "Это архив, Unity его не импортирует. Инструменты и восстановление собственного стенда ещё не адаптированы.\n\n"
        "Отсутствующие в исходной поставке скрипты/меши и ограничения материалов не исправлялись переносом.\n",
        encoding="utf-8")
    manifest = {"schemaVersion": 1, "game": str(game), "destination": str(destination),
                "keep": sorted(keep), "records": records, "copyVerified": True,
                "seconds": round(time.perf_counter() - started, 2)}
    report.mkdir(parents=True, exist_ok=True)
    (report / "copy-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    archive = destination / "Migration"
    archive.mkdir()
    shutil.copy2(report / "copy-manifest.json", archive / "copy-manifest.json")
    shutil.copy2(report / "retention-unity.json", archive / "retention-unity.json")
    print(json.dumps({"passed": True, "copiedFiles": len(records), "bytes": sum(r["bytes"] for r in records),
                      "retainedAssets": len(keep), "seconds": manifest["seconds"], "destination": str(destination)}), flush=True)


def prune(game, destination, report, owner):
    manifest = json.loads((report / "copy-manifest.json").read_text(encoding="utf-8"))
    if manifest["game"] != str(game) or manifest["destination"] != str(destination) or not manifest["copyVerified"]:
        raise ValueError("Манифест не соответствует источнику/назначению")
    keep = set(manifest["keep"])
    keep_metas = {path + ".meta" for path in keep}
    for path in keep:
        parent = pathlib.PurePosixPath(path).parent
        while str(parent).startswith(PACK):
            keep_metas.add(str(parent) + ".meta")
            parent = parent.parent
    def locked():
        lines = (game / "tmp/unity-lock/info").read_text().splitlines()
        values = dict(line.split("=", 1) for line in lines if "=" in line)
        if values.get("owner") != owner or int(values.get("until", "0")) <= time.time():
            raise ValueError("Нет действующей аренды Unity")
    locked()
    remove = []
    for record in manifest["records"]:
        path = record["source"]
        pack = path == PACK + ".meta" or path.startswith(PACK + "/")
        review = path in {REVIEW + ".unity", REVIEW + ".unity.meta", REVIEW + ".meta"} or path.startswith(REVIEW + "/")
        if (pack or review) and path not in keep and path not in keep_metas:
            source = checked_path(game, path)
            backup = checked_path(destination, record["destination"])
            if not source.is_file() or digest(source) != record["sha256"] or digest(backup) != record["sha256"]:
                raise ValueError("Источник/копия изменились: " + path)
            remove.append(record)
    # Полная preflight до первого удаления; каждый путь проверяется повторно при удалении.
    removed_bytes = 0
    for index, record in enumerate(remove):
        if index % 500 == 0:
            locked()
        source = checked_path(game, record["source"])
        source.unlink()
        removed_bytes += record["bytes"]
    for root_relative in [PACK, REVIEW]:
        root = checked_path(game, root_relative)
        if root.is_dir():
            directories = sorted((p for p in root.rglob("*") if p.is_dir()), key=lambda p: len(p.parts), reverse=True)
            for directory in [*directories, root]:
                checked_path(game, directory.relative_to(game).as_posix())
                if not any(directory.iterdir()):
                    directory.rmdir()
    result = {"passed": True, "removedFilesIncludingMeta": len(remove), "removedBytes": removed_bytes,
              "retainedAssets": len(keep), "backup": str(destination), "removed": [r["source"] for r in remove]}
    (report / "prune-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key != "removed"}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["stage", "prune"])
    parser.add_argument("--project", type=pathlib.Path, required=True)
    parser.add_argument("--destination", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    parser.add_argument("--owner")
    args = parser.parse_args()
    game, destination, report = args.project.resolve(), args.destination.resolve(), args.report.resolve()
    if args.action == "stage":
        stage(game, destination, report)
    else:
        prune(game, destination, report, args.owner)

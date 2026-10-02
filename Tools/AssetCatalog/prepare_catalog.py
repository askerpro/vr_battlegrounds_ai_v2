"""Готовит изолированный проект каталога без изменения игрового Assets."""

import argparse
import gzip
import json
import pathlib
import shutil
import tarfile


ALLOWED = {".fbx", ".obj", ".prefab", ".mat", ".png", ".tga", ".jpg", ".jpeg", ".psd", ".exr"}


def extract(package, project):
    mapping = {}
    with gzip.open(package, "rb") as zipped, tarfile.open(fileobj=zipped, mode="r|") as archive:
        for item in archive:
            if item.isfile() and item.name.endswith("/pathname"):
                path = archive.extractfile(item).read().decode("utf-8").splitlines()[0].rstrip("\0")
                relative = pathlib.PurePosixPath(path)
                if relative.parts[0] != "Assets" or ".." in relative.parts or ":" in path or "\\" in path:
                    raise ValueError("Небезопасный путь в архиве: " + path)
                if relative.suffix.lower() in ALLOWED:
                    mapping[item.name.rsplit("/", 1)[0]] = relative
    written = 0
    with gzip.open(package, "rb") as zipped, tarfile.open(fileobj=zipped, mode="r|") as archive:
        for item in archive:
            if not item.isfile() or "/" not in item.name:
                continue
            guid, name = item.name.rsplit("/", 1)
            if guid not in mapping or name not in {"asset", "asset.meta"}:
                continue
            target = project.joinpath(*mapping[guid].parts)
            if name == "asset.meta":
                target = pathlib.Path(str(target) + ".meta")
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("wb") as destination:
                shutil.copyfileobj(archive.extractfile(item), destination)
            written += 1
    return {"selected_assets": len(mapping), "written_files": written, "package": str(package)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=pathlib.Path)
    parser.add_argument("project", type=pathlib.Path)
    parser.add_argument("--game", type=pathlib.Path, default=pathlib.Path.cwd())
    args = parser.parse_args()
    project = args.project.resolve()
    game = args.game.resolve()
    if project == game or game / "Temp" / "LevelDesign" not in project.parents:
        raise ValueError("Каталог должен находиться внутри Temp/LevelDesign игрового проекта")
    if (project / "Assets").exists():
        raise ValueError("Каталог уже содержит Assets; повторный импорт запрещён")
    result = extract(args.package, project)
    for relative in ["Assets/Prefabs/LevelDesign/LD_Alphabet", "Assets/Art/Models/LevelDesign"]:
        shutil.copytree(game / relative, project / relative)
    editor = project / "Assets/Editor/VR_Battlegrounds/LevelDesign"
    editor.mkdir(parents=True, exist_ok=True)
    for source in (game / "Tools/AssetCatalog").glob("*.cs"):
        shutil.copyfile(source, editor / source.name)
    packages = project / "Packages"
    packages.mkdir(parents=True, exist_ok=True)
    (packages / "manifest.json").write_text('{"dependencies":{}}\n', encoding="utf-8")
    settings = project / "ProjectSettings"
    settings.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(game / "ProjectSettings/ProjectVersion.txt", settings / "ProjectVersion.txt")
    # Теги нужны лишь для загрузки эталонных префабов; игровые настройки не меняются.
    shutil.copyfile(game / "ProjectSettings/TagManager.asset", settings / "TagManager.asset")
    (project.parent / "extraction.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()

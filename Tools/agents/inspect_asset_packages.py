"""Грубая инвентаризация unitypackage без распаковки и импорта в Unity."""

import argparse
import collections
import gzip
import json
import pathlib
import tarfile


def inspect(package):
    paths = []
    # Unity хранит метаданные магазина в FEXTRA заголовка gzip.
    # GzipFile корректно пропускает их, в отличие от gzip-режима tarfile._Stream.
    with gzip.open(package, "rb") as compressed, tarfile.open(fileobj=compressed, mode="r|") as archive:
        for entry in archive:
            if entry.isfile() and entry.name.endswith("/pathname"):
                source = archive.extractfile(entry)
                paths.append(source.read().decode("utf-8", errors="replace").splitlines()[0].rstrip("\x00"))
    extensions = collections.Counter(pathlib.PurePosixPath(p).suffix.lower() for p in paths)
    models = [p for p in paths if pathlib.PurePosixPath(p).suffix.lower() in
              {".fbx", ".obj", ".blend", ".dae", ".gltf", ".glb", ".asset"}]
    prefabs = [p for p in paths if p.lower().endswith(".prefab")]
    folders = collections.Counter(str(pathlib.PurePosixPath(p).parent) for p in models + prefabs)
    return {
        "package": str(package), "size_mb": round(package.stat().st_size / 1048576, 1),
        "entries": len(paths), "extensions": dict(extensions.most_common()),
        "model_and_asset_paths": models, "prefab_paths": prefabs,
        "main_folders": folders.most_common(25),
        "scene_paths": [p for p in paths if p.lower().endswith(".unity")],
        "script_paths": [p for p in paths if p.lower().endswith(".cs")],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cache", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    results = []
    for package in sorted(args.cache.rglob("*.unitypackage")):
        if not any(part.startswith("3D ModelsEnvironments") or part == "3D ModelsProps"
                   for part in package.parts):
            continue
        try:
            result = inspect(package)
        except (OSError, tarfile.TarError) as error:
            result = {"package": str(package), "error": str(error)}
        results.append(result)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({k: v for k, v in result.items() if k in
                         {"package", "entries", "extensions", "error"}}, ensure_ascii=False), flush=True)
    if not results or any("error" in result for result in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()

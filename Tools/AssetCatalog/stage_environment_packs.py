"""Распаковывает окружение вне Assets и сохраняет манифест для безопасного импорта."""

import argparse
import gzip
import hashlib
import json
import pathlib
import shutil
import tarfile


def entries(package):
    result = {}
    with gzip.open(package, "rb") as compressed, tarfile.open(fileobj=compressed, mode="r|") as archive:
        for entry in archive:
            if entry.isfile() and entry.name.endswith("/pathname"):
                name = archive.extractfile(entry).read().decode("utf-8").splitlines()[0].rstrip("\0")
                path = pathlib.PurePosixPath(name)
                if not path.parts or path.is_absolute() or ".." in path.parts or ":" in name or "\\" in name:
                    raise ValueError("Небезопасный путь: " + name)
                if path.parts[0] != "Assets":
                    # Некоторые паки включают свой Packages/manifest.json и ProjectSettings.
                    # Это настройки демопроекта, а не импортируемое окружение.
                    continue
                result[entry.name.rsplit("/", 1)[0]] = name
    return result


def extract(package, destination):
    mapping = entries(package)
    records = {}
    with gzip.open(package, "rb") as compressed, tarfile.open(fileobj=compressed, mode="r|") as archive:
        for item in archive:
            if not item.isfile() or "/" not in item.name:
                continue
            guid, part = item.name.rsplit("/", 1)
            if guid not in mapping or part not in {"asset", "asset.meta"}:
                continue
            relative = mapping[guid]
            target = destination.joinpath(*pathlib.PurePosixPath(relative).parts)
            if part == "asset.meta":
                target = pathlib.Path(str(target) + ".meta")
            target.parent.mkdir(parents=True, exist_ok=True)
            # Метаданные каталогов могут прийти после создания самого каталога.
            with target.open("wb") as output:
                shutil.copyfileobj(archive.extractfile(item), output)
            record = records.setdefault(guid, {"guid": guid, "path": relative})
            record[part] = {"source": str(target), "bytes": target.stat().st_size}
            if part == "asset" and target.suffix.lower() in {".cs", ".txt", ".json"} and target.stat().st_size < 100000:
                record["text_sample"] = target.read_text(encoding="utf-8", errors="replace")[:12000]
    return list(records.values())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inventory", type=pathlib.Path)
    parser.add_argument("stage", type=pathlib.Path)
    args = parser.parse_args()
    stage = args.stage.resolve()
    if pathlib.Path.cwd().resolve() / "Temp" / "LevelDesign" not in stage.parents:
        raise ValueError("Подготовка допускается только внутри Temp/LevelDesign")
    stage.mkdir(parents=True, exist_ok=True)
    packages = json.loads(args.inventory.read_text(encoding="utf-8"))
    manifest = []
    for index, package in enumerate(packages):
        source = pathlib.Path(package["package"])
        destination = stage / ("pack_" + str(index).zfill(2))
        checkpoint = destination / "manifest.json"
        if checkpoint.exists():
            record = json.loads(checkpoint.read_text(encoding="utf-8"))
        else:
            assets = extract(source, destination)
            record = {"package": str(source), "assets": assets}
            checkpoint.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
        manifest.append(record)
        (stage / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        nested = [a["path"] for a in record["assets"] if a["path"].lower().endswith(".unitypackage")]
        print(json.dumps({"package": source.name, "assets": len(record["assets"]), "nested": nested}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()

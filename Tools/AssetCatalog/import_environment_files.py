"""Копирует подготовленные ассеты под замком Unity, без перезаписи существующих файлов."""

import argparse
import hashlib
import json
import pathlib
import re
import shutil
import time

from plan_environment_import import digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", type=pathlib.Path)
    parser.add_argument("--owner", required=True)
    args = parser.parse_args()
    game = pathlib.Path.cwd().resolve()
    lock = game / "tmp/unity-lock/info"

    def check_lock():
        values = dict(line.split("=", 1) for line in lock.read_text(encoding="utf-8").splitlines() if "=" in line)
        if values.get("owner") != args.owner or int(values.get("until", "0")) <= time.time():
            raise ValueError("Нет действующего замка Unity у " + args.owner)

    check_lock()
    plan = json.loads(args.plan.read_text(encoding="utf-8"))
    if plan["conflicts"] or len(plan["packages"]) != 13:
        raise ValueError("План импорта неполон или содержит конфликты")
    excluded_roots = json.loads(pathlib.Path(__file__).with_name("catalog-policy.json").read_text(encoding="utf-8"))["excludedAssetRoots"]
    plan["files"] = [f for f in plan["files"] if not any(f["path"].lower().startswith(root.lower()) for root in excluded_roots)]
    assets_root = game / "Assets"
    copied = []
    for index, item in enumerate(plan["files"]):
        if index % 100 == 0:
            check_lock()
        target = (game / item["path"]).resolve()
        if assets_root not in target.parents:
            raise ValueError("Путь вне Assets: " + str(target))
        meta = pathlib.Path(str(target) + ".meta")
        if target.exists() or meta.exists():
            if not target.is_file() or not meta.is_file() or digest(target) != item["sha256"]:
                raise ValueError("Существующий файл нельзя перезаписать: " + str(target))
            text = meta.read_text(encoding="utf-8")
            if not re.search(r"^guid: " + item["guid"] + r"\s*$", text, re.MULTILINE):
                raise ValueError("GUID не совпадает: " + str(meta))
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            # xb исключает перезапись даже при гонке с другим процессом.
            with pathlib.Path(item["source"]).open("rb") as source, target.open("xb") as output:
                checksum = hashlib.sha256()
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    checksum.update(chunk)
                    output.write(chunk)
            if checksum.hexdigest() != item["sha256"]:
                raise ValueError("Источник изменился после подготовки плана: " + str(target))
            with pathlib.Path(item["meta"]).open("rb") as source, meta.open("xb") as output:
                shutil.copyfileobj(source, output)
        copied.append(item["path"])
        if index % 100 == 0:
            (args.plan.parent / "copy-progress.json").write_text(json.dumps({"copied": len(copied), "total": len(plan["files"])}), encoding="utf-8")
            print(str(len(copied)) + "/" + str(len(plan["files"])), flush=True)
    result = {"copied": len(copied), "total": len(plan["files"]), "complete": True}
    (args.plan.parent / "copy-progress.json").write_text(json.dumps(result), encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()

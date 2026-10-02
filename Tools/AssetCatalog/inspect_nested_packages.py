"""Проверяет вложенные варианты URP до выбора файлов для импорта."""

import argparse
import collections
import json
import pathlib

from stage_environment_packs import entries, extract


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", type=pathlib.Path)
    args = parser.parse_args()
    stage = args.stage.resolve()
    packages = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
    result = []
    for index, package in enumerate(packages):
        variants = []
        for asset in package["assets"]:
            if asset["path"].lower().endswith(".unitypackage") and "urp" in asset["path"].lower():
                source = pathlib.Path(asset["asset"]["source"])
                destination = stage / ("urp_" + str(index).zfill(2))
                checkpoint = destination / "manifest.json"
                if checkpoint.exists():
                    record = json.loads(checkpoint.read_text(encoding="utf-8"))
                else:
                    record = {"package": str(source), "assets": extract(source, destination)}
                    checkpoint.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
                variants.append(record)
                counts = collections.Counter(pathlib.PurePosixPath(a["path"]).suffix.lower() for a in record["assets"] if "asset" in a)
                print(json.dumps({"nested": asset["path"], "extensions": counts}, ensure_ascii=False), flush=True)
        result.append({"original": package, "urp": variants})
        (stage / "resolved-packages.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()

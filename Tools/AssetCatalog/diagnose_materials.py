"""Находит исходные ссылки отсутствующих шейдеров и использование материалов."""
import collections
import json
import pathlib
import re

stage = pathlib.Path("Temp/LevelDesign/EnvironmentImport")
plan = json.loads((stage / "import-plan.json").read_text(encoding="utf-8"))
excluded_roots = json.loads(pathlib.Path(__file__).with_name("catalog-policy.json").read_text(encoding="utf-8"))["excludedAssetRoots"]
plan["files"] = [f for f in plan["files"] if not any(f["path"].lower().startswith(root.lower()) for root in excluded_roots)]
verification = json.loads((stage / "unity-verification.json").read_text(encoding="utf-8"))
failed = [s.removeprefix("Нет шейдера: ") for s in verification["issues"] if s.startswith("Нет шейдера: ")]
by_path = {f["path"]: f for f in plan["files"]}
references = collections.Counter()
for f in plan["files"]:
    if f["path"].endswith(".prefab"):
        references.update(set(re.findall(r"guid: ([0-9a-f]{32})", pathlib.Path(f["path"]).read_text(encoding="utf-8-sig"))))
known = {}
for pack in json.loads((stage / "resolved-packages.json").read_text(encoding="utf-8")):
    for variant in [pack["original"]] + pack["urp"]:
        for a in variant["assets"]:
            known[a["guid"]] = a["path"]
groups = {}
for path in failed:
    text = pathlib.Path(path).read_text(encoding="utf-8-sig")
    shader = re.search(r"m_Shader: \{([^}]+)\}", text)[1]
    group = groups.setdefault(shader, {"count": 0, "referenced": 0, "samples": [], "known": ""})
    group["count"] += 1
    group["referenced"] += bool(references[by_path[path]["guid"]])
    guid = re.search(r"guid: ([0-9a-f]{32})", shader)
    group["known"] = known.get(guid[1], "") if guid else "builtin"
    if len(group["samples"]) < 3:
        group["samples"].append({"path": path, "prefab_references": references[by_path[path]["guid"]],
                                 "properties": re.findall(r"^\s+- ([^:]+):", text, re.MULTILINE)})
report = {"shader_groups": groups, "other_issues": [s for s in verification["issues"] if not s.startswith("Нет шейдера: ")]}
(stage / "material-diagnosis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
broken_prefabs = sorted({s.split(": ", 1)[1].split(".prefab/", 1)[0] + ".prefab"
                         for s in verification["issues"] if ".prefab/" in s})
eligibility = {"excluded_prefabs": broken_prefabs,
               "eligible_prefabs": [{"path": f["path"], "guid": f["guid"]} for f in plan["files"]
                                    if f["path"].endswith(".prefab") and f["path"] not in broken_prefabs],
               "unused_hdrp_materials": failed,
               "note": "Пригодность означает целые ссылки, а не соответствие игровому блоку или бюджету Quest."}
(stage / "catalog-eligibility.json").write_text(json.dumps(eligibility, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"eligible_prefabs": len(eligibility["eligible_prefabs"]), "excluded_prefabs": broken_prefabs,
                  "missing_shaders": len(failed), "shader_groups": len(groups),
                  "missing_materials_referenced_directly_by_prefabs": sum(g["referenced"] for g in groups.values()),
                  "other_issues": len(report["other_issues"])}, ensure_ascii=False))

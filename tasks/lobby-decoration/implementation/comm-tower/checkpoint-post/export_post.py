# -*- coding: utf-8 -*-
"""Экспорт поста-КПП (экземпляр post 5 из post-variants.json, мир ~(-71.76, 51.17, -886.0) в LV_Showcase)
в checkpoint-post.json: y=0 — низ бетонного цоколя (земля), начало — центр коробки, +Z — сторона двери.
"""
import json, math, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../../reports/implementation/comm-tower"))
OUTREP = os.path.join(REP, "checkpoint-post")
PROJ = os.path.normpath(os.path.join(ROOT, "../../../../.."))
IMPORTED_DIR = os.path.join(PROJ, "Assets/Art/Lobby/Imported/MilitaryCamp")
PACK = r"F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs/MilitaryCamp/HDRP(Default)/Art"
URP_INDEX = os.path.normpath(os.path.join(ROOT, "../../../reports/asset-evaluation/military-base-megapack-372662/urp-source-cache/index.json"))
POST = int(sys.argv[1]) if len(sys.argv) > 1 else 5
GROUND = 0.367  # низ SM_Border_2m (цоколь) под отметкой пола стен

d = json.load(open(os.path.join(OUTREP, "post-variants.json"), encoding="utf-8"))
post = d["posts"][POST]
common = set(tuple(k) for k in d["commonKeys"])
key = lambda q: (q["stem"], round(q["position"][0], 1), round(q["position"][1], 1), round(q["position"][2], 1))

INTERIOR = ("SM_AmmoCartridgeBoxes", "SM_AmmoCase", "SM_MilitaryCrate", "SM_Chair", "SM_ClothesCabinet", "SM_cabinet",
            "SM_MetalShelves", "SM_Lamp_long")
DECK = ("SM_Fence_", "SM_Stairs_", "SM_Concrete_pile", "SM_Cornice_")
# переменные объекты экземпляра: что берём (по описанию пользователя), остальное — окружение
KEEP_VAR = {"SM_StrappedContainer", "SM_SandBarrier_01", "SM_SandBarrier_02", "SM_SandBarrier_03", "SM_Tire_02", "SM_Fence_90d"}


def group(q, is_common):
    s = q["stem"]; y = q["position"][1]
    if not is_common:
        return "roofProps" if y > 2.5 and not s.startswith("SM_Fence") else ("deck" if s.startswith("SM_Fence") else "groundProps")
    if s.startswith(INTERIOR):
        return "interior"
    if s.startswith(DECK):
        return "deck"
    return "shell"


fbx = json.load(open(os.path.join(REP, "fbx-tris.json"), encoding="utf-8"))
def fbx_name(stem):
    p = os.path.join(PACK, "Prefabs", stem + ".prefab")
    meta = {}
    txt = open(p, encoding="utf-8").read()
    # FBX первого MeshFilter/ссылки: ищем guid модели среди .fbx.meta
    for g in sorted(set(re.findall(r"guid: ([0-9a-f]{32})", txt))):
        meta[g] = True
    for f in os.listdir(os.path.join(PACK, "Assets")):
        if f.endswith(".fbx.meta"):
            g = re.search(r"guid: ([0-9a-f]{32})", open(os.path.join(PACK, "Assets", f), encoding="utf-8").read()).group(1)
            if g in meta:
                return f[:-5]
    return None

need = sorted(set(q["stem"] for q in post["parts"]))
fbx_of = {s: fbx_name(s) for s in need}
missing_fbx = sorted(set(v for v in fbx_of.values() if v and v not in fbx))
if missing_fbx:
    subprocess.run([sys.executable, "-X", "utf8", "-I", os.path.join(ROOT, "..", "fbx_tris.py")] + [os.path.join(PACK, "Assets", f) for f in missing_fbx], check=True)
    fbx = json.load(open(os.path.join(REP, "fbx-tris.json"), encoding="utf-8"))

imported = set(f[:-7] for _, _, fs in os.walk(IMPORTED_DIR) for f in fs if f.endswith(".prefab"))
urp = {os.path.basename(a["path"]): a["guid"] for a in json.load(open(URP_INDEX, encoding="utf-8"))["assets"]}

parts = []
for q in post["parts"]:
    c = key(q) in common
    if not c and q["stem"] not in KEEP_VAR:
        continue
    # переменные ограничиваем пятном здания (+ навес): без генераторов/баков/заборов за пределами
    x, y, z = q["position"]
    if not c and not (-6.5 <= x <= 4.5 and -6.0 <= z <= 6.0):
        continue
    f = fbx_of.get(q["stem"])
    t = fbx[f]["models"][0]["triangles"] if f in fbx else None
    parts.append({"stem": q["stem"], "group": group(q, c),
                  "position": [x, round(y + GROUND, 3), z], "rotationEuler": q["rotationEuler"],
                  "quaternion": q["quaternion"], "scale": q["scale"], "imported": q["stem"] in imported,
                  "lod0Triangles": t})
parts.sort(key=lambda p: (p["group"], p["stem"], p["position"][1], p["position"][0], p["position"][2]))

from collections import Counter, defaultdict
tri = defaultdict(int)
for p in parts:
    tri[p["group"]] += p["lod0Triangles"] or 0
missing = sorted(set(p["stem"] for p in parts if not p["imported"]))
doc = {
    "name": "checkpoint-post",
    "description": "Одноэтажный пост из модулей (LV_Showcase, 6 одинаковых экземпляров; взят экземпляр с ящиками и мешками на крыше): коробка 6.5x9 м по осям стен, плоская крыша-площадка с навесом 2.35 м на стороне -X на 5 столбах, перила, наружная лестница, интерьер (стеллаж с ящиками, шкафы)",
    "source": "HDRP(Default)/Scenes/Showcase/LV_Showcase.unity (MilitaryCamp); скрипты find_posts.py/post_local.py/export_post.py",
    "sourceInstance": {"index": POST, "worldOrigin": post["origin"], "worldYaw": post["yaw"]},
    "frame": "y=0 — низ бетонного цоколя SM_Border_2m (земля); пол стен на y=0.367; начало — центр коробки стен; +Z — сторона двери (наружу); эйлер Unity ZXY и кватернион [x,y,z,w]",
    "footprint": {"wallAxesXZ": [6.5, 9.0], "plinthOuterXZ": [6.7, 9.2], "roofDeckX": [-5.75, 3.4], "roofDeckZ": [-4.65, 4.65],
                  "canopySide": "-X (вынос ~2.35 м, 5 столбов SM_Concrete_pile_A на x=-5.6)",
                  "heights": {"floor": 0.367, "roofDeckTop": 3.517, "railingTop": 4.5}},
    "triangles": {"lod0ByGroup": dict(tri), "lod0Total": sum(tri.values()),
                  "lod0WithoutInterior": sum(v for k, v in tri.items() if k != "interior")},
    "missingStems": missing,
    "missingStemGuids": {s: urp.get(s + ".prefab") for s in missing},
    "parts": parts,
}
json.dump(doc, open(os.path.join(ROOT, "checkpoint-post.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("parts", len(parts), Counter(p["group"] for p in parts))
print("tris", dict(tri), "total", sum(tri.values()))
print("missing", {s: urp.get(s + ".prefab") for s in missing})
print("noFbx", [p["stem"] for p in parts if p["lod0Triangles"] is None])

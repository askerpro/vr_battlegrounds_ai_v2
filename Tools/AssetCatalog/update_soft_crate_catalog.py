"""Обновляет геометрический каталог и сохраняемые рецепты после добавления Soft-ящика."""
import copy
import json
import pathlib
import shutil
import subprocess
import sys
from rank_candidates import recipe_masks

root = pathlib.Path(__file__).resolve().parents[2]
folder = root / "Temp/LevelDesign/IndustrialAnalysis"
path = folder / "measurements.json"
catalog = json.loads(path.read_text(encoding="utf-8"))
# Внешний меш Soft-ящика идентичен исходному кубу; полость меняет только коллайдеры.
block = copy.deepcopy(next(b for b in catalog["blocks"] if b["name"] == "LD_Crate"))
block.update(name="LD_Crate_Soft", path="Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Crate_Soft.prefab",
             colliders=6, geometrySource="LD_Crate: identical exterior cube mesh")
catalog["blocks"] = [b for b in catalog["blocks"] if b["name"] != block["name"]] + [block]
model = next(m for m in catalog["models"] if m["name"] == "Concrete_fence_v1_wall_set_v2")
scale = 1.6 / model["size"]["y"]
fence = copy.deepcopy(next(b for b in catalog["blocks"] if b["name"] == "LD_Wall_Mid"))
fence.update(name="LD_Fence_Mid_Hard", path="Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Fence_Mid_Hard.prefab",
             size=dict(x=9.065, y=1.6, z=0.349),
             geometrySource="Rectangular proxy matching uniformly scaled concrete fence")
catalog["blocks"] = [b for b in catalog["blocks"] if b["name"] != fence["name"]] + [fence]
model = next(m for m in catalog["models"] if m["name"] == "Concrete_fence_v2_S")
scale = 1.6 / model["size"]["y"]
fence = copy.deepcopy(next(b for b in catalog["blocks"] if b["name"] == "LD_Wall_Mid_Soft"))
fence.update(name="LD_Fence_Mid_Soft", path="Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Fence_Mid_Soft.prefab",
             size=dict(x=3.538, y=1.6, z=0.336),
             geometrySource="Fixed rectangular blocking proxy; no decorative mesh",
             collision_height=1.6)
catalog["blocks"] = [b for b in catalog["blocks"] if b["name"] != fence["name"]] + [fence]
for model_name, block_name in [("Palet_v1_set", "LD_PalletFence_Set_Soft"),
                               ("Palet_v1_single", "LD_PalletFence_Single_Soft")]:
    model = next(m for m in catalog["models"] if m["name"] == model_name)
    scale = 1.6 / model["size"]["z"]
    block = copy.deepcopy(next(b for b in catalog["blocks"] if b["name"] == "LD_Wall_Mid_Soft"))
    block.update(name=block_name, path="Assets/Prefabs/LevelDesign/LD_Alphabet/" + block_name + ".prefab",
                 size=dict(x=2.0 if model_name == "Palet_v1_set" else 2.01, y=1.6, z=0.1),
                 geometrySource="Fixed rectangular blocking proxy; no pallet boards")
    block["coverage"] = [mask.count("1") / 256 for mask in block["masks"]]
    catalog["blocks"] = [b for b in catalog["blocks"] if b["name"] != block_name] + [block]
path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding="utf-8")
subprocess.run([sys.executable, str(root / "Tools/AssetCatalog/rank_candidates.py"), str(path)], check=True)
shutil.copyfile(folder / "recipes-for-unity.json",
                root / "Assets/Editor/VR_Battlegrounds/LevelDesign/Data/IndustrialCandidateRecipes.json")

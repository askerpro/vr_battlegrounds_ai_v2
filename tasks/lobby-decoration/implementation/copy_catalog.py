"""Копирование только выбранных моделей/материалов и их замкнутых зависимостей."""
from pathlib import Path
import re,json,shutil,hashlib
from prepare_assets import ROOT, OUT, REPORT, folders

catalog=Path('F:/UnityProjects/EnvironmentPackCatalog')
packs=catalog/'Assets/env_packs'
chosen=[
 packs/'HauntedFarmHouse/URP/Art/Prefabs/SM_Crate_A_01.prefab',
 packs/'RPG_FPS_game_assets_industrial/Other_props/Generators/Generator_v1/Generator_v1.prefab',
 packs/'RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir/Meshes/SM_SilverFir_M_01.fbx']
fir=packs/'RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir'
chosen+=list(fir.rglob('M_SilverFir_Bark_01.mat'))+list(fir.rglob('M_SilverFir_Atlas_01.mat'))
index={}
for pack in ['HauntedFarmHouse','RPG_FPS_game_assets_industrial','RuralTown']:
 for meta in (packs/pack).rglob('*.meta'):
  match=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
  if match:index[match[1]]=Path(str(meta)[:-5])
existing={}
for meta in (ROOT/'Assets').rglob('*.meta'):
 match=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
 if match:existing[match[1]]=Path(str(meta)[:-5])
seen=set(); records=[];unresolved=set()
def copy(src):
 if src in seen:return
 seen.add(src)
 assert src.is_file(),src
 sm=Path(str(src)+'.meta');guid=re.search(r'^guid: ([0-9a-f]{32})',sm.read_text(encoding='utf-8-sig'),re.M)[1]
 dest=OUT/'Catalog'/src.relative_to(packs)
 if guid in existing and existing[guid]!=dest:
  same=hashlib.sha256(existing[guid].read_bytes()).digest()==hashlib.sha256(src.read_bytes()).digest()
  # Одноимённый материал уже адаптирован игровым проектом: сохраняем его владельца.
  if not same and existing[guid].relative_to(ROOT).as_posix()!=src.relative_to(catalog).as_posix():raise RuntimeError('GUID collision: '+str(src)+' '+str(existing[guid]))
  records.append({'source':str(src),'reuse':str(existing[guid].relative_to(ROOT)),'guid':guid});return
 folders(dest.parent);shutil.copy2(src,dest);shutil.copy2(sm,Path(str(dest)+'.meta'));existing[guid]=dest
 records.append({'source':str(src),'destination':str(dest.relative_to(ROOT)),'guid':guid,'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
 if src.suffix in ['.prefab','.mat','.asset']:
  for ref in re.findall(r'guid: ([0-9a-f]{32})',src.read_text(encoding='utf-8-sig',errors='replace')):
   if ref in index:copy(index[ref])
   elif ref not in existing and not ref.startswith('0000000000000000'):unresolved.add(ref)
for src in chosen:copy(src)
(REPORT/'catalog-manifest.json').write_text(json.dumps({'files':records,'unresolved':sorted(unresolved)},indent=2),encoding='utf-8')
print(json.dumps({'copied_or_reused':len(records),'unresolved':sorted(unresolved)},indent=2))

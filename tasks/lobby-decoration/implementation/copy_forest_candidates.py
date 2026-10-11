"""Два кандидата для визуальной оценки, внешний каталог только читается."""
from pathlib import Path
import re, hashlib, shutil, json
root=Path(__file__).resolve().parents[3]
packs=Path('F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs')
chosen=[packs/'RuralTown/URP/Art/Prefabs/SM_SilverFir_L_02.prefab',packs/'AbandonedFactory/Art/Prefabs/SM_SilverFir_06.prefab']
index={}
for pack in ['RuralTown','AbandonedFactory']:
    for meta in (packs/pack).rglob('*.meta'):
        m=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig',errors='ignore'),re.M)
        if m:index[m[1]]=Path(str(meta)[:-5])
existing={}
for meta in (root/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig',errors='ignore'),re.M)
    if m:existing[m[1]]=Path(str(meta)[:-5])
records=[];seen=set();external=set()
def copy(source):
    if source in seen:return
    seen.add(source)
    if not source.is_file():return
    meta=Path(str(source)+'.meta'); guid=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig'),re.M)[1]
    target=root/'Assets/Art/Lobby/Imported/Catalog'/source.relative_to(packs)
    if guid in existing:
        current=existing[guid]
        if hashlib.sha256(current.read_bytes()).digest()!=hashlib.sha256(source.read_bytes()).digest():
            raise RuntimeError('Различные файлы одного GUID: '+str(current)+' / '+str(source))
        records.append({'guid':guid,'reuse':str(current.relative_to(root))});return
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target);shutil.copyfile(meta,Path(str(target)+'.meta'))
    existing[guid]=target;records.append({'guid':guid,'source':str(source),'path':str(target.relative_to(root)),'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
    if source.suffix in ['.prefab','.mat','.asset','.shadergraph','.shadersubgraph']:
        for ref in re.findall(r'(?<![0-9a-f])([0-9a-f]{32})(?![0-9a-f])',source.read_text(encoding='utf-8-sig',errors='ignore')):
            if ref in index:copy(index[ref])
            elif ref not in existing and ref!='0'*32:external.add(ref)
for source in chosen:copy(source)
p=root/'tasks/lobby-decoration/reports/implementation/forest-candidates-copy.json'
p.write_text(json.dumps({'files':records,'external':sorted(external)},indent=2),encoding='utf-8')
print(json.dumps({'files':len(records),'external':len(external),'report':str(p)}))

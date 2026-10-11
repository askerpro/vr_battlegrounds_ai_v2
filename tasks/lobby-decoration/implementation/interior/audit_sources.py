"""Проверка provenance/GUID и свойств новых материалов после фоновой сериализации Editor."""
from pathlib import Path
import re, json, hashlib

root=Path(__file__).resolve().parents[4]
report=root/'tasks/lobby-decoration/reports/implementation/interior'
path=report/'real-source-manifest.json'
manifest=json.loads(path.read_text())
errors=[]; index={}; reserialized=[]
for meta in (root/'Assets').rglob('*.meta'):
    match=re.search(r'^guid: ([a-f0-9]{32})',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
    if match:
        if match[1] in index:errors.append('duplicate GUID '+match[1])
        index[match[1]]=meta
for record in manifest['files']:
    if 'destination' not in record:continue
    dest=root/record['destination']
    actual=hashlib.sha256(dest.read_bytes()).hexdigest()
    adaptation=record.get('adaptation')
    if adaptation:
        material=dest.read_text(encoding='utf-8-sig')
        if 'guid: 933532a4fcc9baf4fa0491de14d08ed7' not in material:errors.append('URP/Lit lost')
        for prop,key in [('_BaseMap','base_guid'),('_BumpMap','normal_guid')]:
            if adaptation[key]:
                m=re.search(r'^    - '+prop+r':\n        m_Texture: .*guid: ([a-f0-9]{32})',material,re.M)
                if not m or m[1]!=adaptation[key]:errors.append('texture changed '+prop+' '+str(dest))
        for guid in re.findall(r'guid: ([a-f0-9]{32})',material):
            if guid not in index and guid not in {'933532a4fcc9baf4fa0491de14d08ed7','d0353a89b1f911e48b9e16bdc9f2e058'}:
                errors.append('unresolved material '+guid)
        if actual!=adaptation['actual_sha256']:
            adaptation.setdefault('adapter_sha256',adaptation['actual_sha256'])
            adaptation['actual_sha256']=actual
            adaptation['serialization_note']='Фоновый импорт открытого Editor: URP AssetVersion/упорядочивание/поля Material; source texture GUID и shader сохранены'
            reserialized.append(str(dest.relative_to(root)))
    elif actual!=record['sha256']:errors.append('source asset SHA changed '+str(dest))
    if Path(str(dest)+'.meta').read_bytes()!=Path(record['source']+'.meta').read_bytes():errors.append('source meta changed '+str(dest))
for record in manifest['generated_maps']:
    if hashlib.sha256((root/record['destination']).read_bytes()).hexdigest()!=record['sha256']:
        errors.append('generated map SHA changed '+record['destination'])
value={'source_files':len(manifest['files']),'generated_maps':len(manifest['generated_maps']),
       'materials_reserialized_by_open_editor':reserialized,'errors':errors,'passed':not errors,
       'scope':'Offline source integrity/GUID/material closure; not native Unity preview, scene or Android'}
path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(report/'real-source-audit.json').write_text(json.dumps(value,indent=2),encoding='utf-8')
print(json.dumps({'passed':not errors,'errors':errors,'source_files':len(manifest['files']),
                  'generated_maps':len(manifest['generated_maps']),'reserialized_materials':len(reserialized)},indent=2))
raise SystemExit(bool(errors))

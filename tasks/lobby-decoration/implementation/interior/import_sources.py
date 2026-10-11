"""Выбранные локальные источники: анализ замкнутых зависимостей и копия с неизменными GUID."""
from pathlib import Path
import argparse, re, json, shutil, hashlib, uuid
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[4]
PACKS = Path('F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs')
OUT = ROOT / 'Assets/Art/Lobby/Interior/Imported'
REPORT = ROOT / 'tasks/lobby-decoration/reports/implementation/interior'
SELECTED = [PACKS/'HauntedFarmHouse/URP/Art/Prefabs/SM_Mug_01.prefab'] + [
    PACKS/f'RuralTown/URP/Art/Prefabs/SM_Chair_{n}.prefab' for n in ('02',)]

def guid(meta):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
    return match[1] if match else None

def refs(path):
    if path.suffix.lower() not in ('.prefab', '.mat', '.asset', '.shadergraph', '.shadersubgraph', '.meta'):
        return set()
    return set(re.findall(r'(?:guid: |"guid"\s*:\s*")([a-f0-9]{32})', path.read_text(encoding='utf-8-sig', errors='replace')))

def folders(path):
    if path.exists(): return
    if path == ROOT/'Assets/Art/Lobby': raise RuntimeError('Общая папка Lobby не подготовлена')
    folders(path.parent)
    path.mkdir()
    meta = Path(str(path)+'.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

def textures(src):
    value=src.read_text(encoding='utf-8-sig')
    result={}
    for match in re.finditer(r'^    - ([^:]+):\n(        m_Texture:.*\n        m_Scale:.*\n        m_Offset:.*)',value,re.M):
        block=match[2]
        g=re.search(r'guid: ([a-f0-9]{32})',block)
        if g: result[match[1]]=(g[1],block)
    def pick(names): return next((result[n] for n in names if n in result),None)
    return value, pick(('_BaseMap','_MainTex','_BaseColorMap')), pick(('_NormalMap','_BumpMap')), pick(('_ORMH','_ORM','_MaskMap'))

generated=[]
def adapt(src,dest):
    value,base,normal,orm=textures(src)
    def scalar(names,default):
        for name in names:
            m=re.search(r'^    - '+re.escape(name)+r': ([\d.e+-]+)$',value,re.M)
            if m:return float(m[1])
        return default
    rough=scalar(('_Roughness',),1)
    metal=scalar(('_Metallic',),0)
    clip=scalar(('_AlphaClip','_AlphaCutoffEnable'),0)
    entries=[]
    if base:entries.append('    - _BaseMap:\n'+base[1])
    if normal:entries.append('    - _BumpMap:\n'+normal[1])
    if orm:
        origin=index[orm[0]]
        channels=Image.open(origin).convert('RGB').split()
        zero=Image.new('L',channels[0].size,0)
        alpha=channels[1].point(lambda v:round(255-max(0,min(255,v*rough))))
        for name,img in [('MetallicSmoothness',Image.merge('RGBA',(channels[2],zero,zero,alpha))),
                         ('Occlusion',Image.merge('RGB',(channels[0],channels[0],channels[0])))]:
            path=OUT/'ConvertedMaps'/f'{src.stem}_{name}.png'
            folders(path.parent)
            if path.exists():raise RuntimeError('Нельзя перезаписать packedmap')
            img.save(path)
            g=uuid.uuid4().hex
            meta=Path(str(origin)+'.meta').read_text(encoding='utf-8-sig')
            meta=re.sub(r'^guid: [a-f0-9]{32}', 'guid: '+g, meta,flags=re.M)
            meta=re.sub(r'    sRGBTexture: \d+', '    sRGBTexture: 0',meta)
            meta=re.sub(r'  alphaUsage: \d+', '  alphaUsage: 1\n  alphaSource: 1',meta)
            Path(str(path)+'.meta').write_text(meta,encoding='utf-8')
            prop='_MetallicGlossMap' if name=='MetallicSmoothness' else '_OcclusionMap'
            uv=orm[1].split('\n',1)[1]
            entries.append(f'    - {prop}:\n        m_Texture: {{fileID: 2800000, guid: {g}, type: 3}}\n'+uv)
            generated.append({'destination':str(path.relative_to(ROOT)),'guid':g,'source':str(origin),
                'conversion': 'ORM.b -> metallic.r; 1-ORM.g*roughness -> smoothness.a' if name=='MetallicSmoothness' else 'ORM.r -> URP occlusion.g',
                'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    colors=re.search(r'^    - _BaseColor: (\{.*\})$',value,re.M) or re.search(r'^    - _Color: (\{.*\})$',value,re.M)
    tint=colors[1] if colors else '{r: 1, g: 1, b: 1, a: 1}'
    keywords=[]
    if normal:keywords.append('_NORMALMAP')
    if orm:keywords += ['_METALLICSPECGLOSSMAP','_OCCLUSIONMAP']
    if clip:keywords.append('_ALPHATEST_ON')
    valid='\n'+''.join('  - '+k+'\n' for k in keywords) if keywords else ' []\n'
    text='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_Name: '+src.stem+'\n  m_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}\n  m_Parent: {fileID: 0}\n  m_ModifiedSerializedProperties: 0\n  m_ValidKeywords:'+valid+'  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 1\n  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: -1\n  stringTagMap: {}\n  disabledShaderPasses: []\n  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n'+('\n'.join(entries)+'\n' if entries else '    []\n')+'    m_Ints: []\n    m_Floats:\n'
    for k,v in [('_WorkflowMode',1),('_Surface',0),('_Blend',0),('_AlphaClip',clip),('_Cutoff',scalar(('_Cutoff','_AlphaCutoff'),.5)),('_Cull',2),('_SrcBlend',1),('_DstBlend',0),('_ZWrite',1),('_Metallic',1 if orm else metal),('_Smoothness',1 if orm else 1-rough),('_BumpScale',scalar(('_NormalStrength','_NormalScale','_BumpScale'),1)),('_OcclusionStrength',scalar(('_AOIntensity',),1)),('_ReceiveShadows',1)]:
        text+='    - '+k+': '+str(v)+'\n'
    text+='    m_Colors:\n    - _BaseColor: '+tint+'\n    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}\n  m_BuildTextureStacks: []\n'
    dest.write_text(text,encoding='utf-8')
    return {'adapter':'URP/Lit','base_guid':base[0] if base else None,'normal_guid':normal[0] if normal else None,
            'orm_guid':orm[0] if orm else None,'roughness_multiplier':rough,'alpha_clip':clip,
            'removed':'historical upgrader MonoBehaviour, custom shader dust/blend effects; source geometry and UV unchanged',
            'actual_sha256':hashlib.sha256(dest.read_bytes()).hexdigest()}

parser = argparse.ArgumentParser()
parser.add_argument('--copy', action='store_true', help='Копировать после проверки списка общих GUID')
args = parser.parse_args()
index = {}
for pack in ('HauntedFarmHouse', 'RuralTown'):
    for meta in (PACKS/pack/'URP').rglob('*.meta'):
        g = guid(meta)
        if g: index[g] = Path(str(meta)[:-5])
existing = {}
for meta in (ROOT/'Assets').rglob('*.meta'):
    g = guid(meta)
    if g:
        if g in existing: raise RuntimeError('Повтор GUID: '+g)
        existing[g] = Path(str(meta)[:-5])
records, unresolved, seen = [], set(), set()
def visit(src):
    g = guid(Path(str(src)+'.meta'))
    if g in seen: return
    seen.add(g)
    if g in existing:
        records.append({'guid':g, 'source':str(src), 'reuse':str(existing[g].relative_to(ROOT))})
        return
    dest = OUT/src.relative_to(PACKS)
    records.append({'guid':g, 'source':str(src), 'destination':str(dest.relative_to(ROOT)),
                    'bytes':src.stat().st_size, 'sha256':hashlib.sha256(src.read_bytes()).hexdigest()})
    dependencies=refs(src) | refs(Path(str(src)+'.meta'))
    if src.suffix=='.mat':
        dependencies={t[0] for t in textures(src)[1:] if t}
    for ref in dependencies:
        if ref == g or ref.startswith('0000000000000000'): continue
        if ref in index: visit(index[ref])
        elif ref not in existing: unresolved.add(ref)
for selected in SELECTED: visit(selected)
REPORT.mkdir(parents=True, exist_ok=True)
manifest = {'selected':[str(p) for p in SELECTED], 'files':records, 'unresolved':sorted(unresolved), 'copied':args.copy}
if args.copy:
    # Незамкнутые built-in package script refs допускаются только после явной проверки ведущим.
    for record in records:
        if 'destination' not in record: continue
        src, dest = Path(record['source']), ROOT/record['destination']
        if dest.exists(): raise RuntimeError('Нельзя перезаписать '+str(dest))
        folders(dest.parent)
        shutil.copy2(src, dest)
        shutil.copy2(Path(str(src)+'.meta'), Path(str(dest)+'.meta'))
        if src.suffix=='.mat':record['adaptation']=adapt(src,dest)
manifest['generated_maps']=generated
(REPORT/'real-source-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps({'selected_guids':[(p.name, guid(Path(str(p)+'.meta'))) for p in SELECTED],
 'copy_files':len([r for r in records if 'destination' in r]), 'reuse_files':len([r for r in records if 'reuse' in r]),
 'new_bytes':sum(r.get('bytes',0) for r in records), 'unresolved':sorted(unresolved)}, indent=2))

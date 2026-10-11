"""Создаёт отдельный мешок и профиль, проверяет GUID новых файлов."""
from pathlib import Path
import re,uuid,json,hashlib,wave
ROOT=Path(__file__).resolve().parents[3]
REPORT=ROOT/'tasks/lobby-decoration/reports/implementation'
def meta(p,kind):
 m=Path(str(p)+'.meta')
 if not m.exists():m.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n{kind}:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
def guid(p):return re.search(r'^guid: ([0-9a-f]{32})',Path(str(p)+'.meta').read_text(encoding='utf-8-sig'),re.M)[1]
for p in (ROOT/'Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration').glob('*.cs'):meta(p,'MonoImporter')
for area in ['Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration','Assets/Art/Lobby/Generated','Assets/Data/Audio']:
 p=ROOT/area;p.mkdir(parents=True,exist_ok=True)
 if not Path(str(p)+'.meta').exists():Path(str(p)+'.meta').write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n',encoding='utf-8')

src=ROOT/'Assets/Art/Lobby/Imported/sandbag-wall-straight/node_1_sandbag-wall-straight_0.obj'
vertices=[];faces=[]
for l in src.read_text().splitlines():
 if l.startswith('v '):vertices.append(tuple(float(n) for n in l.split()[1:]))
 elif l.startswith('f '):faces.append(tuple(int(n)-1 for n in l.split()[1:]))
neighbors={};components=[];seen=set()
for i,f in enumerate(faces):
 for v in f:neighbors.setdefault(tuple(round(x,4) for x in vertices[v]),set()).add(i)
for i in range(len(faces)):
 if i in seen:continue
 group=[];pending=[i]
 while pending:
  j=pending.pop()
  if j in seen:continue
  seen.add(j);group.append(j)
  for v in faces[j]:pending.extend(neighbors[tuple(round(x,4) for x in vertices[v])]-seen)
 components.append(group)
group=max(components,key=len);used=sorted(set(v for i in group for v in faces[i]));points=[vertices[v] for v in used]
minimum=[min(v[k] for v in points) for k in range(3)];maximum=[max(v[k] for v in points) for k in range(3)]
offset=[(minimum[0]+maximum[0])/2,minimum[1],(minimum[2]+maximum[2])/2]
path=ROOT/'Assets/Art/Lobby/Generated/SingleSandbag.obj'
mapping={v:i+1 for i,v in enumerate(used)}
lines=['# Derived CC0 sandbag; largest disconnected component, bottom centred']
lines+=['v '+' '.join(f'{vertices[v][k]-offset[k]:.8g}' for k in range(3)) for v in used]
lines+=['f '+' '.join(str(mapping[v]) for v in faces[i]) for i in group]
path.write_text('\n'.join(lines)+'\n');meta(path,'ModelImporter')
profile=ROOT/'Assets/Data/Audio/LobbyAmbienceProfile.asset';meta(profile,'NativeFormatImporter')
script=guid(ROOT/'Assets/Scripts/Environment/Audio/LobbyAmbienceProfile.cs')
def clip(name):return '{fileID: 8300000, guid: '+guid(ROOT/f'Assets/Audio/SFX/Lobby/{name}.wav')+', type: 3}'
lines=['%YAML 1.1','%TAG !u! tag:unity3d.com,2011:','--- !u!114 &11400000','MonoBehaviour:', '  m_ObjectHideFlags: 0','  m_CorrespondingSourceObject: {fileID: 0}','  m_PrefabInstance: {fileID: 0}','  m_PrefabAsset: {fileID: 0}','  m_GameObject: {fileID: 0}','  m_Enabled: 1','  m_EditorHideFlags: 0',f'  m_Script: {{fileID: 11500000, guid: {script}, type: 3}}','  m_Name: LobbyAmbienceProfile','  m_EditorClassIdentifier: ', '  forest: '+clip('Forest'),'  forestVolume: 0.08','  globalPause: {x: 18, y: 35}','  episodes:']
for kind,mode,name,duration,volume,fade in [(0,1,'Helicopter',18,.35,4),(1,1,'Truck',19,.3,3),(2,0,'Gunfire',2,.13,.2),(3,1,'SoldiersSteps',12,.18,2)]:
 lines += [f'  - kind: {kind}',f'    mode: {mode}','    clips:','    - '+clip(name)]
 if kind==3:lines+=['    - '+clip('SoldiersVoice')]
 lines+=['    cooldown: {x: 70, y: 140}','    repeats: {x: 1, y: 1}',f'    duration: {duration}',f'    volume: {volume}',f'    fade: {fade}','    minDistance: 5','    maxDistance: 150']
profile.write_text('\n'.join(lines)+'\n',encoding='utf-8')
index={};duplicates={}
for m in (ROOT/'Assets').rglob('*.meta'):
 match=re.search(r'^guid: ([0-9a-f]{32})',m.read_text(encoding='utf-8-sig',errors='replace'),re.M)
 if match:
  g=match[1]
  if g in index and ('Lobby' in str(m) or 'Lobby' in str(index[g])):duplicates.setdefault(g,[str(index[g].relative_to(ROOT))]).append(str(m.relative_to(ROOT)))
  else:index[g]=m
result={'guid_duplicates_in_new_area':duplicates,'single_bag_components':len(components),'single_bag_triangles':len(group),'single_bag_bounds_size':[maximum[k]-minimum[k] for k in range(3)],'scene_sha256':hashlib.sha256((ROOT/'Assets/Scenes/Lobby.unity').read_bytes()).hexdigest(),'audio':[]}
for p in (ROOT/'Assets/Audio/SFX/Lobby').glob('*.wav'):
 with wave.open(str(p)) as w:result['audio'].append({'name':p.name,'channels':w.getnchannels(),'seconds':w.getnframes()/w.getframerate()})
(REPORT/'offline-integrity.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))
assert not duplicates

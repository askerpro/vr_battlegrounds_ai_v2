"""Подготовка статических исходников без runtime-импортёра. Внешние проекты только читаются."""
from pathlib import Path
import json, struct, math, uuid, hashlib, wave, array, shutil, re

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Assets/Art/Lobby/Imported'
REPORT = ROOT / 'tasks/lobby-decoration/reports/implementation'
REPORT.mkdir(parents=True, exist_ok=True)

def meta(path, importer='DefaultImporter'):
    p = Path(str(path)+'.meta')
    if not p.exists():
        folder = 'folderAsset: yes\n' if path.is_dir() else ''
        p.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n{folder}{importer}:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')

def folders(path):
    path.mkdir(parents=True, exist_ok=True)
    for p in [path, *path.parents]:
        if p == ROOT / 'Assets': break
        if ROOT / 'Assets' in p.parents: meta(p)

def mul(a,b): return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
IDENTITY = [[float(i==j) for j in range(4)] for i in range(4)]
def matrix(n):
    if 'matrix' in n: return [[n['matrix'][j*4+i] for j in range(4)] for i in range(4)]
    x,y,z,w=n.get('rotation',[0,0,0,1]); s=n.get('scale',[1,1,1]); t=n.get('translation',[0,0,0])
    r=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),t[0]], [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),t[1]], [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),t[2]], [0,0,0,1]]
    for i in range(3):
        for j in range(3): r[i][j]*=s[j]
    return r

def convert(src, name):
    raw=src.read_bytes(); magic,ver,total=struct.unpack_from('<III',raw)
    assert magic==0x46546c67 and ver==2 and total==len(raw)
    p=12; binary=b''; gltf=None
    while p<len(raw):
        size,kind=struct.unpack_from('<II',raw,p); p+=8; data=raw[p:p+size];p+=size
        if kind==0x4e4f534a: gltf=json.loads(data)
        elif kind==0x004e4942: binary=data
    def accessor(idx):
        a=gltf['accessors'][idx]; assert 'sparse' not in a
        view=gltf['bufferViews'][a['bufferView']]; assert view.get('buffer',0)==0
        ct=a['componentType']; fmt,width={5120:('b',1),5121:('B',1),5122:('h',2),5123:('H',2),5125:('I',4),5126:('f',4)}[ct]
        count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        stride=view.get('byteStride',count*width); offset=view.get('byteOffset',0)+a.get('byteOffset',0)
        vals=[]
        for k in range(a['count']):
            v=struct.unpack_from('<'+fmt*count,binary,offset+k*stride)
            if a.get('normalized',False):
                maximum={5120:127,5121:255,5122:32767,5123:65535,5125:4294967295}[ct]
                v=tuple(max(-1,x/maximum) for x in v)
            vals.append(v)
        return vals
    directory=OUT/name; folders(directory); files=[]
    def visit(idx,parent):
        node=gltf['nodes'][idx]; world=mul(parent,matrix(node))
        if 'mesh' in node:
            mesh=gltf['meshes'][node['mesh']]; lines=['# CC0; glTF world transform applied; right-handed OBJ']; base=0; triangles=0; bounds=[]
            for pi,pr in enumerate(mesh['primitives']):
                assert pr.get('mode',4)==4
                positions=accessor(pr['attributes']['POSITION']); inds=accessor(pr['indices']) if 'indices' in pr else [(i,) for i in range(len(positions))]
                for v in positions:
                    transformed=[sum(world[i][j]*v[j] for j in range(3))+world[i][3] for i in range(3)]
                    bounds.append(transformed); lines.append('v '+' '.join(f'{x:.8g}' for x in transformed))
                # Нормали пересчитываются импортёром по преобразованным треугольникам:
                # это корректно при неравномерном масштабе и quantized normals.
                lines.append(f'g primitive_{pi}'); lines.append(f'usemtl material_{pr.get("material",0)}')
                det=world[0][0]*(world[1][1]*world[2][2]-world[1][2]*world[2][1])-world[0][1]*(world[1][0]*world[2][2]-world[1][2]*world[2][0])+world[0][2]*(world[1][0]*world[2][1]-world[1][1]*world[2][0])
                for i in range(0,len(inds),3):
                    face=[inds[i+j][0]+base+1 for j in range(3)]
                    if det<0: face.reverse()
                    lines.append('f '+' '.join(map(str,face))); triangles+=1
                base+=len(positions)
            path=directory/f'node_{idx}_{re.sub("[^a-zA-Z0-9_-]","_",node.get("name","mesh"))}.obj'
            path.write_text('\n'.join(lines)+'\n',encoding='utf-8');meta(path,'ModelImporter')
            files.append(dict(path=str(path.relative_to(ROOT)),name=node.get('name'),triangles=triangles,vertices=base,min=[min(v[i] for v in bounds) for i in range(3)],max=[max(v[i] for v in bounds) for i in range(3)]))
        for child in node.get('children',[]): visit(child,world)
    for n in gltf['scenes'][gltf.get('scene',0)]['nodes']: visit(n,IDENTITY)
    return files

acquired=ROOT/'tasks/lobby-decoration/reports/acquired-assets'
models=[]
for name in ['mess-tent','sandbag-wall-straight']: models+=convert(acquired/f'models/{name}.glb',name)
(REPORT/'converted-models.json').write_text(json.dumps(models,indent=2),encoding='utf-8')

# Небольшой выбранный набор WAV; исходные .meta сохраняются для неизменённых клипов.
audio=ROOT/'Assets/Audio/SFX/Lobby';folders(audio)
library=Path('F:/UnityProjects/_SoundLibrary/Universal Sound FX')
sources={
 'Forest.wav':library/'AMBIENCES/Nature/AMBIENCE_Forest_Wind_17sec_loop_stereo.wav',
 'Helicopter.wav':library/'VEHICLES/Air/Helicopters/HELICOPTER_Hover_Distant_Long_loop_mono.wav',
 'SoldiersSteps.wav':library/'HUMAN/Footsteps/Trainers_Gravel_Compact_Run/FOOTSTEPS_Trainers_Gravel_Compact_Run_x8_loop_mono.wav',
 'SoldiersVoice.wav':library/'VOICES/Words_Phrases/Male_A/VOICE_MALE_Copy_That_1_mono.wav'}
records=[]
for name,src in sources.items():
    dst=audio/name;shutil.copy2(src,dst)
    source_meta=Path(str(src)+'.meta')
    if source_meta.exists(): shutil.copy2(source_meta,Path(str(dst)+'.meta'))
    else: meta(dst,'AudioImporter')
    records.append({'source':str(src),'destination':str(dst.relative_to(ROOT)),'sha256':hashlib.sha256(dst.read_bytes()).hexdigest()})
for name,source in [('Truck.wav','truck-highway-passby.wav'),('Gunfire.wav','gunfire-sfx.wav')]:
    src=acquired/'audio'/source;dst=audio/name
    with wave.open(str(src),'rb') as w:
        assert w.getsampwidth()==2
        rate=w.getframerate();channels=w.getnchannels(); samples=array.array('h',w.readframes(w.getnframes()))
    mono=array.array('h',(int(sum(samples[i:i+channels])/channels) for i in range(0,len(samples),channels)))
    if name=='Gunfire.wav':
        # Простое НЧ-смягчение только декоративного далёкого звука.
        previous=0
        for i,s in enumerate(mono): previous=.8*previous+.2*s;mono[i]=int(previous*.65)
    with wave.open(str(dst),'wb') as w: w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes(mono.tobytes())
    meta(dst,'AudioImporter');records.append({'source':str(src),'destination':str(dst.relative_to(ROOT)),'sha256':hashlib.sha256(dst.read_bytes()).hexdigest(),'processing':'mono average; gunfire low pass and gain'})
(REPORT/'audio-manifest.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
for p in (ROOT/'Assets/Scripts/Environment/Audio').glob('*.cs'):meta(p,'MonoImporter')
folders(ROOT/'Assets/Scripts/Environment/Audio')
print(json.dumps({'models':models,'audio_count':len(records)},indent=2))

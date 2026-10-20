import json
from pathlib import Path

root=Path('tmp/weapon-sight-calibration')
model=json.loads((root/'visual-review/Mk14/geometry.json').read_text(encoding='utf-8-sig'))
s=model['surfaces'][0]
m=s['localToPrefabMeters']
points=[]
for v in s['vertices']:
    a=[v['x'],v['y'],v['z'],1]
    points.append([sum(m[f'e{i}{j}']*a[j] for j in range(4)) for i in range(3)])
parent=list(range(len(points)))
def find(i):
    while parent[i]!=i:
        parent[i]=parent[parent[i]]
        i=parent[i]
    return i
def join(a,b):
    a,b=find(a),find(b)
    if a!=b:parent[b]=a
weld={}
for i,p in enumerate(points):
    key=tuple(round(x,6) for x in p)
    if key in weld:join(i,weld[key])
    else:weld[key]=i
for sub in s['subMeshes']:
    t=sub['triangles']
    for i in range(0,len(t),3):
        join(t[i],t[i+1]);join(t[i],t[i+2])
groups={}
for i in range(len(points)):groups.setdefault(find(i),[]).append(i)
components=[]
for g,indices in groups.items():
    used=set(indices)
    tris=[]
    for sub in s['subMeshes']:
        t=sub['triangles']
        tris.append([i//3 for i in range(0,len(t),3) if t[i] in used])
    components.append({'id':g,'vertexCount':len(indices),'triangleCount':sum(len(t) for t in tris),
        'min':[min(points[i][j] for i in indices) for j in range(3)],
        'max':[max(points[i][j] for i in indices) for j in range(3)],'vertices':indices,'trianglesBySubmesh':tris})
out={'source':model['assetPath'],'sourceHash':model['dependencyHash'],'meshGuid':s['meshGuid'],
    'meshLocalFileId':s['meshLocalFileId'],'weldGridMeters':.000001,'componentCount':len(components),'components':components}
(root/'mk14-components.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
shown=[{k:c[k] for k in ['id','vertexCount','triangleCount','min','max']} for c in components if c['max'][1]>.071]
print('components=',len(components),'high parts=',len(shown))
print(json.dumps(shown[:45],indent=2))

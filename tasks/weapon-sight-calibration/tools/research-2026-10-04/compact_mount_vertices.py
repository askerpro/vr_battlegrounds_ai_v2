import json
from pathlib import Path

def transform(m,v):
    a=[v['x'],v['y'],v['z'],1]
    return [sum(m[f'e{i}{j}']*a[j] for j in range(4)) for i in range(3)]

root=Path('tmp/weapon-sight-calibration/visual-review')
for name in ['Sight_03','Sight_06','Sight_08']:
    data=json.loads((root/name/'geometry.json').read_text(encoding='utf-8-sig'))
    s=data['surfaces'][0]
    pts=[(i,transform(s['localToPrefabMeters'],v)) for i,v in enumerate(s['vertices'])]
    bottom=min(p[1][1] for p in pts)
    selected=[(i,p) for i,p in pts if p[1]<bottom+.0005]
    unique={tuple(round(x,8) for x in p):i for i,p in selected}
    print(name,'bottomY=',bottom, 'mount vertices=',[(i,p) for p,i in unique.items()][:28])

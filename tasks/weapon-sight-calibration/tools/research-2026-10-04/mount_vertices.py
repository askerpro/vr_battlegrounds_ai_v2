import json
from pathlib import Path
def transform(m,v):
    a=[v['x'],v['y'],v['z'],1]
    return [sum(m[f'e{i}{j}']*a[j] for j in range(4)) for i in range(3)]

base=Path('tmp/weapon-sight-calibration/visual-review')
for name in ['SniperRifle','Sight_11']:
    data=json.loads((base/name/'geometry.json').read_text(encoding='utf-8-sig'))
    for s in data['surfaces']:
        if ('Base' if name=='SniperRifle' else 'Sight_11_Mesh') not in s['nodePath']: continue
        values=[(i,transform(s['localToPrefabMeters'],v)) for i,v in enumerate(s['vertices'])]
        if name=='SniperRifle': values=[(i,p) for i,p in values if abs(p[0])<.015 and -.01<p[2]<.30 and p[1]>.078]
        else: values=sorted([(i,p) for i,p in values if abs(p[0])<.009 and -.05<p[2]<.06],key=lambda a:a[1][1])
        unique={tuple(round(x,8) for x in p):i for i,p in values}
        print(name,s['nodePath'],[(i,p) for p,i in unique.items()][:80])

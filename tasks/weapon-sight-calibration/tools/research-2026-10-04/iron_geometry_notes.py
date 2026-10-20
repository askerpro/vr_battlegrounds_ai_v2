import json
from pathlib import Path

def point(m,v):
    a=[v['x'],v['y'],v['z'],1]
    return [sum(m[f'e{i}{j}']*a[j] for j in range(4)) for i in range(3)]

folder=Path('tmp/weapon-sight-calibration/visual-review')
data=json.loads((folder/'IronSights/geometry.json').read_text(encoding='utf-8-sig'))
s=data['surfaces'][1]
pts=[(i,point(s['localToPrefabMeters'],v)) for i,v in enumerate(s['vertices'])]
print('Front bounds', [min(p[j] for _,p in pts) for j in range(3)],[max(p[j] for _,p in pts) for j in range(3)])
print('Front centre tip candidates',sorted([(i,p) for i,p in pts if abs(p[0])<.0015],key=lambda x:-x[1][1])[:12])
print('Front mount points',sorted(pts,key=lambda x:x[1][1])[:12])
weapon=json.loads((folder/'SniperRifle/geometry.json').read_text(encoding='utf-8-sig'))
s=weapon['surfaces'][0]
v=[point(s['localToPrefabMeters'],a) for a in s['vertices']]
for z in [.05,.48]:
    hits=[]
    for sub in s['subMeshes']:
        tr=sub['triangles']
        for k in range(0,len(tr),3):
            a,b,c=[v[n] for n in tr[k:k+3]]
            det=(b[0]-a[0])*(c[2]-a[2])-(c[0]-a[0])*(b[2]-a[2])
            if abs(det)<1e-12:continue
            u=((-a[0])*(c[2]-a[2])-(c[0]-a[0])*(z-a[2]))/det
            w=((b[0]-a[0])*(z-a[2])-(-a[0])*(b[2]-a[2]))/det
            if u>=0 and w>=0 and u+w<=1:
                y=a[1]+u*(b[1]-a[1])+w*(c[1]-a[1])
                hits.append((y,tr[k:k+3],[1-u-w,u,w]))
    print('Rail vertical hit',z, sorted(hits,key=lambda a:-a[0])[:2])

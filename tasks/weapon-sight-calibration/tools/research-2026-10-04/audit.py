"""Read-only offline research; does not load Unity or save/import any asset."""
from pathlib import Path
import re, yaml, numpy as np, struct

ROOT = Path(__file__).resolve().parents[2]
def docs(path):
    text = path.read_text(encoding='utf-8-sig')
    result = {}
    for typ, fid, body in re.findall(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M|re.S):
        obj = yaml.safe_load(body)
        result[int(fid)] = (int(typ), next(iter(obj.values())))
    return result

metas = {}
for base in ('Assets/Prefabs/Weapons', 'Assets/Art/Weapons', 'Assets/Data/Weapons'):
    for p in (ROOT/base).rglob('*.meta'):
        match = re.search(r'^guid: (\w+)', p.read_text(encoding='utf-8-sig'), re.M)
        if match: metas[match[1]] = Path(str(p)[:-5])

def matrix(t):
    q=t['m_LocalRotation']; x,y,z,w=(q[k] for k in ('x','y','z','w'))
    r=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w], [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w], [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])
    out=np.eye(4); out[:3,:3]=r@np.diag([t['m_LocalScale'][k] for k in 'xyz']); out[:3,3]=[t['m_LocalPosition'][k] for k in 'xyz']
    return out

def points(mesh):
    d=next(iter(docs(mesh).values()))[1]['m_VertexData']
    channels=d['m_Channels']; streams={}
    sizes={0:4,1:2,2:1,3:1,4:1,5:1,6:2,7:2,8:4,9:4,10:4,11:4}
    for c in channels:
        if c['dimension']: streams[c['stream']]=max(streams.get(c['stream'],0),c['offset']+c['dimension']*sizes[c['format']])
    pos=channels[0]; n=d['m_VertexCount']; raw=bytes.fromhex(str(d['_typelessdata']))
    offset=0
    for stream,stride in sorted(streams.items()):
        if stream==pos['stream']: break
        offset=(offset+n*stride+15)//16*16
    dtype={0:'<f4',1:'<f2'}[pos['format']]
    return np.ndarray((n,3), dtype=dtype,buffer=raw,offset=offset+pos['offset'],strides=(streams[pos['stream']],sizes[pos['format']])).copy()

reg=next(iter(docs(ROOT/'Assets/Data/Weapons/Resources/WeaponRegistry.asset').values()))[1]
for entry in reg['_weapons']:
    ip=metas[entry['guid']]; info=next(iter(docs(ip).values()))[1]; pp=metas[info['_weaponPrefab']['guid']]; obj=docs(pp)
    names={fid:d['m_Name'] for fid,(typ,d) in obj.items() if typ==1 and 'm_Name' in d}
    trs={fid:d for fid,(typ,d) in obj.items() if typ==4 and 'm_LocalPosition' in d}
    cache={}
    def world(fid):
        if fid not in cache:
            t=trs[fid]; parent=t['m_Father']['fileID']; cache[fid]=(world(parent) if parent in trs else np.eye(4))@matrix(t)
        return cache[fid]
    trbygo={t['m_GameObject']['fileID']:fid for fid,t in trs.items()}
    shots=[d['_shotTypes'] for typ,d in obj.values() if '_shotTypes' in d]
    print('\nWEAPON',info['_weaponId'],pp.relative_to(ROOT),'guid='+info['_weaponPrefab']['guid'])
    print('  visible_mesh_nodes', [names.get(d['m_GameObject']['fileID'],'?') for typ,d in obj.values() if typ==33 and names.get(d['m_GameObject']['fileID'])!='GrabHighlight'])
    if not shots: print('  VARIANT: inherited descriptors require resolved Unity/source audit')
    muzzle=None
    if shots:
        shot=shots[0][0]; sid=shot['_shotSource']['fileID']; muzzle=world(sid)[:3,3]
        print('  shot',np.round(muzzle,6).tolist(),'direction',np.round(world(sid)[:3,2]/np.linalg.norm(world(sid)[:3,2]),6).tolist(),'auto',shot['_useAutomaticProjectileTrajectory'],'speed',shot['_projectileSpeed'])
    for fid,(typ,d) in obj.items():
        if typ!=33: continue
        go=d['m_GameObject']['fileID']; name=names.get(go,'?'); ref=d['m_Mesh']; mp=metas.get(ref.get('guid'))
        if mp is None: continue
        vs=points(mp); tr=world(trbygo[go]); vs=(tr[:3,:3]@vs.T).T+tr[:3,3]
        lo=vs.min(0); hi=vs.max(0)
        if 'XPS' in name or info['_weaponId'] in ('AK105','PPK','M16','Viper','SRM12'):
            print('  mesh',name,mp.relative_to(ROOT),'bounds',np.round(lo,5).tolist(),np.round(hi,5).tolist())
        if 'XPS' in name:
            md=next(iter(docs(mp).values()))[1]; sm=md['m_SubMeshes'][1]; ib=bytes.fromhex(str(md['m_IndexBuffer'])); dt='<u2' if md['m_IndexFormat']==0 else '<u4'; ix=np.frombuffer(ib,dtype=dt,offset=sm['firstByte'],count=sm['indexCount']); lv=vs[np.unique(ix)]
            center=(lv.min(0)+lv.max(0))/2
            print('  LENS bboxcenter',np.round(center,6).tolist(),'bounds',np.round(lv.min(0),6).tolist(),np.round(lv.max(0),6).tolist(),'delta_vs_muzzle',None if muzzle is None else np.round(center-muzzle,6).tolist())

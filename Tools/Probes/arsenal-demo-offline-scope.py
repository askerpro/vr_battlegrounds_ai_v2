# Static GUID graph only: does not invoke Unity or read source FBX/texture bytes.
import json, re, subprocess, time
from pathlib import Path
start=time.perf_counter()
metas=subprocess.check_output(["rg","--files","Assets","-g","*.meta"],text=True).splitlines()
index={}
for name in metas:
    meta=Path(name)
    try:
        with meta.open(encoding="utf-8-sig",errors="replace") as f:
            head="".join(next(f,"") for _ in range(4))
        found=re.search(r"^guid:\s*([0-9a-f]{32})",head,re.M)
        if found:index[found.group(1)]=Path(str(meta)[:-5])
    except OSError:pass
inputs=["Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab","Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab","Assets/Data/Weapons/FullDemoArsenal.asset","Assets/Data/Maps/MapRegistry.asset"]
queue=list(map(Path,inputs)); seen=set(); missing=set()
text_ext={".prefab",".asset",".unity",".mat",".anim",".controller",".overridecontroller",".playable",".mask",".rendertexture",".spriteatlas",".lighting",".flare"}
while queue:
    path=queue.pop()
    if path in seen:continue
    seen.add(path)
    candidates=[Path(str(path)+".meta")]
    if path.suffix.lower() in text_ext:candidates.append(path)
    for file in candidates:
        if not file.is_file():continue
        try:data=file.read_text(encoding="utf-8-sig",errors="replace")
        except OSError:continue
        for guid in re.findall(r"guid:\s*([0-9a-f]{32})",data):
            if guid in index:queue.append(index[guid])
            elif guid!="0"*32:missing.add(guid)
files=set()
for path in seen:
    for f in (path,Path(str(path)+".meta")):
        if f.is_file():files.add(f)
sizes=sorted(((f.stat().st_size,str(f).replace("\\","/")) for f in files),reverse=True)
result={"kind":"offline YAML+meta GUID closure; not AssetDatabase dependency proof","inputs":inputs,"assetPaths":len(seen),"filesWithMeta":len(files),"bytes":sum(x[0] for x in sizes),"scanSeconds":time.perf_counter()-start,"unresolvedGUIDs":sorted(missing),"largest":[{"bytes":size,"path":path} for size,path in sizes[:20]],"paths":[str(p).replace("\\","/") for p in sorted(seen)]}
out=Path("tmp/arsenal-ui-contract/demo-offline-guid-scope.json");out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps({k:v for k,v in result.items() if k not in ("paths","unresolvedGUIDs")},ensure_ascii=False))


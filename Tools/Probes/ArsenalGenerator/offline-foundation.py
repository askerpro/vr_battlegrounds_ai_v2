from pathlib import Path
import xml.etree.ElementTree as ET

root=Path.cwd()
out=root/'tmp/arsenal-generator-proofs'
elements=list(ET.parse('Assembly-CSharp-Editor.csproj').getroot().iter())
tag=lambda e:e.tag.split('}')[-1]
lines=['/nologo','/target:library','/langversion:latest','/nostdlib+','/unsafe+','/nowarn:0436',
       '/define:'+next(e.text for e in elements if tag(e)=='DefineConstants'),
       '/out:"'+str(out/'GeneratorFoundationDraft.dll')+'"']
refs=set()
for e in elements:
    if tag(e)=='Reference':
        hint=next((n.text for n in e if tag(n)=='HintPath'),None)
        if hint:
            p=Path(hint)
            if not p.is_absolute():p=root/p
            if p.exists():refs.add(str(p))
    elif tag(e)=='ProjectReference':
        p=root/'Library/ScriptAssemblies'/(Path(e.attrib['Include']).stem+'.dll')
        if p.exists():refs.add(str(p))
lines.extend('/reference:"'+r+'"' for r in sorted(refs))
lines.extend('"'+str(p)+'"' for p in sorted((out/'draft').glob('*.cs')))
(out/'GeneratorFoundationDraft.rsp').write_text('\n'.join(lines),encoding='utf-8')
print('Prepared foundation draft compile. No Unity native execution.')

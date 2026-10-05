"""Compile-only временных task2 классов; не импортирует Assets/не выполняет Unity."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path.cwd()
out = root / 'tmp/arsenal-generator-proofs'
elements = list(ET.parse('Assembly-CSharp-Editor.csproj').getroot().iter())
tag = lambda e: e.tag.split('}')[-1]
args = ['/nologo', '/target:library', '/langversion:latest', '/nostdlib+', '/unsafe+',
        '/define:' + next(e.text for e in elements if tag(e) == 'DefineConstants'),
        '/out:"' + str(out / 'GeneratorTask2Draft.dll') + '"']
refs = set()
for e in elements:
    if tag(e) == 'Reference':
        p = next((n.text for n in e if tag(n) == 'HintPath'), None)
        if p:
            p = Path(p)
            if not p.is_absolute(): p = root / p
            if p.exists(): refs.add(str(p))
    elif tag(e) == 'ProjectReference':
        p = root / 'Library/ScriptAssemblies' / (Path(e.attrib['Include']).stem + '.dll')
        if p.exists(): refs.add(str(p))
refs.add(str(root / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'))
args += ['/reference:"' + p + '"' for p in sorted(refs)]
for name in ['GeneratorIdentityLifecycleProbe', 'GeneratorIdentityBaselineRunner']:
    p = out / (name + '.draft.cs')
    p.write_text((root / 'Tools/Probes/ArsenalGenerator/Drafts' / (name + '.cs.txt')).read_text(encoding='utf-8-sig'), encoding='utf-8')
    args.append('"' + str(p) + '"')
(out / 'GeneratorTask2Draft.rsp').write_text('\n'.join(args), encoding='utf-8')
print('Draft compile args prepared; no native execution.')

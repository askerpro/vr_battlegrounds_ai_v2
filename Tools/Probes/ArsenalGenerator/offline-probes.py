from pathlib import Path
import xml.etree.ElementTree as ET
import sys

root = Path.cwd()
out = root / 'tmp/arsenal-generator-proofs'
out.mkdir(parents=True, exist_ok=True)
elements = list(ET.parse('Assembly-CSharp-Editor.csproj').getroot().iter())
tag = lambda e: e.tag.split('}')[-1]
lines = ['/nologo', '/target:library', '/langversion:6', '/nostdlib+', '/unsafe+',
         '/define:' + next(e.text for e in elements if tag(e) == 'DefineConstants'),
         '/out:"' + str(out / 'GeneratorProbeDraft.dll') + '"']
references = set()
for e in elements:
    if tag(e) == 'Reference':
        hint = next((n.text for n in e if tag(n) == 'HintPath'), None)
        if hint:
            p = Path(hint)
            if not p.is_absolute(): p = root / p
            if p.exists(): references.add(str(p))
    elif tag(e) == 'ProjectReference':
        p = root / 'Library/ScriptAssemblies' / (Path(e.attrib['Include']).stem + '.dll')
        if p.exists(): references.add(str(p))
references.add(str(root / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'))
if not (root/'Assets/Scripts/Arsenal/ArsenalCompositionCatalog.cs.meta').exists() and (out/'GeneratorFoundationDraft.dll').exists():
    references.add(str(out/'GeneratorFoundationDraft.dll'))
lines.extend('/reference:"' + r + '"' for r in sorted(references))
for i, name in enumerate(sys.argv[1:] or ['stage0-baseline']):
    code = (root / 'Tools/Probes/ArsenalGenerator' / (name + '.cs.txt')).read_text(encoding='utf-8-sig')
    source = out / (name + '.wrapped.cs')
    source.write_text('using System; using System.Linq; using UnityEngine;\npublic static class GeneratorProbe' + str(i) + ' { public static object Run() {\n' + code + '\n} }\n', encoding='utf-8')
    lines.append('"' + str(source) + '"')
(out / 'GeneratorProbeDraft.rsp').write_text('\n'.join(lines), encoding='utf-8')
print('Prepared probe wrappers; this is compile-only, not native evidence.')

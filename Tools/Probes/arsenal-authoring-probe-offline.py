from pathlib import Path
import xml.etree.ElementTree as ET

root = Path.cwd()
out = root / 'tmp/arsenal-visual-stage'
elements = list(ET.parse('Assembly-CSharp-Editor.csproj').getroot().iter())
tag = lambda e: e.tag.split('}')[-1]
lines = ['/nologo', '/target:library', '/langversion:6', '/nostdlib+', '/unsafe+',
         '/define:' + next(e.text for e in elements if tag(e) == 'DefineConstants'),
         '/out:"' + str(out / 'AuthoringProbeDraft.dll') + '"']
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
lines.extend('/reference:"' + r + '"' for r in sorted(references))
for i, name in enumerate(['arsenal-authoring-populate', 'arsenal-authoring-backend-roundtrip', 'arsenal-authoring-inspector-token', 'arsenal-authoring-open-workspace', 'arsenal-authoring-stand-image']):
    code = (root / 'Tools/Probes' / (name + '.cs.txt')).read_text(encoding='utf-8-sig')
    source = out / (name + '.wrapped.cs')
    source.write_text('using System; using System.Linq;\npublic static class AuthoringProbe' + str(i) + ' { public static object Run() {\n' + code + '\n} }\n', encoding='utf-8')
    lines.append('"' + str(source) + '"')
(out / 'AuthoringProbeDraft.rsp').write_text('\n'.join(lines), encoding='utf-8')
print('Prepared populate + atomic backend + atomic Inspector-token probes.')

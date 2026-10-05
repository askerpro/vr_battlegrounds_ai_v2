from pathlib import Path
import xml.etree.ElementTree as ET

root = Path.cwd()
draft = root / 'tmp/arsenal-visual-stage/marker-relocation'
tag = lambda e: e.tag.split('}')[-1]
for name in ['VrBattlegrounds', 'Assembly-CSharp-Editor']:
    elements = list(ET.parse(name + '.csproj').getroot().iter())
    lines = ['/nologo', '/target:library', '/langversion:9', '/nostdlib+', '/unsafe+',
             '/define:' + next(e.text for e in elements if tag(e) == 'DefineConstants'),
             '/out:"' + str(draft / (name + '.dll')) + '"']
    for e in elements:
        if tag(e) == 'Reference':
            hint = next((n.text for n in e if tag(n) == 'HintPath'), None)
            if hint:
                p = Path(hint)
                if not p.is_absolute(): p = root / p
                if p.exists(): lines.append('/reference:"' + str(p) + '"')
        elif tag(e) == 'ProjectReference':
            assembly = Path(e.attrib['Include']).stem
            p = draft / 'VrBattlegrounds.dll' if assembly == 'VrBattlegrounds' else root / 'Library/ScriptAssemblies' / (assembly + '.dll')
            if p.exists() or assembly == 'VrBattlegrounds': lines.append('/reference:"' + str(p) + '"')
        elif tag(e) == 'Compile':
            p = Path(e.attrib['Include'])
            if p.name == 'ArsenalLayoutAuthoringStand.cs': continue
            if name == 'Assembly-CSharp-Editor' and p.name in ['ArsenalLayoutAuthoringStandEditor.cs', 'ArsenalEditorActions.cs']: p = draft / p.name
            if not p.is_absolute(): p = root / p
            lines.append('"' + str(p) + '"')
    if name == 'VrBattlegrounds': lines.append('"' + str(draft / 'ArsenalLayoutAuthoringStand.cs') + '"')
    (draft / (name + '.rsp')).write_text('\n'.join(lines), encoding='utf-8')
print('Prepared separate game-carrier and Editor-consumer response files.')

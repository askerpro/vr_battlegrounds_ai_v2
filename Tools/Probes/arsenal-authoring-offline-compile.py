from pathlib import Path
import xml.etree.ElementTree as ET

workspace = Path.cwd()
project = ET.parse('Assembly-CSharp-Editor.csproj').getroot()
elements = list(project.iter())
def tag(element): return element.tag.split('}')[-1]
defines = next(e.text for e in elements if tag(e) == 'DefineConstants')
lines = ['/nologo', '/target:library', '/langversion:9', '/nostdlib+', '/unsafe+', '/define:' + defines,
         '/out:"' + str(workspace / 'tmp/arsenal-visual-stage/AuthoringEditorDraft.dll') + '"']
for element in elements:
    if tag(element) == 'Reference':
        hint = next((e.text for e in element if tag(e) == 'HintPath'), None)
        if hint:
            path = Path(hint)
            if not path.is_absolute(): path = workspace / path
            if path.exists(): lines.append('/reference:"' + str(path) + '"')
    if tag(element) == 'ProjectReference':
        path = workspace / 'Library/ScriptAssemblies' / (Path(element.attrib['Include']).stem + '.dll')
        if path.exists(): lines.append('/reference:"' + str(path) + '"')
    if tag(element) == 'Compile':
        path = Path(element.attrib['Include'])
        if path.name == 'ArsenalEditorActions.cs': path = Path('tmp/arsenal-visual-stage/ArsenalEditorActions.authoring.cs')
        if not path.is_absolute(): path = workspace / path
        lines.append('"' + str(path) + '"')
for name in ['ArsenalLayoutAuthoringStand.cs', 'ArsenalLayoutAuthoringStandEditor.cs']:
    lines.append('"' + str(workspace / 'tmp/arsenal-visual-stage' / name) + '"')
Path('tmp/arsenal-visual-stage/AuthoringEditorDraft.rsp').write_text('\n'.join(lines), encoding='utf-8')
print('Prepared temp-only Editor response file.')

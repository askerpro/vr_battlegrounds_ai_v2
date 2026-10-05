from pathlib import Path
import difflib
import hashlib
import json

editor = Path('Assets/Editor/VR_Battlegrounds/Arsenal')
target = Path('Assets/Scripts/Arsenal/ArsenalLayoutAuthoringStand.cs')
draft = Path('tmp/arsenal-visual-stage/marker-relocation')
if target.exists() or Path(str(target) + '.meta').exists(): raise RuntimeError('Target script/meta exists; no overwrite')
patch = ['*** Begin Patch']
manifest = []
for name in ['ArsenalLayoutAuthoringStand.cs', 'ArsenalLayoutAuthoringStandEditor.cs', 'ArsenalEditorActions.cs']:
    path = editor / name
    before = path.read_text(encoding='utf-8-sig'); after = (draft / name).read_text(encoding='utf-8-sig')
    patch.append('*** Update File: ' + path.as_posix())
    if name == 'ArsenalLayoutAuthoringStand.cs': patch.append('*** Move to: ' + target.as_posix())
    for line in list(difflib.unified_diff(before.splitlines(), after.splitlines(), n=3))[2:]: patch.append('@@' if line.startswith('@@') else line)
    manifest.append({'source': path.as_posix(), 'target': target.as_posix() if name == 'ArsenalLayoutAuthoringStand.cs' else path.as_posix(),
                     'beforeSha': hashlib.sha256(path.read_bytes()).hexdigest(), 'afterTextSha': hashlib.sha256(after.encode()).hexdigest()})
meta = Path(str(editor / 'ArsenalLayoutAuthoringStand.cs') + '.meta')
contents = meta.read_text(encoding='utf-8-sig')
if 'guid: c415754e62ae4d939bc48ba6933f4617' not in contents: raise RuntimeError('Unexpected sole script GUID')
patch.extend(['*** Update File: ' + meta.as_posix(), '*** Move to: ' + str(target).replace('\\', '/') + '.meta', '@@'])
patch.extend(' ' + line for line in contents.splitlines())
patch.append('*** End Patch')
(draft / 'source.patch').write_text('\n'.join(patch) + '\n', encoding='utf-8')
(draft / 'write-set.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Prepared three-file source changes + exact same-GUID meta move; native files unchanged.')

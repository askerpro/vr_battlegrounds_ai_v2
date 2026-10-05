from pathlib import Path
import difflib
import hashlib
import json
import uuid

root = Path('Assets/Editor/VR_Battlegrounds/Arsenal')
draft = Path('tmp/arsenal-visual-stage')
changes = [(root / 'ArsenalEditorActions.cs', draft / 'ArsenalEditorActions.authoring.cs')]
for name in ['ArsenalLayoutAuthoringStand.cs', 'ArsenalLayoutAuthoringStandEditor.cs']:
    changes.append((root / name, draft / name))
patch = ['*** Begin Patch']
manifest = []
for output, source in changes:
    desired = source.read_text(encoding='utf-8-sig')
    exists = output.exists()
    if output.name != 'ArsenalEditorActions.cs' and exists:
        raise RuntimeError('New owned source already exists; refuse overwrite: ' + str(output))
    before = output.read_text(encoding='utf-8-sig') if exists else ''
    manifest.append({'path': str(output), 'before': hashlib.sha256(before.encode()).hexdigest(), 'after': hashlib.sha256(desired.encode()).hexdigest()})
    if exists:
        patch.append('*** Update File: ' + str(output).replace('\\', '/'))
        diff = list(difflib.unified_diff(before.splitlines(), desired.splitlines(), n=3))[2:]
        for line in diff: patch.append('@@' if line.startswith('@@') else line)
    else:
        patch.append('*** Add File: ' + str(output).replace('\\', '/'))
        patch.extend('+' + line for line in desired.splitlines())
        meta = Path(str(output) + '.meta')
        if meta.exists(): raise RuntimeError('Foreign meta: ' + str(meta))
        guid = uuid.uuid4().hex
        metadata = 'fileFormatVersion: 2\nguid: ' + guid + '\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        patch.append('*** Add File: ' + str(meta).replace('\\', '/'))
        patch.extend('+' + line for line in metadata.splitlines())
patch.append('*** End Patch')
(draft / 'authoring-source.patch').write_text('\n'.join(patch) + '\n', encoding='utf-8')
(draft / 'authoring-source-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps({'files': len(changes), 'patch': str(draft / 'authoring-source.patch'), 'manifest': manifest}))

from pathlib import Path
import difflib
import hashlib
import json

draft = Path('tmp/arsenal-visual-stage/hint-drafts')
paths = [
    'Assets/Scripts/Arsenal/ArsenalPresentationStyle.cs',
    'Assets/Scripts/Arsenal/ArsenalPresentationResolver.cs',
    'Assets/Scripts/Arsenal/ArsenalPresentationApplicator.cs',
    'Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalSupportModuleBuilder.cs',
    'Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalPresetAssetBuilder.cs',
    'Assets/Scripts/Arsenal/ArsenalPriceTag.cs',
    'Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalMagazineOfferInstaller.cs',
]
patch = ['*** Begin Patch']
records = []
for path in paths:
    source = Path(path)
    target = draft / source.name
    before = source.read_text(encoding='utf-8-sig')
    after = target.read_text(encoding='utf-8-sig')
    records.append({'path': path, 'beforeSHA256': hashlib.sha256(source.read_bytes()).hexdigest(),
                    'draftSHA256': hashlib.sha256(target.read_bytes()).hexdigest()})
    delta = list(difflib.unified_diff(before.splitlines(), after.splitlines(), n=3))
    if delta:
        patch.append('*** Update File: ' + path)
        for line in delta[2:]:
            patch.append('@@' if line.startswith('@@') else line)
patch.append('*** End Patch')
(draft / 'native.patch').write_text('\n'.join(patch) + '\n', encoding='utf-8')
(draft / 'inputs.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
print('\n'.join(patch))

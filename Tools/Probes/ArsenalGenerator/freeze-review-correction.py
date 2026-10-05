"""Замораживает уже полученные actual отчёты; Unity writers не запускает."""
from pathlib import Path
import hashlib
import json

root = Path.cwd()
out = root / 'tmp/arsenal-generator-proofs'
names = ['ArsenalCompositionCatalog', 'ArsenalDecorationDescriptor', 'ArsenalFunctionalSlotTemplate',
         'ArsenalStationDescription', 'ArsenalStationResolver', 'ArsenalStationCompositionBinding']
source = [f'Assets/Scripts/Arsenal/{n}.cs' for n in names]
source += ['Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalCompositionMetadataCompiler.cs']
core = source + [p + '.meta' for p in source]
core += ['Assets/Data/Arsenal/ArsenalCompositionCatalog.asset',
         'Assets/Data/Arsenal/ArsenalCompositionCatalog.asset.meta', 'Assets/Data/Arsenal.meta']
probes = ['stage1-resource-freshness.cs.txt', 'stage1-occupied-selection.cs.txt',
          'stage1-nested-compiler.cs.txt', 'stage1-persistent-readback.cs.txt', 'stage1-preservation.cs.txt',
          'stage1-resolve-matrix.cs.txt', 'stage1-android.cs.txt', 'freeze-review-correction.py']
reports = ['stage1-resource-freshness-red', 'stage1-resource-freshness-red-corrected',
           'stage1-resource-freshness-green', 'stage1-occupied-selection', 'stage1-reviewfix-matrix',
           'stage1-reviewfix-android', 'stage1-nested-compiler', 'stage1-reviewfix-persistent']
reports += [f'{prefix}-preservation-{phase}' for prefix in ['stage1-reviewfix', 'stage1-reviewgreen', 'stage1-nested']
            for phase in ['before', 'after']]
evidence = [f'tmp/arsenal-generator-proofs/{n}.json' for n in reports]
evidence += [f'tmp/arsenal-generator-proofs/{n}.md' for n in ['stage1-review-correction-final',
             'stage1-review-correction-progress', 'stage1-persistent-harness-limit',
             'stage1-nested-fixture-manifest', 'stage1-independent-review', 'stage1-review-correction-root-ruling']]
docs = ['Docs/Arsenal/generator-foundation.md', 'Docs/README.md', 'Docs/CHANGELOG.md',
        '.superpowers/sdd/arsenal-generator-plan/progress.md']
failures = []
for n in ['stage1-resource-freshness-green', 'stage1-occupied-selection', 'stage1-reviewfix-matrix',
          'stage1-nested-compiler', 'stage1-reviewfix-persistent']:
    r = json.loads((out / (n + '.json')).read_text(encoding='utf-8-sig'))
    if r.get('passed') is not True or r.get('failures') or r.get('error'):
        failures.append('Report not GREEN: ' + n)
android = json.loads((out / 'stage1-reviewfix-android.json').read_text(encoding='utf-8-sig'))
if android.get('Passed') is not True or android.get('Errors'):
    failures.append('Android not PASS')
for prefix in ['stage1-reviewfix', 'stage1-reviewgreen', 'stage1-nested']:
    a, b = [json.loads((out / f'{prefix}-preservation-{phase}.json').read_text(encoding='utf-8-sig'))
            for phase in ['before', 'after']]
    if a != b:
        failures.append('Preservation mismatch: ' + prefix)
fixture = root / 'Assets/Editor/VR_Battlegrounds/Arsenal/ProbeFixtures/GeneratorNestedDrop-20261005.prefab'
if fixture.exists() or Path(str(fixture) + '.meta').exists():
    failures.append('Fixture residue')
rows = []
for group, paths in [('core', core), ('probes', ['Tools/Probes/ArsenalGenerator/' + p for p in probes]),
                     ('evidence', evidence), ('shared-docs-current', docs)]:
    for p in paths:
        full = root / p
        if not full.is_file():
            failures.append('Missing manifest path: ' + p)
            continue
        data = full.read_bytes()
        rows.append(dict(group=group, path=p, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
report = dict(passed=not failures, failures=failures, files=rows,
              sourceWriteSet=['Assets/Scripts/Arsenal/ArsenalStationDescription.cs'],
              scope='Frozen existing core + current corrected probes/evidence; shared docs are current snapshots, not exclusive ownership',
              task1Accepted=False, productionLinked=False)
(out / 'stage1-review-correction-manifest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(passed=not failures, failures=failures, fileCount=len(rows)), ensure_ascii=False))

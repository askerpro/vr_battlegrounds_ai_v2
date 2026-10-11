"""Только DEVELOPMENT: точный пакет binding в ignored reports, без записи в Assets."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HANDOFF = Path('F:/CodexWorktrees/bots-fix/Vr_Battlegrounds_ai/tasks/bots-fix/reports/weapon-owner-handoff')
CANDIDATE = '8435c2291cfe5d4e2c824bbe8093e217b21034af'
PRIOR = 'c05eabbfb77ebc68a197593c54d1567ee3ab68da'
BASE = '6c0473fda8faa5e217117da0e5f6a4ed2866b634'
ACCEPTED = '8e3c513f5627de786ea0df0c1e6db5d81c78e09c'
ALLOWED = {
    'Assets/Scripts/Weapons/Equipment.meta',
    'Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs',
    'Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs.meta',
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/IUxrGrabAvailability.cs',
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/IUxrGrabAvailability.cs.meta',
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.cs',
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Manipulation.cs',
    'Assets/Scripts/Bots/BotClipBody.cs',
    'Assets/Scripts/Bots/BotClipBody.cs.meta',
    'Assets/Scripts/Bots/BotCombatAssets.cs',
    'Assets/Scripts/Player/PlayerSession.cs',
}

def git(*args):
    return subprocess.run(['git', '-C', str(ROOT), *args], check=True, capture_output=True).stdout

def normalized(data):
    return data.replace(b'\r\n', b'\n')

manifest = json.loads((HANDOFF / 'manifest.json').read_text(encoding='utf-8'))
assert manifest['baseline'] == BASE
assert manifest['priorCandidate'] == PRIOR
assert {item['path'] for item in manifest['paths']} == ALLOWED
assert set(git('diff', '--name-only', BASE, CANDIDATE).decode().splitlines()) == ALLOWED
assert git('rev-parse', CANDIDATE + '^').decode().strip() == PRIOR
assert git('rev-parse', PRIOR + '^').decode().strip() == BASE
assert git('diff', '--name-only', PRIOR, CANDIDATE).decode().splitlines() == [
    'Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs']
overlay = ROOT / 'tasks/weapon-system/reports/binding-development-overlay'
inventory = []
for item in manifest['paths']:
    path = item['path']
    physical = (HANDOFF / 'candidate' / path).read_bytes()
    assert hashlib.sha256(physical).hexdigest() == item['candidateSha256'], path
    blob = git('show', CANDIDATE + ':' + path)
    assert normalized(blob) == normalized(physical), path
    if item['baselineSha256'] is not None:
        baseline = git('show', BASE + ':' + path)
        assert normalized(baseline) == normalized(git('show', ACCEPTED + ':' + path)), path
        assert normalized(baseline) == normalized((ROOT / path).read_bytes()), path
    else:
        assert not (ROOT / path).exists(), path
    target = overlay / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(normalized(blob))
    inventory.append({'path': path, 'gitBlobSha256': hashlib.sha256(blob).hexdigest(), 'overlaySha256': hashlib.sha256(target.read_bytes()).hexdigest()})
report = {'candidate': CANDIDATE, 'parent': BASE, 'acceptedBase': ACCEPTED, 'developmentOnly': True, 'paths': inventory}
(overlay / 'inventory.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'ok': True, 'paths': len(inventory), 'overlay': str(overlay), 'developmentOnly': True}))

# Генерирует .meta новых файлов этапа clips-interaction, стартовые формы вибрации и Resources/HapticRoles.asset.
# Запуск из корня worktree: python -I tasks/haptics-system/tools/gen_waveforms_roles.py .
# Существующие .meta не перезаписываются (GUID стабильны); формы и роли перезаписываются стартовыми значениями.
import os, sys, uuid

root = sys.argv[1]
os.chdir(root)

def write(path, text):
    os.makedirs(os.path.dirname(path) or '.', exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(text)

def guid_of(meta):
    with open(meta, encoding='utf-8') as f:
        for line in f:
            if line.startswith('guid:'):
                return line.split()[1]

FOLDER = "fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
ASSET_META = "fileFormatVersion: 2\nguid: {g}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

scripts = [
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Haptics/UxrHapticWaveform.cs',
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Haptics/UxrHapticPriority.cs',
    'Assets/Scripts/Haptics/HapticRoles.cs',
    'Assets/Scripts/Haptics/HapticOverride.cs',
    'Assets/Scripts/Haptics/InteractionHaptics.cs',
    'Assets/Scripts/Interaction/AnchorReadiness.cs',
    'Assets/Editor/VR_Battlegrounds/Haptics/HapticEditorPreview.cs',
    'Assets/Editor/VR_Battlegrounds/Haptics/UxrHapticClipDrawer.cs',
    'Assets/Editor/VR_Battlegrounds/Haptics/UxrHapticWaveformEditor.cs',
    'Assets/Editor/VR_Battlegrounds/Haptics/HapticTuningWindow.cs',
    'Assets/Tests/EditMode/Haptics/HapticWaveformTests.cs',
]
for s in scripts:
    if not os.path.exists(s + '.meta'):
        write(s + '.meta', "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex)

for d in ['Assets/Data/Haptics', 'Assets/Data/Haptics/Waveforms']:
    os.makedirs(d, exist_ok=True)
    if not os.path.exists(d + '.meta'):
        write(d + '.meta', FOLDER.format(g=uuid.uuid4().hex))

wf_script = guid_of('Assets/ThirdParty/UltimateXR/Runtime/Scripts/Haptics/UxrHapticWaveform.cs.meta')
roles_script = guid_of('Assets/Scripts/Haptics/HapticRoles.cs.meta')

def ystr(s):
    return '"' + ''.join(c if ord(c) < 128 else '\\u%04X' % ord(c) for c in s) + '"'

def num(x):
    return '%g' % x

def so_header(script, name):
    return ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:",
            "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0",
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % script, "  m_Name: " + name, "  m_EditorClassIdentifier: "]

def seg(*p):
    return [(p[i], p[i + 1]) for i in range(0, len(p), 2)]

waveforms = {
    'Steady': (seg(1, 100), 'Ровная сила. Непрерывные состояния (готовность кармана): повтор без паузы даёт ровный гул.'),
    'Click': (seg(1, 30), 'Короткий щелчок: хват, укладка, ход механизма.'),
    'DoublePulse': (seg(1, 50, 0, 70, 1, 50), 'Двойной импульс — отказ, предупреждение.'),
    'TriplePulse': (seg(1, 60, 0, 80, 1, 60, 0, 80, 1, 60), 'Тройной импульс — сбой, критическое уведомление.'),
    'RecoilPistol': (seg(1, 20, 0.7, 20, 0.43, 20, 0.21, 20), 'Отдача пистолета: всплеск со спадом за 80 мс.'),
    'RecoilRifle': (seg(1, 25, 0.72, 25, 0.44, 25, 0.22, 25), 'Отдача винтовки: всплеск со спадом за 100 мс.'),
    'RecoilShotgun': (seg(1, 45, 0.75, 45, 0.45, 45, 0.2, 45), 'Отдача дробовика: всплеск со спадом за 180 мс.'),
    'Death': (seg(1, 1500), 'Гибель: максимум 1,5 с (решение пользователя).'),
}
wf_guid = {}
for name, (segments, desc) in waveforms.items():
    path = 'Assets/Data/Haptics/Waveforms/%s.asset' % name
    lines = so_header(wf_script, name) + ["  _segments:"]
    for a, ms in segments:
        lines += ["  - Amplitude: %s" % num(a), "    DurationMs: %d" % ms]
    lines += ["  _description: %s" % ystr(desc)]
    write(path, '\n'.join(lines) + '\n')
    if not os.path.exists(path + '.meta'):
        write(path + '.meta', ASSET_META.format(g=uuid.uuid4().hex))
    wf_guid[name] = guid_of(path + '.meta')

def clip(field, waveform=None, gain=1.0, priority=2, gap=0, cooldown=0.0):
    ref = '{fileID: 11400000, guid: %s, type: 2}' % wf_guid[waveform] if waveform else '{fileID: 0}'
    return ["  %s:" % field, "    _clip: {fileID: 0}", "    _clipAmplitude: 1", "    _hapticMode: 1",
            "    _fallbackClipType: 0", "    _fallbackAmplitude: 1", "    _fallbackDurationSeconds: -1",
            "    _waveform: " + ref, "    _waveformGain: %s" % num(gain), "    _priority: %d" % priority,
            "    _repeatGapMs: %d" % gap, "    _cooldownSeconds: %s" % num(cooldown), "    _secondaryHandGain: 1"]

# Карманы своего аватара — как прежний PocketHaptics: ровный гул, сила 0.08 (подобрана пользователем 2026-10-08).
pocket = dict(waveform='Steady', gain=0.08, priority=1)
lines = so_header(roles_script, 'HapticRoles')
lines += clip('_readyMagazinePocket', **pocket)
lines += clip('_readyPrimaryAnchor', **pocket)
lines += clip('_readySecondaryAnchor', **pocket)
lines += clip('_readyAvatarOther', **pocket)
lines += clip('_readyWorldAnchor')
lines += clip('_itemGrab')
lines += clip('_itemPlace')
lines += clip('_itemRelease')
path = 'Assets/Resources/HapticRoles.asset'
write(path, '\n'.join(lines) + '\n')
if not os.path.exists(path + '.meta'):
    write(path + '.meta', ASSET_META.format(g=uuid.uuid4().hex))
print('waveforms', len(wf_guid), 'roles ok')

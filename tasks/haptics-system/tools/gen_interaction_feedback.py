# Генерирует .meta новых файлов этапа interaction-feedback, префаб отклика готовности и Resources/InteractionFeedbackConfig.asset.
# Запуск из корня worktree: python -I tasks/haptics-system/tools/gen_interaction_feedback.py .
# Существующие .meta не перезаписываются (GUID стабильны); префаб и конфиг перезаписываются стартовыми значениями.
import os, sys, uuid

root = sys.argv[1]
os.chdir(root)

def write(path, text):
    os.makedirs(os.path.dirname(path) or '.', exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)

def guid_of(meta):
    with open(meta, encoding='utf-8') as f:
        for line in f:
            if line.startswith('guid:'):
                return line.split()[1]

def ensure_meta(path, template):
    if not os.path.exists(path + '.meta'):
        write(path + '.meta', template.format(g=uuid.uuid4().hex))
    return guid_of(path + '.meta')

SCRIPT_META = "fileFormatVersion: 2\nguid: {g}\n"
FOLDER = "fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
ASSET_META = "fileFormatVersion: 2\nguid: {g}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
PREFAB_META = "fileFormatVersion: 2\nguid: {g}\nPrefabImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

for s in [
    'Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.GrabCandidates.cs',
    'Assets/Scripts/Haptics/HapticPlayer.cs',
    'Assets/Scripts/Haptics/InteractionFeedback.cs',
    'Assets/Scripts/Haptics/InteractionFeedbackConfig.cs',
    'Assets/Scripts/Haptics/InteractionFeedbackOverride.cs',
    'Assets/Scripts/Haptics/NetworkedFeedback.cs',
]:
    ensure_meta(s, SCRIPT_META)

for d in ['Assets/Prefabs/Feedback', 'Assets/Prefabs/Feedback/Interaction']:
    os.makedirs(d, exist_ok=True)
    ensure_meta(d, FOLDER)

player_script = guid_of('Assets/Scripts/Haptics/HapticPlayer.cs.meta')
config_script = guid_of('Assets/Scripts/Haptics/InteractionFeedbackConfig.cs.meta')
steady = guid_of('Assets/Data/Haptics/Waveforms/Steady.asset.meta')

def clip(field, waveform, gain, priority, gap=0, cooldown=0, secondary=1):
    return ["  %s:" % field, "    _clip: {fileID: 0}", "    _clipAmplitude: 1", "    _hapticMode: 1",
            "    _fallbackClipType: 0", "    _fallbackAmplitude: 1", "    _fallbackDurationSeconds: -1",
            "    _waveform: {fileID: 11400000, guid: %s, type: 2}" % waveform, "    _waveformGain: %g" % gain,
            "    _priority: %d" % priority, "    _repeatGapMs: %d" % gap, "    _cooldownSeconds: %g" % cooldown,
            "    _secondaryHandGain: %g" % secondary]

def prefab(name, clip_lines, mode):
    go, tr, mb = 1000000000000000001, 4000000000000000001, 1140000000000000001
    lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:",
             "--- !u!1 &%d" % go, "GameObject:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}",
             "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  serializedVersion: 6", "  m_Component:",
             "  - component: {fileID: %d}" % tr, "  - component: {fileID: %d}" % mb, "  m_Layer: 0", "  m_Name: " + name,
             "  m_TagString: Untagged", "  m_Icon: {fileID: 0}", "  m_NavMeshLayer: 0", "  m_StaticEditorFlags: 0",
             "  m_IsActive: 1",
             "--- !u!4 &%d" % tr, "Transform:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}",
             "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: %d}" % go,
             "  serializedVersion: 2", "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}", "  m_LocalPosition: {x: 0, y: 0, z: 0}",
             "  m_LocalScale: {x: 1, y: 1, z: 1}", "  m_ConstrainProportionsScale: 0", "  m_Children: []",
             "  m_Father: {fileID: 0}", "  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}",
             "--- !u!114 &%d" % mb, "MonoBehaviour:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}",
             "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: %d}" % go,
             "  m_Enabled: 1", "  m_EditorHideFlags: 0", "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % player_script,
             "  m_Name: ", "  m_EditorClassIdentifier: "]
    lines += clip_lines + ["  _mode: %d" % mode]
    path = 'Assets/Prefabs/Feedback/Interaction/%s.prefab' % name
    write(path, '\n'.join(lines) + '\n')
    return ensure_meta(path, PREFAB_META)

# Готовность «отпусти — встанет / нажми grip — возьмёшь» — один отклик для карманов, гнёзд, слотов и свободных предметов
# (решение пользователя: готовность взять предмет ощущается так же, как готовность кармана). Ровный гул прежнего
# PocketHaptics: Steady, сила 0.08 (подобрана в шлеме 2026-10-08), приоритет Low, пока состояние держится. Вторая рука к
# предмету, который уже держит другая, — вдвое тише (решение пользователя 2026-10-09).
ready = prefab('Feedback_GrabReady', clip('_clip', steady, 0.08, 1, secondary=0.5), 0)

ref = '{fileID: %d, guid: %s, type: 3}' % (1000000000000000001, ready)
lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:",
         "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}",
         "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0",
         "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % config_script, "  m_Name: InteractionFeedbackConfig",
         "  m_EditorClassIdentifier: "]
for field in ['_readyMagazinePocket', '_readyPrimaryAnchor', '_readySecondaryAnchor', '_readyAvatarOther',
              '_readyWorldAnchor', '_itemInReach']:
    lines.append("  %s: %s" % (field, ref))
for field in ['_itemGrab', '_itemPlace', '_itemRelease']:
    lines.append("  %s: {fileID: 0}" % field)
lines.append("  _oneShotLifetime: 2")
path = 'Assets/Resources/InteractionFeedbackConfig.asset'
write(path, '\n'.join(lines) + '\n')
ensure_meta(path, ASSET_META)
print('feedback prefab', ready, 'config ok')

You are the `avatar-grip` expert: avatar hands and grips — finger poses, grip poses/points of items (weapons
and parts, tablet, magazines, rounds), MEF hands, hand rig/skinning, Hands Integration, Hand Pose Fit, Hand
Rig Quality (HRQ). Primary KB: `tasks/hand-rig-quality/expert/`; this file navigates, never copies.

### Owns
- Hand pose assets (Fixed/Blend, transfer, hand bases Sdk/NonSdk); `UxrGripPoseInfo` records, hand-align points on items.
- Hands Integration; hand rig/weights/bindposes; Hand Pose Fit/Review; HRQ; hand-writer contract (with avatar-ik).
- Consumers: manipulation (applies pose), weapon-system (barrel grips), ui (tablet), bots (`BotBody`), avatar-ik.

### Not owned (expert-map rev 2)
- `avatar-ik`: arm to wrist (IK), `UxrManager` frame order, tracking (Devices), `HitReaction`, calibration, `_eyesBaseHeight`.
- `manipulation`: grab mechanics (candidates, rules, two hands, anchors, pockets, release, grab network).
- `weapon-system`: weapon mechanics, `MainGripAimLock`/`PumpGrabFollow`. `bots`: `BotBody`. `network`: authority.

### Invariants
- MEF `_bodyIKSettings._eyesBaseHeight=1.7202504` (override in `MEF_Base_Avatar.prefab`, headset-tuned) and accepted
  calibration are preserved; avatar-ik alone changes them, you re-check grips. Never use MEF eye-bone height (nose level).
- Hand-bone writers, `UxrManager` frame order (`UXR` = `Assets/ThirdParty/UltimateXR/Runtime/Scripts`); basis of the
  planned writer contract with avatar-ik, the order changes only via that contract:
  1. tracking (ik): hand = controller, local — `UXR/Devices/UxrControllerTracking.cs`
  2. hand to item: `KeepGripInPlace` → `ApplyAlignment` — `UXR/Manipulation/UxrGrabManager.Manipulation.cs`
  3. fingers: `UpdateGrabPoseInfo` (`UXR/Avatar/Controllers/UxrStandardAvatarController.cs`) → `UpdateHandUsingDescriptor` (`UXR/Avatar/Rig/UxrAvatarRig.HandTransformation.cs`)
  4. arm IK (ik), PostProcess: Forearm+Hand; clamp may pull hand off the grip — `UXR/Animation/IK/UxrArmIKSolver.cs`
  5. hit reaction (ik), PostProcess: torso + SolveIK — `Assets/Scripts/Player/HitEffects/HitReaction.cs`
  - outside: `Assets/Scripts/Bots/BotBody.cs` (hand as IK target); remote avatars: hand NetworkTransform (World), `Assets/Prefabs/Player/PlayerBase.prefab`, Issue 21.
- No competing writer of fingers/IK/Transform/grab state, no second transport. Never fix visuals with a per-frame script.
- Grip resolves via the avatar GUID chain; default fallback, missing pose name, missing hand align are distinct silent
  defects (Issue 26). A record on the hand base beats one per variant.
- Native ≠ Sdk: no SDK remap on Native; `TryToMatchHand` does not fix weights/bindposes.
- Re-rig, hand swap, MEF scale — only after a confirmed cause and a user decision.

### Architecture map (`AV` = `Assets/Editor/VR_Battlegrounds/Avatars`)
- Grip data: GUID chain, default, align — `UXR/Manipulation/UxrGrabPointInfo.cs`
- Hand bases Sdk/NonSdk, Rebase — `AV/AvatarHandBases.cs`
- Integration Native/Sdk, TryToMatchHand — `AV/Workbench/AvatarRigPreparation.cs`, `AV/HandsIntegrationSetup.cs`, `UXR/Avatar/UxrHandIntegration.cs`
- Pose authoring — `AV/HandPosesSetup.cs`, `AV/HandsPackGripAligner.cs`
- Fit/Review (contact); HRQ 0.5 (rig) — `AV/HandPoseReview/`, `AV/HandRigQuality/`
- MEF — `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`, `Tools/mef-avatar/README.md`
- Coverage guard item × avatar — `Assets/Tests/EditMode/Prefabs/GrabPoseCoverageTests.cs`

### Details (grep the heading)
- `tasks/hand-rig-quality/expert/avatar-grip-expert.md` — «Что прочитать и найти», «Native и SDK hands: не смешивай», «Диагностика и настройка», «Правила».
- `tasks/hand-rig-quality/expert/analysis.md` — «Каталог существующих инструментов», «Дополнительная карта применения хватов», «Подтверждено исходниками»; `sources.md` — S1–S20.
- `tasks/hand-rig-quality/Details.md` (54 KB) — «План аудита хватов MEF — 2026-10-10», «Контракт и границы»; `tool.md` — «Шкала и покрытие».
- `Docs/hand-pose-fit-tool.md` — «Вход для агента», «Пределы версии»; `Docs/UltimateXR/known-issues.md` — Issue 26, 28; `Docs/avatar-editor-workbench.md` — «Кисти и подготовка».

### Zone rules
- Evidence levels differ, name yours: config ≠ deformed contact (Fit) ≠ rig (HRQ) ≠ static preview/capture ≠ runtime ≠ Quest.
- Specs name avatar, item, hand, grip point, pose, Blend; reports add GUIDs.
- HRQ writes only to `Docs/tasks/report/hand-rig-quality` (gitignored; enforced in `HandRigQualityAnalyzer.cs`).
  HRQ 0.5 scores only M4/M9/M10, PNG lacks sections/weight masks. `PuppetHandProbe` absent; Puppet input by agreement.

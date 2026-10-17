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
- Fit signed/rules metrics are approximate. Distance uses the whole item surface; winding-based sign has
  limits on open/reversed meshes. `ok` means no gross deviation from an SDK band, never grip acceptance.
- Edit preview applies numeric bend/offset only to a temporary static clone, remeasures and restores it.
  Source hash/dirty checks do not prove runtime or Quest behavior.

### Architecture map (`AV` = `Assets/Editor/VR_Battlegrounds/Avatars`)
- Grip data: GUID chain, default, align — `UXR/Manipulation/UxrGrabPointInfo.cs`
- Hand bases Sdk/NonSdk, Rebase — `AV/AvatarHandBases.cs`
- Integration Native/Sdk, TryToMatchHand — `AV/Workbench/AvatarRigPreparation.cs`, `AV/HandsIntegrationSetup.cs`, `UXR/Avatar/UxrHandIntegration.cs`
- Pose authoring — `AV/HandPosesSetup.cs`, `AV/HandsPackGripAligner.cs`
- Fit/Review schema 4, mesh-fit-0.6-grip-rules — `AV/HandPoseReview/`: signed per-finger/distal-pad metrics,
  SDK bands, gross rules, hints, review sheets/edit preview; HRQ 0.5 (rig) — `AV/HandRigQuality/`.
- Grip Extraction — `AV/GripExtraction/GripExtraction.cs` (`RunJson`): pack source → target profile/rig →
  placement/encoder → pose assets/record writer. Reuses importer recipes, registry and align/cleanup helpers;
  no second grip writer. Measure/Preview/Apply are distinct; avatar-record/manual adapters and auto-bend
  are not implemented. Profiles separate rig prefab, record GUID and pose registry.
- Save identity: GripRecordWriter adds synchronous NetworkAssetIdNormalizer + disk check after
  SaveAsPrefabAsset, retaining identity guard. Worker647: compiled witness + Apply accepted Viper PASS,
  all3 asset bytes unchanged/canonical id. Worker683: full EditMode57,56PASS1FAIL (older Cyborg own-head);
  identifiers4/4 and hand-chain13/13 PASS. User accepted merge on 2026-10-15; publication tracked in task Readme.
- MEF — `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`, `Tools/mef-avatar/README.md`
- Coverage guard item × avatar — `Assets/Tests/EditMode/Prefabs/GrabPoseCoverageTests.cs`

### Details (grep the heading)
- `tasks/hand-rig-quality/expert/avatar-grip-expert.md` — «Что прочитать и найти», «Native и SDK hands: не смешивай», «Диагностика и настройка», «Правила».
- `tasks/hand-rig-quality/expert/analysis.md` — «Каталог существующих инструментов», «Дополнительная карта применения хватов», «Подтверждено исходниками»; `sources.md` — S1–S20.
- `tasks/hand-rig-quality/Details.md` (54 KB) — «План аудита хватов MEF — 2026-10-10», «Контракт и границы»; `tool.md` — «Шкала и покрытие».
- `Docs/hand-pose-fit-tool.md` — «Вход для агента», «Пределы версии»; `Docs/UltimateXR/known-issues.md` — Issue 26, 28; `Docs/avatar-editor-workbench.md` — «Кисти и подготовка».

### Zone rules
- Partial source review 2026-10-14 at origin/dev d3c62ca5: old owns delta + Grip Extraction API/boundaries
  reviewed; MKR9 mirror defect separately diagnosed. `reviewed_sha` stays unchanged: this is no full-zone,
  runtime or watch-asset review. Evidence: tasks/hand-rig-quality/reports/resume-2026-10-14/mkr9-diagnosis.md.
- Evidence levels differ, name yours: config ≠ deformed contact (Fit) ≠ rig (HRQ) ≠ static preview/capture ≠ runtime ≠ Quest.
- Specs name avatar, item, hand, grip point, pose, Blend; reports add GUIDs.
- HRQ writes only to `Docs/tasks/report/hand-rig-quality` (gitignored; enforced in `HandRigQualityAnalyzer.cs`).
  HRQ 0.5 scores only M4/M9/M10, PNG lacks sections/weight masks. `PuppetHandProbe` absent; Puppet input by agreement.

### Как использовать этот профиль
- Агент задачи: перед правкой путей из `owns` (`expert.json`) прочитать этот файл и нужный раздел Details; уровень доказательства называть явно (Zone rules).
- Ревьюер (чеклист diff):
  - `_eyesBaseHeight` и принятая калибровка MEF не изменены без решения avatar-ik/пользователя;
  - порядок писателей кисти прежний; нет нового писателя пальцев/IK/Transform/grab state, второго транспорта, покадровой правки визуала;
  - хват через GUID-цепочку; fallback, нет имени позы, нет hand align — не маскируются (Issue 26);
  - Native ≠ Sdk; re-rig, смена кисти, масштаб MEF — только с подтверждённой причиной и решением пользователя;
  - запись хвата в prefab сохраняет identity (NetworkAssetIdNormalizer + проверка на диске);
  - Fit `ok`, preview и hash не выдаются за приёмку хвата, runtime или Quest.

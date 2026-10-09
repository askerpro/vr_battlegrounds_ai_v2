# Ограниченный перечень источников

2026-10-09. Текущий checkout: `F:/CodexWorktrees/hands-rig-quality/Vr_Battlegrounds_ai`,
HEAD `f8daff6843f35c569268fc33f0d4b9a8537dd8d0`. Linked worktree подтверждён различием
absolute-git-dir и git-common-dir. Исходники HRQ/HandPoseReview частично dirty/untracked;
это анализ доступного рабочего состояния, не только коммита. Команды — чтение; Unity не запускалась.
Ссылки на строки — навигация в этом рабочем состоянии, не стабильные permalink.

## Главные источники кистей/Hands Integration и инструментов

- S14: [Workbench/AvatarRigPreparation.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/Workbench/AvatarRigPreparation.cs),
  прочитан полностью: Inspect/Build, Native/Sdk ветки, rig info и pose output.
- S15: [Workbench/AvatarEditorToolsPanel.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/Workbench/AvatarEditorToolsPanel.cs),
  строки1–117: sdkAllowed/CanRunSdkSteps, явные цели, Blender/SDK actions;
  [AvatarEditorWindow.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/Workbench/AvatarEditorWindow.cs)
  адресный поиск OpenFor/Native предупреждения/OpenSdkPose.
- S16: [HandsIntegrationSetup.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandsIntegrationSetup.cs),
  [FinalizeRigMappingSetup.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/FinalizeRigMappingSetup.cs)
  Setup/MapHandRig/MapFinger; [UxrHandIntegration.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrHandIntegration.cs)
  прочитан полностью TryToMatchHand/Awake. Native logic integration отличается от SDK replacement/remap.
- S17: [HandRigQualityAnalyzer.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/HandRigQualityAnalyzer.cs)
  Analyze/Validate/OutputDirectory1–67, адресный поиск Measure statuses;
  [HandRigQualityTypes.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/HandRigQualityTypes.cs)
  request/metric/snapshot/report;
  [HandRigQualityCalibration.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/HandRigQualityCalibration.cs)
  Apply83–120/fingerprints/score;
  [HandRigQualityWindow.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/HandRigQualityWindow.cs)
  menu/calibration/Analyze (адресный поиск).
- S18: [Docs/hand-pose-fit-tool.md](../../../Docs/hand-pose-fit-tool.md), разделы назначения,
  запуска, SDK встроенной диагностики, runtime, показателей/reliability и fingerprints;
  код [HandPoseFitAnalyzer.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/HandPoseFitAnalyzer.cs)
  Analyze/AnalyzeCaptured/export найден адресным поиском. Docs содержит старый BakeMesh(false);
  текущий общий capture true/matrix и task docs приоритетны для этой детали.
- S19: [UxrHandPoseEditorWindow.cs](../../../Assets/ThirdParty/UltimateXR/Editor/Manipulation/HandPoses/UxrHandPoseEditorWindow.cs)
  MenuItem99/Open145 найдены адресно; SDK окно доступно через AvatarEditorWindow.OpenSdkPose.
- S20: [Tools/mef-avatar/README.md](../../../Tools/mef-avatar/README.md), прочитаны соответствующие
  разделы исторических этапов, weights/export/scale/bind и предупреждение реконструкции;
  скрипты00–10 инвентаризированы, не запускались;
  [CustomAvatarPipelineMenu.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/CustomAvatarPipelineMenu.cs)
  SDK шаги73–78 и Blender routes найдены адресно. Будущий stage E из HRQ Details в текущих скриптах не найден.

## Дополнительные источники применения хватов

| ID | Источники и проверенные точки |
|---|---|
| S1 | [PlayerGrabManager.cs](../../../Assets/Scripts/Player/PlayerGrabManager.cs) строки13–90: delegate lifecycle/ghost; [GrabRules.cs](../../../Assets/Scripts/Player/GrabRules.cs) строки12–62: map admission и цепочка правил |
| S2 | [TwoHandGrabPolicy.cs](../../../Assets/Scripts/Player/TwoHandGrabPolicy.cs) строки27–77: выбор/уступка занятой точки |
| S3 | [SupportGripRequiresMain.cs](../../../Assets/Scripts/Interaction/SupportGripRequiresMain.cs) строки27–39; [AnchoredItemGrabRule.cs](../../../Assets/Scripts/Interaction/AnchoredItemGrabRule.cs) строки28–49; [GrabOnlyWhenParentHeld.cs](../../../Assets/Scripts/Interaction/GrabOnlyWhenParentHeld.cs) метод AllowsGrab (прочитан через адресный поиск) |
| S4 | [UxrGrabPointInfo.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabPointInfo.cs) строки348–366; [UxrGripPoseInfo.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGripPoseInfo.cs) HandPose/hand align поля (адресный поиск); [UxrGrabbableObject.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.cs) строки1622–1655 |
| S5 | UxrGrabbableObject.cs строки1130–1193; [UxrGrabManager.Querying.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Querying.cs), [UxrGrabManager.GrabCandidates.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.GrabCandidates.cs) — точки входа найдены; полного candidate audit не было |
| S6 | [UxrStandardAvatarController.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs) строки1004–1080; [UxrManager.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/UxrManager.cs) адресный поиск фаз1931–2033, без доказательства полного runtime IK |
| S7 | [UxrGrabManager.Manipulation.cs](../../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Manipulation.cs) public Grab/Release/Place96/126/165; UpdateManipulation666–730; grab NotifyBegin/Grabbed/Removed/EndSync987–1027; release Releasing/NotifyEnd1138–1160 и Released/EndSync1218–1253; constraints2135–2190 |
| S8 | [MainGripAimLock.cs](../../../Assets/Scripts/Weapons/MainGripAimLock.cs) handler ConstraintsApplied; [PumpGrabFollow.cs](../../../Assets/Scripts/Weapons/PumpGrabFollow.cs) handler ConstraintsApplied/FollowHand. Прочитаны полностью как предметные адаптеры удержания |
| S9 | [UxrMagazinePocket.cs](../../../Assets/Scripts/Interaction/UxrMagazinePocket.cs) строки47–113,253–291; [AnchorPlacementReadiness.cs](../../../Assets/Scripts/Interaction/AnchorPlacementReadiness.cs) TryGetReadyGrabber, прочитан полностью |
| S10 | [StateEventAuthority.cs](../../../Assets/Scripts/Network/StateEventAuthority.cs) IsAuthorOfItem/IsWorldAuthority/ShouldSend/TryGetHolders; [NetworkStateRelay.cs](../../../Assets/Scripts/Network/NetworkStateRelay.cs) строки195–309: publication/transport/replay exclusions. Полного security/identity/snapshot audit нет |
| S11 | UxrGrabbableObject.cs строки2022–2040, RegularPhysicsSyncCoroutine. Старт/все release authority ветки не аудированы |
| S12 | [HandPosesSetup.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandPosesSetup.cs) Setup/pose/controller assignments (адресный поиск); [HandPoseFitRuntimeCapture.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/HandPoseFitRuntimeCapture.cs) Request guards; [HandPoseFitSnapshot.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/HandPoseFitSnapshot.cs) runtime constructor/CaptureHand; [HandMeshSample.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/Core/HandMeshSample.cs) HandMeshCapture.Capture77–120; [HandRigCapture.cs](../../../Assets/Editor/VR_Battlegrounds/Avatars/HandRigQuality/HandRigCapture.cs) Build/descriptor/capture (адресный поиск) |

## Документы текущей задачи и проекта

- S13: [Readme.md](../../Readme.md), [Details.md](../../Details.md), [tool.md](../../tool.md): границы,
  единый capture, MEF eyes override, статический срез0.5, старая база/открытая динамика и Puppet контракт.
- [troubleshooting.md](../../../Docs/troubleshooting.md): строки297–363 (сеть/физика/две руки),
  адресный поиск симптомов410–440 (вставленный предмет/placement).
- [UltimateXR/known-issues.md](../../../Docs/UltimateXR/known-issues.md): Issue13/14 найдены поиском;
  Issue26/28/30 прочитаны1021–1094. Отчёты внутри этих секций исторические; claims «исправлено»
  не преобразованы в свежий PASS. Issue28 содержит старый статус, который расходится с WIP/task docs.
- [Docs/README.md](../../../Docs/README.md): адресный поиск карты существующих проверок917–930.
  [gameplay.md](../../../Docs/gameplay.md): адресный поиск требований grabbing/pockets; полного чтения нет.
- [.agents/rules/project-workflows.md](../../../.agents/rules/project-workflows.md):
  verification184–202 прочитан; разделы координации/delegation найдены поиском.
  Канонический AGENTS.md и точный разрешённый scope переданы principal в поручении.

## Структурный образец — другой checkout, только чтение

Каталог `F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/tasks/weapon-system/reports/expert-kb/`:

- `weapon-system-expert.agent.md`, `README.md`, `architecture.md`, `invariants.md`, `decisions.md`,
  `vision.md`, `workflow.md`, `roadmap.md`, `migration.md` прочитаны по содержанию.
- `sight-calibration.md`: адресный поиск границ хвата/ShotSource/writer и раздел инвариантов79–101.
- Это черновая KB в reports; её docs/profile migration — отдельный будущий этап.
  Указанные там принятые SHA/этапы не подтверждены чтением Git соседней задачи.
- Формат Claude YAML, model opus, memory project, skill add-weapon отделены от знаний;
  в grip draft модель/memory/skills не скопированы. Предметное содержимое оружейной системы
  не является обязательной KB эксперта хватов.

## Предел проверки

Principal дополнительно проверил AvatarRigPreparation.Build, HandsIntegrationSetup.Setup,
HandMeshSample/HandMeshCapture и HandRigQualityRenderer.Render. Уточнено: Native тоже использует
integration logic; Bones Transform[] не сериализуется; PNG не содержит оверлея вычисленных сечений
или масок весов. Диагностические зависимости от позы сформулированы как гипотезы, не готовый диагноз.

Читался ограниченный набор методов и документов, перечисленный выше. Сцены/префабы, полный SDK,
реестр всех предметов/аватаров, Android compilation, TestRunner, Play/Quest и два клиента не проверялись.
Результат исследования — предложение scope и профиля; положительный runtime verdict отсутствует.

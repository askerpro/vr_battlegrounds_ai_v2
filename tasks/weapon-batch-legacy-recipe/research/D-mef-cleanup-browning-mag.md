# Исследование D: чистка MEF_Base_Avatar, вторая точка BrowningHiPower_Magazine (2026-10-23, read-only)

## MEF_Base_Avatar (b6fe59db…)
- Только в `Assets/Prefabs/Weapons/**`: 34 префаба (17 стволов + 17 магазинов/патронов) + 6 неизменяемых доноров (не трогать).
  67 записей (55 с позой, 11 без позы с align — затворы/помпы/слайды, 1 пустая — BrowningHiPower_Magazine точка 1),
  132 align (66 пар), 52 папки `MEF_Base_Avatar` (115 потомков), ещё 26 MEF-align вне папок (`Left/Right Grab` рядом с Cyborg
  на затворе/помпе) в AK105, AR15, BrowningHiPower, FabarmSDASS, Herrington, MKR9, MP5K, Mk14, PPK, SRM12, Scar, SniperRifle,
  TR15, Uzi, Viper. Развёрнутые копии патронов Fabarm/Herrington — только запись MEF.
- Ссылки: записи Cyborg ссылаются на узлы внутри MEF-папки — AR15 (`AR15.prefab:148-149, 187-188`, 4 узла, две одноимённые
  папки) и Fabarm (`FabarmSDASS.prefab:2348-2349`, 2 узла): перенести в `Grabs/CyborgHandsBase/<точка>` (`SetParent(...,true)`).
  40 overrides `m_Modifications` (`__prefabGuid`/`__isInPrefab` на SnapTransform узлов MEF-папки магазина) в 15 стволах
  (AK105 2, AR15 4, BrowningHiPower 4, MKR9 2, MP5K 4, Mk14 2, PPK 2, R08 2, Revolver 2, SRM12 2, Scar 4, SniperRifle 4, TR15 2,
  Uzi 2, Viper 2) — снять `PrefabUtility.RemoveUnusedOverrides` после чистки магазина. Других ссылок нет.
- Вне оружия записей MEF нет; сам префаб MEF удалять нельзя (стенды AvatarPuppetStand/ClipScrub/SitCandidates, CyborgLegsBuilder,
  AvatarMixamoLocomotionSetup, `HandsPackPoseImporter.MefBaseAvatarPath`). В игре записи MEF не используются.
- Инструмент (сухой прогон + применение), например `Assets/Editor/VR_Battlegrounds/Avatars/RetiredGripAvatarCleanup.cs`:
  проход A магазины (записи MEF через SerializedObject, `HandsPackPoseImporter.RemoveUnused`, сироты `(MEF_cyborg_hands)` в
  AR15_Magazine и пустая папка BrowningHiPower_Magazine — только если без ссылок и только Transform+SnapTransform),
  SaveAsPrefabAsset + NetworkAssetIdNormalizer; проход B стволы — то же + перенос 6 Cyborg-узлов + копии патронов +
  RemoveUnusedOverrides. Порядок магазин → ствол.
- Чтобы пересборка не вернула MEF: `HandsPackWeaponBuilder` — `RetiredGripAvatars = {MefBaseAvatarGuid}`,
  `DroppedGripAvatars = SdkSampleAvatars ∪ Retired`; `RemoveOrphanGripEntries` (`:1052`) по Dropped; `BuildMagazine` после `:912`
  удалять пустые `Grabs/MEF_Base_Avatar`; `WeaponGripCarryOver.cs:200` — `&& !droppedGuids.Contains(e.AvatarGuid)`; передавать
  Dropped во все Restore (`HandsPackWeaponBuilder.cs:325`, `HandsPackLegacyRebuild.cs:109`, `KinemationWeaponBuilder.cs:369`, `:374`).
  Источник возврата — доноры `Shotgun_real_mag` (запись MEF), `Gun_real_mag` (пустая папка) и `Restore` `:200`.
- Явные писатели MEF: `HandsPackPoseImporter.GripAvatarGuids = {MEF}` (`:62`, запись `:246-259`, через него
  `KinemationWeaponBuilder.ImportPoses` `:439-446`), режим `WeaponGripPoseMode.ReimportFromPack` (`WeaponGripCarryOver.cs:150`),
  Grip Extraction `GripTargetProfile.Mef()` (`:100-117`), цель по умолчанию "MEF" (`GripExtractionRequest.cs:32`), R0
  (`GripExtraction.cs:246`).
- Тесты: `PumpGrabFollowTests.cs:27-34` хватает помпу Fabarm аватаром MEF — ожидание устареет (перевести на Cyborg после
  приёмки); GrabPoseCoverage, CyborgGripEntriesOwnership, AvatarGripRegistration (фикстура), PackMagazineStructure,
  HandPoseGripRules — не зависят.

## BrowningHiPower_Magazine `_additionalGrabPoints[0]`
- Вторая точка только у этого магазина; из донора `Gun_real_mag.prefab:1270-1318` (наследие сэмпла SDK, GrabBall); копируется
  `BuildMagazine` (`:877-889`) при `PreserveDonorGameplay`. История: cf8f1b4ba — записи `[]`; a2c2f7e51 — пустые MEF и Cyborg
  (инспектор `Editor_CheckRegisterAvatarsInNewGrabPoints`).
- Участвует в игре: включена, bothHands, `_maxDistanceGrab` 0.12, близость/align — корень магазина; SDK выбирает точку по
  расстоянию+углу → магазин может встать в ладонь pivot'ом с общей позой Grab; `_allowMultiGrab` + `TwoHandGrabPolicy` — вторая рука
  уходит на точку 1 вместо перехвата; своя зона досягаемости.
- Тест зелёный за счёт пустой записи Cyborg. Убрать обе записи, оставив точку — красный; убрать точку — зелёный.
- Вариант A (рек.): убрать точку + правка сборщика (иначе легаси вернёт 2 точки и `Restore` упадёт «точек хвата 2, было 1»
  `WeaponGripCarryOver.cs:164-165`): удалять доп. точки магазина без записей после `RemoveOrphanGripEntries` или флаг рецепта.
- Вариант B: нормальный хват Cyborg точки 1 (поза, align вручную, приёмка в шлеме).

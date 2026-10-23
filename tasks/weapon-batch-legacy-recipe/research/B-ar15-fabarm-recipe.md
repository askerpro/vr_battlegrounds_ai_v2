# Исследование B: AR15 и FabarmSDASS на полный рецепт Hands без доноров (2026-10-23, read-only)

## Главное
1. `BuildFull` сам не работает без доноров: `RifleDonor`/`PistolDonor` (`HandsPackWeaponBuilder.cs:160-191`) берут физику,
   хват, якорь, снаряд, спуск, звук выстрела у живых `AR15.prefab`/`BrowningHiPower.prefab`; AWSF затвора — у снимков
   `LegacyBuildDonors/M16_Rifle_prefab` и `Gun_real` (`:826-834`). AR15 в `RifleDonor` = донор сам себе. `ShotgunReal`
   (`:114-147`) берёт хват/якорь/физику у AR15 (`:137-141`), спуск/помпу/дробь — у снимка `Shotgun_real` (`:144`).
2. Звуки из пака полный путь Hands не выводит: только явные поля `ShotAudio` (`:698-699`), `SlideBack/ForwardAudio`
   (`:838-843`), `TakeOutAudio` (`:601-602`), `LoadAudio` (`:750`). Помпа — копия `UxrShotgunPump` донора (`:809`). Вставка в
   MagAnchor обнуляется (`:597`). Вывод звуков из пака есть только у KINEMATION: `MakeSounds` (`KinemationWeaponBuilder.cs:380-416`,
   `KinemationAudio.Shot/CutAtEvent/SplitAtGap`).
3. MechanismMotion — только легаси (`ExportMotion` `HandsPackLegacyRebuild.cs:305-334`, `InstallMotion` `:348-421`); у T-38
   `_motion: 0`. У AR15 motion двигает `Rifle_Detail_01..04` (затвор, спуск, две детали) — перевод как есть потеряет анимацию;
   у Fabarm — только спуск.
4. У AR15 в паке затвор и спуск неподвижны (`Docs/tasks/T-39-weapon-interaction-extension.md:59`): ход −0.06 по z и спуск 40°
   приходят от донора (`ResolveActionTravel :451-476`, `ResolveTriggerRotation :481-493`, только при PreserveDonorGameplay).
   Без них `WeaponSystemAuthoring.ReadSlideGeometry` (`:633-634`) откажет «Ручка затвора без хода». Первый шаг —
   `HandsPackWeapon("Hands_Automatic_Rifle03","Rifle_Body_Mesh").Report()` и клипы `Get`, `Aim_Shot`.
5. Патрон Fabarm разворачивает `ManualLoadingAuthoring.ConfigureWeapon:61-62` (общее с Herrington).

## Что легаси берёт у доноров (`HandsPackLegacyRebuild.Configure :248-281`, `Recipes :209-246`)
UxrTag, MagazineBase = снимок, MagazineTag, ёмкость (кроме Fabarm), GripDonor = снимок M16 (обоим), FirearmDonor = снимок
ствола, PreserveDonorGameplay, NetworkAssetId/MagazineAssetId, TargetLength из масштаба корпуса донора, StaticParts, AR15
FinalRootScale 0.70; AR15 Trigger/ActionPart из донора (`Rifle_Detail_02/01_Mesh`); Fabarm ShotAudio=null,
ExcludedSourceParts `Shogun_Patron_mesh001`.
PreserveDonorGameplay: копия NI (`:518-524`), shotTypes донора (`:635-669`), ShotgunPellets (`:674`), RecoilAccumulator/
WeaponSpread (`:706-718`), запасной ход/спуск (`:451`, `:481`), шаблон магазина = донор (`:889-900`, `:976`).
Общее (донор по полям): Rigidbody, якорь (AudioSource, AnchorSound), хват корня, снаряд, UxrFirearmWeapon (выстрел, нет
патронов, режим, темп), граббабл затвора/помпы, UxrShotgunPump, AWSF.
Из пака: оси/место корпуса, детали, ход/спуск из клипа, дуло/ShotSource/BarrelCheck, MagAnchor, точки по ладоням
(отдача, MainGripAimLock, подсветка), меш магазина, `_orig`.

## Звуки сейчас и в паке
AR15: выстрел `Effects/Automatic_rifle/VFX/Sounds/MachineGun2.wav` (тот же у Scar, Uzi, MP5K, SniperRifle; `WeaponShotSoundTests`
в known-red), затвор `Assets/Audio/SFX/Universal/Zatvor1/2`. Fabarm: выстрел из пака (`10Shotgun_Set/edit/shot.mp3`), помпа —
SDK `ShotgunPump01/02`, 0.3. Пак: `1Assault_Rifle_Set` — `Rifle_Shot_1..3`, `Reload.mp3`, отдельного затвора нет;
`10Shotgun_Set` — `shot`, `Shot_Full`, `Zatvor.mp3`, `Reload_1`.

## Рецепты
AR15: Folder `Hands_Automatic_Rifle03`, Body `Rifle_Body_Mesh`, PoseClip `Aim_Idle`, ActionClip `Shot`, Trigger
`Rifle_Detail_02_Mesh`, Action `Rifle_Detail_01_Mesh`, Static `Rifle_Detail_03/04_Mesh`, SupportGrip, UxrTag `M16_Rifle`,
MagazinePart `Rifle_Magazine_Mesh`, MagazineName `AR15_Magazine`, MagazineTag `M16_Mag`, 30, FinalRootScale 0.70,
TargetLength ровно как у легаси (baselineLength preflight). Не хватает: ход/спуск, MechanismMotion, звук затвора.
Fabarm (`ShotgunReal` есть): перенести ExcludedSourceParts, снять доноры `:137-144`, поле звуков помпы (`_audioSlide`,
`_audioSlideBack`, `WeaponSystemAuthoring.cs:36-38`), порог помпы — SDK 0.7 (= текущему `_extractionGate`), проверить
TargetLength 0.72 vs масштаб 1.1892964.
Ожидаемые расхождения: звуки (цель); вспышка AR15 `Muzzle_Rifle`→`Muzzle_Default`; баланс тот же (WeaponBalanceApplier);
скорость/дальность снаряда (AR15 400/120, Fabarm 300/60) — только при шаблоне «текущий префаб»; _assetId тот же;
хваты KeepCurrent — при том же масштабе корня; overrides `__prefabGuid` вложенного магазина AR15 пересоздадутся;
прицел SightKit_TR15 вернёт AfterBuild.
`_orig` Grip/Support: нужны позы (`HandsPackPoseImporter.cs:84-125` или вывод из рецепта, см. исследование C).

## Browning и доноры
Browning остаётся на легаси (Shot_end, HoldOpen, расширение хода `InstallMotion :382-397`). Сузить `Targets` (`:32`), `Recipes`,
`CaptureDonors` (`:188-190`) — по Targets; `DonorSnapshot` (`:165-166`) оставить (AWSF `:832`).
| Снимок | Читают | Удалить? |
| `M16_Rifle_prefab` | AWSF RifleDonor (Scar, Uzi, MP5K, SniperRifle), `WeaponNamingMigration.cs:75` | нет |
| `M16_Magazine`, `Shotgun_real_mag` | легаси-рецепт, миграция | да, с правкой миграции |
| `Shotgun_real` | `ShotgunReal.FirearmDonor`, миграция | если рецепт откажется |
| `Gun_real`, `Gun_real_mag` | Browning, AWSF PistolDonor (PPK, Revolver) | нет |
Историческая команда миграции имён (`ArsenalEditorWindow.cs:301`) упадёт без снимков.

## План правок
1. `HandsPackWeaponBuilder.cs`: рецепт AR15, `ShotgunReal` без доноров, общий набор T38+AR15+Fabarm (`:299`); поля рецепта —
   явные ход/спуск «нет в паке», звуки помпы, звук вставки, флаг MechanismMotion, звуки пака; помпа (`:806-822`) и MagAnchor
   (`:592-603`) — из рецепта; `ResolveActionTravel/TriggerRotation` — явные значения без Preserve; AWSF запасной путь — порог 0.855,
   возврат 1.5 константами; `BuildFull` — порядок motion (без хоста → motion → хост), outputs `Art/Weapons/HandsPack/<Name>` +
   звуки, проверки GUID/root localFileID/assetId и `ValidateSourceParts`; удалить `BuildShotgunReal` (`:149-153`).
2. Звуки Hands на `KinemationAudio` (аналог `MakeSounds`), добавить `Excursion` в `HandsPackWeapon`.
3. `HandsPackLegacyRebuild.cs` — только Browning; `ExportMotion/InstallMotion` — в общий класс.
4. `ManualLoadingAuthoring.cs:61-62` — вложенный экземпляр + overrides.
5. Окно арсенала: `ArsenalEditorActions.cs:188-189`, `:202-203`; `ArsenalEditorWindow.cs:402-406` (3+3 → Browning), `:363`, `:371-373`
   (`Ar15ScaleCalibration` лишняя).
6. Дубли заглушек: `PackOrigPoses.cs:176-189`, `WeaponInteractionReview.cs:27-28,54-74`, `WeaponInternalAmmoAlignment.cs:197`,
   `PackMagazineStructureTests.cs:27-31`.
7. Позы Grip/Support AR15/Fabarm (зона avatar-grip).
8. Тесты — после приёмки.
9. Docs: SKILL add-weapon `:10-11,97`, `Docs/grip-extraction.md:535`, `Docs/weapons/sight-calibration.md:236,283`,
   `Docs/Arsenal/editor-workbench-plan.md:166,169`; `Docs/README.md:474-476` — общий индекс, не правим.

## Риски
Откат: звуки и motion в `extraPaths`; preflight `Excursion` до перезаписи. Порядок: T-38 копируют выстрел/вспышку живого AR15 —
дать стволам T-38 явный `ShotAudio`. Тесты: HandsPackWeaponTests (ход/масштаб AR15), WeaponFeedbackTests (TakeOutClip якоря),
GrabPoseCoverage, CyborgGripEntriesOwnership, PackMagazineStructure, NetworkAssetIdOnDisk. Сеть: состав вложенных объектов в
префабах с NI — ревью network, heavy. Зоны: weapon-system, avatar-grip, network.

## Вопросы пользователю (из исследования)
1. «Без доноров» для того, чего нет в паке (физика, хват, якорь, скорость/дальность снаряда, режим, темп): шаблон — текущий
   префаб ствола (как у магазина) или константы в рецепте. 2. Ход затвора AR15 0.06 / спуск 40°: константы или искать клип.
3. Звук затвора AR15: из `Reload.mp3`, `8Pistol/Zatvor.mp3` или дефолт категории. 4. Помпа Fabarm: резать `Zatvor.mp3`;
выстрел `Shot_Full` или `edit/shot`. 5. Снятие/вставка магазина AR15 из `Reload.mp3`. 6. Вспышка AR15. 7. Удаление доноров
(безопасно `M16_Magazine`, `Shotgun_real_mag`, `Shotgun_real`). 8. Новые `_uxrUniqueId` скрытого патрона. 9. MechanismMotion в
полный путь сразу; Browning в этом этапе? 10. `ManualLoadingAuthoring` для обоих дробовиков сразу.

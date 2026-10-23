# F: этап 3 — AR15/FabarmSDASS на полный рецепт, общие значения без доноров, звуки из пака (2026-10-23)

Реализация кода без Unity: офлайн-компиляция всех сборок проекта и тестов — **код 0, 0 ошибок** (`offline_build.py --tests`).
Ни Unity, ни тесты не запускались; ассеты не менялись (снимки `LegacyBuildDonors` удалит главный агент).

## Файлы

Новые (`Assets/Editor/VR_Battlegrounds/Gameplay/`, с `.meta`):
- `HandsPackBuildDefaults.cs` — общие значения вместо доноров для Hands и KINEMATION.
- `HandsPackAudio.cs` — `HandsPackSoundRecipe` (что резать) и `HandsPackAudio.MakeSounds` (нарезка `KinemationAudio`).
- `HandsPackMechanism.cs` — общий MechanismMotion (`Export`/`Install`), `ResolveStaticParts`, `ValidateSourceParts`.

Изменённые: `HandsPackWeaponBuilder.cs` (рецепт, AR15, FABARM, T-38, `Full`, `BuildFull`, `PrefabIdentity`, сборка без доноров),
`HandsPackLegacyRebuild.cs` (только Browning, публичный рецепт `Browning`, без снимков), `HandsPackWeaponImporter.cs`
(`HandsPackWeapon.Excursion`), `KinemationWeaponBuilder.cs` (`Rifle/Pistol` без доноров, SRM12 `ManualCycle`),
`WeaponBalanceApplier.cs` (`_cycleType`), `PackOrigPoses.cs` (`FullSetRecipes` = AR15+FABARM, `LegacyStubs` = `HandsPackLegacyRebuild.Browning`),
`WeaponInteractionReview.cs` (общий набор, без `LegacyPalmSources`), `WeaponNamingMigration.cs` (не требует снимков),
`Arsenal/ArsenalEditorActions.cs`, `Arsenal/ArsenalEditorWindow.cs`, `Arsenal/ArsenalBuildPreflight.cs`.
Удалены: `Ar15ScaleCalibration.cs` (+ `.meta`) — размер задаёт рецепт (`FinalRootScale`), команда окна снята; `BuildShotgunReal`.

## Ключевые решения

**Константы (`HandsPackBuildDefaults`)**, только отличия от инициализаторов SDK/Unity; новые элементы списков —
`boxedValue = new UxrGrabPointInfo()/UxrShotDescriptor()`, спуск — `Activator.CreateInstance(UxrFirearmTrigger, true)` (internal-класс SDK):
Rigidbody kinematic + ContinuousDynamic (сериализованно); хват корня: сопротивление 0.03/0.05, `_autoCreateStartAnchor`,
`_alignToController` у каждой точки, `_inputButtons` 0; граббабл механизма: preview 1, `_useParenting` 0, `_dropSnapMode` 0;
якорь 0.15, `_allowSwap` 0; AnchorSound: `_onlyByHand` 1, `_takeOutOnlyByHand` 0, снятие — свой кусок или `Magazine_drop.mp3`;
снаряд: длина 0.03, маски 4105/9, fade 3; спуск: `TriggerNoAmmo.wav` 0.3, досыл (None → false);
затвор (AWSF): порог **0.855**, возврат **1.5**, пружина; помпа: порог 0.7, громкость звуков **0.3**.

**Поля рецепта** (`HandsPackWeaponRecipe`): `RootScale` (размер легаси-сборки), `StaticParts = null` (= все остальные детали
источника, как считала легаси), `ClaimAllSourceParts`, `StaticActionTravel`/`StaticTriggerDegrees` («нет в паке»),
`ManualCycle` (+ `IsManualCycle` = ManualCycle или Pump), `MechanismMotion`, `EmptyActionClip`, `MuzzleEffect`,
`ProjectileLength`/`ProjectileImpactForce`/`DecalLayers`, `PackSounds`. Удалены: `GripDonor*`, `FirearmDonor`,
`PreserveDonorGameplay`, `NetworkAssetId`/`MagazineAssetId`, `MagazineGripDonor`, `ShotCycle`.
- AR15: `RootScale 0.83` (= корпус снимка M16 → ровно легаси `TargetLength/габарит`), `FinalRootScale 0.70`, Trigger
  `Rifle_Detail_02_Mesh`, Action `Rifle_Detail_01_Mesh`, `StaticActionTravel (0,0,-0.06)`, `StaticTriggerDegrees 40`,
  `MechanismMotion`, `Muzzle_Rifle`, теги `M16_Rifle`/`M16_Mag`, 30.
- FABARM: `RootScale 1.1892964` (= текущий корень и снимок; прежняя `TargetLength 0.72` не сверена), Excluded
  `Shogun_Patron_mesh001`, `ManualCycle`, `MechanismMotion`, снаряд 0.34 / ×50 / декаль Default, без доноров.
- Browning (легаси): `RootScale 0.68`, теги `Gun`/`MagGun`, шаблон — свой `BrowningHiPower_Magazine`, `Muzzle_Pistol`,
  `EmptyActionClip Shot_end`, HoldOpen. Явных флагов вместо Preserve не понадобилось: assetId — канон от GUID
  (нормализатор + сверка `PrefabIdentity`), RecoilAccumulator/WeaponSpread ставит баланс, эффекты — поле `MuzzleEffect`.

**TargetLength AR15 без Unity вычислен**: легаси давала масштаб корня = масштаб корпуса снимка (YAML: M16 0.83, Gun_real 0.68,
Shotgun_real 1.1892965), поэтому вместо TODO — `RootScale` (то же значение масштаба, что `baselineLength/габарит`).

**Режим огня**: сборщик пишет `ManualReload` при `IsManualCycle`, иначе полуавтомат-заглушку; `WeaponBalanceApplier` пишет
Full/Semi по `WeaponInfo.FullAuto` всем спускам, кроме `ManualReload` (предупреждение, если у ручного FullAuto). Сборка
отказывает до записи, если механизм ручной, а `FullAuto` = true (`RequireCycleMatchesBalance`). ManualCycle: SniperRifle,
SRM12, FABARM (+ любая помпа).

**Скорость/дальность снаряда**: сборщик ставит дефолт SDK (30 м/с, 300 м; дробь — дальность 60), баланс переписывает, если
поля заданы. Предупреждение баланса оставлено (комментарий: отказ — когда миграция обязательна).

**Полный путь** (`BuildFull`): `PrefabIdentity.Capture` → перенос хватов (снимок) → звуки на своём экземпляре модели →
`ResolveStaticParts` + `ValidateSourceParts` → [motion: `Export` + сохранение] → `Build(configureHost: !motion)` →
[motion: `Install` → `ConfigureHost`] → `Normalize` + `Verify` GUID/root localFileID/assetId → `Restore` хватов → подсветка
магазина. Откат: `RebuildOutputs(r)` = Interaction, `Art/Weapons/HandsPack/<имя>` (motion), `Assets/Audio/SFX/Weapons/HandsPack/<имя>`.

## Звуки: ствол → источник

Сопоставление моделей и наборов — по именам: звуки пака не используются ни одним префабом/контроллером пака (rg по GUID).

| Ствол (модель) | Набор | Выстрел | Затвор/помпа | Снятие магазина | Прочее |
|---|---|---|---|---|---|
| AR15 (Rifle03) | 1Assault_Rifle_Set | `Rifle_Shot_1` | куски `Reload.mp3` по ходу `Rifle_Detail_01` в `Reload` | кусок `Reload.mp3` по ходу магазина | — |
| Scar (Rifle01) | 1Assault_Rifle_Set | `Rifle_Shot_2` | `Reload.mp3` в `Recharge` | `Reload.mp3` | — |
| MP5K (Rifle04) | 1Assault_Rifle_Set | `Rifle_Shot_3` | `Reload.mp3` в `Reload` | `Reload.mp3` | — |
| Uzi (Tommy_gun) | 12Tommy_Gun | `Tommy_GunI_1` | `Tommy_Gun_Reload` в `Reloading` | то же | — |
| PPK (Gun02) | 8Pistol | `Shot_1` | `Zatvor.mp3` по паузе | `Reload_Full` в `Reload` | — |
| Browning (Gun) | 8Pistol | `Shot 4` (= прежний 9Run_Set) | `Zatvor.mp3` по паузе | `Reload_Full` в `Recharge` | — |
| SniperRifle | 11Sniper_Rifle_Set | `Sniper_Rifle_Shot_1` | `Reload.mp3` в `Recharge` | то же | — |
| Revolver (Gun_03) | 8Pistol | **`Revolver_Shot.wav` (R08)** — прежнее решение пользователя | нет затвора | **`Magazine_drop`** (звука барабана нет) | — |
| FabarmSDASS (Shotgun) | 10Shotgun_Set | `edit/shot` | `SplitAtGap(Zatvor.mp3)`, громкость 0.3 | гнездо/окно — `Magazine_drop` | заряжание `Reload_1` (кусок) |

Дефолты вместо пака: вставка магазина у всех — общий `MagazineInsert_Default` (`AmmoInsertSoundTests` запрещает свой);
«нет патронов» — `TriggerNoAmmo` 0.3; затвор → дефолт категории хоста, если `ActionPart` в клипе перезарядки неподвижен
(Rifle — AK105, Pistol — Viper, Sniper — Zatvor2/1); снятие → `Magazine_drop`, если магазин в клипе неподвижен. Отчёт
`[HandsPackAudio]` в логе называет каждый такой случай. Кусок `MagIn` для съёмных магазинов не режется (не звучал бы).

## Подтвердить на worker

1. Клипы и звуки: `ResolveActionTravel/TriggerRotation` AR15 (ожидается 0 → рецепт), имена клипов перезарядки
   (`Recharge`/`Reload`/`Reloading` — по суффиксу файлов), `Excursion` магазина и затвора у каждого ствола, лог `[HandsPackAudio]`.
2. Размер: AR15 `RootScale 0.83` = легаси (`Tip−ShotSource` = 0.01/0.83 в текущем AR15.prefab); FABARM 1.1892964, Browning 0.68.
3. `ValidateSourceParts` с `ClaimAllSourceParts` у AR15/FABARM/Browning; `boxedValue` нового `UxrFirearmTrigger`
   (null `_shotAudio` → дефолт `UxrAudioSample`).
4. Порядок: `WeaponProjectileMigration.CaptureFromPrefabs` **до** первой пересборки (иначе снимет 30/300).
5. Слух в шлеме: выстрелы T-38, затвор/магазин из `Reload.mp3`, помпа FABARM (громкость 0.3).

## Ссылки на снимки после правки

rg по пути и по GUID шести снимков (`febf0014…`, `7e771b2b…`, `6634aa63…`, `1f489b1c…`, `073ed2af…`, `0ad6e0da…`) в
`Assets`/`Tools`/`ProjectSettings`: в коде и ассетах ссылок нет; путь остался только в комментариях
(`RetiredGripAvatarCleanup.cs:18` — чужой этап, `WeaponNamingMigration.cs:74`). Документы: `Docs/README.md:476,484`,
`Docs/Arsenal/editor-workbench-plan.md:169`, `.claude/skills/add-weapon/SKILL.md:97-139` (доноры), `experts/weapon-system/expert.json:45`
(`Ar15ScaleCalibration.cs` в owns), исторические `Docs/tasks/*`.

## Тесты (не менялись; ожидания, которые устареют)

`WeaponSystemPilotPrefabTests` (FABARM: `ShotgunPump01/02` → `FabarmSDASS_BoltBack/BoltForward`), `WeaponShotSoundTests`
(известный красный должен позеленеть — свой клип у каждого), `WeaponFeedbackTests` (звуки затвора/снятия), `HandsPackWeaponTests`
(AR15 ход/спуск/масштаб), `WeaponBalanceTests` (если сверяет `_cycleType`), `KinemationWeaponTests` (скорость снаряда до миграции),
`PumpGrabFollowTests`, `PumpAimTests`, `ManualLoadingPrefabTests`, `CorpseTests` (×50 сохранён), `ShotgunPelletsTests`, `MuzzleEffectTests`,
`WeaponDropPhysicsTests`, `AnchorActivationAudioTests`, `AmmoInsertSoundTests`, `WeaponScaleTests` (комментарий про `Ar15ScaleCalibration`),
`NetworkAssetIdOnDiskTests`, `UxrUniqueIdOnDiskTests`, `WeaponEjectionTests`, `GrabPoseCoverageTests`. Компиляции не мешает ни один:
`PackMagazineStructureTests` (T38 + явные пути) и `WeaponSightPrefabTests` (`RebuildAll` с `WeaponGripPoseMode`) сохранены.

## Риски

- Звуки пака Hands могут не совпадать по времени с клипами (KINEMATION синхронен, Hands — не проверено) — решать на слух.
- До миграции снаряда пересобранный ствол получает 30 м/с; миграция после пересборки записала бы дефолт в баланс.
- Новые значения против прежних донорских: `WeaponSpread._recoveryTime` FABARM 0.4605 → 0.35 (без неточности не влияет),
  `_prefabInstantiateOnTipParent` дробинок FABARM 0 → 1 (вспышки у них нет), AWSF `_logBoltCycle` 1 → 0 (хост AWSF снимает).
- Выстрелы и снятие магазина T-38 меняются (MachineGun2/Shot 4/Magazine_drop → куски наборов) — это и есть решение, но
  меняет звук принятых стволов.
- В worktree параллельно работает другой этап (появился `RetiredGripAvatarCleanup.cs.meta`); `PackOrigPoses.cs` и
  `HandsPackWeaponBuilder.cs` правились обоими.

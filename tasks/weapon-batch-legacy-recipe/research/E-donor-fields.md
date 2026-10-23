# Исследование E: поля доноров сборщика Hands → общие значения по категориям (2026-10-23, read-only)

Источник данных: YAML 20 префабов (9 Hands, 3 снимка `LegacyBuildDonors`, 8 KINEMATION) — скрипты
`reports/donor_fields.py` (поля по компонентам → `reports/fields.json`) и `reports/compare.py` (сводка по ролям →
`reports/cmp.txt`). Код — рабочее дерево ветки `task/weapon-batch-legacy-recipe` (с незакоммиченной правкой снаряда в балансе).

## Главное

1. **После переноса скорости/дальности в баланс доноры «винтовка» (AR15/снимок M16) и «пистолет» (BrowningHiPower/снимок
   Gun_real) не различаются ни в одном оставшемся поле**, кроме режима огня (`_cycleType`) и звука выстрела. Rigidbody, хват
   корня, граббабл затвора, якорь, AudioSource и AnchorSound якоря, AWSF — **одинаковы у всех 20 префабов** (включая KINEMATION).
   Категории по «винтовка/пистолет» не нужны: нужен **один общий набор + различие по виду механики** (Slide / Pump / None) и
   несколько полей дробовика.
2. Режим огня выводится из баланса без исключений: `WeaponInfo._fullAuto` → FullyAutomatic/SemiAutomatic совпадает с текущим
   донорским значением у всех 17 стволов; ManualReload — только у SniperRifle/SRM12 (рецепт `ShotCycle`) и FabarmSDASS
   (**сейчас от снимка Shotgun_real, в рецепте нет** — без донора нужен `ShotCycle = ManualReload` или правило «Pump → ManualReload»).
3. Дробовик отличается от общего набора только полями сэмпла SDK у FABARM: `_projectileLength` 0.34, `_projectileImpactForceMultiplier`
   50, `_createDecalLayerMask` = Default; Herrington (тоже дробовик, донор-винтовка) — 0.03 / 1 / Default|Ground. Это решение, не
   данные: множитель 50 задаёт толчок трупа (`CorpseTests` «дробина 300×50 упирается в предел»).
4. **KINEMATION тоже читает доноров**: `KinemationWeaponBuilder.Rifle/Pistol` (`:50`, `:53`) = `RifleDonor/PistolDonor` → живые
   AR15/BrowningHiPower. Значения у них те же, поэтому общий набор KINEMATION не меняет, но код меняется в обоих сборщиках.
5. Снимки `M16_Rifle_prefab` и `Gun_real` удалить можно (условия — §3). Обязательная правка: `HandsPackLegacyRebuild.CaptureDonors`
   (`:184-207`) **пересоздаёт все снимки из живых префабов**, если их нет — без её удаления снимки вернутся при первом `RebuildAll`.

## 1. Где берутся значения у донора

### HandsPackWeaponBuilder.cs

| Место | Компонент / что | Кто затем перезаписывает (исключено из §2) | Остаётся от донора |
|---|---|---|---|
| `:509-510` | загрузка `GripDonor`, `FirearmDonor` | — | — |
| `:513` `CopyFields` | `Rigidbody` корня | — | все поля (§2.1) |
| `:518-524` (Preserve) | `NetworkIdentity` | `_assetId` = рецепт (`:522`) | `sceneId/serverOnly/visibility` = дефолты Mirror — донор не нужен |
| `:582` `CopyFields` | `UxrGrabbableObjectAnchor` (MagAnchor) | `_compatibleTags` (`:583-584`); `_alignTransform`, `_dropProximityTransform`, все `_activateOn*`, `_grabProxy` = null, оба `UseSelf` (`:585-588`); у внутреннего — `WeaponInternalAmmoAlignment.ConfigureAnchor` (`:614`); `ActivateOnCompatibleNear` — `WeaponInteractionInstaller` | `_maxPlaceDistance`, `_allowSwap` |
| `:593` `EditorUtility.CopySerialized` | `AudioSource` якоря | `playOnAwake` = false (`:594`), `clip` = null (`:597`) | все прочие настройки AudioSource (= дефолт Unity) |
| `:599` `CopyFields` | `AnchorSound` | `_source` (`:600`); `_takeOutClip`, если `TakeOutAudio` (`:601-602`) | `_takeOutClip` (Hands), `_onlyByHand`, `_takeOutOnlyByHand` |
| `:618` `CopyFields` | `UxrGrabbableObject` корня | `_rigidBodySource`, `_tag`, `_additionalGrabPoints.size`, `_startAnchor` (`:619-624`), `…ProximityTransformUseSelf` (`UseGripAsProximity :854-860`); CopyFields сам пропускает `_avatarGripPoseEntries`, `_grabProximityBox`, `_enableOnHandNear`, `_alignToControllerAxes`, `_grabProximityTransform` (`:1110-1116`); `EnableOnHandNear` — `WeaponInteractionInstaller.Apply`; записи и `_selectedAvatarForGrips` — `WeaponGripCarryOver.Restore` | все поля точек хвата и физики хвата (§2.2) |
| `:629` `CopyFields` | `UxrProjectileSource` | `_weaponAnimator` (`:630`); не-Preserve: размер `_shotTypes`, копия [0]→[1], трассер, попадание, декаль, вспышка, `_prefabInstantiateOnTipParent/Life`, `_prefabInstantiateOnImpactLife` (`:635-659`), урон/дальность дроби (`:664-667`); `_shotSource`/`_tip` (`:644-645`); **баланс**: `_projectileSpeed`, `_projectileMaxDistance` (если заданы в `WeaponInfo`), `_projectileDamageNear/Far`, `_penetrationPower` (`WeaponBalanceApplier :111-122`) | `_useAutomaticProjectileTrajectory`, `_shotAnimationVarName`, `_projectileLength`, `_projectileImpactForceMultiplier`, `_collisionLayerMask`, `_prefabScenarioImpactDecalLife`, `_decalFadeoutDuration`, `_createDecalLayerMask`; у Preserve (AR15, Browning, Fabarm) — **все эффекты** (вспышка `Muzzle_Rifle`/`Muzzle_Pistol`, трассер, попадание) |
| `:674` (Preserve) `CopyFields` | `ShotgunPellets` | `_pellets`, `_spreadDegrees` — баланс (`:88-96`) | `_triggerIndex` 0, `_pelletShotIndex` 1 (= дефолты скрипта) |
| `:678` `CopyFields` | `UxrFirearmWeapon` | `_recoilAxes` (`:680`); trigger[0]: `_triggerGrabbable`, `_grabbableGrabPointIndex`, `_triggerTransform`, `_ammunitionMagAnchor`, ось/угол спуска из клипа (`:682-692`), `_useHasReloadedForSemiAndFullAuto`=false у None (`:694-695`), `_cycleType` при `ShotCycle` (`:696-697`), `_shotAudio` при `ShotAudio` (`:698-699`); **баланс**: `_maxShotFrequency`, `_recoilAngle*`, `_recoilOffset*`, `_recoilDurationSeconds` (`:74-85`) | `_triggers.size`, `_projectileShotIndex`, `_cycleType`, `_useHasReloadedForSemiAndFullAuto`, `_shotAudio`, `_shotAudioNoAmmo`, `_shotHapticClip` |
| `:479-494` | спуск (Preserve и угол клипа 0): `_triggerRotationAxis/Degrees` донора | — | у AR15 40° (= дефолт SDK `UxrFirearmTrigger._triggerRotationDegrees = 40`) |
| `:706-718` (Preserve) | `RecoilAccumulator`, `WeaponSpread` | баланс: весь `_pattern` RecoilAccumulator и `_axes`; у WeaponSpread `_spread` и три `_inaccuracy*` (`ApplyRecoil/ApplySpread`); компоненты баланс ставит сам (`EnsureComponent :63-64`) | `WeaponSpread._pattern._recoveryTime` (Fabarm 0.4605; при нулевых неточностях эффекта нет) |
| `:437-477` | ход затвора (Preserve и ход клипа 0): `TranslationLimits` граббабла действия донора | — | у AR15 −0.06 по z (снимок M16) |
| `:792` `CopyFields(FindPart(gripDonor))` | граббабл затвора/помпы | `_translationConstraintMode`, `_translationLimitsMin/Max`, `_rotationConstraintMode` (`:793-796`), proximity UseSelf (`:797`), `_controlParentDirection`, `_ignoreGrabbableParentDependency` (`:802-803`); `EnableOnHandNear` и align — `WeaponInteractionInstaller` | остальные поля (§2.3) |
| `:809` `CopyFields` | `UxrShotgunPump` (донор — `FirearmDonor`) | `_pump`, `_localPumpDirection/Offset` (`:810-812`); компонент выключен; `WeaponSystemAuthoring.Configure` читает `_slideThreshold` и 4 звука (`ReadPumpNumbers :614-619`, `WriteAudio :749-756`) и **удаляет** компонент | `_slideThreshold`, `_audioSlide*` (вибрации помпы хост не переносит) |
| `:826-835` | `AutomaticWeaponSlideFeedback`: у донора, иначе у его снимка (`HandsPackLegacyRebuild.DonorSnapshot`, `:832`) | `_slide` (`:836`), звуки при `SlideBack/ForwardAudio` (`:838-843`); хост читает `_slideThreshold`, `_autoReturnSpeed`, `_autoReturnOnRelease`, 4 звука (`ReadFeedback :604-612`, `WriteAudio :757-767`) и **удаляет** AWSF | `_slideThreshold`, `_autoReturnSpeed`, `_autoReturnOnRelease`, звуки (у Hands) |
| `:889`, `:892-900`, `:976` (Preserve) | шаблон магазина = `MagazineBase` (снимок) | — | весь магазин-шаблон (сеть, физика, звук) |

Прочие читатели донора: `ManualLoadingAuthoring.cs:144-154` копирует `AnchorSound` гнезда (донорский) на окно приёма патрона
(у Fabarm `_takeOutClip` окна = `Magazine_drop.mp3`); `ArsenalBuildPreflight.cs:42-43` (входы) и `:181-207` (`ValidateDonors`);
`KinemationWeaponBuilder.cs:50,53`.

### HandsPackLegacyRebuild.cs

| Место | Что берётся у снимка |
|---|---|
| `:40`, `:65`, `:184-207` `CaptureDonors` | создаёт 6 снимков из живых префабов, если нет ни одного |
| `:165-166` `DonorSnapshot` | путь снимка для запасного AWSF сборщика (`HandsPackWeaponBuilder :832`) |
| `:225-234` | AR15: `TriggerPart` и `ActionPart` — имена мешей из снимка M16 |
| `:236` | Fabarm: `ShotAudio = null` → выстрел снимка `Shotgun_real` |
| `:252` | `UxrTag` = тег корня снимка |
| `:254` | `MagazineBase` = снимок магазина (шаблон магазина при Preserve) |
| `:255` | `MagazineGripDonor` (сборка не читает) |
| `:256`, `:259` | `MagazineTag`, `MagazineCapacity` из снимка магазина |
| `:260-261` | `GripDonor` = снимок (AR15 и Fabarm → `M16_Rifle_prefab`, Browning → `Gun_real`), `FirearmDonor` = снимок ствола |
| `:262` | `PreserveDonorGameplay = true` (включает все Preserve-ветки выше) |
| `:264-278` | `TargetLength` = габарит корпуса × `lossyScale` корпуса снимка |

## 2. Значения оставшихся полей

Обозначения: **ВСЕ** — одинаково у 20 префабов (9 Hands, 3 снимка, 8 KINEMATION); **SDK** — дефолт инициализатора поля
(SDK/скрипт проекта/Unity). «≠SDK» — значение, которое обязано попасть в общий набор явно.

### 2.1 Rigidbody корня — ВСЕ одинаково

| Поле | Значение | SDK/Unity |
|---|---|---|
| `m_Mass` 1, `m_LinearDamping` 0, `m_AngularDamping` 0.05, `m_UseGravity` 1, `m_Interpolate` 0, `m_Constraints` 0, центр/тензор неявные | как | = Unity |
| `m_IsKinematic` | 1 | ≠ (false) |
| `m_CollisionDetection` | 2 (ContinuousDynamic, `WeaponDropPhysicsTests`) | ≠ (Discrete) |

### 2.2 UxrGrabbableObject корня — ВСЕ одинаково (основная и дополнительная точка)

| Поле | Значение | SDK |
|---|---|---|
| `_controlParentDirection` 1, `_ignoreGrabbableParentDependency` 0, `_priority` 0, `_allowMultiGrab` 1, `_translationConstraintMode` 0, `_rotationConstraintMode` 0, `_autoRotationProvider` 1, `_rotationLongitudinalAxis` Z, `_needsTwoHandsToRotate` 0, `_lockedGrabReleaseDistance` 0.4, `_rigidBodyDynamicOnRelease` 1, `_vertical/_horizontalReleaseMultiplier` 1, `_previewGrabPosesMode` 3, `_firstGrabPointIsMain` 1, `_useParenting` 1, `_autoAnchorMaxPlaceDistance` 0.1, `_dropAlignTransformUseSelf` 1, `_dropSnapMode` 3, `_dropProximityTransformUseSelf` 1 | как | = SDK |
| `_translationResistance` | 0.03 | ≠ (0) |
| `_rotationResistance` | 0.05 | ≠ (0) |
| `_autoCreateStartAnchor` | 1 | ≠ (false) |
| точка: `_grabMode` 0, `_useDefaultGrabButtons` 1, `_bothHandsCompatible` 1, `_handSide` 0, `_snapMode` 3, `_snapDirection` 0, `_snapReference` 1, `_grabProximityMode` 0, `_maxDistanceGrab` 0.2, `_grabberProximityUseDefault` 1, `_grabberProximityIndex` 0, `_hideHandGrabberRenderer` 0, `_defaultGripPoseInfo` пусто/−1 | как | = SDK |
| точка: `_alignToController` | 1 | ≠ (false) |
| точка: `_inputButtons` | 0 | ≠ (Grip), но при `_useDefaultGrabButtons`=1 не используется |

Дополнительная точка (`_additionalGrabPoints[0]`) — те же значения, что основная (ВСЕ одинаково, где она есть).

### 2.3 Граббабл затвора/помпы — ВСЕ одинаково (M16 `Recoil`, Gun_real `GateContainer`, Shotgun_real `Pump`, все Slide/Pump)

| Поле | Значение | SDK |
|---|---|---|
| `_priority` 0, `_allowMultiGrab` 1, `_autoRotationProvider` 1, ось Z, `_lockedGrabReleaseDistance` 0.4, `_translation/_rotationResistance` 0, `_rigidBodySource` null, `_rigidBodyDynamicOnRelease` 1, множители 1, `_firstGrabPointIsMain` 1, `_autoCreateStartAnchor` 0, `_autoAnchorMaxPlaceDistance` 0.1, `_tag` пусто, `_additionalGrabPoints` 0, точка: snap 3/0/1, proximity 0, 0.2, `_alignToController` 0 | как | = SDK |
| `_previewGrabPosesMode` | 1 (только левая) | ≠ (3) |
| `_useParenting` | 0 | ≠ (true) |
| `_dropSnapMode` | 0 (DontSnap) | ≠ (3) |

### 2.4 Якорь магазина

| Компонент.поле | Значение | SDK |
|---|---|---|
| Anchor `_maxPlaceDistance` | 0.15 ВСЕ (окно приёма патрона `CartridgeIntake` — 0.1, его пишет `ManualLoadingAuthoring`) | ≠ (0.1) |
| Anchor `_allowSwap` | 0 ВСЕ | = SDK |
| AudioSource (все поля, кроме клипа) | ВСЕ одинаково: громкость 1, высота 1, приоритет 128, 2D (pan level 0), log rolloff 1–500 м, `playOnAwake` 0 | = Unity, кроме `playOnAwake` (его и так ставит сборщик) |
| AnchorSound `_onlyByHand` | 1 ВСЕ | = скрипт |
| AnchorSound `_takeOutOnlyByHand` | 0 ВСЕ (выброс кнопкой без руки) | ≠ (true) |
| AnchorSound `_takeOutClip` | Hands: `Assets/Audio/SFX/Universal/Magazine_drop.mp3` у всех 9 и снимков; KINEMATION: свой `*_MagOut.wav` | ≠ (пусто — без звука) |

### 2.5 UxrProjectileSource, выстрел [0] (и [1] дроби)

| Поле | AR15 Scar Uzi MP5K SniperRifle | Browning PPK Revolver | FabarmSDASS | Herrington (K) | прочие K | SDK |
|---|---|---|---|---|---|---|
| `_useAutomaticProjectileTrajectory` | 1 | 1 | 1 | 1 | 1 | = |
| `_projectileLength` | 0.03 | 0.03 | **0.34** | 0.03 | 0.03 | ≠ (0.01) |
| `_projectileImpactForceMultiplier` | 1 | 1 | **50** | 1 | 1 | = (1) |
| `_collisionLayerMask` | 4105 = Default+Ground+Hitbox (ВСЕ) | | | | | ≠ (Everything) |
| `_createDecalLayerMask` | 9 = Default+Ground | 9 | **1 = Default** | 9 | 9 | ≠ (Everything) |
| `_prefabScenarioImpactDecalLife` | 10 (ВСЕ) | | | | | = |
| `_decalFadeoutDuration` | 3 (ВСЕ) | | | | | ≠ (7) |
| `_prefabInstantiateOnTipWhenShot` | AR15 `Muzzle_Rifle` (Preserve), прочие `Muzzle_Default` | Browning `Muzzle_Pistol` (Preserve), прочие Default | Default | Default | Default | — |
| [1] `_prefabInstantiateOnTipParent` | — | — | 0 (Preserve) | 1 | — | сборщик ставит 1 |
| `_projectileSpeed` (→ баланс) | 400 | 300 | 300 | 400 | винтовки 400, Viper/R08 300 | 30 |
| `_projectileMaxDistance` (→ баланс) | 120 | 120 | 60 | 60 | 120 | 300 |

Трассер, попадание, декаль, `…Life` и `…Parent` сборщик ставит сам (не-Preserve) — вне общего набора. Скорость/дальность
уходят в баланс, но **префаб без донора получит дефолт SDK 30 м/с / 300 м**, если в `WeaponInfo` они не заданы: до удаления
доноров у всех стволов каталога должна пройти `WeaponProjectileMigration.CaptureFromPrefabs`, а предупреждение
`WeaponBalanceApplier :104-107` стоит сделать отказом.

### 2.6 UxrFirearmWeapon, спуск [0]

| Поле | AR15 Scar Uzi MP5K | SniperRifle | Browning PPK | Revolver | Fabarm | K | SDK |
|---|---|---|---|---|---|---|---|
| `_triggers.size` / `_projectileShotIndex` | 1 / 0 ВСЕ | | | | | | список пуст |
| `_cycleType` | 2 FullAuto | 0 Manual (рецепт) | 1 Semi | 1 Semi | **0 Manual (снимок Shotgun_real, не рецепт)** | AK105 Mk14 MKR9 TR15 Herrington 2, SRM12 0 (рецепт), Viper R08 1 | 0 |
| `WeaponInfo._fullAuto` (для сверки) | 1 | 0 | 0 | 0 | 0 | совпадает со всеми | — |
| `_useHasReloadedForSemiAndFullAuto` | 1 | 1 | 1 | 0 (None) | 0 (снимок; при Manual не используется) | 1, R08 0 (None) | ≠ (false) |
| `_shotAudio` | `Effects/Automatic_rifle/VFX/Sounds/MachineGun2.wav` | то же | `Sounds/9Run_Set/Shot 4.mp3` | `Audio/SFX/Weapons/Revolver/Revolver_Shot.wav` (рецепт) | `10Shotgun_Set/edit/shot.mp3` | свои `*_Shot.wav` | — |
| `_shotAudio._volume` | 1 ВСЕ | | | | | | |
| `_shotAudioNoAmmo` | `UltimateXR/Samples/FullScene/Audio/TriggerNoAmmo.wav`, 0.3 ВСЕ | | | | | | ≠ (пусто) |
| `_shotHapticClip` | Shot, 1, Mix ВСЕ | | | | | | = |
| `_triggerRotationAxis` | X ВСЕ | | | | | | = |
| `_triggerRotationDegrees` (только запасной путь) | AR15 40 (снимок M16), прочие из клипа | — | Browning 18 из клипа (снимок 40 не используется) | 29 | 17 | из клипа | 40 |

### 2.7 Механика затвора/помпы (вход хоста)

| Поле | M16 и Gun_real (снимки AWSF) | Shotgun_real (UxrShotgunPump) | Хост сейчас | Дефолт скрипта |
|---|---|---|---|---|
| порог (`_slideThreshold` → `_rig._extractionGate`) | 0.855 | 0.7 | 0.855 у всех Slide, вкл. KINEMATION; Fabarm 0.7 | 0.7 (AWSF и SDK); у `WeaponSystemAuthoring.Source` тоже 0.7 |
| `_autoReturnSpeed` → `_springReturnSpeed` | 1.5 | — | 1.5 ВСЕ | 1.5 |
| `_autoReturnOnRelease` → `_releasedAction` | 1 (Spring) | — (Stay) | Slide 0=Spring, Fabarm 1=Stay | true |
| `_chamberRoundOnSlideReturn`, `_logBoltCycle`, вибрации | 1, 1, … | вибрации помпы | хост не читает — пропадают с AWSF | — |
| звуки | см. §4 | см. §4 | | |

### 2.8 Прочее

- `ShotgunPellets`: `_triggerIndex` 0, `_pelletShotIndex` 1 — дефолты скрипта; остальное баланс.
- `RecoilAccumulator` — целиком баланс + `_axes` сборщика. `WeaponSpread` — баланс, кроме `_recoveryTime` (Fabarm 0.4605 от снимка,
  Herrington 0.35 = дефолт `SpreadPattern`); при нулевых неточностях роли не играет.
- `NetworkIdentity` — дефолты Mirror, `_assetId` из рецепта; донор не нужен.

### Итог по категориям

| Набор | Поля ≠ дефолту | Где различаются |
|---|---|---|
| Общий (все стволы) | Rigidbody kinematic + ContinuousDynamic; хват корня: resistance 0.03/0.05, `_autoCreateStartAnchor`, `_alignToController` у точек; якорь 0.15; AnchorSound `_takeOutOnlyByHand`=false; снаряд: длина 0.03, маски 4105/9, fade 3; спуск: NoAmmo 0.3, `_useHasReloaded`=true (None → false) | нигде |
| Slide (затвор) | граббабл действия: preview 1, `_useParenting` 0, `_dropSnapMode` 0; AWSF порог 0.855, возврат 1.5, Spring | нигде (одинаково у винтовок, пистолетов и KINEMATION) |
| Pump | тот же граббабл действия (+`_controlParentDirection` — уже правило сборщика); порог 0.7 (= SDK); Stay | — |
| Дробовик (дробь) | только если сохранять FABARM: длина 0.34, сила 50, декаль Default | FABARM ≠ Herrington — **решение** |
| Режим огня | — | `WeaponInfo.FullAuto` / рецепт ManualReload |
| Звук выстрела, снятия магазина, затвора | — | по стволу (§4) |

## 3. Форма хранения и удаление снимков

### Константы в коде vs ScriptableObject

| | Константы в сборщике (`HandsPackBuildDefaults`, Editor) | ScriptableObject `Assets/Data/WeaponSystem/HandsPackBuildDefaults.asset` |
|---|---|---|
| Один владелец | значение и его писатель — в одном классе сборщика | два места правды: ассет и сборщик-копировщик; правка ассета без пересборки не доходит до префабов, префабы молча расходятся с ассетом |
| Рантайм | значения нужны только при сборке (копируются в префабы) | ассет не читается в игре — лишний сериализованный ассет без потребителя |
| Ревью | правка видна в diff `.cs`, маршрут ревью weapon-system | YAML ассета; «один выполняющийся этап на сериализованный ассет» |
| Объём | ~25 значений (§2 «Итог») | то же |
| Пример в проекте | `WeaponSystemAuthoring.DefaultsSpecs`, `EjectionPorts` — константы writer'а | `WeaponFeedbackDefaults` — ассет, потому что его **читает рантайм** (подставляет дефолт при проигрывании) |

**Рекомендация — константы в коде**: отдельный статический класс (например `HandsPackBuildDefaults` рядом со сборщиком) с
наборами «Общий», «Slide», «Pump» и методами `Apply(Rigidbody)`, `ApplyRoot(SerializedObject)`, `ApplyAction(...)`, `ApplyAnchor(...)`,
`NewShot()`, `NewTrigger()`; используют оба сборщика (Hands и KINEMATION). Ассет оправдан только если значения станут читаться в игре.

Граница с другими писателями (не дублировать):
- хост WeaponSystem пишет механизм, звуки механизма, вибрации; сборщик отдаёт ему только AWSF/UxrShotgunPump как входной формат
  (порог, возврат, звуки-оверрайды). Звуки затвора без оверрайда → `WeaponFeedbackDefaults` категории хоста (§4).
  Порог 0.855 можно перенести и в хост (например в `WeaponReadinessProfile` или `Source.Gate`), но это правка зоны хоста —
  при сборщике он остаётся константой Slide.
- баланс пишет урон, темп, отдачу, разлёт, скорость/дальность; режим огня логично отдать туда же (из `FullAuto`) с исключением
  ManualReload из рецепта/механики — либо правило в сборщике `ShotCycle ?? (Pump ? Manual : FullAuto ? Full : Semi)`, но
  тогда сборщику нужен `WeaponInfo`.
- взаимодействие (`WeaponInteractionInstaller`) — подсветка; carry-over — записи хвата.

Техническая грабля: у нового компонента `UxrFirearmWeapon._triggers`, `UxrProjectileSource._shotTypes`,
`UxrGrabbableObject._additionalGrabPoints` — пустые списки без инициализатора. Сейчас элементы приходят копией донора. Без донора
наращивание `arraySize` у пустого списка, по всей видимости, даёт нулевые значения, а не инициализаторы полей SDK (маска 0,
скорость 0, `_maxDistanceGrab` 0) — проверить. Надёжно: `element.boxedValue = new UxrFirearmTrigger()` / `new UxrShotDescriptor()` /
`new UxrGrabPointInfo()` (неявные конструкторы дают дефолты SDK), затем общий набор.

### Можно ли удалить `M16_Rifle_prefab` и `Gun_real*`

Да, при условиях:

| Читатель | Замена |
|---|---|
| AWSF запасной путь `HandsPackWeaponBuilder :826-834` (T-38 Scar/Uzi/MP5K/SniperRifle/PPK и KINEMATION с затвором) | константы Slide |
| `RifleDonor`/`PistolDonor` и `KinemationWeaponBuilder :50,53` (живые AR15/Browning — после пересборки AR15 по полному рецепту они сами станут выходом этого сборщика) | общий набор; поля `GripDonor*`/`FirearmDonor` убрать |
| `ResolveActionTravel/TriggerRotation` запасной путь (`:451-494`) | для AR15 — клипы (исследование B) или явные поля рецепта; Browning их не использует (ход 0.0463 и спуск 18° — из клипа, у снимка 0.0189 и 40°) |
| `HandsPackLegacyRebuild.Recipes :225-234` (имена деталей AR15) | уходит вместе с AR15 из легаси |
| `Configure :250-281` для Browning | `UxrTag` "Gun", `MagazineTag` "MagGun" — константы рецепта; ёмкость — баланс (`MagazineSize` 13); `MagazineBase` → собственный `BrowningHiPower_Magazine` (как `MagazineTemplate` без Preserve, `:974-979`); `TargetLength` — константа (значение preflight `baselineLength`) или габарит корпуса текущего префаба; `NetworkAssetId` уже читается с живого префаба |
| `PreserveDonorGameplay` для Browning (эффекты выстрела донора, шаблон магазина) | развести на явные флаги: «сохранить assetId», «шаблон — свой магазин»; эффекты — `Muzzle_Default` (сейчас `Muzzle_Pistol` — изменение вида, решение) или поле рецепта |
| `CaptureDonors`, `DonorSnapshot`, `Donor` (`:165-207`) | удалить; иначе снимки пересоздадутся |
| `WeaponNamingMigration.cs:75` и команда окна арсенала (`ArsenalEditorWindow.cs:301`) | историческая миграция требует все 6 снимков — убрать вместе с тремя уже решёнными |
| `ArsenalBuildPreflight.cs:42-43`, `:181-207` | проверять общий набор, а не доноров |

Что Browning ещё нужно от легаси без снимков: только пак (клипы `Shot`, `Shot_end`, motion), свой магазин и константы рецепта.

## 4. Звуки

| Ствол | Выстрел | Нет патронов | Затвор назад / вперёд (оверрайд хоста) | Вперёд с досылом | Вставка магазина | Снятие магазина |
|---|---|---|---|---|---|---|
| AR15 | `MachineGun2.wav` (сам себе) | TriggerNoAmmo 0.3 | `Universal/Zatvor2` / `Zatvor1` (снимок M16) | дефолт Rifle: `AK105_BoltForward` | общий `MagazineInsert_Default.wav` | `Universal/Magazine_drop.mp3` |
| Scar, MP5K | `MachineGun2.wav` (живой AR15) | то же | Zatvor2 / Zatvor1 (снимок M16) | дефолт Rifle | общий | Magazine_drop |
| Uzi | `MachineGun2.wav` (живой AR15) | то же | Zatvor2 / Zatvor1 | дефолт **Pistol** (`Viper_BoltForward`; категория хоста — Pistol) | общий | Magazine_drop |
| SniperRifle | `MachineGun2.wav` (живой AR15) | то же | Zatvor2 / Zatvor1 | дефолт Sniper (`Zatvor1`) | общий | Magazine_drop |
| BrowningHiPower | `9Run_Set/Shot 4.mp3` (сам себе) | то же | Zatvor2 / Zatvor1 (снимок Gun_real) | дефолт Pistol (`Viper_BoltForward`) | общий | Magazine_drop |
| PPK | `Shot 4.mp3` (живой Browning) | то же | Zatvor2 / Zatvor1 (снимок Gun_real) | дефолт Pistol | общий | Magazine_drop |
| Revolver | `Revolver_Shot.wav` (рецепт, из R08) | то же | — | — | общий | Magazine_drop |
| FabarmSDASS | `10Shotgun_Set/edit/shot.mp3` (снимок; рецепт тоже `edit/shot.mp3`) | то же | SDK `ShotgunPump01` / `ShotgunPump02`, громкость **0.3** (снимок) | дефолт Shotgun: `ShotgunPump02`, громкость 1 | окно приёма: `10Shotgun_Set/Reload_1.mp3` (рецепт) | окно приёма: Magazine_drop (копия AnchorSound гнезда) |
| KINEMATION ×8 | свой (`MakeSounds`) | то же | свои BoltBack/BoltForward (оба «вперёд») | свой | общий (свой MagIn — только окну Herrington) | свой MagOut |

Наблюдения:
- У Hands-затворов сейчас смесь: «вперёд пустой» — Zatvor1 оверрайдом, «вперёд с досылом» — дефолт категории (AK105/Viper).
  Без донорских звуков оба станут дефолтом категории — согласованно; Sniper-дефолт и сейчас Zatvor2/1.
- Помпа FABARM без оверрайда получит те же клипы дефолта Shotgun, но в 3,3 раза громче (1 против 0.3).
- `Magazine_drop.mp3` на снятии — единственный донорский звук якоря. Общего дефолта снятия нет
  (`MagazineAnchorSoundDefaults` хранит только вставку; пустой `_takeOutClip` = без звука).
- Вставка уже общая у всех (`MagazineInsert_Default.wav` из `Resources/MagazineAnchorSoundDefaults.asset`), донор не участвует.
- Выстрел T-38 Scar/Uzi/MP5K/SniperRifle и PPK — **живой донор**: перевод AR15 на `Rifle_Shot` из пака молча сменил бы выстрел
  четырём стволам при их пересборке. Без доноров у каждого рецепта нужен явный `ShotAudio`.

Предложение по источникам (решение пользователя: резать из пака, как `KinemationWeaponBuilder.MakeSounds`):

| Звук | Из пака (нарезка `KinemationAudio`) | Дефолт категории хоста / общий |
|---|---|---|
| Выстрел | AR15 `1Assault_Rifle_Set/Rifle_Shot_*`; Fabarm `10Shotgun_Set/edit/shot` или `Shot_Full`; кандидаты T-38: Uzi `12Tommy_Gun/Tommy_Gun_*`, SniperRifle `11Sniper_Rifle_Set/Sniper_Rifle_Shot_*`, Scar/MP5K `1Assault_Rifle_Set`, PPK/Browning `8Pistol/Shot_*` (`9Run_Set` — побайтовая копия `8Pistol`) | нет — общего выстрела у реестра быть не должно (`WeaponShotSoundTests`) |
| Нет патронов | — | общий: `TriggerNoAmmo` 0.3 (константа общего набора; хост и так играет `_dryFire` дефолта) |
| Затвор назад/вперёд | AR15 — куски `1Assault_Rifle_Set/Reload.mp3`; пистолеты — `8Pistol/Zatvor.mp3`; помпа Fabarm — `10Shotgun_Set/Zatvor.mp3` | без нарезки: Rifle AK105, Pistol Viper, Sniper Zatvor2/1, Shotgun ShotgunPump01/02 |
| Вставка магазина | — (гнездо магазина — общий звук) | общий `MagazineInsert_Default.wav` |
| Снятие магазина | AR15 и прочие — кусок `Reload.mp3` своего набора (`Excursion` детали магазина в клипе `Reload`, как у KINEMATION) | иначе — константа `Magazine_drop.mp3` в общем наборе сборщика или новый общий дефолт снятия в `MagazineAnchorSoundDefaults` (рантайм, зона manipulation) |
| Заряжание патрона (окно Fabarm) | `10Shotgun_Set/Reload_1.mp3` (рецепт) | — |

## Вопросы

1. Форма: константы в коде (рекомендация) или ассет?
2. Ключ наборов — вид механики (Slide/Pump/None) и общий набор, без «винтовка/пистолет» (значения у них совпадают)?
3. Поля сэмпла SDK у FABARM (длина 0.34, сила 50 — толчок трупа, декаль только Default): сохранить как поля «дробь» или
   привести к общим (как у Herrington)?
4. Режим огня: в баланс из `WeaponInfo.FullAuto` (+ ManualReload из рецепта) или правило в сборщике? FABARM — `ShotCycle = ManualReload` в рецепт.
5. Порог затвора 0.855: константа Slide в сборщике или перенос в хост/профиль?
6. Звук снятия магазина без нарезки: константа `Magazine_drop` в сборщике или общий дефолт в `MagazineAnchorSoundDefaults`?
7. Выстрелы T-38 (сейчас MachineGun2 у четырёх и Shot 4 у PPK): закрепить текущие явным `ShotAudio` или резать из пака?
8. Browning без снимков: вспышка `Muzzle_Pistol` → `Muzzle_Default`, допустимо? `TargetLength` — константой?
9. Громкость помпы FABARM: 0.3 оверрайдом или дефолт Shotgun (1.0) — проверить на слух.

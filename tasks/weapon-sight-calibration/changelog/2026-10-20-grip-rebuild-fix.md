# 2026-10-20 — полная пересборка KINEMATION: NRE импорта поз хвата

Исполнитель — субагент avatar-grip. Ничего не закоммичено. Отчёты, дампы и резервные копии лежат в
`../reports/grip-fix/` (вне Git).

## Причина

`HandsPackPoseImporter.GripAvatars` хранил путь `Assets/Prefabs/Player/MEF_Base_Avatar.prefab`. В WIP-коммите
`bef2f0311` (2026-10-17) префаб перенесли в `Player/Unused/`, GUID `b6fe59db…` при этом сохранился.
`LoadAssetAtPath` стал возвращать null, и `AssignToGrabPoint` падал на `avatarPrefab.name` (строка 209).
Структура префаба оружия, разрезание прицела и удаление SDK-стволов к сбою отношения не имеют.

К моменту падения импорт уже успевал записать ассеты поз и базу поз, а сборщик — префаб.

Тот же устаревший путь был в `ArsenalBuildPreflight` (const `Mef`) и в двух областях команд `ArsenalEditorWindow`.
Поэтому кнопки пересборки в окне арсенала падали на preflight.

## Исправление

- `Assets/Editor/VR_Battlegrounds/Avatars/HandsPackPoseImporter.cs`:
  - аватары хвата задаются GUID (`MefBaseAvatarGuid`), путь разрешается через `GUIDToAssetPath`, по образцу
    `GripTargetProfile.ResolveCyborgBasePath`;
  - если префаба нет, бросается `FileNotFoundException` с GUID;
  - `Import` разрешает аватары до первой записи поз и базы поз.
- `Assets/Editor/VR_Battlegrounds/Arsenal/ArsenalBuildPreflight.cs` и `ArsenalEditorWindow.cs` берут путь из
  `HandsPackPoseImporter.MefBaseAvatarPath`.

## Пересборка AK105 и Mk14

Каждый ствол собран так: `KinemationWeaponBuilder.Build` → `WeaponBalanceApplier.Apply`. Сборка прошла до конца,
включая `AfterBuildLogged`. Сравнение с префабом до пересборки сделано семантическим дампом SerializedObject
всех компонентов, без учёта `_uxrUniqueId`.

Совпадают:
- позы `Kinemation_*_Grip/Support` — побайтно;
- записи хвата и трансформы выравнивания `MEF_Base_Avatar`;
- база поз `PlayerBase_NonSdkHands` — побайтно;
- все меши, включая `*_SightSplit` и `*_SightFront`, и звуки — побайтно;
- `SightFrontFrame/SightFront`, в том числе сдвиг мушки AK105 (локальная позиция (0.00144, 0.07277, 0.40968));
- `ShotSource`, `UxrFirearmWeapon`, `UxrProjectileSource`, `RecoilAccumulator`, `Rigidbody`;
- `NetworkIdentity._assetId` (AK105 1794343293, магазин 3287218346);
- `AK105_Weapon.asset` — баланс файл не изменил.

Отличия: шаги, которые `KinemationWeaponBuilder.Build` не вызывает, хотя в текущих префабах они есть.
- Слой хвата Cyborg (GUID `71b74b0d…`, Grip Extraction профиль MefCyborgHands) теряется:
  - записи точек 0/1 с позами `Prefabs/Player/HandPoses/CyborgHands/Kinemation_*`;
  - `HandsPack/*/MEF_cyborg_hands/*`;
  - запись затвора и `Charger/* Grab (MEF_cyborg_hands)`.
- `_selectedAvatarForGrips` (поле только для превью в редакторе): было CyborgHandsBase, стало MEF_Base_Avatar.
- `WeaponSystem._rig` откатывается к старой схеме: вместо мигрированного рига снова появляются
  `WeaponMechanismVisuals` и `WeaponChamberingReminder` (миграция `WeaponSystemAuthoring.MigrateWeapons` не вызывается).
- Магазин теряет:
  - `ImpactSound#1` и дочерний `ImpactAudio` (`ImpactSoundInstaller`);
  - `MagMachinegunGeo/GrabHighlight`;
  - `UxrAudioManipulation._audioOnPlace` (MagPlace).
- Магазин: записи MEF и Cyborg поменялись местами в массиве. Набор записей тот же.
- Mk14: пропали осиротевшие узлы `MEF_Base_Avatar/{Left,Right} (MEF_cyborg_hands)`. Метаданные редактора
  `__isInPrefab`/`__prefabGuid` у snap-трансформов сбросились.
- `MechanismMotion.asset`: изменился только `rid` managed reference.
- Создаётся неиспользуемая папка `Art/Weapons/Interaction/<ствол>/` (4 региона). Префаб ссылается на
  `Kinemation/<ствол>/Interaction`.

Итог: пересобранный префаб не эквивалентен текущему. Обе пересборки откачены из резервной копии (`cmp` совпал).
Пересобранные варианты сохранены в `reports/grip-fix/rebuilt-*`. Лишние папки Interaction перенесены в
`reports/grip-fix/stray-*`.

## Проверки

- Компиляция без ошибок.
- `ArsenalBuildPreflight.Validate(AK105)` проходит.
- EditMode: HandsPackHandPoseTests, AvatarHandPoseChainTests, KinemationWeaponTests, NetworkAssetIdOnDiskTests,
  WeaponBalanceTests — 108 тестов, 104 прошли, 4 упали. Все падения в KinemationWeaponTests и связаны с прицелами
  раундов 1–2, не с этой правкой:
  - треугольники MKR9 — 31549 при бюджете 30000;
  - треугольники R08 — 24605 при бюджете 22000;
  - шейдер сетки TR15_XPS2;
  - нет `MeshContainer/SM_Attach_AR15_XPS2` у TR15.

## Раунд 2 (решения пользователя): режим поз и полная пересборка

### Режим поз (`WeaponGripPoseMode`, `Avatars/WeaponGripCarryOver.cs`)

- **«Оставить текущие позы» (`KeepCurrent`)** — по умолчанию. `HandsPackPoseImporter` не вызывается.
  - До сборки `WeaponGripCarryOver.Capture` снимает с текущих оружия и магазина записи хвата всех аватаров
    (MEF, Cyborg и записи SDK у магазина). Снимаются точки 0/1, затвор, магазин, `_selectedAvatarForGrips` и
    трансформы выравнивания с локальной позой и цепочкой родителей.
  - После сборки `Restore` возвращает их. Записи переносимых аватаров заменяют записи сборщика, порядок прежний.
  - Недостающие узлы создаются с прежней позой; лист получает прежнюю позу. Узлы геометрии не трогаются.
  - Одноимённые братья различаются номером: у варианта магазина два `Grab Left (MEF_cyborg_hands)`.
  - Ставшие лишними трансформы сборщика удаляются через `HandsPackPoseImporter.RemoveUnused`.
  - Порядок среди братьев восстанавливается только у узлов хвата.
- **«Перезаписать позы из пака» (`ReimportFromPack`)** — прежний импорт MEF. Записи аватаров, отличных от
  `HandsPackPoseImporter.GripAvatarGuidSet()` (Cyborg и др.), всё равно переносятся.
- **Где выбирается:**
  - параметр `poses` у `KinemationWeaponBuilder.Build`, `HandsPackWeaponBuilder.BuildFull` и
    `HandsPackLegacyRebuild.RebuildAll` (по умолчанию `KeepCurrent`);
  - выпадающий список «Позы хвата» на вкладке пересборки `ArsenalEditorWindow`, общий для всех кнопок пересборки.
  - Отдельные кнопки «Импортировать позы кистей …» остаются явной перезаписью.

### Шаги полной пересборки

- **KINEMATION** (`KinemationWeaponBuilder.Build`):
  1. Capture.
  2. Звуки, затем геометрия и магазин (`HandsPackWeaponBuilder.Build`).
  3. Импорт поз — только в режиме перезаписи.
  4. Restore: до механики, потому что области подсветки строятся по хвату MEF.
  5. Отдача у рукояти.
  6. `KinemationFixReview` (механика, подсветка, магазин).
  7. Restore повторно: `WeaponInteractionInstaller` переставляет трансформы хвата затвора.
  8. TR15 optic.
  9. `WeaponRebuildFinish.Complete`.
- **Hands T-38** (`BuildFull`):
  1. Capture.
  2. Build.
  3. Импорт — только в режиме перезаписи.
  4. Restore.
  5. Отдача.
  6. `WeaponInteractionReview.Apply(оружие, магазин)`: области по итоговому хвату и подсветка отдельного магазина.
  7. Restore.
  8. Complete.
- **Legacy** (`RebuildAll`): прежние шаги, затем импорт (только в режиме перезаписи), Restore и Complete.
  Этот путь не прогонялся.
- **`Gameplay/WeaponRebuildFinish.Complete`** — общий конец пересборки:
  1. прицел (`WeaponSightAuthoring.AfterBuildLogged`);
  2. `WeaponSystemAuthoring.MigrateWeapons(имя)` — при провале исключение;
  3. `WeaponBalanceApplier.Apply` для WeaponInfo ствола;
  4. `UxrUniqueIdPersister.Normalize` для магазина и оружия.

  Шаг 4 нужен потому, что `SaveAsPrefabAsset` пишет `__isInPrefab: 0`, а постпроцессор в фоне редактора ждёт фокуса.
- **Лишняя `Art/Weapons/Interaction/<имя>`.** Её создавал `HandsPackWeaponBuilder` (области по умолчанию), а
  `KinemationFixReview` потом пек в `Kinemation/<имя>/Interaction`. Теперь у рецепта есть `InteractionFolder`,
  KINEMATION задаёт его через `KinemationFixReview.InteractionFolder`. Тот же метод использует `WeaponInteractionReview`.
- **Порог извлечения 0,855 → 0,7 и звуки затвора MP5K.** Живой донор (AR15/BrowningHiPower) переведён на
  WeaponSystem, и его `AutomaticWeaponSlideFeedback` больше нет. Сборщик копировал «ничего», а `Configure` ставил
  значения по умолчанию. Теперь при отсутствии AWSF у донора поля берутся из его immutable снимка
  (`HandsPackLegacyRebuild.DonorSnapshot`: `M16_Rifle_prefab`, `Gun_real`).

### Сравнение (семантический дамп, без `_uxrUniqueId` и `__isInPrefab/__prefabGuid`)

Дампы — `reports/grip-fix/{ak105,mk14,mp5k}-r4*`, резервная копия — `backup/round2-pre`.

Оружие AK105, Mk14, MP5K совпадает с прежним полностью:
- хваты MEF и Cyborg, затвор;
- прицел, ShotSource;
- `WeaponSystem`, включая `_extractionGate` 0,855 и звуки затвора MP5K;
- баланс, `_assetId`;
- области подсветки — по содержимому (у MP5K отличается только CRLF/LF рабочей копии).

Позы, база поз, меши и звуки побайтно те же.

Остаются объяснимые отличия:
1. **Магазин.** Прежние варианты `*_mag` несли исторические лишние объекты, сборка их не воспроизводит:
   - второй `ImpactSound` со своим `ImpactAudio` — дубль: база `MagMachinegun` уже даёт свой, удар звучал дважды;
   - неактивный `MagMachinegunGeo/GrabHighlight`, на который никто не ссылается;
   - пара собственных `Grab Left/Right (MEF_cyborg_hands)` с той же позой, что одноимённые узлы базы. Запись Cyborg
     теперь указывает на узлы базы, поза та же.
2. **AK105, вложенный магазин:** пропал override `_audioOnPlace = MagPlace`. Это остаток коммита `c56dd3e93`, который
   снимал двойной звук вставки.
3. **Mk14:** не перенесены осиротевшие `MEF_Base_Avatar/{Left,Right} (MEF_cyborg_hands)`, на них нет ссылок.
4. `MechanismMotion.asset` — новый `rid` managed reference. У всех UXR-компонентов новые `_uxrUniqueId`, у
   объектов — новые fileID. Ссылки `--- MANAGERS ---` и `WeaponInfo` на оружие и магазины разрешаются.

Повторная пересборка AK105 и MP5K семантически идемпотентна (r4 = r5). Режим «Перезаписать» на AK105 дал тот же
результат, что «Оставить»: позы пака совпали побайтно, Cyborg перенесён.

Пересобранные AK105, Mk14 и MP5K оставлены в рабочем дереве. Откат — из `backup/round2-pre`.

### Старый путь MEF в шести местах

Переведены на `HandsPackPoseImporter.MefBaseAvatarPath`: `AvatarMixamoLocomotionSetup.MainAvatarPrefab`,
`CyborgLegsBuilder.TemplatePath`, `AvatarEditorToolsPanel`, `AvatarPuppetStandMenu.MefMeshPath`.

`ClipScrubStandMenu` и `SitCandidatesStandMenu` находятся в своей asmdef (`VrBattlegrounds.PuppetStand.Editor`) и
`Assembly-CSharp-Editor` не видят. Там путь разрешается тем же GUID через `AssetDatabase.GUIDToAssetPath`.

### Проверки раунда 2

- Компиляция без ошибок, консоль без ошибок.
- EditMode, 10 классов: HandsPackHandPose, AvatarHandPoseChain, GrabPoseCoverage, KinemationWeapon,
  NetworkAssetIdOnDisk, UxrUniqueIdOnDisk, ImpactSound, WeaponFeedback, WeaponScale, WeaponBalance. Всего 335 тестов,
  8 упали:
  - 4 в KinemationWeapon — прежние, связаны с прицелами (MKR9/R08 по треугольникам, TR15 XPS2 ×2);
  - 3 в GrabPoseCoverage — `DogTagPanel` × три аватара Cyborg/MEF: нет записи хвата у DogTag. Префаб не трогался,
    правка к этому отношения не имеет;
  - 1 — `UxrUniqueIdOnDiskTests` превысил тайм-аут 180 с в общем прогоне. Отдельный прогон: 1/1 прошёл за 153 с.
- Тесты не писались и не менялись.

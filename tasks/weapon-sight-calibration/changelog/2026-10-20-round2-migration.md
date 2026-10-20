# 2026-10-20 — раунд 2: миграция арсенала на новую схему калибровки

Поверх `9ca826bb1`. Ничего не закоммичено; ждёт проверки в Unity и шлеме. Скриншоты лежат в
`../reports/round2/<Weapon>/` (вне Git): `01_through_sight`, `02_side_moving`, `03_side_rear`, `04_side_all`.
Дистанция пристрелки Z = 15 м, значения в мм.

## Схема (код)

Сборка ствола: сборщик пака → `WeaponSightAuthoring.AfterBuild(prefabPath)` (шаги 2–3) → баланс (как раньше,
отдельной командой).

- **Родной прицел** (`SightSplitTable`): `ApplyNative`. Порядок шагов:
  1. Вторая временная копия префаба → `SightPartSplitter.Apply`. Splitter сначала возвращает уже разделённый
     префаб к исходному мешу (`RevertMigrated`), потом делит.
  2. Меши сохраняются ассетами по `SightPartSplitter.MeshAssetPath`. Копия идёт через Mesh API (`CopyMesh`):
     `CopySerialized` терял вершины многопоточных мешей пака Hands. Имя меша = имя файла, `m_IsReadable` = 0.
  3. Результат переносится в основную копию (`Sync`). Существующие `Sight{Role}Frame/Sight{Role}[/Mesh]`
     обновляются на месте, поэтому fileID не меняются. Подъём рамок Scar переносится.
  4. Сдвиг берётся из записи, если совпадают отпечаток ShotSource и дистанция. Иначе ставится 0 и в отчёт идёт
     «пересвести на стенде».
  5. Сверка TRS всех узлов вне прицела и цепочки ShotSource → `SaveAsPrefabAsset` → канонический `_assetId` → readback.
  - Исходный меш (`*_Body.asset` Kinemation, FBX пака) не перезаписывается. Корпус пишется в
    `{оружие}_{узел}_SightSplit.asset` — это отличие от плана 485, нужное для повторяемости.
- **Комплект**: прежний `ApplyToPrefab` (nested `SightKit_*`, поза из записи).
- Ствол со своим прицелом не получает комплект (отказ). Родной прицел с записью комплекта — отказ «пересвести».
- **Вызывают**:
  - `KinemationWeaponBuilder.Build`, `HandsPackWeaponBuilder.BuildFull`, `HandsPackLegacyRebuild.RebuildAll` —
    через `AfterBuildLogged`: ошибка не прерывает сборку, пишется в GameLog как предупреждение;
  - кнопка стенда «Применить к префабу» — через `Apply` → `ApplyToPrefab`, ветка Kit = null → `ApplyNative`.
- **Стенд** (`SightAutoZeroPanel`):
  - «Сохранить в таблицу» и «Применить» включены для родного прицела: запись с Kit = null и сдвигом детали;
  - PPK сохраняет сдвиг 0;
  - стенд открывается на уже разделённом префабе (копия делится заново, сдвиг из записи показывается).

## Стволы

| Ствол | Режим / прицел | Сдвиг | До → после на 15 м | Гор. 15 м | 5/10/20 м после |
|---|---|---|---|---|---|
| AK105 | родной, столбик мушки | +0,911 | −51,2 → 0 | 0,0 | 31,9/16,0/−16,0 |
| Mk14 | родной, мушка вниз (ход −7) | −6,407 | 161,0 → 0 | 0,0 | 14,8/7,4/−7,4 |
| Viper | родной, целик на затворе (ход −4) | −3,072 | −291,2 → 0 | 0,0 | 9,6/4,8/−4,8 |
| R08 | родной, целик | +0,573 | 54,1 → 0 | 0,0 | 16,0/8,0/−8,0 |
| MP5K | родной, столбик мушки | −0,904 | 52,3 → 0 | 1,8 | 38,3/19,2/−19,2 |
| Uzi | родной, мушка с ушками | −0,904 | 71,6 → 0 | **32,5** | 24,4/12,2/−12,2 |
| Herrington | родной, лопатка (остров 1) | +1,650 | −84,0 → 0 | 0,0 | 17,9/8,9/−8,9 |
| Revolver | родной, целик | +0,392 | 24,2 → 0 | 1,2 | 15,5/7,7/−7,7 |
| Gun (BrowningHiPower) | родной, мушка на затворе | −0,190 | 17,7 → 0 | 0,0 | 8,8/4,4/−4,4 |
| Scar | родной, рамки подняты, столбик | −2,025 | 92,5 → 0 | −2,8 | 49,8/24,9/−24,9 |
| SRM12 | родной, столбик (ход −6) | −5,163 | 175,3 → 0 | −0,2 | 43,1/21,5/−21,5 |
| PPK | родной, без подвижной детали | — | 14,7 (только показ) | −1,0 | 16,1/15,4/14,1 |
| M16 (AR15) | SightKit_TR15, R→F 205 мм | −5,301 | 401,7 → 0 | 0,0 | 42,0/21,0/−21,0 |
| SniperRifle | SightKit_TR15, R→F 485 мм | −6,469 | 210,7 → 0 | 0,0 | 41,8/20,9/−20,9 |
| MKR9 | SightKit_MX68, R→F 171 мм | +0,007 | −0,6 → 0 | 0,0 | 53,3/26,6/−26,6 |
| TR15 | SightKit_XPS2 (раунд 1) | — | — | — | — |
| ShotgunReal | не калибруется | — | — | — | — |

Посадка комплектов: высота планки по лучам вниз с временных MeshCollider на копии. Корень комплекта стоит на
вершинах зубцов. Под основаниями поверхность на уровне низа основания: зазор 0,00…0,09 мм, не врезано.
- M16: целик на планке ствольной коробки впереди сложенного штатного BUIS, мушка — на переднем конце цевья.
- SniperRifle: целик на задней части длинной планки, мушка — на передней (+2,94 мм к оси: края планки выше центра).
- MKR9: целик на планке ствольной коробки, мушка на планке цевья.

Таблица `WeaponSightTable.asset`: 16 записей — 12 родных, M16/SniperRifle/MKR9, TR15.

## Новые меш-ассеты (24)

Kinemation:
- `AK105/Meshes/AK105_{Base_SightSplit,SightFront}`
- `Mk14/Meshes/Mk14_{Base_SightSplit,SightFront}`
- `Viper/Meshes/Viper_{Bolt_SightSplit,SightRear}`
- `R08/Meshes/R08_{Base_SightSplit,SightRear}`
- `Herrington/Meshes/Herrington_{Herrington_1187_Skeleton_SightSplit,SightFront}`
- `SRM12/Meshes/SRM12_{Sight_SightSplit,SightFront}`

HandsPack:
- `MP5K/Meshes/MP5K_{Base_SightSplit,SightFront}`
- `Uzi/Meshes/Uzi_{Base_SightSplit,SightFront}`
- `Revolver/Meshes/Revolver_{Base_SightSplit,SightRear}`
- `BrowningHiPower/Meshes/BrowningHiPower_{Gate_SightSplit,SightFront}`
- `Scar/Meshes/Scar_{Scar_Detail_03_SightSplit,SightFront}`

PPK меши не получает: у него нет выносимой детали, создаются только узлы-точки.

## Идемпотентность

- `AfterBuild` всех 12 родных стволов, повторно: 50/50 файлов (префабы, магазины, меши) побайтно те же.
- Комплекты M16/SniperRifle/MKR9/TR15: префабы побайтно те же.
- Полная пересборка сборщиком пака (`KinemationWeaponBuilder.Build(AK105)`) падает **до** шага прицела:
  NRE в `HandsPackPoseImporter.AssignToGrabPoint:209` (позы хвата, зона avatar-grip).
  Частично записанные `AK105.prefab`, `AK105_mag.prefab` и созданная `Art/Weapons/Interaction/AK105/` возвращены
  из резервной копии и удалены — дерево как до запуска.

## Убраны SDKGun, SDK ShotGun, Machinegun

Подробно — `Assets/Unused/Weapons/README.md`.
- Убраны ссылки:
  - WeaponRegistry (−3) и FullDemoArsenal (−3; CurrentGameplayArsenal их не содержал);
  - ArsenalCompositionCatalog (−3 строки с магазинами);
  - `--- MANAGERS ---` spawnPrefabs: Gun, Machinegun, Shotgun;
  - `BotCombatStand.unity` (Weapons, −3 строки YAML);
  - `WeaponSystemAuthoring.Weapons` (−3) и ключи окон выброса Gun/Machinegun;
  - донор спуска `HandsPackWeaponBuilder.ShotgunReal` → снимок `LegacyBuildDonors/Shotgun_real.prefab`.
- Перенесено в `Assets/Unused/Weapons/{SDKGun,Shotgun,Machinegun}/` через MoveAsset (GUID сохранены): 3 WeaponInfo,
  3 префаба, 3 раскладки слота, 8 регионов взаимодействия.
- Оставлены как общие: магазины `MagGun`/`MagShotgun`/`MagMachinegun` (базы магазинов 11 стволов и константы
  сборщиков) и их записи в spawnPrefabs; позы кисти `Demo*`.
- Устаревают отпечатки (зона gameplay): `MapRuntimeCatalog` — Lobby `ArsenalFingerprint` (FullDemoArsenal),
  BotCombatStand `ContentFingerprint`. Чем обновить: `MapBootstrapMigration.RebakeCatalog`
  (`Tools/VR Battlegrounds/Maps/Map Bootstrap/Rebake Catalog`). Не запускал.
- Assets/Unused не сканирует никто по пути. Но глобальные `FindAssets("t:WeaponInfo")` без папки найдут перенесённые
  WeaponInfo:
  - `BotCombatStandBuilder:75` — при пересоздании стенда вернёт их в список;
  - `AnchorActivationAudioTests:80`;
  - `ArsenalLayoutAuthoringStandEditor:111,262`;
  - `WeaponSystemAuthoring.CheckCategory`.
- Перенесённые legacy-файлы были с CRLF и не нормализованы (содержимое не правилось).

## Проверки (EditMode, узкие)

Компиляция без ошибок, консоль чистая.

Группы Sights, SightSplit, NetworkAssetIdOnDisk, WeaponBalance, KinemationWeapon, WeaponScale, WeaponSystemWavePrefab,
WeaponEjection: **254/271**. 17 красных:
- устарели из-за миграции:
  - `SightPartSplitterTests.Selectors_FindExactlyOneIslandEach` ×11 — читает меш узла префаба, теперь это корпус без
    детали;
  - `Scar_RaiseMatchesHingeAxis` — префаб уже поднят, Δ 0,056 мм при допуске 0,05;
- устарели с раунда 1: `Table_CoversAgreedWeapons` (SRM12), `KinemationWeaponTests` TR15 ×2 (узел XPS2);
- новое: `…треугольники_в_бюджете(MKR9)` — 31549 > 30000; MX68 добавляет 3026 треугольников, у TR15-комплекта 3078;
- прежнее: `…(R08)` — 24605 > 22000.

`HandPoseGripRulesTests` (Editor) ссылаются на старые пути `Gun/Machinegun/Shotgun.prefab` — устарели, не запускал.

## Тесты после приёмки (пользователь проверил раунды 2–3 и пересборку, «всё хорошо»)

- `SightPartSplitterTests`:
  - `Selectors_FindExactlyOneIslandEach` проверяет селекторы по исходному мешу спецификации
    (`SightPartSplitter.SourceMesh`);
  - `Scar_RaiseMatchesHingeAxis` берёт сложенную позу из спецификации;
  - `Table_CoversAgreedWeapons` дополнен SRM12.
- `KinemationWeaponTests`:
  - из обвесов TR15 убран `SM_Attach_AR15_XPS2`;
  - окно сетки XPS2 проверяется по узлу `SightKit_XPS2/XPS2`;
  - бюджет MKR9 поднят до 32000 (MX68 +3026);
  - новый тест `Коллиматор_TR15_вложенный_комплект_XPS2`.
- `HandPoseGripRulesTests`:
  - правила хвата проверяются на BrowningHiPower, AK105 (точка 1) и FabarmSDASS (Pump);
  - превью правки позы — на сэмпле `ThirdParty/.../ShootingRange/Weapons/Gun.prefab`.
- `WeaponScaleTests`: убраны мёртвые эталоны Gun, Shotgun, Machinegun.
- Новый `Sights/WeaponSightPrefabTests`:
  - постоянное разделение родного прицела (узлы, меш-ассеты, pivot, ход);
  - сдвиг родного прицела из таблицы;
  - комплект вложен в позе записи;
  - крепление комплекта в пазу планки (контакт и ось ±0,3 мм, упор ±1 мм);
  - сборщики по умолчанию в режиме `KeepCurrent`.
- Прогон Sights, SightSplit, KinemationWeapon, WeaponScale, WeaponSystemWavePrefab, WeaponEjection, NetworkAssetIdOnDisk,
  WeaponBalance, HandPose*: **477/478**. Единственный красный — посторонний бюджет R08 (24605 > 22000).

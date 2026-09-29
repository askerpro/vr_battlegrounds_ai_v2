---
name: add-weapon
description: Добавить в игру новое огнестрельное оружие — от модели из пака Hands Weapons Animations (или другого) до префаба с магазином, регистрации в арсенале, сети Mirror и зелёных тестов. Использовать, когда пользователь просит «добавить оружие», «собрать дробовик/пистолет/винтовку», «интегрировать оружие из пака», «сделать новое оружие на стену арсенала», или даёт путь к модели оружия.
---

# Добавление оружия

Задача: $ARGUMENTS

Эталон, собранный по этому маршруту: `Assets/Prefabs/Weapons/ShotgunReal/` (`Shotgun_real` +
`Shotgun_real_mag`), рецепт — `HandsPackWeaponBuilder.ShotgunReal`. Вручную на глаз собраны
`Gun_real` и M16 — **геометрию** они повторяют точно, а **механику** (ход затвора, угол
спуска) нет: у `Gun_real` затвор 1.89 см против 4.23 в паке, спуск 40° против 18°.

## 0. Окружение

- Нужен Unity MCP. Нет — `/unity-check`; без MCP префабы не менять.
- `execute_code` — CodeDom: `UnityEngine.Object` вместо `Object`, без `dynamic`. Редакторные
  классы проекта (`VrBattlegrounds.Editor.Gameplay.*`) вызываются напрямую.
- Префабы собираются в **preview-сцене** (`EditorSceneManager.NewPreviewScene`) — открытую
  карту не пачкать.

## 1. Разведка модели

Пак `Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/<Hands_X>/` — это FPS-руки
с оружием, не готовое оружие. Каждая деталь (корпус, затвор/помпа, спуск, курок, магазин) —
отдельный `SkinnedMeshRenderer`, жёстко привязанный к **одной** кости.

```csharp
using (var pack = new VrBattlegrounds.Editor.Gameplay.HandsPackWeapon("Hands_Shotgun", "Shogun_Base_mesh"))
    return pack.Report();   // все детали × все клипы: ход в см, угол в °
```

Или меню `Tools/VR Battlegrounds/Gameplay/Hands Pack Weapon Report` (лог в консоль).
Нужно определить: корпус, подвижную деталь (помпа/затвор) и клип, где она ходит (`Shot`),
спуск, деталь у окна магазина, клип с руками на оружии (`Aming_Idle`/`Aim_Idle`).
Посмотреть модель глазами — рендер в preview-сцене в PNG (пример — в истории
`HandsPackWeaponBuilder`, камера ортографическая сбоку).

**Не использовать Animator пака в рантайме.** Он двигает руки FPS и кости оружия; в VR
руки — аватар UltimateXR, помпу/затвор двигает рука игрока, позиции синхронизирует сеть.
Клипы пака — **источник данных**: из них вычисляются ход и углы.

## 2. Геометрия — вычислением, не на глаз

Место детали = `кость × bindpose` её основной кости (`HandsPackWeapon.PartInBody`). Трансформ
самого рендерера **не** совпадает с местом меша (расхождение до 1.5 в матрице) — если ставить
`MeshFilter` по нему, деталь съезжает. Детали ставятся с масштабом 1 (единицы меша), реальный
размер задаёт **масштаб корня** (`WeaponScaleTests`, правило CLAUDE.md).

Проверено: `BuildParts` воспроизводит ручную расстановку `Gun_real` с точностью 0.00 мм.

## 3. Хват — из ладоней пака

Клип с руками на оружии показывает, где лежат ладони. Ладонь переводится в точку хвата
UltimateXR калибровкой с донора — оружия того же пака с вручную настроенным хватом
(`GripCalibration`). Точность: калибровка с `Gun_real` предсказывает правую руку M16 с
ошибкой 1.1 см / ~20°. Для длинноствольного донор — M16, для пистолета — `Gun_real`.

Позы (`UxrHandPoseAsset`, blend) берутся у донора: рукоять — поза основной точки,
цевьё/помпа — поза дополнительной. Записи делаются для `MEF_Base_Avatar` — как у M16 и
`Gun_real`. У Heavy Soldier и Cyborg поз нет ни у одного оружия проекта
(`GrabPoseCoverageTests` красный и до нового оружия) — это отдельная задача. Покрытие новой
`WeaponInfo` × каждый аватар реестра показывает `GrabPoseCoverageTests` — по тест-кейсу на пару.

## 4. Сборка

Рецепт (`HandsPackWeaponRecipe`) — единственное, что пишется руками: детали пака, клипы,
длина прототипа, теги, доноры. Образец — `HandsPackWeaponBuilder.ShotgunReal`. Сборщик:

- корень: `Rigidbody` (от донора, `Continuous Dynamic`), `UxrGrabbableObject`,
  `UxrProjectileSource`, `UxrFirearmWeapon`, `NetworkIdentity`, `AnchoredItemCollisionIgnore`,
  `OutOfWorldGuard`; помпа → `UxrShotgunPump`, затвор → `AutomaticWeaponSlideFeedback`;
- `MeshContainer`: корпус с выпуклым `MeshCollider`, статичные детали, спуск (им вращает
  `UxrFirearmWeapon`, угол и ось — из клипа), `Tip`/`ShotSource` по кольцу вершин у дула,
  `RecoilSourceAxes` у рукояти, `MagAnchor` у окна магазина со звуком донора;
- помпа/затвор — отдельный граббабл у корня с `RestrictLocalOffset` на ход из клипа и
  `GrabOnlyWhenParentHeld`. Помпа наводит ствол (`Control Parent Direction` вкл.,
  `Ignore Grabbable Parent Dependency` выкл. — как у сэмпла SDK, `PumpAimTests`), затвор —
  нет (наоборот, как рукоятка M16);
- снаряд — `Effects/Tracer_Default`: тонкий светлый след (`TrailRenderer`), не меш — меш пули
  в шлеме выглядит стрелкой (`TracerVisibilityTests`); скорость: винтовка 400 м/с, пистолет и
  дробовик 300;
- `ShotSource` — на 1 см позади `Tip`, `Tip` — на срезе ствола по вершинам модели
  (`ShotOriginTests`); `BarrelObstruction` с казённой точкой `BarrelCheck` в коробке — ствол в
  стене не стреляет (`BarrelObstructionTests`);
- вспышка у дула — `Effects/Muzzle_Default` (MuzzleFlash Particle Pack, ×0.12), привязана к дулу,
  без своего звука и света (`MuzzleEffectTests`); у эффектов пака выключать мягкие частицы —
  у стены они гаснут;
- попадание — `Effects/Impact_Default` и `Effects/ImpactDecal_Default`, не сэмплы SDK
  (`ImpactEffectTests`); эффекты под другие поверхности — из `Weapon Effects` Particle Pack,
  тем же способом: одноразово, без демо-мишени, свой выброс вместо суб-эмиттеров;
- помповое: второй тип выстрела для дробинок без вспышки у дула, `ShotgunPellets`
  (`ShotgunPelletsTests`), `PumpGrabFollow` — помпа без запаздывания на промах хвата
  (`PumpGrabFollowTests`);
- магазин — **вариант** префаба-магазина донора (сеть, физика, звуки, хват сэмпловых
  аватаров), геометрия заменена мешем пака, масштаб корня = масштаб оружия; вложенный экземпляр
  в якоре без `NetworkIdentity`, со `Start Anchor` и `Rigid Body Source`.

Настройки, не выводимые из пака (снаряд, урон, звук выстрела, отдача, физика хвата),
копируются у доноров **по полям**, без `_uxrUniqueId`/`__prefabGuid` и без
`m_CorrespondingSourceObject` (у донора-варианта он указывает на чужую базу).

```csharp
VrBattlegrounds.Editor.Gameplay.HandsPackWeaponBuilder.Build(recipe);
```

Повторная сборка перезаписывает префабы на месте — GUID и ссылки на них сохраняются.
После сборки — `Tools/VR Battlegrounds/Gameplay/Apply Game Tags` (корень → `Weapon`,
магазин → `Magazine`; сборщик теги не ставит, правило CLAUDE.md).

Модель не из пака Hands — шаги 2–3 делаются вручную, остальное то же; тест `HandsPackWeaponTests`
к такому оружию не применим.

## 5. Регистрация

| Что | Где |
|---|---|
| `WeaponInfo` (`Assets/Data/Weapons/<Name>_Weapon.asset`) | префаб, магазин, число запасных, цена, категория, `WeaponPositionOffset` на стене |
| реестр | `Assets/Data/Weapons/Resources/WeaponRegistry.asset` → `_weapons` |
| сеть | `Assets/Prefabs/Managers/--- MANAGERS ---.prefab` → `GameNetworkManager.spawnPrefabs`: оружие **и** магазин |
| стена | `Assets/Prefabs/Arsenal/StandardArsenalWall.prefab` → `FirearmSlotController._weaponInfo` слота |
| эталон размера | `WeaponScaleTests.RealLengths` — длина реального прототипа |
| механика из пака | `HandsPackWeaponTests.Cases` — детали, помпа/затвор, спуск |

Префабы менять через `PrefabUtility.LoadPrefabContents` → `SaveAsPrefabAsset` → `Unload`;
после — `git diff` по файлу: изменение должно быть в одну-две строки.

Смещение на стене подобрать так, чтобы оружие не выходило за перфопанель слота
(`WeaponHangFitsSlotTests` меряет по осям якоря). Теги хвата: карманы и слоты принимают
оружие по `UxrGrabbableObject.Tag` — брать существующий (`Shotgun`, `M16_Rifle`, `MagShotgun`…),
новый тег требует правки `AvatarPocketSetup.Pockets` и всех аватаров.

## 6. Проверка

1. **Сначала красный.** Кейс нового оружия в `HandsPackWeaponTests` до сборки — красный
   (нет префаба); тест ловит и настоящие дефекты: на `Gun_real` он красный по ходу затвора
   и углу спуска при зелёной геометрии.
2. Весь `VrBattlegrounds.Tests.EditMode`. Касаются оружия: `HandsPackWeaponTests`,
   `WeaponScaleTests`, `WeaponDropPhysicsTests`, `WeaponPartGrabTests`, `WeaponSlideTravelTests`,
   `OutOfWorldGuardTests`, `AnchorActivationAudioTests`, `WeaponHangFitsSlotTests`,
   `PumpAimTests`, `PumpGrabFollowTests`, `ShotgunPelletsTests`, `TracerVisibilityTests`,
   `ImpactEffectTests`, `NetworkAssetIdOnDiskTests` (после пересохранения префабов —
   `Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids`),
   `GameTagsTests`, `UxrUniqueIdOnDiskTests`, `AvatarLoadoutTests` (карманы).
   Падения, которые были до задачи, отличать от новых: ищи имя нового префаба в сообщении.
3. `AndroidCompileGate.Run()` — сборщик лежит в `Assets/Editor`, в билд не попадает.
4. Чего тестом не проверить — хват в шлеме (форма кисти на рукояти/помпе) и вид на стене в
   игре. Точки хвата вычислены с точностью ~1 см / ~20°; подправить в инспекторе
   `UxrGrabbableObject` (превью поз MEF) и прогнать тесты снова.

## 7. Документация

`Docs/CHANGELOG.md` (что добавлено), `Docs/README.md` (новые классы), при новой грабле —
`Docs/UltimateXR/known-issues.md`. Коммит — только после проверки пользователем, `/commit`.

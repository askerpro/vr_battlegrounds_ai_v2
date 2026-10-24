# maps-rename — имена карт по темам, Maps/Game и Maps/Dev

Обновлено 2026-10-24. Владелец `maps-rename` (Claude), основной checkout по указанию пользователя,
ветка `task/maps-rename` от `origin/dev` 93a5f0b6d.

## Цель и мотивация

Безымянные TestMap1–3 получают имена своих тем, а игровые карты отделены от карт для разработки.
Это подготовка к работе над картами: следующая задача — `map-military-base` (бывшая TestMap1).

| Было | Стало | Меню |
|---|---|---|
| `Maps/TestMap1.unity` | `Maps/Game/MilitaryBase.unity` | Военная база |
| `Maps/TestMap2.unity` | `Maps/Game/Warehouse.unity` | Склад |
| `Maps/ServiceYard.unity` | `Maps/Game/ServiceYard.unity` | Сервисный двор |
| `Maps/TestMap3.unity` | `Maps/Dev/BlockStand.unity` | Стенд блоков (debugOnly) |
| `Maps/ReferenceMap04.unity` | `Maps/Dev/ReferenceMap04.unity` | Эталон 04 (debugOnly) |

Папка с именем сцены рядом с `.unity` — данные запекания Unity (свет, окклюзия), перенесены вместе со сценой.

Стенды (по просьбе пользователя в той же задаче) — единый корень `Assets/Scenes/Stands/<область>/`:

| Было | Стало |
|---|---|
| `Dev/AvatarPuppetStand`, `Dev/ClipScrub(.asset)`, `Dev/SitCandidates(.asset)` | `Stands/Avatar/` |
| `Tools/CommonArsenalReview`, `Debug/ArsenalLayoutAuthoring` | `Stands/Arsenal/` |
| `Debug/BotCombatStand`, `BotCombatStandMap/Registry.asset`, `BotStand*.mat` | `Stands/Bots/` |
| `UI_SETUP/TeamSelection_Screen` | удалена по решению пользователя (с 2026-04 без ссылок) |
| генерируемые инструментами `Tools/ContactCatalogLab`, `Tools/IndustrialCandidateReview` | `Stands/LevelDesign/` (при создании) |

Пустые `Dev`, `Tools`, `Debug`, `UI_SETUP`, `ExcludedFromIndex` удалены. В корне `Scenes` — только Offline, Lobby, Maps, Stands.
`MapData_TestMap1/2/3` → `MapData_MilitaryBase/Warehouse/BlockStand`, GUID сохранены.

## Решения

- Перенос — `AssetDatabase.MoveAsset`/`RenameAsset` в Editor: GUID сцен, данных запекания и MapData прежние.
- Подпапки внутри `Maps/`: инструменты и тесты ищут по префиксу `Assets/Scenes/Maps/` рекурсивно, логика не менялась.
- Ключи станций в сценах (`TestMap1.SpawnZone.ArsenalWall-1` и т.п.) не меняются: по контракту они назначаются
  один раз и стабильны при переименовании.
- `Scenes/Debug`, `Scenes/Dev`, `Scenes/Tools` (стенды launch-infra/bots) не трогались.
- Самостарт стендов мимо Offline (`PlayModeStartFromOffline.StandScenesFolder`) — теперь `Stands/Avatar/`: те же три
  сцены, что были в `Dev/`. `PlayLaunchSceneMigration` размечает прежний охват (стенды аватара + CommonArsenalReview),
  пустая ArsenalLayoutAuthoring не размечается.
- `ArsenalEditorWindow` искал обзор по несуществующему `Scenes/Debug/CommonArsenalReview.unity`; теперь путь верный.
- Каталог перезапечён `MapBootstrapMigration.RebakeCatalog`: preflight passed, 7 карт. Попутно обновился
  отпечаток Lobby — он отставал после `f2f5599a0` (lobby-arena-cleanup перезапёк Lobby без перезапечки каталога).
- В тестах и инструментах заменены только пути и имена сцен, ожидания прежние.

## Статус

- [x] Перенос сцен и данных, переименование MapData, `sceneName`/`displayName`
- [x] EditorBuildSettings, каталог, ссылки в коде, тестах, `Tools/e2e`, текущих документах
- [x] Компиляция в Editor без ошибок
- [x] Проверка пользователем в Unity 2026-10-24: структура, карты, стенды в порядке. BotCombatStand шёл через Lobby —
  не регрессия: toolbar Play с профилем `SceneSource=active` идёт Offline → Lobby → автозагрузка DebugOrchestrator
  для любой карты; маршрут `ServerStartupRoute` для нового пути стенда валиден (план и Validate проверены в Editor)
- [ ] Существующие EditMode-тесты карт; heavy `--profile editor` и `--profile windows`
- [ ] Ревью зон level-design, gameplay, launch-infra; журнал level-design; коммит и вливание

## Известное, не из этой задачи

- `MapGameplayHierarchy.ValidateAll` падает на Lobby: «неверное количество зон/станций: 1/1» — ожидание валидатора
  (4 станции в лобби) расходится с текущей сценой; передать владельцу lobby-arena-cleanup / level-design.
- `Assets/Scenes/Debug/BotCombatStandMap.asset` — `description: TestMap1` (зона bots, не менялось).
- `Docs/README.md` (общий индекс, правка вне обычной задачи) ещё называет TestMap1/2 и `Scenes/Dev/` — нужна правка
  владельцем индекса. CHANGELOG, журналы, датированные планы и отчёты сохраняют старые имена как историю.
- `Docs/weapons/sight-calibration.md` описывает `Scenes/Tools/WeaponSight*`; таких сцен в репозитории нет, путь не менялся.
- У других задач в ветках остаются старые пути; после rebase на эту правку им нужна замена имён.

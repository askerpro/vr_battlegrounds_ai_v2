> Точка входа в документацию **игры** VR Battlegrounds AI: скрипты, классы, API, компоненты.
> Обновлять при добавлении классов, модулей, зависимостей или изменении архитектуры.
> Обязательно документировать обнаруженные побочные эффекты SDK (например, систему Precaching).
>
> Правила работы агентов, жёсткие ограничения и правила поиска — в [`CLAUDE.md`](../CLAUDE.md).

---

## Структура папки `Docs/`

| Файл | Содержание |
|---|---|
| `README.md` | Этот файл — технический справочник: скрипты, классы, API, компоненты |
| `tasks/` | **Очередь работ по аудиту:** [индекс со статусами и графом блокировок](tasks/README.md) + файлы задач `T-01`…`T-24` |
| `audit/` | Аудит 2026-08 (справочники, не меняются): [находки](audit/network-audit-2026-08.md), [оценка архитектуры](audit/architecture-review-2026-08.md) |
| `troubleshooting.md` | **Симптом → причина.** Индекс багов по внешнему проявлению, читать первым при расследовании |
| `testing.md` | Тестирование: шесть уровней от юнит-тестов до чек-листа в шлеме |
| `gameplay.md` | Геймдизайн: что делает игрок, правила, режимы, структура матча |
| `combat-networking.md` | Бой по сети: решения T-23 (компенсация задержки) и T-24 (где симулировать пули), замеры и методики |
| `session-architecture.md` | Сессия, роли устройств (VR/PC/Server), Host/Client |
| `game-manager.md` | GameManager, система режимов: создание assets, настройка, поток действий |
| `ui-menu-architecture.md` | Архитектура UI Меню (MVC), экраны, префабы, контроллеры |
| `ui-fonts.md` | Шрифты UI: как TMP рисует текст, шрифт проекта, как применять, как добавить символ |
| `magazine-pocket.md` | Механика "умного магазина" (Smart Magazine Pocket) |
| `level-design.md` | Проектирование карт и арен |
| `Arsenal/` | Стена арсенала: [дизайн](Arsenal/ArsenalWall_Design_RU.md), [код](Arsenal/Arsenal_Code_Architecture_RU.md) |
| `LegsAnimator_UI_Reference_RU.md` | Справочник по параметрам Legs Animator |
| `Roadmap.md` | План развития проекта |
| `CHANGELOG.md` | Журнал архитектурных и значимых изменений проекта |
| `version-control.md` | Git/Plastic workflow: автодублирование коммитов, хук, диагностика |
| `unity-mcp.md` | Локальный фикс Unity MCP `execute_code` на Windows (MAX_PATH) |
| `Mirror/mirror-patches.md` | Правки вендорного Mirror: что, зачем, как перенести при обновлении |

---

## Быстрая навигация

- **Что чинить прямо сейчас, что чем блокировано** → [`tasks/README.md`](tasks/README.md)
- **Баг: почему это происходит** → [`troubleshooting.md`](troubleshooting.md)
- **Как тестировать сеть и VR** → [`testing.md`](testing.md)
- **Прогнать e2e на двух процессах одной командой** → [`testing.md`](testing.md#ярус-c--два-процесса-настоящий-e2e)
- **Аудит: 21 находка** → [`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md)
- **Аудит: 5 корневых решений архитектуры** → [`audit/architecture-review-2026-08.md`](audit/architecture-review-2026-08.md)
- **Геймплей, режимы, матч, арена** → [`gameplay.md`](gameplay.md)
- **Попадания и снаряды по сети: задержка, цена симуляции, решения** → [`combat-networking.md`](combat-networking.md)
- **Сессия, роли устройств, Host/Client** → [`session-architecture.md`](session-architecture.md)
- **GameManager, режимы, assets, настройка** → [`game-manager.md`](game-manager.md)
- **Архитектура UI Меню (MVC)** → [`ui-menu-architecture.md`](ui-menu-architecture.md)
- **Шрифты, кириллица, «квадраты вместо букв»** → [`ui-fonts.md`](ui-fonts.md)
- **Умный магазин (Magazine Pocket)** → [`magazine-pocket.md`](magazine-pocket.md)
- **Стена арсенала** → [`Arsenal/Arsenal_Code_Architecture_RU.md`](Arsenal/Arsenal_Code_Architecture_RU.md)
- **Проектирование карт** → [`level-design.md`](level-design.md)
- **План развития** → [`Roadmap.md`](Roadmap.md)
- **История изменений (Changelog)** → [`CHANGELOG.md`](CHANGELOG.md)
- **Git/Plastic workflow и post-commit hook** → [`version-control.md`](version-control.md)
- **Unity MCP: фикс execute_code (Windows)** → [`unity-mcp.md`](unity-mcp.md)
- **UltimateXR SDK** → [`UltimateXR/README.md`](UltimateXR/README.md)
- **Архитектура UltimateXR** → [`UltimateXR/architecture.md`](UltimateXR/architecture.md)
- **Известные проблемы SDK** → [`UltimateXR/known-issues.md`](UltimateXR/known-issues.md)

---

## Структура папки `Assets/`

| Путь | Содержание |
|---|---|
| `Assets/Scripts/` | Игровые скрипты |
| `Assets/Scenes/` | Сцены игры |
| `Assets/Scenes/Maps/` | Сцены карт (например `Arena_Warehouse.unity`) |
| `Assets/Prefabs/` | Префабы |
| `Assets/Prefabs/Player/` | Префаб игрока |
| `Assets/Prefabs/GameModes/` | Префабы режимов (`RespawnMode.prefab`, `EliminationMode.prefab`) |
| `Assets/Data/Maps/` | `MapRegistry.asset` + `MapData` assets |
| `Assets/Data/GameModes/` | `GameModeRegistry.asset` + `GameModeData` assets |
| `Assets/Data/Teams/` | `TeamData` assets (`Terrorists.asset`, `SpecialForces.asset`) |
| `Assets/Resources/` | `GameSettings.asset` (загружается через `Resources.Load`) |
| `Assets/Tests/EditMode/` | EditMode-тесты (`VrBattlegrounds.Tests.EditMode.asmdef`, только редактор) |

Вне `Assets/`:

| Путь | Содержание |
|---|---|
| `Tools/e2e/` | Дирижёр e2e-прогона на двух процессах (ярус C) и артефакты прогонов в `results/` |
| `Build/e2e/` | Собранный плеер для e2e (в `.gitignore`) |

---

## Ключевые скрипты

### Сеть — `Assets/Scripts/Network/`

| Класс | Файл | Описание |
|---|---|---|
| `GameNetworkManager` | `Network/GameNetworkManager.cs` | Сетевой транспорт Mirror. Только коллбэки подключения, события `PlayerConnected/Disconnected/ServerSceneChanged/ClientSceneChanged`. Никакой игровой логики. |
| `NetworkStateRelay` | `Network/NetworkStateRelay.cs` | Транспорт **канала состояния UltimateXR** — `byte[]`-блобы, которыми едут захваты, состояние оружия и здоровье со смертью. Живёт на `SessionContext.prefab`, спавнится один раз при старте сервера. Раньше канал проходил через `UxrMirrorAvatar` и ломался при каждой смене аватара (T-12, [`UltimateXR/sdk-patches.md`](UltimateXR/sdk-patches.md), Патч 1). |
| `AvatarStateEventGate` | `Network/AvatarStateEventGate.cs` | Придерживает исходящие события сетевого аватара, пока он не выровнял `UniqueId` (`CombineIdSource` пуст до `CombineUniqueId`), и отдаёт их релею по сигналу `UxrMirrorAvatar.AvatarSpawned` — сериализация тогда пишет общие id (NET-26). Отдаёт, а не отбрасывает: `OnControllerInputChanged` SDK не повторяет. Несетевые аватары и объекты вне аватаров не трогает; очередь выбрасывается по `AvatarDespawned` или через 10 с без выравнивания. |
| `DespawnedObjectEventFilter` | `Network/DespawnedObjectEventFilter.cs` | Не пускает в канал состояния события сетевого объекта, который Mirror уже снял со спавна (`netId` есть, в `spawned` нет), но Unity ещё не уничтожила: его `OnDisable` (телепорт) уходил другой стороне, где объекта уже нет. |
| `NetworkUxrIdentity` | `Network/NetworkUxrIdentity.cs` | **Идентичность объектов UltimateXR, заспавненных в рантайме** (NET-16). Канал состояния адресует компоненты по `UniqueId`, а у объекта, созданного в рантайме, он на каждой машине свой — и тогда не применяется **ни одно** событие манипуляции. Класс выравнивает его по `netId` механизмом самого SDK (`CombineUniqueId`) сразу на обеих сторонах: сервер зовёт `SpawnServerObject` вместо `NetworkServer.Spawn`, клиенту в `GameNetworkManager.OnStartClient` ставятся обработчики спавна Mirror. Он же гасит «Auto Anchor» до `Awake` — иначе объект рождается внутри лишнего родителя, которого на другой машине нет. Аватары исключены: их ведёт сам SDK (`UxrMirrorAvatar`). |

**Статические события `GameNetworkManager`:**

| Событие | Когда | Подписчики |
|---|---|---|
| `PlayerConnected` | Игрок заспавнился на сервере | `PlayersManager`, `MapManager`, `DebugOrchestrator` |
| `PlayerDisconnected` | Игрок отключился | `PlayersManager` |
| `ServerSceneChanged` | Сервер завершил загрузку сцены | `DebugOrchestrator` |
| `ClientSceneChanged` | Клиент завершил загрузку сцены | `NetworkStateRelay` (перезапрашивает снимок состояния) |

**Два канала репликации.** Первый — штатный Mirror (`SyncVar`, `ClientRpc`): команда, счёт,
выбор карты, фаза раунда. Второй — канал состояния UltimateXR через `NetworkStateRelay`:
захваты предметов, состояние оружия, **здоровье и смерть** (`UxrActor.Life` — синхронизируемое
свойство). Каналы независимы: разные модели авторитета и разные гарантии порядка.

---

### Менеджеры — `Assets/Scripts/Managers/`

| Класс | Файл | Описание |
|---|---|---|
| `PlayersManager` | `Managers/PlayersManager.cs` | Список игроков, фильтрация: `Players`, `GetAlivePlayers(team)`, `GetPlayers(team)`. Синглтон на том же GO что и `NetworkManager`. |
| `MapManager` | `Managers/MapManager.cs` | **Единственная точка входа для смены карты внутри живой сессии.** Откладывает `ServerChangeScene` на конец кадра через корутину. Ждёт не по таймеру, а по условию `ConnectionsSettled()` — ни одно соединение не в середине `AddPlayer` (T-17). **Не отвечает за вход в сессию и выход из неё:** это ведёт сам Mirror по полям `onlineScene`/`offlineScene` у `GameNetworkManager`, и они — законное исключение из правила (разбор — NET-21 в `audit/network-audit-2026-08.md`). `LoadMap` выходит первой строкой по `!NetworkServer.active`, то есть на клиенте после разрыва он неприменим в принципе. |
| `GameManager` | `Managers/GameManager.cs` | Хранит выбор сессии (карта + режим). DontDestroyOnLoad вместе с NetworkManager. SyncVar реплицирует выбор клиентам. Методы: `SetSession()`, `StartSession()`. |
| `GameplayManager` | `Managers/GameplayManager.cs` | Матч: счёт, победитель, `StartGameplay()`, `StopGameplay()`. Режим ищет по `modeId` из `GameManager`. |
| `UxrActor` | `UltimateXR/.../UxrActor.cs` | Базовая система урона UltimateXR. Игрок умирает, когда `UxrActor` вызывает событие смерти. |
| `SetManager` | `GameModes/EliminationMode/SetManager.cs` | Сет: N раундов, счёт раундов, смена сторон, `ForceStop()`. Владелец машины раунда: тикает её и применяет единственный переход, который она не делает сама, — «итоги показаны → новый раунд». Исход сета отдаёт обязательным колбэком конструктора, а не событием. |
| `RoundManager` | `GameModes/EliminationMode/RoundManager.cs` | Машина фаз раунда: таблица переходов `Setup → Equipment → Countdown → Combat → Resolution → Scoreboard`, таймеры, `RequestRoundEnd()`, `ForceStop()`. Про `EliminationMode` не знает; о готовности игроков спрашивает `RoundReadiness`. `Tick()` возвращает переход значением — см. [gameplay.md](gameplay.md#машина-состояний-раунда-кто-чем-владеет). Фазу наружу раздаёт `EliminationMode`. |
| `RoundReadiness` | `GameModes/EliminationMode/RoundReadiness.cs` | **Готовность живых игроков к раунду (T-29):** кто готов, кого ждём, не пора ли начинать без опоздавших. Вынесен из `RoundManager` отдельным классом, потому что один и тот же ответ нужен в трёх местах — машине раунда (сдвинуть фазу), режиму (отдать состав неготовых клиентам) и инспектору (показать, почему раунд стоит). Обычный C#-класс поверх `IPlayerRoster`: предел ожидания и оба правила матча гоняются EditMode-тестом. Сбрасывает готовность в начале каждого раунда — `Reset(teams)`. |
| `RoundReadinessTimeoutRule` | `GameModes/EliminationMode/RoundReadiness.cs` | Правило матча при истечении предела ожидания: `AutoReady` (умолчание — объявить готовность за неготовых) или `StartWithoutPending` (стартовать, оставив их в списке ожидаемых). Разбор выбора — [gameplay.md](gameplay.md#кого-ждём-и-сколько). |

**Иерархия менеджеров матча:**

```
GameplayManager      — матч (5 карт, счёт, победитель)
└── EliminationMode  — сетевая оболочка: SyncVar, ClientRpc, ServerTick
    └── SetManager   — сет: раунды, счёт раундов, владелец перехода «раунд → раунд»
        └── RoundManager — фазы раунда: таблица переходов и таймеры
```

`SetManager` и `RoundManager` — обычные C#-классы, не `MonoBehaviour` и не сетевые:
создаются через `new`, тикаются из `EliminationMode.ServerTick(deltaTime)`.

Серверная логика выполняется только на сервере (`[Server]` Mirror). Клиенты получают обновления через `ClientRpc`.

---

### Режимы игры — `Assets/Scripts/GameModes/`

| Класс | Файл | Описание |
|---|---|---|
| `GameMode` | `GameModes/GameMode.cs` | Абстрактный базовый класс. Поле `ModeId` — строковый ключ для поиска. Методы: `CheckWinCondition`, `CanRespawn`, `OnRoundEnd`. Свойство `PlayerRoster` — откуда режим узнаёт об игроках; по умолчанию `PlayersManagerRoster`, в тестах подменяется. Состояния команд (`TeamRuntimeData`) получают этот же реестр в `Initialize`. |
| `EliminationMode` | `GameModes/EliminationMode/EliminationMode.cs` | Раунд до полного уничтожения команды. ModeId = `elimination`. Сетевая оболочка сета и раунда: `SyncVar` фазы с хуком, `ServerTick`, `ClientRpc` для UI. Таймеры фаз считаются от `NetworkTime` — в сеть уезжает только момент старта фазы (T-19). Отложенные респавны держит списком и снимает в начале каждого раунда (T-10). Состав неготовых выкладывает клиентам состоянием — `[SyncList<uint>] PendingReadiness` (T-29); предел ожидания и правило матча настраиваются полями `_readinessTimeLimit` / `_readinessTimeoutRule`. |
| `RespawnMode` | `GameModes/RespawnMode.cs` | Возрождение при возврате на спавн. ModeId = `respawn`. |
| `GameModeData` | `GameModes/GameModeData.cs` | ScriptableObject: `modeId`, `displayName`, `icon`. Создать: `Create > VrBattlegrounds > Game Mode Data`. |
| `GameModeRegistry` | `GameModes/GameModeRegistry.cs` | ScriptableObject-список режимов. `GetById(modeId)`. Назначить в `GameManager` и `AdminMenuController`. |
| `IPlayerRoster` | `GameModes/IPlayerRoster.cs` | Узкий доступ логики матча к списку игроков: `GetPlayers(team)`, `GetAlivePlayers(team)`, `GetAllPlayers()`. Боевая реализация `PlayersManagerRoster` — обёртка над `PlayersManager.Instance`, отсутствие менеджера отдаёт пустым списком. **Единственный источник сведений об игроках для режима** (NET-12, NET-18): благодаря этому весь серверный путь матча гоняется EditMode-тестом без живых аватаров и без синглтонов. |

> При добавлении нового режима — создать наследника `GameMode`, переопределить `OnRoundEnd`, `CanRespawn`, `CheckWinCondition`. Не менять базовую логику `RoundManager`.

---

### Игрок — `Assets/Scripts/Player/`

| Класс | Файл | Описание |
|---|---|---|
| `PlayerSession` | `Player/PlayerSession.cs` | Сессия игрока: переживает смену скина, команды и карты. Счёт, команда, никнейм, `deviceToken`, калибровка (`CalibrationScale` T-14, `CalibrationHeightOffset` VR-08, `IsCalibrated` T-30 — все три едут одним `PublishLocalCalibration`). **Готовность к раунду (T-29)** — явное состояние `ReadyState` с единственной точкой записи `ServerSetReady(bool, reason)`; клиент объявляет намерение через `CmdSetReady`. `IsInSpawnZone` — условие (выход из зоны снимает готовность), `HasGrabbedDogTag` — жест. Признак «в зоне» **вычисляется** из `SpawnZoneTeamIndex` («в зоне какой команды я стою», `SyncVar`): булев флаг засчитывал чужую базу за свою и затирался выходом из чужой зоны — находка RDY-04. Оттуда же `IsInEnemySpawnZone`. Разбор — [gameplay.md](gameplay.md#готовность-к-раунду--явное-состояние). |
| `PlayerController` | `Player/PlayerController.cs` | Состояние: здоровье, команда (`TeamIndex`), `IsAlive`. Подписывается на `UxrActor.Died`. Ссылка на свою `PlayerSession` кэшируется в `OnStartServer`/`OnStartClient`. **Событие `PlayerDied` поднимается на каждой машине** — из `OnActorDied`, а не из `[Server] Die()`: смертельный урон уводит актора в `DieInternal`, а его канал состояния UltimateXR переигрывает у клиентов. Раньше сигнал жил только на сервере, и мёртвый игрок не узнавал о своей смерти вовремя (остаток NET-04). `Die()` остался серверной половиной: режим наблюдателя, `RpcOnDied`, оповещение игрового режима. |
| `PlayerGrabManager` | `Player/PlayerGrabManager.cs` | Разрешения на захват: ставит каждому `UxrGrabber` игрока свой `CanGrabDelegate`. Мёртвый игрок не берёт ничего; остальные правила — в `TwoHandGrabPolicy`. На смерти отпускает всё из рук. |
| `TwoHandGrabPolicy` | `Player/TwoHandGrabPolicy.cs` | Хват двумя руками: точка, которую держит другая рука, уступает достижимой свободной точке того же предмета, иначе вторая рука перехватывает оружие вместо поддержки ([known-issues, Issue 13](UltimateXR/known-issues.md)). Нет свободных точек рядом — передача из руки в руку работает. Тест — `TwoHandGrabPolicyTests`. |
| `GrabRules` | `Player/GrabRules.cs` | Все правила «можно ли этой руке взять эту точку» в одном месте: `GrabOnlyWhenParentHeld`, затем `TwoHandGrabPolicy`. Их ставит в `CanGrabDelegate` `PlayerGrabManager`; по делегату UltimateXR решает и подсветку при приближении руки. Новое правило захвата добавляется сюда, а не в менеджер. |
| `VrCalibrationController` | `Player/VrCalibrationController.cs` | Калибровка позиции аватара относительно физической арены. |
| `AvatarManager` | `Player/Avatars/AvatarManager.cs` | Создание, горячая замена и уничтожение физических аватаров на сервере. `SpawnAvatar` — первичный спавн, `ChangeAvatar` — пересоздание под новую команду/скин и **после смены карты** (`GameNetworkManager.OnServerReady`). Где создавать — спрашивает у `AvatarSpawnPointResolver`, что создавать — у `AvatarSpawnStrategy`. Порядок источников позиции в `SpawnAvatar`: снимок сессии, но только на **своей** карте (`SessionSnapshot.CanRestorePlaceOn`) → место, заданное калибровкой → зона команды. Ветка «позиция из сообщения подключения» убрана как находка CAL-02. |
| `AvatarSpawnPointResolver` | `Player/Avatars/AvatarSpawnPointResolver.cs` | Точка спавна аватара (WPN-03, CAL-01): **место, заданное калибровкой** → `TeamSpawnZone` своей команды → `NetworkStartPosition` из Mirror → начало координат с предупреждением. Возвращает `AvatarSpawnPoint` — позицию, поворот и источник, чтобы строка в логе отвечала на «почему игрок здесь». Первая ветка спрашивается, только когда вызывающий передал сессию, то есть после смены карты (T-30). Разбор трёх случаев `ChangeAvatar` — [session-architecture.md](session-architecture.md#где-создаётся-аватар-находка-wpn-03). |
| `CalibratedSpawnRegistry` | `Player/Avatars/CalibratedSpawnRegistry.cs` | Серверная память о месте **откалиброванного** игрока (CAL-01, T-30). Подписан на `MapManager.MapLoadStarted` и снимает позы откалиброванных игроков, пока старая сцена ещё жива, в системе координат её якорей; на новой карте пересчитывает их через якоря новой сцены. Клиент присылает один бит `PlayerSession.IsCalibrated` — саму позу сервер и так видит через `NetworkTransform` аватара. Второй источник мест — путь подключения (CAL-02): `PlayersManager.HandlePlayerConnect` кладёт сюда позу из `GamePlayerConnectMessage`, потому что снять её самому серверу не по чему — клиент был на другой карте. Гейт «применять или нет» один на оба источника и живёт в `TryResolve`. |

---

### Взаимодействие — `Assets/Scripts/Interaction/`

| Класс | Файл | Описание |
|---|---|---|
| `UxrMagazinePocket` | `Interaction/UxrMagazinePocket.cs` | Карман на несколько магазинов поверх одного `UxrGrabbableObjectAnchor`. |
| `AnchoredItemCollisionIgnore` | `Interaction/AnchoredItemCollisionIgnore.cs` | Отключает столкновения корпуса оружия с тем, что вставлено в его якоря. Выпуклый коллайдер корпуса охватывает шахту магазина, и kinematic-магазин выталкивал брошенное оружие под пол (PHY-01). Вставленный предмет ищется по иерархии (прямой потомок якоря с `Rigidbody`), сверка — каждый `FixedUpdate`. Обязателен на оружии с якорем магазина. |
| `OutOfWorldGuard` | `Interaction/OutOfWorldGuard.cs` | Kill-zone: предмет ниже `_killY` (−50) удаляет сервер через `NetworkServer.Destroy`, вне сессии — локально. Предмет без собственного `netId` (встроенный магазин `Machinegun`/`Shotgun`/`M16`) каждая машина удаляет сама: сетевое удаление до клиентов не дойдёт. Выбор ветки — чистая функция `Decide`. Останавливает вечное падение и вечную рассылку `UpdateRigidbody` (PHY-01). Предмет в руке не трогает. Обязателен на всех динамических префабах оружия и магазинов. |
| `MainGripAimLock` | `Weapons/MainGripAimLock.cs` | Поддерживающая рука не поворачивает оружие: пока держит только основная точка, запоминает позу относительно основной руки, при хвате двумя руками возвращает её в `ConstraintsApplied` — после доворота SDK ко второй руке, до `KeepGripsInPlace`. Ставится на оружие, где вторая рука только поддерживает (пистолет `Gun_real`); на винтовке не нужен — там цевьё и должно наводить ствол. Тест — `GunTwoHandAimTests` ([known-issues, Issue 14](UltimateXR/known-issues.md)). |
| `AnchorPlaceSound` | `Interaction/AnchorPlaceSound.cs` | Звук вставки в якорь (магазин в оружие, в карман, оружие за спину) по событию `Placed`, только если вложила рука. Заменяет `Play On Awake` на объекте `Activate On Placed`, который SDK включает и при спавне (AUD-01, [known-issues #10](UltimateXR/known-issues.md)). |
| `GrabOnlyWhenParentHeld` | `Interaction/GrabOnlyWhenParentHeld.cs` | Деталь предмета (затвор, помпа, чека) берётся, только когда предмет-родитель уже в руке у того же игрока; иначе к лежащему пистолету подносишь руку — активны и рукоять, и затвор. Галочка `Require Parent Held` — снять, если деталь должна браться всегда. Родитель — ближайший `UxrGrabbableObject` выше по иерархии (`GrabbableParent` SDK у затвора пуст). Стоит на каждой детали оружия, проверка — `WeaponPartGrabTests`. |

---

### Физическое пространство — `Assets/Scripts/PhysicalSpaceUtils/`

| Класс | Файл | Описание |
|---|---|---|
| `PhysicalSpaceSyncManager` | `PhysicalSpaceUtils/PhysicalSpaceSyncManager.cs` | Совмещение физической комнаты игрока с виртуальной ареной. Две процедуры: по якорям (две точки на полу) и по росту (пол, затем масштаб). `IsCalibrated` — факт состоявшейся калибровки по якорям, `IsCalibrating` — идёт ли процесс прямо сейчас. Живёт на `PersistentRoot`, переживает смену карт. **Калибровочный сдвиг накладывается один раз, в момент калибровки:** повторное наложение на каждый новый аватар увозило бы игрока ещё раз на ту же дельту (T-30). Копит место аватара этой машины **в координатах якорей** текущей сцены (`RecordLocalAvatarPlace` → `TryGetSavedAvatarPlace`, кэш системы координат на дескриптор активной сцены) — его игрок и приносит с собой в `GamePlayerConnectMessage`. Хранение мировой позы было находкой CAL-02: переводить её в момент отправки сообщения уже поздно, клиент к тому времени на карте сервера. |
| `PhysicalSpaceAnchor` | `PhysicalSpaceUtils/PhysicalSpaceAnchor.cs` | Точка на карте, которой в реальной комнате соответствует физическая метка. Нужны две, с `id` 0 и 1. Лежат внутри префаба арены `Environment.prefab`. |
| `PhysicalSpaceAnchorFrame` | `PhysicalSpaceUtils/PhysicalSpaceAnchorFrame.cs` | Система координат карты по паре якорей: начало — якорь `id=0`, ось `+Z` смотрит на `id=1`, крен и тангаж отброшены. Нужна потому, что **мировые координаты якорей у карт не совпадают**: обе карты собраны из одного префаба арены, но в `TestMap1` он повёрнут на 90° вокруг Y относительно `TestMap2` и `Lobby`. Место игрока, сохранённое в мировых координатах, при смене карты развернуло бы вместе с ареной; сохранённое относительно якорей — нет. |

---

### Карты — `Assets/Scripts/Maps/`

| Класс | Файл | Описание |
|---|---|---|
| `MapData` | `Maps/MapData.cs` | ScriptableObject с данными карты. |
| `MapRegistry` | `Maps/MapRegistry.cs` | ScriptableObject-список всех карт. Назначить в `AdminMenuController._mapRegistry`. |
| `TeamSpawnZone` | `Maps/TeamSpawnZone.cs` | Коллайдер зоны возрождения для команды. Проверяет присутствие игроков. Сессии сообщает только факт и **свою команду** (`ReportZoneState` → `PlayerSession.ServerEnterSpawnZone` / `ServerExitSpawnZone`); что это значит для конкретного игрока, решает сессия — там лежит его команда. Прежде зона решала сама и писала «в зоне» любому вошедшему, включая забежавшего в чужую базу (находка RDY-04). |
| `SpawnZoneCreator` | `Editor/SpawnZoneCreator.cs` | Опция в меню GameObject для авто-создания префаба зоны спавна на сцене. |
| `NetworkAssetIdNormalizer` | `Editor/VR_Battlegrounds/VersionControl/NetworkAssetIdNormalizer.cs` | Держит на диске канонический `NetworkIdentity._assetId` (хеш GUID префаба), чтобы поле не скакало в диффах. Постпроцессор импорта: после импорта сетевого префаба с другим числом правит **одну строку в файле** — не сохраняет префаб через Unity, иначе на диск ушли бы и перевыданные UltimateXR id. Работает только с `Assets/Prefabs` (ThirdParty не трогает), только в основном редакторе вне Play Mode. Ручной прогон — `Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids`. |
| `UxrUniqueIdPersister` | `Editor/VR_Battlegrounds/VersionControl/UxrUniqueIdPersister.cs` | Постпроцессор импорта: после импорта префаба из `Assets/Prefabs` ставит UXR-компонентам верные `__isInPrefab`/`__prefabGuid` правкой строк в файле (id не меняет), а флаги, унаследованные от базы, — сохранением через Unity. Неверные флаги приносит Apply to Prefab со сцены; с ними редактор перевыдаёт `_uxrUniqueId` на каждом реимпорте, и хост и клиент MPPM расходятся (MPPM-02). Только основной редактор вне Play Mode. Ручной прогон — `Tools/VR Battlegrounds/VersionControl/Persist UltimateXR Unique Ids`. |
| `GameTagsTool` | `Editor/VR_Battlegrounds/Gameplay/GameTagsTool.cs` | `Tools/VR Battlegrounds/Gameplay/Apply Game Tags`: заводит теги в TagManager и расставляет их по `GameTagRules` во всех префабах `Assets/Prefabs` и сценах `Assets/Scenes`. Идемпотентен; вложенные префабы обрабатывает раньше внешних, чтобы не плодить override'ы; чужие теги (`MainCamera`, `EditorOnly`) не трогает; сцену с несохранёнными правками пропускает. Из кода — `GameTagsTool.Run()`. |

**Поля `MapData`:**

| Поле | Тип | Описание |
|---|---|---|
| `displayName` | `string` | Название в меню |
| `sceneName` | `string` | Имя сцены Unity (совпадает с именем в Build Settings) |
| `preview` | `Sprite` | Превью для UI |
| `description` | `string` | Краткое описание |
| `arenaSizeMeters` | `Vector2` | Физические размеры в метрах (X = ширина, Z = глубина) |
| `maxPlayers` | `int` | Максимум игроков |

**Как добавить новую карту:**
1. Создать сцену: `Assets/Scenes/Maps/Arena_NewMap.unity`
2. Добавить в Build Settings: `File → Build Settings → Add Open Scenes`
3. Создать `MapData` asset: ПКМ в Project → `Create → VrBattlegrounds → Map Data`
   - `sceneName` = имя файла сцены без расширения
4. Открыть `Assets/Data/Maps/MapRegistry.asset`, добавить в массив `maps[]`

---

### Ядро — `Assets/Scripts/Core/`

| Класс | Файл | Описание |
|---|---|---|
| `GameLog` | `Core/GameLog.cs` | Единственная точка логирования. Категорию знает сам логгер: `GameLog.Match.Info("...")`, `GameLog.Player.Verbose("...", this)`. Каналы `Network`, `Player`, `Match`, `Debug`, `WeaponSystem`, `UI`, `PhysicalSpace`, `Arsenal` — один в один поля `GameSettings`. `GameLog.Error(...)` пишется всегда, независимо от уровня. Никогда не использовать `Debug.Log` напрямую. |
| `GameTags` | `Core/GameTags.cs` | Константы тегов главной категории: `Player` (встроенный), `Weapon`, `Magazine`, `SpawnZone`, `Arsenal`, `Environment`. Один тег на объект — отвечает «что это в первую очередь»; признаки (метательное, берётся в руку) читаются по компонентам. Тег висит на корне сущности, `Environment` — прямо на коллайдерах геометрии. Строками теги в коде не писать. |
| `GameTagRules` | `Core/GameTagRules.cs` | Единственное правило «компоненты → тег»: `UxrAvatar` → `Player`, `UxrFirearmWeapon`/`UxrGrenadeWeapon` → `Weapon`, `UxrFirearmMag` → `Magazine`, `TeamSpawnZone` → `SpawnZone`, `ArsenalWallController`/`ArsenalSlotController` → `Arsenal`; не-trigger коллайдер вне `Rigidbody`/аватара/грабаблов/якорей/спавн-зон/`Canvas` → `Environment`. По нему работают и инструмент разметки, и `GameTagsTests`. |
| `GameLogChannel` | `Core/GameLog.cs` | Канал одной категории (`readonly struct`). Уровень тянет из `GameSettings` **в момент вызова**, поэтому правка `GameSettings.asset` в инспекторе действует без перезапуска. `IsEnabled(level)` — для случаев, где дорога сама сборка строки. |
| `GameSettings` | `Core/GameSettings.cs` | ScriptableObject с уровнями логирования по категориям. Без ассета в `Resources/` отдаёт экземпляр со значениями по умолчанию, а не `null`. |
| `LogLevel` | `Core/LogLevel.cs` | Enum: `None / Errors / Warnings / Info / Verbose`. |
| `TeamData` | `Core/TeamData.cs` | ScriptableObject с данными команды. По сети синхронизируется только `int teamIndex`. |
| `TeamRegistry` | `Core/TeamRegistry.cs` | Реестр команд. |
| `AppRoleManager` | `Core/AppRoleManager.cs` | Хранит текущую `DeviceRole` (VR/PC/Server) и `NetworkRole` (Host/Client), используется для сборки UI и логики. |
| `PersistentRoot` | `Managers/PersistentRoot.cs` | Глобальный DontDestroyOnLoad узел и точка входа инициализации: `Awake` объявляет состав менеджеров, `Start` его проверяет. Сам состав — в `ManagerBootstrap`. Он же отсеивает дубликат ветки, когда сцена `Offline` загружается второй раз, — и делает это **в `Start`**, чтобы компонент, уходящий из ветки своим ходом (`Mirror.NetworkManager`), успел уйти живым, а не выключенным (NET-20). |
| `ManagerOrder` | `Managers/ManagerOrder.cs` | **Единственное место, где записан порядок инициализации менеджеров** (T-17). Константы отсюда подставляются в `[DefaultExecutionOrder]` на самих менеджерах. Добавляешь менеджер — сначала строка здесь, потом атрибут на классе. |
| `ManagerBootstrap` | `Managers/ManagerBootstrap.cs` | Объявленный состав постоянных менеджеров и проверка, что он поднялся. Поднимает событие `Ready` (опоздавший подписчик получает сигнал сразу) — подписка вместо опроса «а появился ли сосед». Менеджеры не создаёт: они лежат готовыми на префабе `--- MANAGERS ---`. Таблица времён жизни — [`session-architecture.md`](session-architecture.md#времена-жизни-менеджеров-t-17). |

**Категории логов в `GameSettings`:**

| Свойство | Категория | Затрагивает |
|---|---|---|
| `LogLevelNetwork` | Сеть | `GameNetworkManager`, `MapManager`, `GameNetworkDiscovery` |
| `LogLevelPlayer` | Игрок | `PlayerController` |
| `LogLevelMatch` | Матч | `MatchManager`, `SetManager`, `RoundManager` |
| `LogLevelUI` | Интерфейс | `MenuController`, `LocalMenuManager`, Кнопки, HUD |
| `LogLevelWeaponSystem` | Weapon System | Механики оружия (`UxrFirearmWeapon`, `AutomaticWeaponSlideFeedback`) |
| `LogLevelDebug` | Отладка | `DebugOrchestrator` (по умолчанию `Verbose`) |

---

### UI — `Assets/Scripts/UI/`

| Класс | Файл | Описание |
|---|---|---|
| `MenuController` | `UI/Menu/MenuController.cs` | Глобальный MVC контроллер меню, управляет открытием, поворотом. |
| `LocalMenuManager` | `UI/Menu/LocalMenuManager.cs` | Запрашивает префаб меню в зависимости от контекста сцены и спавнит его. |
| `MenuScreen` | `UI/Menu/MenuScreen.cs` | Базовый класс для экранов планшета (SessionSetup, Calibration и т.д.) |
| `MenuPrefabRegistry` | `UI/Menu/MenuPrefabRegistry.cs` | Дерево префабов (Role -> Context -> GameMode), хранящее ссылки на GameObject'ы планшетов. |
| `HUDWidget_GameNotification` | `UI/HUD/HUDWidget_GameNotification.cs` | Слушает семантические `GameMode` события и локализует уведомления. |
| `PlayerHUDManager` | `UI/HUD/PlayerHUDManager.cs` | Спавнит и управляет дочерними виджетами HUD привязанными к голове игрока. |

**Система Уведомлений (Event-Driven Notifications):**
- Разовые уведомления (`OnRoundEndedLocal`, `OnSetStartedLocal`, `OnRoundStartedLocal`) игровые режимы шлют через `[ClientRpc]`: их не нужно знать задним числом.
- Режимы не знают про UI и не генерируют текст ("Победили Синие").
- Автономные UI-виджеты (как `HUDWidget_GameNotification`) подписываются на эти события, сами формируют финальную строку (с учетом имен команд) и отображают её.

**Что нужно знать вновь подключившемуся — состояние, а не событие.** Фаза раунда
(`EliminationMode.OnRoundStateChangedLocal`) раздаётся не из `ClientRpc`, а из хука
`[SyncVar] _roundState` — см. [«Фаза раунда»](gameplay.md#фаза-раунда--состояние-а-не-событие).
Правило общее: `ClientRpc` годится для «раунд начался» со звуком, но не для того,
что определяет текущее состояние мира.

---

### Отладка — `Assets/Scripts/Debug/`

| Класс | Файл | Описание |
|---|---|---|
| `DebugOrchestrator` | `Debug/DebugOrchestrator.cs` | Автостарт при Play: назначает команду, грузит карту, стартует матч. Только вызовы публичных API. |
| `DebugBootstrapConfig` | `Debug/DebugBootstrapConfig.cs` | ScriptableObject с параметрами `DebugOrchestrator`. |
| `PlayModeStartFromOffline` | `Editor/PlayModeStartFromOffline.cs` | Скрипт редактора. Автоматически перехватывает Play Mode, заставляя Unity стартовать с Offline-сцены и прокидывая текущую сцену в конфиг. |

**Харнесс e2e на двух процессах (ярус C) — `Assets/Scripts/Debug/E2E/`**

Как запускать и на что смотреть — [`testing.md`](testing.md#ярус-c--два-процесса-настоящий-e2e).
Весь код закрыт `#if !VRBG_NO_E2E` и без аргумента `-e2eScenario` не поднимается.

| Класс | Файл | Описание |
|---|---|---|
| `E2ERunner` | `Debug/E2E/E2ERunner.cs` | Точка входа. Поднимается через `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, если задан `-e2eScenario`. Прокручивает сценарий вручную, чтобы поймать исключение, и гарантирует запись файла вердикта при любом исходе. Реестр сценариев — метод `All()`. |
| `E2EContext` | `Debug/E2E/E2EContext.cs` | Разбор аргументов командной строки: `-e2eScenario`, `-e2eRole`, `-e2eResult`, `-e2eTimeout`, `-e2eMap`, `-e2eClients`, `-e2eServerAddress`, `-e2eDeviceToken`. |
| `E2EResult`, `E2ECheck` | `Debug/E2E/E2EResult.cs` | Машиночитаемый вердикт. Проверки объявляются заранее (`Declare`), поэтому недошедшие несут причину обрыва. JSON пишется чистым ASCII. |
| `IE2EScenario` | `Debug/E2E/IE2EScenario.cs` | Контракт сценария: имя для CLI и корутина `Run`. |
| `E2EWait`, `E2EWaitOutcome` | `Debug/E2E/E2EWait.cs` | Явное ожидание условия. `Until(...)` кладёт в `E2EWaitOutcome` ответ на три вопроса — что ждали, что получили, сколько ждали, — и сверх условия принимает `observe` (снимок состояния мира в момент выхода) и `abortIf` (причина, по которой ждать дальше бессмысленно: например, связь с сервером пропала). Второе отличает «условие не наступило» от «наблюдать стало нечем» — на этой разнице обожглась TEST-01. `Hold(seconds)` — выдержка там, где условия нет. |
| `DedicatedServerArsenalScenario` | `Debug/E2E/Scenarios/DedicatedServerArsenalScenario.cs` | Сценарий `dedicated-server-arsenal` — находки NET-06, NET-13, NET-07, RDY-01. Роль сервера гонит матч, объявляет готовность и выносит вердикт; клиенты занимают команды, служат контролем к NET-06 и наблюдают общую стену. Проверка стены двухтактная (T-29): сервер объявляет готовность за **одного** игрока — стена обязана остаться открытой у всех (RDY-01); затем за всех — закрытие обязано доехать до обоих клиентов (NET-07). Момент «готов ровно один» клиенты ловят по реплицированному `PlayerSession.ReadyState`, а не по договорённости о времени. Общий объект выбирается по наименьшему `netId`. |
| `AvatarSwapDeathReplicationScenario` | `Debug/E2E/Scenarios/AvatarSwapDeathReplicationScenario.cs` | Сценарий `avatar-swap-death-replication` — находки NET-02 и NET-01. Сервер убивает одного игрока дважды: до пересоздания чужого аватара (контроль) и после (сама находка). Смерть здесь — `Life = 0`, выставленный свойством: сценарию нужен голый факт доставки по каналу состояния, а не путь `UxrActor.Died` (его проверяет `player-death-signal`). Вердикт выносит сервер, клиенты зеркалят наблюдение через `PlayerSession`. |
| `CalibrationScaleReplicationScenario` | `Debug/E2E/Scenarios/CalibrationScaleReplicationScenario.cs` | Сценарий `calibration-scale-replication` — находка VR-01 (T-14). Два клиента объявляют разные пропорции (0.80 и 1.30); вердикт с двух сторон — сервер сверяет свои экземпляры аватаров (он считает попадания), каждый клиент сверяет **чужой** аватар. Саму калибровку не проверяет: она требует шлема, клиенты шлют готовый результат. |
| `SessionRecoveryOnReconnectScenario` | `Debug/E2E/Scenarios/SessionRecoveryOnReconnectScenario.cs` | Сценарий `session-recovery-on-reconnect` — находка ARCH-01, зелёный. Одному клиенту раздаётся состояние (имя, команда, скин, счёт, здоровье, позиция), сервер рвёт соединение, клиент возвращается тем же `deviceToken`. Роль клиента целиком служит контролем «переподключение работает», вердикт о восстановлении выносит сервер. Запускать с `-Clients 1`. Обход NET-20 снят 2026-08-21 — сценарий зелёный без него и служит сторожем переподключения. |
| `ArsenalItemGrabScenario` | `Debug/E2E/Scenarios/ArsenalItemGrabScenario.cs` | Сценарий `arsenal-item-grab` — находки NET-16 и NET-17, зелёный после выравнивания `UniqueId`. Клиент берёт оружие настоящим `UxrGrabManager.GrabObject`, сервер проверяет, ушёл ли предмет из его слота. Матч не запускается: стены открывает сам сценарий с сервера. Общий предмет выбирается по наименьшему `netId` среди заспавненных в рантайме (`sceneId == 0`) хватаемых объектов, поэтому проверка не зависит от привязки предмет→слот у клиента (NET-23). Печатает `UniqueId` предмета с обеих сторон — их расхождение и есть корень NET-16. Запускать с `-Clients 1`. |
| `PlayerDeathSignalScenario` | `Debug/E2E/Scenarios/PlayerDeathSignalScenario.cs` | Сценарий `player-death-signal` — остаток находки NET-04. Сервер бьёт аватар клиента настоящим уроном (`UxrActor.ReceiveDamage`), клиент проверяет две вещи подряд: доехало ли состояние (`IsAlive`) и пришло ли уведомление (`PlayerController.PlayerDied`). Контроль зелёный при красном уведомлении — находка в чистом виде. В отличие от `avatar-swap-death-replication`, идёт **через** `UxrActor.Died`: тот ходит мимо намеренно. Запускать с `-Clients 1`. |
| `ShotPipelineBudgetScenario` | `Debug/E2E/Scenarios/ShotPipelineBudgetScenario.cs` | Сценарий `shot-pipeline-budget` — **измерительный**, для решений T-23 и T-24 (см. [`combat-networking.md`](combat-networking.md)). Сервер стреляет из оружия арсенала (`UxrProjectileSource.Shoot`), обе стороны считают живые снаряды по корню сцены и снимают `rtt` / `NetworkClient.bufferTime`. Единственная фальсифицируемая проверка — «у клиента появились свои снаряды»: её краснота опровергла бы посылку T-24 о том, что каждая машина симулирует все пули. Порогов «хорошо/плохо» внутри нет намеренно. Запускать с `-Clients 1`. |
| `RoundReadinessMatchScenario` | `Debug/E2E/Scenarios/RoundReadinessMatchScenario.cs` | Сценарий `round-readiness-match` — задача T-29, дефект NET-07/RDY-01 и последний непокрытый пункт T-09. **Единственный сценарий, прогоняющий раунд целиком:** `Equipment → Countdown → Combat →` настоящая смерть (`UxrActor.ReceiveDamage`) `→ Resolution → Scoreboard →` следующий раунд. Готовность выставляет сервер (`PlayerSession.ServerSetReady`) — до T-29 ярус C упирался в фазу закупки, из которой без рук не выйти. Проверяет четыре вещи: RDY-01 (готов один из двух — общая стена открыта и на сервере, и у обоих клиентов), сквозной раунд, длительности `Resolution` (3 с) и `Scoreboard` (5 с) **по настоящим часам**, а не по тикам EditMode, и RDY-04 (игрок, уведённый в базу противника, не считается стоящим в своей зоне; контрольная пара — возвращают на свой спавн и требуют признак обратно). Шаг RDY-04 стоит до старта матча: в фазе `Equipment` идёт предел ожидания готовности в 45 с. Запускать с `-Clients 2`. |
| `WeaponHitDamageScenario` | `Debug/E2E/Scenarios/WeaponHitDamageScenario.cs` | Сценарий `weapon-hit-damage` — сторож находки [T-28](tasks/T-28-weapon-prefab-config.md). Сервер наводит настоящее оружие арсенала на живого игрока и стреляет через `UxrFirearmWeapon.TryToShootRound` — то есть всем трактом целиком, а не прямым `UxrActor.ReceiveDamage`. Проверка «попадание отняло здоровье» была **красной до правки масок**; с 2026-08-23 маски исправлены (T-28) и она зелёная. Рядом с ней стоит контрольный опыт: та же наводка, тот же выстрел, но маска слоёв на несколько секунд подменена на эталонную `Default\|Ground` — он зелёный, и разница между двумя строками и есть доказательство, что причина ровно в `CollisionLayerMask`. Попутно печатает обзор скинов (у каких аватаров вообще есть непроходной коллайдер) и настройки выстрела всех стволов реестра. Запускать с `-Clients 1`. |
| `AvatarSpawnPointScenario` | `Debug/E2E/Scenarios/AvatarSpawnPointScenario.cs` | Сценарий `avatar-spawn-point` — сторож находки [WPN-03](audit/network-audit-2026-08.md). Три случая `ChangeAvatar` за один прогон: после смены карты аватар обязан оказаться в зоне спавна своей команды (до правки — в `(0, 0, 0)`); смена скина не имеет права двигать игрока (контроль — игрока предварительно уводят из зоны на 6 м, иначе «остался» и «переехал в зону» неразличимы); смена команды обязана перенести в базу **новой** стороны. Отдельная проверка-предусловие требует, чтобы зоны были разведены с началом координат: иначе сломанный и исправленный код неотличимы, и прогон недействителен. `DebugOrchestrator` гасится первой строкой — именно он маскирует находку. Запускать с `-Clients 1`. |
| `CalibratedPositionPersistsScenario` | `Debug/E2E/Scenarios/CalibratedPositionPersistsScenario.cs` | Сценарий `calibrated-position-persists` — сторож находки CAL-01 (T-30). Обе ветки правила в одном прогоне на паре `TestMap1` → `TestMap2`: клиент-1 объявляет калибровку и после смены карты обязан остаться на своём месте в арене, клиент-2 не объявляет и обязан оказаться в зоне своей команды. Эталон вычисляется из зон спавна, а не из якорей, и **сдвинут от центра арены на 4 м**: центр — пивот её поворота, он у карт общий и в мировых координатах тоже. Запускать с `-Clients 2`. |
| `ConnectPlaceAcrossMapScenario` | `Debug/E2E/Scenarios/ConnectPlaceAcrossMapScenario.cs` | Сценарий `connect-place-across-map` — сторож находки CAL-02. То же правило, но на **пути подключения**: два круга «пожил на карте → ушёл → сервер сменил карту → вернулся» на одном игроке, первый без калибровки (обязан вернуться в зону команды), второй с ней (обязан вернуться на своё место в арене). Карта меняется **после** разрыва — иначе снимок отключённой сессии снимется уже на новой карте и путь не воспроизведётся. Возврат — прямым `StartClient` по рецепту `session-recovery-on-reconnect`. Запускать с `-Clients 1`. |
| `E2EPlayerBuilder` | `Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs` | Сборка плеера под Windows в `Build/e2e/`. Меню `Tools/VR Battlegrounds/Debug/Собрать e2e-плеер (Windows)`, для CI — `RunBatch`. |
| `Run-E2E.ps1` | `Tools/e2e/Run-E2E.ps1` | Дирижёр: добивает осиротевшие процессы, проверяет свежесть билда, поднимает сервер и клиентов, ждёт вердикты, гасит процессы, сводит отчёт. Хранить **в UTF-8 с BOM**. |

**Поля `DebugBootstrapConfig`:**

| Поле | Тип | По умолчанию | Описание |
|---|---|---|---|
| `enabled` | `bool` | `true` | Включить быструю инициализацию |
| `autoTeam` | `TeamData` | `null` | Команда локального игрока |
| `autoStartMatch` | `bool` | `true` | Автостарт при достаточном числе игроков |
| `minPlayersToAutoStart` | `int` | `1` | Минимум игроков для автостарта |
| `autoLoadMapScene` | `string` | `""` | Имя сцены для автозагрузки (пусто = не грузить) |

**Как подключить `DebugOrchestrator` (один раз):**
1. Создать пустой GameObject в OfflineScene, назвать `DebugOrchestrator`
2. Добавить компонент `DebugOrchestrator`
3. Создать asset: `Create → VrBattlegrounds → Debug Bootstrap Config`
4. Назначить asset в поле `Config`

**Последовательность событий при старте:**
```
Play → OfflineScene → NetworkManager поднимает хост → Lobby
→ ServerSceneChanged → DebugOrchestrator.TryAutoLoadMap()
  → MapManager.LoadMap(autoLoadMapScene)
  → Карта загружается
→ ClientConnected / LocalAvatarChanged → сигнал менеджеру о готовности аватара
→ Начинается процесс **Precaching** в `UxrManager` (инстанцирование объектов `IUxrPrecacheable`)
→ PlayerConnected → если `GameMode.CanStartGameplay()` → `GameplayManager.StartGameplay()`
```

---

### Editor-автоматизация аватаров — `Assets/Editor/VR_Battlegrounds/Avatars/`

| Класс / файл | Назначение |
|---|---|
| `CustomAvatarPipelineMenu` | Меню `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/*`: запускает подготовку выбранного FBX через Blender CLI и существующий UXR setup wizard. |
| `BlenderScripts/*.py` | Подготовка FBX вне Unity: ампутация родных кистей, добавление wrist torsion bones, добавление `LeftEye` / `RightEye`. |
| `ApplyEyeMapping` | Прописывает `LeftEye` / `RightEye` в Humanoid mapping выбранного FBX после Blender-экспорта. |
| `CoreAvatarSetup`, `HandsIntegrationSetup`, `ControllerAndCameraSetup`, `FinalizeRigMappingSetup`, `CreatePrefabSetup`, `HandPosesSetup` | Атомарные шаги `UXR Setup Wizard`, которые можно запускать вручную или через `CustomAvatarPipelineMenu`. |

Ручной быстрый путь: выделить FBX asset в Project window и запустить `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/Run Full Selected FBX Pipeline`.

---

### Editor-утилиты UI — `Assets/Editor/VR_Battlegrounds/UI/`

| Класс | Назначение |
|---|---|
| `ProjectFontTool` | Шрифт проекта (Roboto Condensed). `Tools/VR Battlegrounds/UI/Шрифт — пересобрать атласы`: статические SDF-ассеты из `.ttf` с набором `CharacterSet`, Bold в таблице начертаний, шрифт TMP по умолчанию и глобальный fallback. `…/Шрифт — применить к UI-префабам`: переназначает шрифт всем TMP-текстам в `Assets/Prefabs/UI`. Подробно — [`ui-fonts.md`](ui-fonts.md). |

---

### Editor-утилиты арсенала — `Assets/Editor/VR_Battlegrounds/Arsenal/`

| Класс | Назначение |
|---|---|
| `ArsenalSlotPreview` | Превью содержимого слотов в редакторе: `__ItemPreview__` на якоре предмета и `__MagPreview__` на якоре магазина. Объекты `DontSave` — в сцену и префаб не пишутся, поэтому сервис сам пересоздаёт превью у всех слотов загруженных сцен и открытого префаба после перекомпиляции, выхода из Play Mode, открытия сцены и префаба; перед входом в Play Mode удаляет. Повторный `Ensure` существующее превью не трогает. |
| `ArsenalSlotEditorBase` / `FirearmSlotControllerEditor` | Инспекторы слотов: выбор `WeaponInfo` из `WeaponRegistry`, подгонка смещений по превью и сохранение в ассет. Превью при смене предмета пересоздают через `ArsenalSlotPreview`, своего кода создания не держат. |

---

### Тесты — `Assets/Tests/EditMode/`

Сборка `VrBattlegrounds.Tests.EditMode` (`includePlatforms: ["Editor"]`, поэтому в билд
под Quest не попадает). Прогон: `run_tests(mode="EditMode", assembly_names=[...])`.

| Класс / файл | Назначение |
|---|---|
| `Maps/ArenaGeometryCollisionTests` | Каждый меш из моделей арен (`Assets/Models/Arenas/**.fbx`), стоящий в префабах `Assets/Prefabs/Arenas`, имеет не-trigger коллайдер. Иначе препятствие видно, но пули и брошенное оружие проходят сквозь него, а тег `Environment` на него не встаёт. |
| `Prefabs/NetworkAssetIdOnDiskTests` | У каждого сетевого префаба `Assets/Prefabs` на диске канонический `_assetId` (`NetworkIdentity.AssetGuidToUint` от своего GUID) — у обычного строкой компонента, у варианта переопределением. Ловит, если отвалился `NetworkAssetIdNormalizer`. |
| `Prefabs/UxrUniqueIdOnDiskTests` | У UXR-компонентов префабов `Assets/Prefabs` верные флаги `__isInPrefab`/`__prefabGuid` и id в памяти есть на диске (в файле префаба или его баз). Ловит MPPM-02 и отвалившийся `UxrUniqueIdPersister`. |
| `Prefabs/UxrUniqueIdStabilityTests` | Патч SDK 10: префаб-ассет с неверными флагами сохраняет `_uxrUniqueId` после `OnValidate`, а экземпляр в сцене получает свой. |
| `Prefabs/GameTagsTests` | Теги `GameTags` заведены в TagManager; у каждого объекта всех префабов `Assets/Prefabs` и сцен `Assets/Scenes` тег совпадает с `GameTagRules` (сцены читаются через `OpenPreviewScene`, открытое в редакторе не трогается); плюс само правило на синтетических объектах. Починка расхождений — `Apply Game Tags`. |
| `SetManagerScoringTests` | Подсчёт победителя сета в `SetManager` — чистая логика, без сети. Раунды проигрываются прокруткой `SetManager.Tick`. Команды синтетические, с индексами, которых нет в `TeamRegistry`: так проверяется, что счёт идёт по переданному составу, а не по глобальному реестру (T-08). |
| `RoundFlowSupport` | Общая оснастка тестов матча: `StubPlayerRoster` (подставной реестр игроков) и `RoundFlowDriver` (прокрутка фиксированным шагом 0.25 с с записью наблюдённых фаз). |
| `Network/MirrorTestHarness` | Базовый класс сетевых тестов: поднимает Mirror сервером **без сокета** (ярус A) и, по требованию, локального клиента (ярус B). Сбрасывает синглтоны проекта между тестами. Рецепт и границы — [`testing.md`](testing.md#как-тестировать-сетевую-логику). |
| `Network/EliminationModeServerTests` | Серверная логика режима: заполнение `TeamStates`, одно очко за выигранный сет (T-02), запуск матча без `PlayersManager` и остановка при пустом реестре (NET-18). Первый тест — проверка самого харнесса. |
| `Network/RespawnSubscriptionTests` | Жизненный цикл отложенного респавна (T-10, MATCH-05): за три раунда обработчики на `TeamSpawnZone.PlayerEntered` не копятся, а подписка пропущенного раунда не срабатывает в бою следующего. Число подписчиков читается из поля события рефлексией — поднять field-like event снаружи нельзя. |
| `Network/RoundTimerNetworkTimeTests` | Таймеры фаз через `NetworkTime` (T-19, NET-09): тик внутри фазы не помечает объект грязным (при этом смена фазы — помечает, это контрольный тест), остаток отсчёта и боя считается от момента старта фазы, до боя показывается полная длительность, после боя остаток замирает. |
| `Network/RoundPhaseFlowTests` | Фазы раунда боевым путём `ServerTick → SetManager → RoundManager` (T-09, MATCH-02): все шесть фаз по порядку, длительность `Resolution` и `Scoreboard` в тиках, рост номера раунда после полного цикла (сторож MATCH-06), запрет заканчивать сет раньше экрана итогов. |
| `Network/HostClientHarnessTests` | Ярус B: локальный клиент поднялся, `SpawnMessage` доходит до `NetworkClient.spawned`. |
| `Network/PlayerSessionReplicationTests` | Репликация `PlayerSession` через настоящую сериализацию Mirror: `TeamIndex` и связь с аватаром доезжают до клиента, смена скина переключает связь, гонка спавнов чинится аватаром, `PlayerController.Session` кэшируется (T-11). |
| `Network/SessionRecoveryTests` | Снимок сессии при отключении: позиция, здоровье, флаг `NeedsPhysicalRestore` (T-04). Плюс карта и калибровка (CAL-02): снимок помнит, **на какой карте** снят, и `CanRestorePlaceOn` разрешает мировую позу только там же — на соседней карте та же точка означает другое место арены; `IsCalibrated` переживает отключение так же, как команда и скин; мёртвого на место гибели по-прежнему не возвращают. |
| `Network/NetworkStateRelayTests` | Канал состояния как объект сессии (T-12): подписка на хосте ровно одна (NET-03), отписка при остановке сервера, отсутствие статики в `UxrMirrorAvatar` и в релее, наличие релея и ненулевой `assetId` на `SessionContext.prefab`. Саму доставку блобов проверяет ярус C — в host-режиме она была бы ложно-зелёной. |
| `Network/CalibrationScaleReplicationTests` | Пропорции игрока (T-14, VR-01): границы значения на сервере и отказ от NaN, репликация `CalibrationScale` настоящей сериализацией Mirror, применение масштаба к **чужому** аватару (а не только к `UxrAvatar.LocalAvatar`), идемпотентность, переезд масштаба на пересозданный аватар. Три из шести были красными до правки. |
| `Network/MapLoadReadinessTests` | Условие готовности к смене карты (T-17): все четыре сочетания `isReady` × `identity` плюс проверка, что условие берётся по всем соединениям сразу. Заменяет собой пятисекундный таймаут в `MapManager`. |
| `Managers/ManagerInitOrderTests` | Порядок инициализации (T-17): у каждого менеджера есть `[DefaultExecutionOrder]` со значением из `ManagerOrder`, значения не совпадают, корень раньше всех, сеть позже тех, кого зовёт из колбэков, состав `ManagerBootstrap` состоит только из синглтонов. Стережёт пару «константа ↔ атрибут», которая расходится молча. |
| `Arsenal/ArsenalSlotOccupancyTests` | Занятость слота арсенала (T-15, NET-13): после сетевой выдачи слот занят и пополнения не просит, а когда предмет унесли или уничтожили — снова пустеет. Плюс блокировка: заблокированный слот действительно выключает захват предмета. |
| `Network/CalibrationHeightReplicationTests` | Смещение пола (VR-08): границы значения на сервере и отказ от NaN, репликация `CalibrationHeightOffset` настоящей сериализацией Mirror, сдвиг пивота камеры **чужого** аватара, неприкосновенность горизонтальных осей, переезд на пересозданный аватар без повторного накопления. Шесть тестов. |
| `Prefabs/PrefabCompositionTests` | Состав префабов как утверждение (ARCH-01, VR-07). Проверяет **размещение**, а не логику: все постоянные менеджеры лежат на `--- MANAGERS ---`, у каждого аватарного префаба на корне есть обязательные сетевые и XR-компоненты, пивот камеры — прямой потомок корня (контракт `UxrAvatar.InitializeCamera`), на камере есть `NetworkTransform` (это канал трекинга головы; смещение пола с VR-08 едет отдельно, `SyncVar`-ом сессии). Аватары **ищутся** по наличию `PlayerController` на корне в `Assets/Prefabs/Player`, а не перечисляются. Два теста красные — держат открытой VR-07 до правки префаба `Heavy_Soldier_Base_Avatar`. Плюс у каждого `NetworkBehaviour` в префабах проекта есть `NetworkIdentity` на себе или у родителя (NET-25). |
| `Network/NetworkSpawnableRegistrationTests` | Регистрация сетевых префабов (NET-22). Незарегистрированный в `NetworkManager.spawnPrefabs` префаб сервер спавнит у себя молча и успешно, а у клиента не появляется ничего — отказ односторонний и на хосте невидимый. Проверяется сериализованный список на префабе `--- MANAGERS ---`, а не рантайм: именно в этом виде он уезжает в билд. Два теста из трёх красные — держат открытой NET-22 до нажатия кнопки в инспекторе `WeaponRegistry`. |
| `Network/AvatarStateEventGateTests` | События сетевого аватара до `CombineUniqueId` придерживаются и уходят по `AvatarSpawned` (настоящий путь `UxrMirrorAvatar.InitializeNetworkAvatar`) по порядку и уже с выровненными id; выровненный аватар, несетевой аватар и объект вне аватара не задерживаются; очередь выбрасывается при снятии аватара и по сроку (NET-26). |
| `Network/NetworkUxrIdentityTests` | Идентичность объектов UltimateXR в рантайме (NET-16): один префаб с одним `netId` даёт один и тот же `UniqueId`, разные `netId` разводятся, выравнивание доходит до вложенных компонентов, повторный вызов ничего не сдвигает, созданный инстанс рождается выключенным и без авто-якоря. Второй процесс не нужен: свойство локальное — исходные идентификаторы приезжают из общего ассета, а `netId` раздаёт сервер. |
| `Network/RoundReadinessTests` | Готовность к раунду как явное состояние (T-29, RDY-01): не все готовы — фаза стоит; готовы все — идёт дальше; готовность можно отменить до отсчёта; выход из зоны её снимает, а возврат не возвращает; новый раунд сбрасывает её всем; предел ожидания срабатывает не раньше срока, а по срабатывании ведёт себя по выбранному правилу (`AutoReady` / `StartWithoutPending`); состав неготовых выкладывается состоянием и пустеет вне фазы закупки. Двенадцать тестов. |
| `Arsenal/ArsenalWallStateReplicationTests` | Состояние стены арсенала (T-15, NET-07, RDY-01): серверный канал фазы открывает и закрывает стену, закрытие доезжает до позднего клиента настоящей сериализацией Mirror, **жетон общую стену не закрывает** (T-29), локальный обработчик фазы заспавненную стену не трогает, а незаспавненная ведёт состояние сама. |
| `Network/UniqueComponentDebugInfoTests` | Ссылка на UXR-компонент в сетевом событии несёт отладочное описание (патч SDK 8): ненайденный компонент называет путь и тип предмета отправителя; без описания формат прежний (17 байт); null и строка не сдвигают следующие поля. |
| `Arsenal/DogTagHeldDisableTests` | Закрытая стена запрещает брать жетон флагом `IsGrabbable`, не выключая компонент: иначе у держащего жетон стирается запись о захвате и отпускание падает с «RuntimeManipulationInfo not found». Новая закупка снова разрешает захват. |
| `Arsenal/DogTagSetupTests` | Жетон стены арсенала стоит в своём якоре с первого кадра: `Start Anchor` и `Rigid Body Source` заданы, тело kinematic, правила PHY-01 — во всех префабах и сценах. У жетонов и якорей разных стен в сцене разные `UniqueId` (читаются сохранённые значения, автогенерация UltimateXR на время теста выключена). |
| `Arsenal/ArsenalAnimatorTests` | Анимация стены арсенала на настоящих `ArsenalWall.controller` и `Arsenal_Open.anim`: `Idle_Open`/`Idle_Closed` держат открытую и закрытую позы шторки и полки, повторная команда в ту же позу ничего не двигает и не оставляет залежавшегося триггера, разворот посреди анимации идёт с текущей позы. |
| `Player/AvatarSpawnPointResolverTests` | Выбор точки спавна (WPN-03): берётся зона **своей** команды, чужая зона точкой спавна не становится (иначе игрок появится в базе противника), команда без назначения зоны не спрашивает вовсе, карта без единой зоны даёт определённый запасной вариант, поворот зоны наследуется. Пять тестов. |
| `Player/PhysicalSpaceAnchorFrameTests` | Система координат карты по паре якорей (T-30, CAL-01): круговой перевод точки, начало в якоре `id=0`, второй якорь на оси `+Z`, **одна и та же точка арены даёт одни и те же координаты на повёрнутой на 90° карте** (иначе одна калибровка на сессию невозможна), перенос позиции и поворота между картами, отказ на слипшихся якорях, независимость направления от высоты якорей. Восемь тестов. |
| `Player/CalibratedSpawnRegistryTests` | Две ветки выбора точки спавна (T-30, CAL-01): откалиброванный возвращается на своё место, а не в зону; место переносится на повёрнутую карту вместе с ареной; неоткалиброванный идёт в зону **даже при готовом снимке** (признак — единственное, что разводит ветки); без снимка и на карте без якорей откалиброванный тоже идёт в зону; без переданной сессии ветка не спрашивается вовсе. Шесть тестов. |
| `Player/TwoHandGrabHarness` | Не тест — общая обвязка хвата двумя руками. `TwoHandGrabCases` перебирает пары «оружие из `WeaponInfo` × аватар из `AvatarRegistry`», у которых включены `Allow Multi Grab` и `First Grab Point Is Main` и есть свои позы для обеих точек; пути не называются. `TwoHandGrabHarness` поднимает настоящие префабы вне Play Mode (`Awake` рук и `UpdateManipulation` через рефлексию, аватар в `UpdateExternally` — иначе `Align To Controller` берёт поворот у чужой модели контроллера). `AssertManipulationLive` — сторож: оружие реально следует за рукой, иначе проверки поворота зеленеют ложно. |
| `Player/GunTwoHandGrabTests` | Вторая рука берёт дополнительную точку, а не перехватывает оружие (патч SDK 11 + `TwoHandGrabPolicy`, [Issue 13](UltimateXR/known-issues.md)). На каждую пару из `TwoHandGrabCases`; плюс проверка, что пар больше нуля. |
| `Player/GunTwoHandAimTests` | Поддерживающая рука не поворачивает оружие (`MainGripAimLock`, [Issue 14](UltimateXR/known-issues.md)). Только пары, где дополнительная точка ближе 10 см к основной (поддержка, а не цевьё). Без компонента — 66° на сдвиг руки в 3 см. |
| `Player/TwoHandGrabPolicyTests` | Правило `TwoHandGrabPolicy` в чистом виде, без SDK: занятая точка уступает достижимой свободной, иначе передача из руки в руку. Пять тестов. |
| `Player/WeaponPartGrabTests` | Детали предметов (`GrabOnlyWhenParentHeld`): у каждой вложенной `UxrGrabbableObject` в `Assets/Prefabs`, кроме лежащих в якоре, стоит компонент; на настоящих префабах из `WeaponInfo` × аватары с позами — у лежащего оружия деталь недоступна, у оружия в руке доступна. |
| `Player/SavedAvatarPlaceTests` | Место, которое игрок приносит с собой при подключении (CAL-02): `PhysicalSpaceSyncManager` копит позу **в координатах якорей**, а не в мировых; без пары якорей не копит вовсе (непереводимая поза хуже её отсутствия); `GamePlayerConnectMessage` несёт именно её и честно сообщает признак калибровки. Три теста. |
| `Prefabs/WeaponDropPhysicsTests` | Брошенное оружие остаётся на полу (PHY-01): у каждого динамического префаба из `Assets/Prefabs/Weapons` есть твёрдый коллайдер на своём `Rigidbody`; каждый роняется на реальную геометрию `Lobby`/`TestMap1`/`TestMap2` в её preview-сцене со скоростями 0, 10 и 25 м/с; пол держит и контрольное `Discrete`-тело на 25 м/с. Три теста. |
| `Prefabs/AvatarLoadoutTests` | Оснащение каждого аватара из `AvatarRegistry`, по тест-кейсу на аватар: карманы `Anchor_Back` (основное), `Anchor_Hip_R` (дополнительное) и `UxrMagazinePocket` внутри скелета с `GrabProxy`; карманы принимают оружие и магазины всех `WeaponInfo` (`Rifle` → спина, `Pistol` → бедро, совместимость решает `IsCompatibleObject`); `AvatarRig` размечен до пальцев; события Grip/Button1 обеих рук ведут на позы, которые у аватара есть; у каждого `UxrGrabbableObject` из `Assets/Prefabs` и каждого предмета сцен Build Settings (сэмплы SDK, объекты только сцены) для каждой точки хвата есть **своя** запись аватара или его родительского префаба, не `DefaultGripPoseInfo` (своя с пустой позой допустима — берётся общая `Grab`), GrabProxy карманов и вложенные префабы не дублируются; у каждой руки один `UxrGrabber`, лазер и телепорт с мишенью; `ValidTargetLayers` телепорта содержит слой пола под `TeamSpawnZone` всех сцен Build Settings; на корне `PlayerLoadoutManager` и не больше одного `UxrDummyControllerInput`. |
| `Prefabs/OutOfWorldGuardTests` | Kill-zone (PHY-01): `OutOfWorldGuard` стоит на каждом динамическом префабе оружия и удаляет предмет только ниже порога; таблица `Decide` по семи ролям — кто удаляет (сервер, сам, ждёт сервер), включая предмет без своего `netId`. Сам вызов `NetworkServer.Destroy` в EditMode не проверяется. |
| `Prefabs/AnchorActivationAudioTests` | Звук при спавне (AUD-01): на объектах `Activate On …` якорей нет `AudioSource` с `Play On Awake`; у каждого `AnchorPlaceSound` назначен источник и есть якорь. |
| `UI/UiFontCoverageTests` | Покрытие глифами (UI-01): каждый символ TMP-текстов в `Assets/Prefabs/UI` есть в запечённом атласе шрифта или его fallback; шрифт TMP по умолчанию содержит кириллицу. См. [`ui-fonts.md`](ui-fonts.md). |
| `Maps/SpawnZoneOwnershipTests` | Кому зона спавна засчитывает «в зоне» (RDY-04): чужая зона не засчитывается за свою, выход из **чужой** зоны не снимает нахождение в своей (так выглядит перенос между базами — «вошёл в новую» приходит раньше «вышел из старой»), перенос в базу противника снимает готовность, возврат её не возвращает, зона сообщает сессии **свою** команду, а не команду вошедшего, своя зона по-прежнему засчитывается, зона без команды молчит. Семь тестов. |

---

## Ключевые префабы

| Префаб | Путь | Описание |
|---|---|---|
| Игрок | `Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab` | PrefabVariant на основе `CyborgAvatar_URP`. |
| Контекст сессии | `Assets/Prefabs/Managers/SessionContext.prefab` | Сетевые сервисы уровня сессии: `SessionManager` (выбор карты и режима) и `NetworkStateRelay` (канал состояния UltimateXR). Спавнится один раз в `GameNetworkManager.OnStartServer`, живёт до остановки сервера. |

**Компоненты префаба игрока:**

| Компонент | Назначение |
|---|---|
| `UxrAvatar` | VR-тело, руки, камера |
| `UxrStandardAvatarController` | Обновление аватара по вводу контроллеров |
| `UxrMirrorAvatar` | Инициализация сетевого аватара, `CombineUniqueId`, ownership. Канал состояния через него **не идёт** — он в `NetworkStateRelay` |
| `NetworkIdentity` | Идентификатор Mirror |
| `NetworkTransformUnreliable` | Синхронизация трансформа |
| `UxrActor` | Система урона UltimateXR |
| `PlayerController` | Команда, здоровье, смерть, возрождение |

> При добавлении или изменении компонентов игрока — работать с префабом, не с объектами сцены.

---

## Статус реализации

| Механика | Статус | Приоритет |
|---|---|---|
| `PlayersManager` | ✅ Реализовано | — |
| `MapManager` | ✅ Реализовано | — |
| `DebugOrchestrator` | ✅ Реализовано | — |
| `GameManager` | ✅ Реализовано | — |
| `GameModeData` / `GameModeRegistry` | ✅ Реализовано | — |
| `GameplayManager` | ✅ Реализовано | — |
| `SetManager` | ✅ Реализовано | — |
| `RoundManager` | ✅ Реализовано | — |
| `GameMode` — Respawn | ✅ Реализовано | — |
| Команды | ✅ Реализовано | — |
| `GameMode` — Elimination | ✅ Реализовано | — |
| MVC Архитектура Меню (`MenuController`) | ✅ Реализовано | — |
| `MenuSessionSetup` (Выбор карт/режимов) | ✅ Реализовано | — |
| `MenuTeamSelection` (Выбор команды) | 🔧 В процессе | Средний |
| `VrCalibrationController` | 🔧 Заготовка | Средний |
| `EliminationModeEditor` | ✅ Реализовано | — |

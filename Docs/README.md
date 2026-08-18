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
| `testing.md` | Тестирование: шесть уровней от юнит-тестов до чек-листа в шлеме |
| `gameplay.md` | Геймдизайн: что делает игрок, правила, режимы, структура матча |
| `session-architecture.md` | Сессия, роли устройств (VR/PC/Server), Host/Client |
| `game-manager.md` | GameManager, система режимов: создание assets, настройка, поток действий |
| `ui-menu-architecture.md` | Архитектура UI Меню (MVC), экраны, префабы, контроллеры |
| `magazine-pocket.md` | Механика "умного магазина" (Smart Magazine Pocket) |
| `level-design.md` | Проектирование карт и арен |
| `Arsenal/` | Стена арсенала: [дизайн](Arsenal/ArsenalWall_Design_RU.md), [код](Arsenal/Arsenal_Code_Architecture_RU.md) |
| `LegsAnimator_UI_Reference_RU.md` | Справочник по параметрам Legs Animator |
| `Roadmap.md` | План развития проекта |
| `CHANGELOG.md` | Журнал архитектурных и значимых изменений проекта |
| `version-control.md` | Git/Plastic workflow: автодублирование коммитов, хук, диагностика |
| `unity-mcp.md` | Локальный фикс Unity MCP `execute_code` на Windows (MAX_PATH) |

---

## Быстрая навигация

- **Что чинить прямо сейчас, что чем блокировано** → [`tasks/README.md`](tasks/README.md)
- **Как тестировать сеть и VR** → [`testing.md`](testing.md)
- **Прогнать e2e на двух процессах одной командой** → [`testing.md`](testing.md#ярус-c--два-процесса-настоящий-e2e)
- **Аудит: 21 находка** → [`audit/network-audit-2026-08.md`](audit/network-audit-2026-08.md)
- **Аудит: 5 корневых решений архитектуры** → [`audit/architecture-review-2026-08.md`](audit/architecture-review-2026-08.md)
- **Геймплей, режимы, матч, арена** → [`gameplay.md`](gameplay.md)
- **Сессия, роли устройств, Host/Client** → [`session-architecture.md`](session-architecture.md)
- **GameManager, режимы, assets, настройка** → [`game-manager.md`](game-manager.md)
- **Архитектура UI Меню (MVC)** → [`ui-menu-architecture.md`](ui-menu-architecture.md)
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
| `MapManager` | `Managers/MapManager.cs` | **Единственная точка входа для смены карты.** Откладывает `ServerChangeScene` на конец кадра через корутину. |
| `GameManager` | `Managers/GameManager.cs` | Хранит выбор сессии (карта + режим). DontDestroyOnLoad вместе с NetworkManager. SyncVar реплицирует выбор клиентам. Методы: `SetSession()`, `StartSession()`. |
| `GameplayManager` | `Managers/GameplayManager.cs` | Матч: счёт, победитель, `StartGameplay()`, `StopGameplay()`. Режим ищет по `modeId` из `GameManager`. |
| `UxrActor` | `UltimateXR/.../UxrActor.cs` | Базовая система урона UltimateXR. Игрок умирает, когда `UxrActor` вызывает событие смерти. |
| `SetManager` | `GameModes/EliminationMode/SetManager.cs` | Сет: N раундов, счёт раундов, смена сторон, `ForceStop()`. Владелец машины раунда: тикает её и применяет единственный переход, который она не делает сама, — «итоги показаны → новый раунд». Исход сета отдаёт обязательным колбэком конструктора, а не событием. |
| `RoundManager` | `GameModes/EliminationMode/RoundManager.cs` | Машина фаз раунда: таблица переходов `Setup → Equipment → Countdown → Combat → Resolution → Scoreboard`, таймеры, `RequestRoundEnd()`, `ForceStop()`. Про `EliminationMode` не знает; о живых игроках спрашивает `IPlayerRoster`. `Tick()` возвращает переход значением — см. [gameplay.md](gameplay.md#машина-состояний-раунда-кто-чем-владеет). Фазу наружу раздаёт `EliminationMode`. |

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
| `GameMode` | `GameModes/GameMode.cs` | Абстрактный базовый класс. Поле `ModeId` — строковый ключ для поиска. Методы: `CheckWinCondition`, `CanRespawn`, `OnRoundEnd`. |
| `EliminationMode` | `GameModes/EliminationMode/EliminationMode.cs` | Раунд до полного уничтожения команды. ModeId = `elimination`. |
| `RespawnMode` | `GameModes/RespawnMode.cs` | Возрождение при возврате на спавн. ModeId = `respawn`. |
| `GameModeData` | `GameModes/GameModeData.cs` | ScriptableObject: `modeId`, `displayName`, `icon`. Создать: `Create > VrBattlegrounds > Game Mode Data`. |
| `GameModeRegistry` | `GameModes/GameModeRegistry.cs` | ScriptableObject-список режимов. `GetById(modeId)`. Назначить в `GameManager` и `AdminMenuController`. |
| `IPlayerRoster` | `GameModes/IPlayerRoster.cs` | Узкий доступ логики матча к списку игроков: «кто в этой команде жив». Боевая реализация `PlayersManagerRoster` — обёртка над `PlayersManager.Instance`. Нужен, чтобы машину раунда можно было прогнать EditMode-тестом: живой аватар в EditMode не поднимается. |

> При добавлении нового режима — создать наследника `GameMode`, переопределить `OnRoundEnd`, `CanRespawn`, `CheckWinCondition`. Не менять базовую логику `RoundManager`.

---

### Игрок — `Assets/Scripts/Player/`

| Класс | Файл | Описание |
|---|---|---|
| `PlayerController` | `Player/PlayerController.cs` | Состояние: здоровье, команда (`TeamIndex`), `IsAlive`. Подписывается на `UxrActor.Death`. Ссылка на свою `PlayerSession` кэшируется в `OnStartServer`/`OnStartClient`. |
| `VrCalibrationController` | `Player/VrCalibrationController.cs` | Калибровка позиции аватара относительно физической арены. |

---

### Карты — `Assets/Scripts/Maps/`

| Класс | Файл | Описание |
|---|---|---|
| `MapData` | `Maps/MapData.cs` | ScriptableObject с данными карты. |
| `MapRegistry` | `Maps/MapRegistry.cs` | ScriptableObject-список всех карт. Назначить в `AdminMenuController._mapRegistry`. |
| `TeamSpawnZone` | `Maps/TeamSpawnZone.cs` | Коллайдер зоны возрождения для команды. Проверяет присутствие игроков. |
| `SpawnZoneCreator` | `Editor/SpawnZoneCreator.cs` | Опция в меню GameObject для авто-создания префаба зоны спавна на сцене. |

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
| `GameLog` | `Core/GameLog.cs` | Единственная точка логирования. Никогда не использовать `Debug.Log` напрямую. |
| `GameSettings` | `Core/GameSettings.cs` | ScriptableObject с уровнями логирования по категориям. |
| `LogLevel` | `Core/LogLevel.cs` | Enum: `None / Errors / Warnings / Info / Verbose`. |
| `TeamData` | `Core/TeamData.cs` | ScriptableObject с данными команды. По сети синхронизируется только `int teamIndex`. |
| `TeamRegistry` | `Core/TeamRegistry.cs` | Реестр команд. |
| `AppRoleManager` | `Core/AppRoleManager.cs` | Хранит текущую `DeviceRole` (VR/PC/Server) и `NetworkRole` (Host/Client), используется для сборки UI и логики. |
| `PersistentRoot` | `Managers/PersistentRoot.cs` | Глобальный DontDestroyOnLoad узел. Отвечает за инстанцирование глобальных менеджеров (например, `SessionManager`). |

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
| `DedicatedServerArsenalScenario` | `Debug/E2E/Scenarios/DedicatedServerArsenalScenario.cs` | Сценарий `dedicated-server-arsenal` — находки NET-06, NET-13, NET-07. Роль сервера гонит матч и выносит вердикт; клиенты занимают команды, служат контролем к NET-06 и участвуют в проверке общей стены: `client-1` берёт жетон, сервер и `client-2` обязаны увидеть, что стена закрылась. Общий объект выбирается по наименьшему `netId`. |
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

### Тесты — `Assets/Tests/EditMode/`

Сборка `VrBattlegrounds.Tests.EditMode` (`includePlatforms: ["Editor"]`, поэтому в билд
под Quest не попадает). Прогон: `run_tests(mode="EditMode", assembly_names=[...])`.

| Класс / файл | Назначение |
|---|---|
| `SetManagerScoringTests` | Подсчёт победителя сета в `SetManager` — чистая логика, без сети. Раунды проигрываются прокруткой `SetManager.Tick`. |
| `RoundFlowSupport` | Общая оснастка тестов матча: `StubPlayerRoster` (подставной реестр игроков) и `RoundFlowDriver` (прокрутка фиксированным шагом 0.25 с с записью наблюдённых фаз). |
| `Network/MirrorTestHarness` | Базовый класс сетевых тестов: поднимает Mirror сервером **без сокета** (ярус A) и, по требованию, локального клиента (ярус B). Сбрасывает синглтоны проекта между тестами. Рецепт и границы — [`testing.md`](testing.md#как-тестировать-сетевую-логику). |
| `Network/EliminationModeServerTests` | Серверная логика режима: заполнение `TeamStates`, одно очко за выигранный сет (T-02). Первый тест — проверка самого харнесса. |
| `Network/RoundPhaseFlowTests` | Фазы раунда боевым путём `ServerTick → SetManager → RoundManager` (T-09, MATCH-02): все шесть фаз по порядку, длительность `Resolution` и `Scoreboard` в тиках, рост номера раунда после полного цикла (сторож MATCH-06), запрет заканчивать сет раньше экрана итогов. |
| `Network/HostClientHarnessTests` | Ярус B: локальный клиент поднялся, `SpawnMessage` доходит до `NetworkClient.spawned`. |
| `Network/PlayerSessionReplicationTests` | Репликация `PlayerSession` через настоящую сериализацию Mirror: `TeamIndex` и связь с аватаром доезжают до клиента, смена скина переключает связь, гонка спавнов чинится аватаром, `PlayerController.Session` кэшируется (T-11). |
| `Network/SessionRecoveryTests` | Снимок сессии при отключении: позиция, здоровье, флаг `NeedsPhysicalRestore` (T-04). |
| `Network/NetworkStateRelayTests` | Канал состояния как объект сессии (T-12): подписка на хосте ровно одна (NET-03), отписка при остановке сервера, отсутствие статики в `UxrMirrorAvatar` и в релее, наличие релея и ненулевой `assetId` на `SessionContext.prefab`. Саму доставку блобов проверяет ярус C — в host-режиме она была бы ложно-зелёной. |
| `Arsenal/ArsenalSlotOccupancyTests` | Занятость слота арсенала (T-15, NET-13): после сетевой выдачи слот занят и пополнения не просит, а когда предмет унесли или уничтожили — снова пустеет. Плюс блокировка: заблокированный слот действительно выключает захват предмета. |
| `Arsenal/ArsenalWallStateReplicationTests` | Состояние стены арсенала (T-15, NET-07): серверный канал фазы открывает и закрывает стену, состояние доезжает до позднего клиента настоящей сериализацией Mirror, закрытие по жетону расходится всем, локальный обработчик фазы заспавненную стену не трогает, а незаспавненная ведёт состояние сама. |

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

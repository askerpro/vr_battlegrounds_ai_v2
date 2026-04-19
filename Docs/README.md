> **Для Copilot:** это точка входа в документацию **игры** VR Battlegrounds AI.
> Обновлять при добавлении новых классов, модулей, зависимостей или изменении архитектуры.
> Обязательно документировать обнаруженные побочные эффекты SDK (например, систему Precaching).

---

## Структура папки `Docs/`

| Файл | Содержание |
|---|---|
| `README.md` | Этот файл — технический справочник: скрипты, классы, API, компоненты |
| `gameplay.md` | Геймдизайн: что делает игрок, правила, режимы, структура матча |
| `magazine-pocket.md` | Механика "умного магазина" (Smart Magazine Pocket) |
| `game-manager.md` | GameManager, система режимов: создание assets, настройка, поток действий |
| `ui-menu-architecture.md` | Архитектура UI Меню (MVC), экраны, префабы, контроллеры |
| `CHANGELOG.md` | Журнал архитектурных и значимых изменений проекта |
| `version-control.md` | Git/Plastic workflow: автодублирование коммитов, хук, диагностика |
| `unity-mcp.md` | Локальный фикс Unity MCP `execute_code` на Windows (MAX_PATH) |
| `AI_Navigation.md` | 🤖 Технические инструкции для ИИ-агентов (правила оптимизированного поиска M.A.P.) |

---

## Быстрая навигация

- **Геймплей, режимы, матч, арена** → [`gameplay.md`](gameplay.md)
- **Умный магазин (Magazine Pocket)** → [`magazine-pocket.md`](magazine-pocket.md)
- **GameManager, режимы, assets, настройка** → [`game-manager.md`](game-manager.md)
- **Архитектура UI Меню (MVC)** → [`ui-menu-architecture.md`](ui-menu-architecture.md)
- **История изменений (Changelog)** → [`CHANGELOG.md`](CHANGELOG.md)
- **Git/Plastic workflow и post-commit hook** → [`version-control.md`](version-control.md)
- **Unity MCP: фикс execute_code (Windows)** → [`unity-mcp.md`](unity-mcp.md)
- **UltimateXR SDK** → `Docs/UltimateXR/README.md` (открыть через `#file:`)
- **Архитектура UltimateXR** → `Docs/UltimateXR/architecture.md`

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

---

## Ключевые скрипты

### Сеть — `Assets/Scripts/Network/`

| Класс | Файл | Описание |
|---|---|---|
| `GameNetworkManager` | `Network/GameNetworkManager.cs` | Сетевой транспорт Mirror. Только коллбэки подключения, события `PlayerConnected/Disconnected/ServerSceneChanged`. Никакой игровой логики. |

**Статические события `GameNetworkManager`:**

| Событие | Когда | Подписчики |
|---|---|---|
| `PlayerConnected` | Игрок заспавнился на сервере | `PlayersManager`, `MapManager`, `DebugOrchestrator` |
| `PlayerDisconnected` | Игрок отключился | `PlayersManager` |
| `ServerSceneChanged` | Сервер завершил загрузку сцены | `DebugOrchestrator` |

---

### Менеджеры — `Assets/Scripts/Managers/`

| Класс | Файл | Описание |
|---|---|---|
| `PlayersManager` | `Managers/PlayersManager.cs` | Список игроков, фильтрация: `Players`, `GetAlivePlayers(team)`, `GetPlayers(team)`. Синглтон на том же GO что и `NetworkManager`. |
| `MapManager` | `Managers/MapManager.cs` | **Единственная точка входа для смены карты.** Откладывает `ServerChangeScene` на конец кадра через корутину. |
| `GameManager` | `Managers/GameManager.cs` | Хранит выбор сессии (карта + режим). DontDestroyOnLoad вместе с NetworkManager. SyncVar реплицирует выбор клиентам. Методы: `SetSession()`, `StartSession()`. |
| `GameplayManager` | `Managers/GameplayManager.cs` | Матч: счёт, победитель, `StartGameplay()`, `StopGameplay()`. Режим ищет по `modeId` из `GameManager`. |
| `UxrActor` | `UltimateXR/.../UxrActor.cs` | Базовая система урона UltimateXR. Игрок умирает, когда `UxrActor` вызывает событие смерти. |
| `SetManager` | `GameModes/EliminationMode/SetManager.cs` | Сет: N раундов, смена сторон, `ForceStop()`. |
| `RoundManager` | `GameModes/EliminationMode/RoundManager.cs` | Раунд: FSM (WaitingForPlayers → Countdown → Active → Ended), таймер, победа через `GameMode`, `ForceStop()`. |

**Иерархия менеджеров матча:**

```
GameplayManager       — матч (5 карт, счёт, победитель)
└── SetManager     — сет (смена команд, счёт сетов)
    └── RoundManager — раунд (готовность, старт, конец)
```

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

> При добавлении нового режима — создать наследника `GameMode`, переопределить `OnRoundEnd`, `CanRespawn`, `CheckWinCondition`. Не менять базовую логику `RoundManager`.

---

### Игрок — `Assets/Scripts/Player/`

| Класс | Файл | Описание |
|---|---|---|
| `PlayerController` | `Player/PlayerController.cs` | Состояние: здоровье, команда (`TeamIndex`), `IsAlive`. Подписывается на `UxrActor.Death`. |
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
- Игровые режимы (`EliminationMode`, `RespawnMode`) вызывают `[ClientRpc]`, которые на клиенте поднимают "Чистые семантические события C#" (например `OnRoundEndedLocal(TeamData)`).
- Режимы не знают про UI и не генерируют текст ("Победили Синие").
- Автономные UI-виджеты (как `HUDWidget_GameNotification`) подписываются на эти события, сами формируют финальную строку (с учетом имен команд) и отображают её.

---

### Отладка — `Assets/Scripts/Debug/`

| Класс | Файл | Описание |
|---|---|---|
| `DebugOrchestrator` | `Debug/DebugOrchestrator.cs` | Автостарт при Play: назначает команду, грузит карту, стартует матч. Только вызовы публичных API. |
| `DebugBootstrapConfig` | `Debug/DebugBootstrapConfig.cs` | ScriptableObject с параметрами `DebugOrchestrator`. |
| `PlayModeStartFromOffline` | `Editor/PlayModeStartFromOffline.cs` | Скрипт редактора. Автоматически перехватывает Play Mode, заставляя Unity стартовать с Offline-сцены и прокидывая текущую сцену в конфиг. |

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

## Ключевые префабы

| Префаб | Путь | Описание |
|---|---|---|
| Игрок | `Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab` | PrefabVariant на основе `CyborgAvatar_URP`. |

**Компоненты префаба игрока:**

| Компонент | Назначение |
|---|---|
| `UxrAvatar` | VR-тело, руки, камера |
| `UxrStandardAvatarController` | Обновление аватара по вводу контроллеров |
| `UxrMirrorAvatar` | Сетевая синхронизация аватара через Mirror |
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

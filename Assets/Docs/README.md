> **Для Copilot:** это точка входа в документацию **игры** VR Battlegrounds AI.
> Обновлять при добавлении новых классов, модулей, зависимостей или изменении архитектуры.

---

## Структура папки `Assets/Docs/`

| Файл | Содержание |
|---|---|
| `README.md` | Этот файл — технический справочник: скрипты, классы, API, компоненты |
| `gameplay.md` | Геймдизайн: что делает игрок, правила, режимы, структура матча |
| `game-manager.md` | GameManager, система режимов: создание assets, настройка, поток действий |

---

## Быстрая навигация

- **Геймплей, режимы, матч, арена** → [`gameplay.md`](gameplay.md)
- **GameManager, режимы, assets, настройка** → [`game-manager.md`](game-manager.md)
- **UltimateXR SDK** → `Assets/ThirdParty/UltimateXR/Docs/_context/README.md` (открыть через `#file:`)
- **Архитектура UltimateXR** → `Assets/ThirdParty/UltimateXR/Docs/_context/architecture.md`

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
| `MatchManager` | `Managers/MatchManager.cs` | Матч: счёт, победитель, `StartMatch()`, `StopMatch()`. Режим ищет по `modeId` из `GameManager`. |
| `SetManager` | `GameModes/EliminationMode/SetManager.cs` | Сет: N раундов, смена сторон, `ForceStop()`. |
| `RoundManager` | `GameModes/EliminationMode/RoundManager.cs` | Раунд: FSM (Countdown → Active → Ended), таймер, победа через `GameMode`, `ForceStop()`. |

**Иерархия менеджеров матча:**

```
MatchManager       — матч (5 карт, счёт, победитель)
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
| `PlayerController` | `Player/PlayerController.cs` | Состояние: здоровье, команда (`TeamIndex`), `IsAlive`. |
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

**Категории логов в `GameSettings`:**

| Свойство | Категория | Затрагивает |
|---|---|---|
| `LogLevelNetwork` | Сеть | `GameNetworkManager`, `MapManager`, `GameNetworkDiscovery` |
| `LogLevelPlayer` | Игрок | `PlayerController` |
| `LogLevelMatch` | Матч | `MatchManager`, `SetManager`, `RoundManager` |
| `LogLevelDebug` | Отладка | `DebugOrchestrator` (по умолчанию `Verbose`) |

---

### UI — `Assets/Scripts/UI/`

| Класс | Файл | Описание |
|---|---|---|
| `AdminMenuController` | `UI/AdminMenuController.cs` | Меню администратора: выбор карты, режима, управление матчем. Только Host. |
| `PlayerMenuController` | `UI/PlayerMenuController.cs` | Меню игрока: выбор команды, калибровка VR. |

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
  → LocalAvatarChanged → назначается команда
  → PlayerConnected → если игроков >= min → StartMatch()
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
| `MatchManager` | ✅ Реализовано | — |
| `SetManager` | ✅ Реализовано | — |
| `RoundManager` | ✅ Реализовано | — |
| `GameMode` — Respawn | ⬜ Не реализовано | **Первый приоритет** |
| Команды | ⬜ Не реализовано | Высокий |
| `GameMode` — Elimination | ⬜ Не реализовано | Средний |
| `AdminMenuController` | 🔧 Обновлено | Средний |
| `PlayerMenuController` | 🔧 Заготовка | Средний |
| `VrCalibrationController` | 🔧 Заготовка | Средний |

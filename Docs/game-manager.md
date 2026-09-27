> Обновлять при изменении `GameManager`, `GameModeData`, `GameModeRegistry` или архитектуры выбора режима.

---

# GameManager — выбор сессии (карта + режим)

## Что такое GameManager

`GameManager` — синглтон `NetworkBehaviour`, который **переживает смену сцен** (живёт на том же GameObject что и `GameNetworkManager` и `MapManager`).

**Единственная ответственность:** хранить выбор администратора — карту и режим — и синхронизировать их на всех клиентах через `SyncVar`.

```
DontDestroyOnLoad GO
> Обновлять при изменении `GameManager`, `GameModeData`, `GameModeRegistry` или архитектуры выбора режима.

---

# GameManager — выбор сессии (карта + режим)

## Что такое GameManager

`GameManager` — синглтон `NetworkBehaviour`, который **переживает смену сцен** (живёт на том же GameObject что и `GameNetworkManager` и `MapManager`).

**Единственная ответственность:** хранить выбор администратора — карту и режим — и синхронизировать их на всех клиентах через `SyncVar`.

```
DontDestroyOnLoad GO
  ├── GameNetworkManager
  ├── MapManager
  └── GameManager          ← карта + режим для следующей сессии
```

**Почему не MapManager и не MatchManager:**
- `MapManager` — транспорт (грузит сцены), не хранит состояние
- `GameplayManager` — живёт в сцене карты и уничтожается при её смене

---

## API GameManager

| Метод / Свойство | Сервер/Клиент | Описание |
|---|---|---|
| `SetSession(mapScene, modeId)` | Только сервер | Устанавливает карту и режим. Реплицируется клиентам через `SyncVar`. |
| `StartSession()` | Только сервер | Загружает выбранную карту через `MapManager.LoadMap()`. |
| `SelectedMap` | Оба | `MapData` выбранной карты (из `MapRegistry`). |
| `SelectedGameModeData` | Оба | `GameModeData` выбранного режима (из `GameModeRegistry`). |
| `SelectedMapScene` | Оба | Имя сцены выбранной карты (`string`). |
| `SelectedModeId` | Оба | Идентификатор выбранного режима (`string`). |

---

## Система игровых режимов

Режим разделён на **два слоя**:

| Слой | Тип | Где живёт | Что делает |
|---|---|---|---|
| `GameModeData` | ScriptableObject | В `Assets/Data/GameModes/` | Данные для UI: название, иконка, `modeId`, команды, префаб логики |
| `GameMode` | NetworkBehaviour | Инстанцируется в сцену при StartMatch | Полная логика режима: управляет собственной структурой матча |

**Поток жизни режима:**
```
GameModeData.modePrefab
  → NetworkServer.Spawn  (GameplayManager.StartGameplay)
  → GameMode.Initialize(teams)
  → GameMode.StartGameplayWhenReady()  ← режим сам проверяет CanStartGameplay()
  → GameMode.StartMatch()              ← режим сам управляет сетами/раундами/таймером
  → GameMode.MatchEnded event          ← режим сигнализирует о завершении
  → GameplayManager.OnMatchEnded
  → NetworkServer.UnSpawn + Destroy (GameplayManager.CleanupGameMode)
```

**Почему `GameMode : NetworkBehaviour` а не `MonoBehaviour`:**
режим сам держит реплицируемое состояние матча — счёт команд, номер раунда, фазу раунда
(`SyncVar`, `SyncList`, `SyncDictionary`) — и сам шлёт `ClientRpc`. Без `NetworkBehaviour`
клиентам не досталось бы ничего из этого.

> `SetManager` и `RoundManager` при этом **не** сетевые и даже не `MonoBehaviour`: это
> обычные C#-объекты, создаваемые через `new` в `EliminationMode.InitializeActiveGame`,
> и тикает их `EliminationMode.ServerTick`. Вся сеть — в режиме, вся логика матча — в них.
> Именно поэтому их целиком покрывают EditMode-тесты. (Раньше здесь было написано, что они
> спавнятся через `NetworkServer.Spawn` — это никогда не соответствовало коду.)

**Что умеет каждый режим самостоятельно:**

| Режим | Структура | Победа | Респавн |
|---|---|---|---|
| `EliminationMode` | Матч → Сеты → Раунды | Больше сетов выиграно | ❌ нет |
| `RespawnMode` | Один длинный матч (таймер) | Больше фрагов | ✅ всегда |
| `LobbyMode` | Нет матча — свободная игра, одна команда «Лобби» | — | ✅ |

### Режим сцены и выбор матча

Режим, который запускает `GameplayManager`, берётся из двух **разных** источников, и они
не смешиваются (`GameplayManager.ResolveGameModeData`):

| Источник | Где задан | Что означает | Старт |
|---|---|---|---|
| **Режим сцены** | поле `_sceneGameMode` у `GameplayManager` в сцене | режим, который эта сцена **есть** (лобби — `LobbyMode`) | сам, в `GameplayManager.OnStartServer` |
| **Выбор матча** | `SessionManager.SelectedGameModeData` (админ, `GameModeRegistry`) | режим **следующего** матча на карте | по команде администратора или автостарту `DebugOrchestrator` |

Режим сцены задан — выбор матча не спрашивается вовсе. Поэтому выбор администратора в лобби
не меняет режим лобби, а лобби-режима нет в `GameModeRegistry` и в меню выбора режима матча.
На картах режим сцены пуст (проверяет `GameModeWiringTests`).

Почему поле на `GameplayManager`, а не `MapData`: лобби не карта из `MapRegistry` (добавить
его туда — показать в выборе карт), а в самой сцене поле видно там, где его и ищут, —
у объекта, который режим спавнит. Альтернатива — отдельный компонент «режим сцены»; она
дала бы ещё один объект в каждой сцене без выигрыша.

`GameplayManager` теперь есть и в лобби (`MatchManager` в `Lobby.unity`), поэтому
`GameplayManager.Instance` равен null только в Offline и в окне смены сцены.

### Правила, которые объявляет режим

Системы вне режима не знают его конкретного типа — спрашивают базовый `GameMode`:

| Свойство / событие | Кто читает | `EliminationMode` | `LobbyMode` |
|---|---|---|---|
| `WeaponsEnabled` | `GameplayManager.Update` → `UxrWeaponManager` | только в `Combat` | всегда |
| `ArsenalRules.IsOpen` | `ArsenalWallController.ApplyModeRules` (сервер) | только в `Equipment` | всегда |
| `ArsenalRules.UsesReadinessTag` | стена, каждая машина | `RoundStartRule == Readiness` | нет |
| `ArsenalRules.ReplacesLostWeapons` | стена, сервер | нет | да, через 2 с |
| `ArsenalRefillRequestedServer` | стена, сервер | на входе в `Setup` | — |
| `OnPlayerDied(player)` | `GameplayManager.OnPlayerDied` | условие победы раунда | — (смерти нет) |
| `PlayersTakeDamage` | `PlayerController` на `UxrActor.DamageReceiving` (отмена урона) | да | нет |
| `TeamChoiceLocked` | `TeamChangeRules`, планшет | после старта матча | нет |
| `ModeData` (`modeId` SyncVar) | HUD (`PlayerHUDManager`), политика команд, минимум игроков | `Elimination_GameModeData` | `Lobby_GameModeData` (HUD нет) |

Свойства — состояние, стена сверяется с ним каждый кадр; событие одно — разовое
пополнение пустых слотов, потому что фаза `Setup` бывает короче кадра. Режима нет
(карта до старта матча) — стена не трогается, оружие стреляет.

### Раздача команд режимом

Команда игрока — команда **активного режима сцены**. Лобби-режим раздаёт свою единственную
команду сам; на карте команду матча выбирает игрок или выдаёт админ (этап Б).

**Политика режима** (`GameMode.TeamAssignmentPolicy`) задаётся данными режима —
полем `GameModeData.teamAssignment`, а не кодом:

| `teamAssignment` | Политика | Где |
|---|---|---|
| `AutoBalance` | `AutoBalanceTeamPolicy` — расчёт `TeamAutoBalance.Plan`: в самую малочисленную, при равенстве в первую по списку, стоящие в командах режима не двигаются | `Lobby_GameModeData` (команда одна — её получают все) |
| `PlayerChoice` | `PlayerChoiceTeamPolicy` — никого не назначает | `Elimination_GameModeData`, `Respawn_GameModeData` |

Режим без данных (EditMode-тесты) — `PlayerChoice`: сам никого не двигает. Политика
применяется в `GameMode.ServerAssignTeams` — при старте (`StartGameplayWhenReady`) и при
каждом подключении (`PlayersManager.OnSessionConnected`, приходит до спавна аватара).
Данные режима известны и клиенту: режим реплицирует `modeId`, а `GameModeCatalog.Find`
ищет его сначала у режима сцены (лобби-режима в реестре матча нет), затем в `GameModeRegistry`.

**Входы смены команды** — у `GameplayManager`, правила — `TeamChangeRules`, исполнение —
`SessionTeamAssigner`:

| Кто | Вход | Правило |
|---|---|---|
| Игрок (планшет) | `PlayerSession.CmdRequestTeamChange` → `ProcessTeamChangeRequest` | только команда активного режима и только пока `GameMode.TeamChoiceLocked == false` (до старта матча). Скин в своей команде — всегда |
| Админ (экран «Игроки и команды») | `PlayerSession.CmdAdminAssignTeam` → `ServerAdminAssignTeam(admin, target, teamId)` | право админа (`TeamChangeRules.IsAdmin`: хост или `IsAdmin`), любая команда `TeamRegistry`, в любой момент |
| Админ, разово | `PlayerSession.CmdAdminAutoBalance` → `ServerAdminAutoBalance(admin)` | автобаланс игроков без команды режима; политику режима не меняет |
| Режим | `GameMode.ServerAssignTeams` | по политике режима |

**Почему выбор закрывается стартом матча.** После старта смена стороны — это выход из
раунда посреди боя: составы уже разыграны (сеты, смена сторон), а перебежчик ломает
баланс. Опоздавшему команду выдаёт админ. `TeamChoiceLocked` у Elimination —
`_matchState != WaitingForPlayers`, у Respawn — идёт ли матч; это SyncVar-состояние,
поэтому планшет клиента знает его сам.

**Матч ждёт команд.** `GameMode.AllPlayersHaveModeTeam()` — у каждого подключённого игрока
(не зрителя) есть команда режима; состав берётся у `PlayerRoster`. Elimination проверяет его
в `IsPlayersReady` вместе с минимумом игроков — теперь из **своих** данных
(`GameMode.MinPlayersToStart` ← `ModeData.minPlayersToStart`), а не из выбора матча
в `SessionManager`. Respawn — в `CanStartGameplay`.

**Исполнение** (`SessionTeamAssigner`): поднимает `GameplayManager.OnPlayerTeamChangeRequested`
(хуки режима); нет аватара — пишет команду в сессию (спавн сам возьмёт зону и скин);
аватар жив — `AvatarManager.ChangeAvatar`, **на том же месте** (смена команды никого не
двигает). Скин по выбору игрока либо сохраняется (`TeamData.IndexOfAvatar`) для админа
и политики.

Игрок без команды режима на карте появляется в нейтральной точке (откалиброванный — по
калибровке), лог уровня `Info`: это ожидание выбора, а не сбой. Команды раньше раздавал
`DebugOrchestrator` (`teamsForAutoAssign`) — удалено. Зрители (`GameRole.Spectator`) команд
не получают. Тесты — `TeamChoiceTests`, `TeamAutoBalanceTests`, `GameModeRulesTests`.

---

## Шаг 1 — Создать префабы режимов

Для каждого режима — отдельный префаб:

1.  **ПКМ в Hierarchy → Create Empty**, назвать `RespawnModePrefab`
2.  Добавить компонент `RespawnMode` (или `EliminationMode`)
3.  **Сохранить как префаб**: перетащить в `Assets/Prefabs/GameModes/`

```
Assets/Prefabs/GameModes/
  ├── RespawnMode.prefab      ← содержит RespawnMode
  └── EliminationMode.prefab  ← содержит EliminationMode
```

> компонентам режимов в префабе не нужно заполнять никаких полей в Inspector — команды передаются через `Initialize()` из `GameModeData`.

---

## Шаг 2 — Создать GameModeData assets

Для каждого режима нужен один asset.

**ПКМ в Project → Create → VrBattlegrounds → Game Mode Data**

| Asset | modeId | displayName | modePrefab | teams[] |
|---|---|---|---|---|
| `GameModeData_Respawn.asset` | `respawn` | Возрождение | `RespawnMode.prefab` | [Команда A, Команда B] |
| `GameModeData_Elimination.asset` | `elimination` | Ликвидация | `EliminationMode.prefab` | [Команда A, Команда B] |

Сохранить в: `Assets/Data/GameModes/`

---

## Шаг 3 — Создать GameModeRegistry asset

Один asset на проект — список всех режимов.

**ПКМ в Project → Create → VrBattlegrounds → Game Mode Registry**

В поле `Modes[]` добавить оба созданных `GameModeData` asset-а.

Сохранить в: `Assets/Data/GameModes/GameModeRegistry.asset`

---

## Шаг 4 — Настроить GameObject NetworkManager

На GameObject с `GameNetworkManager` и `MapManager` добавить компонент `GameManager`.

В Inspector `GameManager` назначить:

| Поле | Что назначить |
|---|---|
| `Map Registry` | `Assets/Data/Maps/MapRegistry.asset` |
| `Game Mode Registry` | `Assets/Data/GameModes/GameModeRegistry.asset` |

---

## Шаг 5 — Настроить AdminMenuController

На GameObject `AdminMenuController` в Lobby-сцене:

| Поле | Что назначить |
|---|---|
| `Map Registry` | `Assets/Data/Maps/MapRegistry.asset` |
| `Game Mode Registry` | `Assets/Data/GameModes/GameModeRegistry.asset` |

---

## Поток действий администратора

```
[Lobby]
Администратор выбирает карту → AdminMenuController.OnMapSelected(index)
  → GameManager.SetSession(mapScene, currentModeId)

Администратор выбирает режим → AdminMenuController.OnModeSelected(index)
  → GameManager.SetSession(currentMap, modeId)

Администратор нажимает "Начать игру" → AdminMenuController.OnStartSessionPressed()
  → GameManager.StartSession()
  → MapManager.LoadMap(selectedMapScene)
  → [Карта загружается]

Администратор нажимает "Старт матча" → AdminMenuController.OnStartMatchPressed()
  → GameplayManager.StartMatch()
  → читает GameManager.SelectedModeId
  → находит GameMode-компонент по modeId
  → игроки выбирают команду в планшете (или её выдаёт админ: экран «Игроки и команды»)
  → матч ждёт, пока команда режима будет у всех (GameMode.AllPlayersHaveModeTeam)
  → запускает матч (ожидая `CanStartGameplay()`)

[Матч идёт]
Администратор нажимает "Стоп / Лобби" → AdminMenuController.OnStopMatchPressed()
  → GameplayManager.StopMatch()
  → MapManager.LoadMap("Lobby")
  → [Lobby загружается: GameplayManager карты уничтожен, GameplayManager лобби
     сам запускает LobbyMode, всем выдаётся команда «Лобби»]
  → GameManager.SelectedModeId и SelectedMapScene — сохранены
```

---

## Этапы инициализации

Жизненный цикл системы при старте:

1.  **Avatar Setup**: `UxrAvatar` пробуждается и устанавливает `LocalAvatar`.
2.  **Registration**: Генерируется событие `LocalAvatarChanged`.
3.  **Precaching**: `UxrManager` ловит активацию аватара и запускает `TryPrecaching()`.
    - Все объекты с `IUxrPrecacheable` создаются перед камерой на несколько кадров.
    - Экран в этот момент затемнен через `UxrCameraFade`.
4.  **Gameplay Start**: Когда аватар готов и сервер разрешил, `GameplayManager` запускает логику режима.

---

## Добавление нового режима

1.  Создать класс-наследник `GameMode`:
    ```csharp
    public class MyMode : GameMode
    {
        // Команды приходят через Initialize() — не задавать в Inspector
        public override void OnRoundEnd() { ... }
        public override bool CanRespawn() => false;
        public override TeamData CheckWinCondition() { ... }
    }
    ```

2.  Создать префаб: `Assets/Prefabs/GameModes/MyMode.prefab` с компонентом `MyMode`

3.  Создать `GameModeData` asset: `modeId = "my_mode"`, назначить `modePrefab` и `teams[]`

4.  Добавить asset в `GameModeRegistry.modes[]`

> Менять `GameplayManager`, `SetManager`, `RoundManager` и сцены карт **не нужно**.

---

## Частые ошибки

| Ошибка в логе | Причина | Решение |
|---|---|---|
| `GameManager не содержит выбранного режима` | `StartMatch()` вызван до `SetSession()` | Убедиться что администратор выбрал режим перед стартом |
| `GameModeData '...' не содержит modePrefab` | Поле `modePrefab` не заполнено в asset режима | Назначить префаб в Inspector `GameModeData` |
| `Префаб режима '...' не содержит компонент GameMode` | В префабе отсутствует компонент-наследник `GameMode` | Добавить `RespawnMode` / `EliminationMode` на GO префаба |
| `GameModeData '...' содержит менее 2 команд` | Поле `teams[]` не заполнено в `GameModeData` | Назначить два `TeamData` asset-а в поле `teams[]` |
| `карта не выбрана` / `режим не выбран` | `StartSession()` вызван до `SetSession()` | Порядок: сначала `SetSession`, потом `StartSession` |

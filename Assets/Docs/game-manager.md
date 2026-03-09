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
`SetManager` и `RoundManager` внутри `EliminationMode` спавнятся через `NetworkServer.Spawn` — они должны быть видны клиентам. Если бы `GameMode` был `MonoBehaviour`, эти дочерние объекты не получили бы сетевой идентификатор.

**Что умеет каждый режим самостоятельно:**

| Режим | Структура | Победа | Респавн |
|---|---|---|---|
| `EliminationMode` | Матч → Сеты → Раунды | Больше сетов выиграно | ❌ нет |
| `RespawnMode` | Один длинный матч (таймер) | Больше фрагов | ✅ всегда |

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
  → запускает матч (ожидая `CanStartGameplay()`)

[Матч идёт]
Администратор нажимает "Стоп / Лобби" → AdminMenuController.OnStopMatchPressed()
  → GameplayManager.StopMatch()
  → MapManager.LoadMap("Lobby")
  → [Lobby загружается, GameplayManager уничтожен]
  → GameManager.SelectedModeId и SelectedMapScene — сохранены
```

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

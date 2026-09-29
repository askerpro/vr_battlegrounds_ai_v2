> Обновлять при изменении `SessionManager`, `MatchSeries`, `GameplayManager`, `GameModeData`,
> `GameModeRegistry`, `MapData.supportedModes` или архитектуры выбора режима.

---

# Сессия матча: выбор (режим + карты), серия карт, режим на карте

Три объекта, три ответственности:

| Объект | Где живёт | Что делает |
|---|---|---|
| `SessionManager` | `SessionContext` (спавнится в `GameNetworkManager.OnStartServer`, DontDestroyOnLoad) | Хранит **выбор** администратора: режим матча и список карт серии. Единственное место поиска данных по идентификатору: `FindModeData(modeId)`, `FindMap(sceneName)` |
| `MatchSeries` | тот же `SessionContext` | Ведёт **серию**: какая карта сейчас, общий счёт (сколько карт выиграла команда), итоги карт. Переживает смену режима на карте и смену карт |
| `GameplayManager` | объект `MatchManager` в каждой сцене (карты и лобби) | Ведёт **режим на карте**: разминка при старте, «Начать матч» — режим матча на месте, конец матча — снова разминка |

Команды (выбор игроком, выдача админом, автобаланс) — не у менеджеров, а в статическом
сервисе `MatchTeams`; право админа — `SessionPermissions.IsAdmin`.

```
DontDestroyOnLoad
  └── SessionContext (NetworkIdentity)
        ├── SessionManager   ← выбор: режим + карты следующей серии
        ├── NetworkStateRelay
        └── MatchSeries      ← ход серии: текущая карта, общий счёт
Сцена карты / лобби
  └── MatchManager
        └── GameplayManager  ← режим на карте (разминка ⇄ матч)
```

---

## API

### `SessionManager`

| Метод / Свойство | Сервер/Клиент | Описание |
|---|---|---|
| `SetSeries(modeId, maps)` | Только сервер | Режим и карты следующей серии. Реплицируется (`SyncVar` + `SyncList`). |
| `SetSession(mapScene, modeId)` | Только сервер | Серия из одной карты (то, что сейчас выбирает меню). |
| `StartSession()` | Только сервер | `MatchSeries.ServerBegin(SelectedMaps)` — грузит первую карту. |
| `SelectedGameModeData`, `SelectedModeId` | Оба | Выбранный режим матча. |
| `SelectedMaps`, `SelectedMapScene`, `SelectedMap` | Оба | Карты серии; первая карта. |
| `FindModeData(modeId)` | Оба | `GameModeData` по `modeId` из `GameModeRegistry` — **единственный путь**, и для разминки тоже. |
| `FindMap(sceneName)`, `MapRegistry`, `ModeRegistry` | Оба | Реестры и поиск карты по сцене. |

### `MatchSeries`

| Метод / Свойство | Сервер/Клиент | Описание |
|---|---|---|
| `ServerBegin(maps)` | Сервер | Сбрасывает общий счёт, запоминает карты, грузит первую (`MapManager.LoadMap`). |
| `ServerRecordMapResult(winner)` | Сервер | Итог карты в общий счёт; `null` — ничья. Зовётся сам по `GameplayManager.GameplayEnded`. |
| `ServerAdvance()` | Сервер | Следующая карта; после последней — конец серии и лобби. Зовётся сам через `_nextMapDelay` (10 с) после конца матча на карте. |
| `ServerEnd()` | Сервер | Досрочный конец (кнопка «Стоп / Лобби»). |
| `IsRunning`, `Maps`, `CurrentIndex`, `CurrentMapScene` | Оба | Состояние серии. |
| `GetMapWins(team)`, `Results` | Оба | Общий счёт: выиграно карт; итоги карт по порядку (`teamIndex`, −1 — ничья). |
| `GetRoundsWon(team, map)`, `GetKills/GetDeaths/GetAssists(session, map)` | Оба | Сквозная статистика: `map` — индекс карты серии или `MatchSeries.Total` (−1). Хранится строками `TeamStats`/`PlayerStats` (`SyncList`), TOTAL — сумма. |
| `ServerRecordRoundWin`, `ServerRecordKill` | Сервер | Запись статистики (зовётся сама по событиям `GameplayManager`). |

Конец серии (последняя карта или «Стоп / Лобби») **отпускает команды матча**: игрок с командой
не из разминки получает команду «Разминка» со своим скином
(`SessionTeamAssigner.ApplyBeforeSceneChange` — без пересоздания аватара, сцена всё равно
сменится). Иначе в лобби он стоял бы в CT без зоны и выбирал бы скины только из CT. Счёт
серии остаётся до начала следующей — его видно в лобби.

### `GameplayManager`

| Метод / Свойство | Описание |
|---|---|
| `OnStartServer` | Карта (любая, и лобби) стартует в разминке — `ServerStartWarmup()`. |
| `StartMatch()` | «Начать матч»: разминка → режим матча на месте. Режим — `MapModeRules.ResolveMatchMode`: выбор админа, если совместим с картой, иначе первый совместимый; в лобби совместимых нет — отказ (`Warning`). Вызов до спавна менеджера (автостарт отладки из `Awake`) откладывается до `OnStartServer`. |
| `StopMatch()` | Матч без победителя → разминка на этой карте. Серию не двигает, снимок паузы отбрасывает. |
| `PauseMatch()` / `ResumeMatch()` / `IsPaused` | «Пауза»: снимок матча (`MatchSnapshot`), прерванный раунд без победителя, карта — в разминку (`GameplayState.Paused`). «Продолжить»: режим матча заново со снимка — тот же номер раунда, сеты, счёт. |
| `RoundWon`, `PlayerKilled` (события экземпляра, сервер) | Раунд доигран и выигран; игрок погиб (жертва, убийца, ассистенты). Слушает `MatchSeries` — статистика. |
| `GameplayEnded` (событие экземпляра) | Режим матча объявил победителя. Слушает `MatchSeries`. Карта сразу уходит в разминку. |
| `ActiveGameMode`, `IsMatchActive`, `CurrentMap` | Режим этой машины; идёт ли матч (разминка — не матч); `MapData` своей сцены. |
| `ActiveGameModeChangedLocal` (статическое) | Режим этой машины сменился. HUD и стена арсенала переподписываются. |


---

## Система игровых режимов

Режим разделён на **два слоя**:

| Слой | Тип | Где живёт | Что делает |
|---|---|---|---|
| `GameModeData` | ScriptableObject | В `Assets/Data/GameModes/` | Данные для UI: название, иконка, `modeId`, команды, префаб логики |
| `GameMode` | NetworkBehaviour | Спавнится `GameplayManager` при старте карты (разминка) и по «Начать матч» | Полная логика режима: управляет собственной структурой матча |

**Поток жизни режима:**
```
GameModeData.modePrefab
  → Instantiate + GameMode.Initialize(data)   ← до спавна (GameplayManager.ServerSwitchTo)
  → NetworkServer.Spawn
  → GameMode.StartGameplayWhenReady()  ← команды по политике, пополнение стен, CanStartGameplay()
  → GameMode.StartGameplay()           ← режим сам управляет сетами/раундами/таймером
  → GameMode.GameplayEnded event       ← режим объявил победителя
  → GameplayManager.OnGameplayEnded → GameplayEnded (серия) → ServerSwitchTo(разминка)
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
| `WarmupMode` («Разминка») | Нет матча — переходная стадия между матчами | — | — (смерти нет) |

### Разминка и совместимость режимов с картой

Любая карта — и лобби, и боевая — стартует в **разминке** (`WarmupMode`, бывший `LobbyMode`),
пока администратор не нажмёт «Начать матч». Разминка лежит в `GameModeRegistry` рядом
с режимами матча, с флагом `GameModeData.isWarmup`: в выбор режима матча (`MenuSessionSetup.TabModes`)
она не попадает, а данные по `modeId` ищутся одним путём — `SessionManager.FindModeData`.

**Почему флаг, а не отдельный реестр.** Режим по сети — только строка `modeId`, и клиенту
нужен один путь от строки к данным. Раньше лобби-режима в реестре не было, и поиск шёл
в два места (`GameModeCatalog`: «режим сцены» у `GameplayManager`, затем реестр). Флаг
держит один реестр и одну выборку «режимы матча» для меню. Альтернатива — перечисление
«роль режима» — даёт то же при единственном нематчевом значении.

**Совместимость — у карты** (`MapData.supportedModes`), выбор из списка — чистые правила
`MapModeRules`:

| Карта | `supportedModes` | Старт | «Начать матч» |
|---|---|---|---|
| Лобби (`MapData_Lobby`, `MapRegistry.lobby`) | `[warmup]` | разминка | отказ: режимов матча нет |
| `TestMap1`, `TestMap2` | `[warmup, elimination, respawn]` | разминка | выбор админа, если совместим, иначе первый совместимый |
| Сцена не из реестра / пустой список | — | разминка реестра | выбор админа или первый режим матча реестра |

Поле «режим сцены» (`GameplayManager._sceneGameMode`) и `GameModeCatalog` удалены.

### Смена режима на месте

`GameplayManager.ServerSwitchTo`: текущий режим останавливается (`StopGameplay`, если он
не закончился сам), снимается с сети, новый инициализируется **до** спавна (modeId и команды
уезжают клиенту начальным состоянием, HUD хоста сразу знает режим) и спавнится. Сцена
не перезагружается, `GameplayManager` тот же.

Что уходит вместе с режимом: его правила-компоненты (уборка пола, карман, раундовые магазины),
счёт на карте (`_teamScores` — сеты Elimination), машина раундов. Что делает новый режим
на старте: пол чистый (`ModeStartCleanup` на префабе каждого режима, на каждой машине),
пустые слоты стен пополнены (режим поднимает `ArsenalRefillRequestedServer` в
`StartGameplayWhenReady`), стена дальше сверяется с его `ArsenalRules`. Что **не** трогается:
команды игроков, общий счёт серии (`MatchSeries`), статистика (`PlayerSession.Kills/Deaths/Score`).
Снаряжение в руках, кобурах и карманах остаётся у игрока (см. развилку в CHANGELOG 2026-09-27).

### Снаряжение не переживает переходов

При любой смене режима на карте (`ServerSwitchTo`: разминка → матч, матч → разминка, пауза,
«Продолжить») и перед сменой карты (`MatchSeries.Load`) сервер зовёт
`EquipmentStrip.ServerStripAll`: у каждого игрока руки отпускают всё, оружие и магазины из рук
и кобур **уничтожаются** через сеть (не роняются — упавшее стало бы ничьим), карман магазинов
очищается, ничьё с пола убирается. Путь общий с `AvatarTeardown` (руки → снаряжение), но
предмет исчезает, а не падает. Планшет и жетон только отпускаются (`EquipmentStrip.IsEquipment`).
Раундовые магазины Elimination (`RoundMagazineRefill`) выдаются как раньше. Первый режим карты
(старт сцены) снимать нечего. Сопутствующий патч SDK 13: команда захвата, дошедшая до сервера
уже после уничтожения предмета, разрывала соединение (`Docs/UltimateXR/sdk-patches.md`).

### Пауза и «Продолжить»

**Решение — снимок, а не приостановленный экземпляр.** На паузе карта в разминке, а режим на
карте один: живой Elimination рядом с разминкой отвечал бы на те же вопросы (оружие, арсенал,
урон) и держал бы подписки. Снимок (`MatchSnapshot`) — несколько чисел: счёт команд режима (сеты),
счёт раундов сета **без** прерванного раунда (`SetManager.ScoresAtRoundStart`), номер
прерванного раунда, остаток таймера (Respawn). Хранит его `GameplayManager` карты — пауза
это состояние матча на этой карте, со сменой карты она теряет смысл.

- Режим объявляет `GameMode.SupportsPause`, `CaptureSnapshot`, `RestoreSnapshot` (зовётся после
  `Initialize`, до спавна). Elimination и Respawn умеют, разминка — нет.
- Прерванный раунд не засчитывается ни в счёт карты, ни в общий счёт серии: серия получает раунд
  только по окончании раунда (`SetManager` → `EliminationMode.ServerReportRoundWon` на
  `CycleCompleted`), а не в момент, когда победитель стал известен.
- «Продолжить» — `SetManager.ResumeSet`: тот же сет, раунд с тем же номером заново.
- **Решение пользователя:** убийства, смерти и ассисты, случившиеся в прерванном раунде,
  **остаются** в статистике — раунд не засчитывается, но то, что в нём произошло, не откатывается.
- `EliminationMode.StartGameplay` больше не сбрасывает состояние: корутина старта зовёт его
  кадром позже, и сброс перезапускал продолженный сет с раунда 1 (найдено в Play mode).

### Сквозная статистика серии

Живёт в `MatchSeries` (объект `SessionContext`) и переживает смену режима, паузу и смену карт.
Ведётся по сессии (`MatchSeries.PlayerKey`: токен устройства, без него — `netId` сессии), а не по
аватару. Команды — выигранные раунды по картам (и выигранные карты), игроки — убийства, смерти,
ассисты по картам; TOTAL — сумма строк (`SeriesStatsTable`).

**Карта без серии** (прямая `MapManager.LoadMap` — отладка, E2E). Статистика ведётся и тогда:
неявная серия из одной текущей карты (`MatchSeries.IsAdHoc`, `IsRecording`), одна строка карты
и TOTAL, равный ей; экран «Статистика» показывает её с пометкой «Карта без серии».

- *Почему неявная серия, а не отдельный режим записи:* хранилище, репликация, геттеры и экран
  те же — различие одно: серия не идёт (`IsRunning = false`), поэтому нет перехода к следующей
  карте, нет лобби, нет кнопки «Стоп» и конец серии не отпускает команды. Отдельный режим записи
  дублировал бы строки, геттеры и экран ради того же результата.
- Начинается с первого события статистики на карте (раунд, гибель) — сцена берётся у текущего
  `GameplayManager`; в лобби событий нет, и строка там не появляется.
- **Другая карта без серии начинает статистику заново** (та же сцена — продолжает). Обоснование:
  прямые загрузки — отладка и E2E, между собой не связаны, и накопление смешало бы разные
  прогоны. Альтернатива — копить до явного сброса; развилка отмечена в CHANGELOG. Начало серии
  (`ServerBegin`) очищает статистику без серии.

**Кто убил.** Пуля приходит в `UxrActor.ReceiveImpact` с актором стрелка
(`UxrWeaponManager`: `ProjectileSource.TryGetWeaponOwner()`), тот едет в
`UxrDamageEventArgs.ActorSource`. `PlayerController.OnDamageReceiving` (урон не отменён режимом)
пишет источник в `DamageLedger`; смертельный урон тоже проходит через `DamageReceiving`
(а `DamageReceived` — нет), поэтому к `Die` последний источник — убийца. `Die` передаёт
убийцу и ассистентов (прочие ранившие) в `GameplayManager.OnPlayerDied` → режим
(`OnPlayerKilled` — фраги Respawn) и событие `PlayerKilled` → серия. Самоубийство и урон без
источника (`ReceiveDamage(float)`) убийства не дают, смерть — дают.

### Правила, которые объявляет режим

Системы вне режима не знают его конкретного типа — спрашивают базовый `GameMode`
у `GameplayManager.Instance.ActiveGameMode` (статический дубль `GameMode.Current` удалён):

| Свойство / событие | Кто читает | `EliminationMode` | `WarmupMode` |
|---|---|---|---|
| `WeaponsEnabled` | `GameplayManager.Update` → `UxrWeaponManager` | только в `Combat` | всегда |
| `ArsenalRules.IsOpen` | `ArsenalWallController.ApplyModeRules` (сервер) | только в `Equipment` | всегда |
| `ArsenalRules.UsesReadinessTag` | стена, каждая машина | `RoundStartRule == Readiness` | нет |
| `ArsenalRules.ReplacesLostWeapons` | стена, сервер | нет | да, через 2 с |
| `ArsenalRefillRequestedServer` (событие **экземпляра**) | стена, сервер (переподписка по `ActiveGameModeChangedLocal`) | на старте режима и на входе в `Setup` | на старте режима |
| `OnPlayerDied(player)` | `GameplayManager.OnPlayerDied` | условие победы раунда | — (смерти нет) |
| `PlayersTakeDamage` | `PlayerController` на `UxrActor.DamageReceiving` (отмена урона) | да | нет |
| `TeamChoiceLocked` | `TeamChangeRules`, планшет | после старта матча | нет |
| `IsWarmup`, `ModeData` (`modeId` SyncVar) | HUD, политика команд, минимум игроков, спавн | `Elimination_GameModeData` | `Warmup_GameModeData` (HUD нет) |

Свойства — состояние, стена сверяется с ним каждый кадр; событие — разовое пополнение пустых
слотов, потому что фаза `Setup` бывает короче кадра. Событие стало событием экземпляра:
режим на карте меняется на месте, и статическое событие пришлось бы разбирать, чей это запрос.

### Раздача команд режимом

Политика — одно перечисление в данных режима (`GameModeData.teamAssignment`), ветка по нему —
в `GameMode.ServerAssignTeams`. Интерфейс `ITeamAssignmentPolicy` и `PlayerChoiceTeamPolicy`
удалены: точка расширения без второго потребителя. Чистый расчёт `TeamAutoBalance.Plan` остался.

| `teamAssignment` | Что делает | Где |
|---|---|---|
| `KeepOrDefault` | команда матча (любая ненулевая) сохраняется; игрок без команды получает первую команду режима | `Warmup_GameModeData` |
| `AutoBalance` | все без команды режима — в самую малочисленную (`TeamAutoBalance.Plan`) | сейчас нигде (и разово — кнопкой админа) |
| `PlayerChoice` | никого: выбирает игрок в планшете или выдаёт админ; матч ждёт, пока команда будет у всех | `Elimination_GameModeData`, `Respawn_GameModeData` |

Режим без данных (EditMode-тесты) — `PlayerChoice`; в тестах значение подменяется
присваиванием `GameMode.TeamAssignment`. Политика применяется при старте режима
(`StartGameplayWhenReady`) и при каждом подключении (`PlayersManager.OnSessionConnected`).

**Входы смены команды** — статический сервис `MatchTeams` (раньше жили в `GameplayManager`),
правила — `TeamChangeRules`, право админа — `SessionPermissions.IsAdmin` (Player), исполнение —
`SessionTeamAssigner`:

| Кто | Вход | Правило |
|---|---|---|
| Игрок (планшет) | `PlayerSession.CmdRequestTeamChange` → `MatchTeams.ServerPlayerRequest(mode, …)` | скин в своей команде — всегда (и в разминке с командой матча); команда — только из активного режима и пока `TeamChoiceLocked == false` |
| Админ (экран «Игроки и команды») | `PlayerSession.CmdAdminAssignTeam` → `MatchTeams.ServerAdminAssign(mode, admin, target, teamId)` | право админа (хост или `IsAdmin`), любая команда `TeamRegistry`, в любой момент |
| Админ, разово | `PlayerSession.CmdAdminAutoBalance` → `MatchTeams.ServerAdminAutoBalance(mode, admin)` | автобаланс игроков без команды режима; политику режима не меняет |
| Режим | `GameMode.ServerAssignTeams` | по `teamAssignment` |
| Конец серии | `MatchSeries` → `SessionTeamAssigner.ApplyBeforeSceneChange` | команда не из разминки → «Разминка», скин сохраняется |

**Почему выбор закрывается стартом матча.** После старта смена стороны — это выход из
раунда посреди боя: составы уже разыграны (сеты, смена сторон), а перебежчик ломает
баланс. Опоздавшему команду выдаёт админ. `TeamChoiceLocked` у Elimination —
`_matchState != WaitingForPlayers`, у Respawn — идёт ли матч; это SyncVar-состояние,
поэтому планшет клиента знает его сам.

**Планшет в разминке.** Игроку с командой матча (CT/T) планшет предлагает только его команду —
смену скина (`MenuTeamSelection.ResolveAvailableTeams`); иначе выбор скина «Разминки» молча
перевёл бы его из команды матча. Игроку без команды — «Разминка» (все скины).

**Матч ждёт команд.** `GameMode.AllPlayersHaveModeTeam()` — у каждого подключённого игрока
(не зрителя) есть команда режима; состав берётся у `PlayerRoster`. Elimination проверяет его
в `IsPlayersReady` вместе с минимумом игроков из своих данных (`MinPlayersToStart`).
Respawn — в `CanStartGameplay`.

**Исполнение** (`SessionTeamAssigner`): поднимает `MatchTeams.TeamChangeRequested`
(хуки режима); нет аватара — пишет команду в сессию (спавн сам возьмёт зону и скин);
аватар жив — `AvatarManager.ChangeAvatar`, **на том же месте**. Скин по выбору игрока либо
сохраняется (`TeamData.IndexOfAvatar`) для админа, политики и конца серии.

**Состояние нового аватара.** `AvatarManager` после спавна зовёт `GameMode.ServerAdmitAvatar(avatar,
continuesPrevious)`. Замена (`ChangeAvatar` при живом прежнем) сначала переносит здоровье или выбывание
(`AvatarManager.CarryLifeState`), и `continuesPrevious = true`; аватар без прошлого (смена карты, первый
вход) — `false`. Elimination в матче делает такой аватар выбывшим и назначает возрождение в зоне
(подготовка, закупка); аватары, созданные до старта режима, проходят то же правило в `StartGameplay`.

Игрок без команды режима на карте появляется в нейтральной точке (откалиброванный — по
калибровке), лог уровня `Info`: это ожидание выбора, а не сбой. То же для «Разминки» на
боевой карте — зон у неё там нет по построению. Зрители (`GameRole.Spectator`) команд
не получают. Тесты — `MatchFlowTests`, `MapModeRulesTests`, `TeamChoiceTests`,
`TeamAutoBalanceTests`, `GameModeRulesTests`, `GameModeWiringTests`.

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
| `Warmup_GameModeData.asset` | `warmup` | Разминка | `WarmupMode.prefab` | [«Разминка»] — `isWarmup`, `KeepOrDefault` |

Сохранить в: `Assets/Data/GameModes/`

---

## Шаг 3 — Создать GameModeRegistry asset

Один asset на проект — список всех режимов.

**ПКМ в Project → Create → VrBattlegrounds → Game Mode Registry**

В поле `Modes[]` добавить оба созданных `GameModeData` asset-а.

Сохранить в: `Assets/Data/GameModes/GameModeRegistry.asset`

---

## Шаг 4 — Настроить SessionContext

Префаб `Assets/Prefabs/Managers/SessionContext.prefab` (спавнит `GameNetworkManager`): на нём
`SessionManager` и `MatchSeries`. В Inspector `SessionManager` назначить:

| Поле | Что назначить |
|---|---|
| `Map Registry` | `Assets/Data/Maps/MapRegistry.asset` |
| `Game Mode Registry` | `Assets/Data/GameModes/GameModeRegistry.asset` |

---

## Шаг 5 — Настроить меню выбора сессии

На `MenuSessionSetup` (планшет админа в лобби):

| Поле | Что назначить |
|---|---|
| `Map Registry` | `Assets/Data/Maps/MapRegistry.asset` |
| `Game Mode Registry` | `Assets/Data/GameModes/GameModeRegistry.asset` |

---

## Поток действий администратора

```
[Лобби — разминка]
Админ выбирает режим (вкладка), кликает карты по порядку — очередь с номерами (MapQueue),
повторный клик убирает карту → «Начать» → PlayerSession.CmdAdminStartSeries
  → AdminMatchCommands.ServerStartSeries → SessionManager.SetSeries + StartSession
  → MatchSeries.ServerBegin(maps) → MapManager.LoadMap(maps[0])

[Карта — разминка]  GameplayManager.OnStartServer → ServerStartWarmup
  команды матча сохраняются, игрок без команды — «Разминка»; арсенал открыт, урона нет

Админ «Начать матч» → MenuMatchManager.OnStartMatchPressed → GameplayManager.StartMatch
  → MapModeRules.ResolveMatchMode(карта, выбор админа) → режим матча на месте
  → игрок без команды матча выбирает её в планшете (или выдаёт админ)
  → матч ждёт, пока команда режима будет у всех (GameMode.AllPlayersHaveModeTeam)

[Матч кончился]  GameMode.RaiseGameplayEnded → GameplayManager.GameplayEnded
  → MatchSeries.ServerRecordMapResult (общий счёт)
  → карта — снова разминка
  → через _nextMapDelay (10 с) MatchSeries.ServerAdvance
       → следующая карта серии (стартует в разминке)
       → или, после последней: команды матча → «Разминка», LoadMap(лобби)

[Экран «Матч» у админа] кнопки — PlayerSession.CmdAdminMatchCommand → AdminMatchCommands.ServerExecute
  «Начать матч» — разминка → матч        (видна: матча нет, карта допускает матч)
  «Пауза»       — PauseMatch             (видна: идёт матч)
  «Продолжить»  — ResumeMatch            (видна: пауза)
  «Стоп»        — MatchSeries.ServerEnd → лобби (видна: идёт серия)
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

3.  Создать `GameModeData` asset: `modeId = "my_mode"`, назначить `modePrefab` и `teams[]`;
    на префаб добавить `ModeStartCleanup` (проверяет `GameModeWiringTests`)

4.  Добавить asset в `GameModeRegistry.modes[]` и в `supportedModes` карт, где режим допустим

> Менять `GameplayManager`, `SetManager`, `RoundManager` и сцены карт **не нужно**.

---

## Частые ошибки

| Ошибка в логе | Причина | Решение |
|---|---|---|
| `Матч не начат: на карте '…' нет совместимого режима матча` | «Начать матч» в лобби или на карте без режимов матча в `supportedModes` | Штатно для лобби; для карты — добавить режим в `MapData.supportedModes` |
| `GameModeData '...' не содержит modePrefab` | Поле `modePrefab` не заполнено в asset режима | Назначить префаб в Inspector `GameModeData` |
| `Префаб режима '...' не содержит компонент GameMode` | В префабе отсутствует компонент-наследник `GameMode` | Добавить `RespawnMode` / `EliminationMode` на GO префаба |
| `GameModeData '...' содержит менее 2 команд` | Поле `teams[]` не заполнено в `GameModeData` | Назначить два `TeamData` asset-а в поле `teams[]` |
| `карта не выбрана` / `режим не выбран` | `StartSession()` вызван до `SetSeries()`/`SetSession()` | Порядок: сначала `SetSeries`, потом `StartSession` |
| `На карте '…' нет разминки` | В реестре нет режима с `isWarmup` | Вернуть `Warmup_GameModeData` в `GameModeRegistry` |

> Обновлять при изменении `SessionManager`, `Series`, `MapReferee`, `GameModeData`,
> `GameModeRegistry`, `MapData.supportedModes` или архитектуры выбора режима.

---

# Сессия матча: выбор (режим + карты), серия карт, режим на карте

Владельцы состояния:

| Объект | Где живёт | Что делает |
|---|---|---|
| `SessionManager` | `SessionContext` (спавнится в `GameNetworkManager.OnStartServer`, DontDestroyOnLoad) | Хранит **выбор** администратора: режим матча и список карт серии. Единственное место поиска данных по идентификатору: `FindModeData(modeId)`, `FindMap(sceneName)` |
| `Series` | тот же `SessionContext` | Ведёт **серию**: какая карта сейчас, общий счёт (сколько карт выиграла команда), итоги карт. Переживает смену режима на карте и смену карт |
| `MapRunAuthority` | тот же `SessionContext` | **Единственный издатель** описания текущего запуска карты (`MapRunSnapshot`): config, статус сборки, `MapState`, активный режим и его эпоха. Сервер и клиенты читают состояние карты только отсюда |
| `MapBootstrap` | объект `MapRoot` в каждой сцене реестра | **Собирает запуск карты**: проверяет `MapRoot`, разрешает `MapRunConfig` по `MapRuntimeCatalog`, спавнит служебные `MapReferee` и координатор развёртывания, открывает server Ready |
| `MapReferee` | спавнится `MapBootstrap` из `MapRuntimeCatalog` (в сцене его нет) | Ведёт **режим на карте**: разминка при старте, «Начать матч» — режим матча на месте, конец матча — снова разминка. Каждую смену режима публикует через `MapRunAuthority` |

Устройство запуска, допуск и отказы — раздел [Запуск карты](#запуск-карты-mapbootstrap) ниже;
дизайн и решения — [map-runtime-bootstrap-design](tasks/map-runtime-bootstrap-design.md).

Команды (выбор игроком, выдача админом, автобаланс) — не у менеджеров, а в статическом
сервисе `TeamChangeRequests`; право админа — `SessionPermissions.IsAdmin`.

```
DontDestroyOnLoad
  └── SessionContext (NetworkIdentity)
        ├── SessionManager   ← выбор: режим + карты следующей серии
        ├── NetworkStateRelay
        ├── Series           ← ход серии: текущая карта, общий счёт, режим серии
        └── MapRunAuthority  ← описание текущего запуска карты (единственный издатель)
Сцена карты / лобби (авторское)
  ├── MapRoot + MapBootstrap  ← паспорт карты и ссылки; сборка запуска
  ├── Environment / Gameplay / PhysicalArenaLayout
Сцена карты (создаёт сервер, NetworkServer.Spawn)
  ├── MapReferee                    ← режим на карте (разминка ⇄ матч)
  ├── ArsenalEquipmentCoordinator   ← выдвижение оборудования станций
  └── <режим> (WarmupMode / EliminationMode / …)
```

---

## API

### `SessionManager`

| Метод / Свойство | Сервер/Клиент | Описание |
|---|---|---|
| `SetSeries(modeId, maps)` | Только сервер | Режим и карты следующей серии. Реплицируется (`SyncVar` + `SyncList`). |
| `SetSession(mapScene, modeId)` | Только сервер | Серия из одной карты (то, что сейчас выбирает меню). |
| `StartSession()` | Только сервер | `Series.ServerBegin(SelectedMaps)` — грузит первую карту. |
| `SelectedGameModeData`, `SelectedModeId` | Оба | Выбранный режим матча. |
| `SelectedMaps`, `SelectedMapScene`, `SelectedMap` | Оба | Карты серии; первая карта. |
| `FindModeData(modeId)` | Оба | `GameModeData` по `modeId` из `GameModeRegistry` — **единственный путь**, и для разминки тоже. |
| `FindMap(sceneName)`, `MapRegistry`, `ModeRegistry` | Оба | Реестры и поиск карты по сцене. |

### `Series`

| Метод / Свойство | Сервер/Клиент | Описание |
|---|---|---|
| `ServerBegin(maps, modeId)` | Сервер | Сбрасывает общий счёт, запоминает карты и **режим серии** (`CapturedModeId`), грузит первую (`MapLoader.LoadMap`). Выбор режима в меню во время серии относится к следующей серии. Снаряжение изымается только под принятую загрузку (`MapLoader.CanAcceptLoad`). |
| `ServerRecordMapResult(winner)` | Сервер | Итог карты в общий счёт; `null` — ничья. Зовётся сам по `MapReferee.Finished`. |
| `ServerAdvance()` | Сервер | Следующая карта; после последней — конец серии и лобби. Зовёт только кнопка админа «Следующая карта» (`MapCommand.NextMap`): после конца матча карта стоит в разминке и ждёт. |
| `ServerEnd()` | Сервер | Досрочный конец (кнопка «Стоп / Лобби»). |
| `IsRunning`, `Maps`, `CurrentIndex`, `CurrentMapScene` | Оба | Состояние серии. |
| `GetMapWins(team)`, `Results` | Оба | Общий счёт: выиграно карт; итоги карт по порядку (`teamIndex`, −1 — ничья). |
| `GetRoundsWon(team, map)`, `GetKills/GetDeaths/GetAssists(session, map)` | Оба | Сквозная статистика: `map` — индекс карты серии или `Series.Total` (−1). Хранится строками `TeamStats`/`PlayerStats` (`SyncList`), TOTAL — сумма. |
| `ServerRecordRoundWin`, `ServerRecordKill` | Сервер | Запись статистики (зовётся сама по событиям `MapReferee`). |

Конец серии (последняя карта или «Стоп / Лобби») **распускает команды**: у всех игроков
команда снимается (`SessionTeamAssigner.ClearBeforeSceneChange` — без пересоздания аватара,
сцена всё равно сменится). В лобби игрок появляется без команды — киборгом — и выбирает
команду на следующую серию. Счёт серии остаётся до начала следующей — его видно в лобби.

### `MapReferee`

| Метод / Свойство | Описание |
|---|---|
| `ServerStartRun()` (internal) | Карта (любая, и лобби) стартует в разминке. Зовёт `MapBootstrap` после CompositionReady; коммит разминки открывает server Ready. |
| `GoLive()` | «Начать матч»: разминка → режим матча на месте. Режим — согласованный при загрузке `MapRunConfig.MatchIntent` (режим серии, если совместим с картой, иначе первый совместимый); в лобби режима матча нет — отказ (`Warning`). Вызов до server Ready (автостарт отладки из `Awake`) откладывается и выполняется сразу после разминки. |
| `Stop()` | Матч без победителя → разминка на этой карте. Серию не двигает, снимок паузы отбрасывает. |
| `Pause()` / `Resume()` / `IsPaused` | «Пауза»: снимок матча (`PauseSnapshot`), прерванный раунд без победителя, карта — в разминку (`MapState.Paused`). «Продолжить»: режим матча заново со снимка — тот же номер раунда и счёт карты. |
| `RoundWon`, `PlayerKilled` (события экземпляра, сервер) | Раунд доигран и выигран; игрок погиб (жертва, убийца, ассистенты). Слушает `Series` — статистика. |
| `Finished` (событие экземпляра) | Режим матча объявил победителя. Слушает `Series`. Карта сразу уходит в разминку. |
| `ActiveGameMode`, `IsLiveOrPaused`, `CurrentState`, `CurrentMap` | Режим этой машины; идёт ли матч (разминка — не матч); состояние карты — из `MapRunAuthority.Current`; `MapData` своей сцены. |
| `ActiveGameModeChangedLocal` (статическое) | Режим этой машины сменился. Нотификации часов (`WatchGameEvents`) и стена арсенала переподписываются. Удалённый клиент принимает только режим с `netId` из descriptor: поздний `OnStartClient` прежнего режима текущую ссылку не перебивает. |

Неуправляемый судья (стенды `BotCombatStand`, `CommonArsenalReview` и EditMode-тесты: `MapReferee`
лежит в сцене или создан тестом, `MapBootstrap` нет) сохраняет прежний путь: разминка в
`OnStartServer`, состояние — собственный `SyncVar`. Карта реестра, стартующая так, пишет
`Warning` — её нужно перевести на `MapRoot`.

## Запуск карты (`MapBootstrap`)

Новая карта не требует ручной расстановки служебных менеджеров: автор кладёт один `MapRoot`
(паспорт `MapData`, корни `Environment`/`Gameplay`, `PhysicalArenaLayout`, зоны и станции), остальное
собирает `MapBootstrap` по центральному `MapRuntimeCatalog` (назначен в `GameNetworkManager`).

```
Сервер (любой путь старта: смена карты, onlineScene, сцена без смены — опрос предусловий в Update)
  MapRunAuthority готов + Mirror догрузил сцену
  → MapRoot.ValidateBindings(includeSceneScans: false) ← отказ: MapRoot.Invalid [ошибки]
  → MapRuntimeCatalog.Resolve(request)          ← режим: Series.CapturedModeId / выбор админа / NoMatch
  → MapRunAuthority.BeginRun                    ← новый RunKey, статус Preparing
  → ArsenalStationPresetBinding.Prepare(MapData.arsenalPreset)
  → Instantiate MapReferee + ArsenalEquipmentCoordinator → в сцену карты → NetworkServer.Spawn
  → CommitPrepared                              ← CompositionReady: gameplay ещё закрыт
  → MapReferee.ServerStartRun → разминка → CommitMode  ← server Ready: допуск открыт
  → отложенные аватары (MapRunAdmission.DrainAvatars), стены пополняются запросом режима
MapLoader.MapLoadStarted → MapRunAuthority.Close ← Closing: scope.Cancellation отменён, допуск закрыт
Выгрузка сцены → MapRunAuthority.Retire → scope снимает службы, отложенные аватары сбрасываются
```

**Closing** отделяет закрытие писателей от teardown. Принятая загрузка следующей карты сразу
закрывает текущий запуск. Все выдачи предметов карты проверяют допуск в своей единственной точке:
оружие стены — `ReplenishSlotsWhere`, магазины станции — `ArsenalMagazineSupply.SpawnStock`, карман и
кобура игрока по правилам режима — `PlayerLoadoutManager.ServerEnsureMagazines`/`ServerGiveWeapon`
(через `MapRunAdmission.CanActivateActiveMap`: аватары живут вне сцены карты), покупка бота. Иначе
бесконечный карман разминки досыпал бы магазины после изъятия снаряжения серией, и они уехали бы на
следующую карту. `MapReferee` не коммитит новый режим, аватары не создаются, владельцы по
`MapRunScope.Cancellation` прекращают пополнение и RPC. Службы и станции живут до выгрузки сцены. Ещё не начатый запуск
этой сцены после `MapLoadStarted` не начинается.

**Допуск (`MapRunAdmission`)** — производный ответ, без своего флага готовности:
`CanActivateMapGameplay(scene)` истинно для сцены без `MapBootstrap` (стенд) и для управляемой —
только после server Ready и до Closing. До него стена арсенала не выдаёт оружие (Mirror спавнит станции
сцены раньше сборки), а `AvatarManager` откладывает создание тела: по одному запросу на сессию,
делегат читает команду и скин сессии в момент допуска.

**Проверки карты в рантайме и в редакторе разные.** Сканы всей сцены (уникальность сетевых
`sceneId` и UltimateXR id каждого компонента) — авторская проверка: preflight сборки, миграция,
`MapRunPreflight`. В рантайме системы законно добавляют в сцену свои объекты — `UxrManager`
навешивает `UxrCanvas` с пустым id на world-space канвасы (known-issues, Issue 36), — поэтому
`MapBootstrap` проверяет только привязки и станции, а целостность сцены подтверждает отпечаток
содержимого из каталога.

**Отказ** — именованный `GameLog.Error` с картой, кодом и до 10 причин (`MapRoot.Invalid`,
`Map.Resolution`, `Composition.Exception`, `Mode.WarmupFailed`…). Оружие, аватары и режим при
этом не выдаются. Сломанную карту ловит раньше сборка: `MapCatalogBuildStep` перезапекает
каталог и прогоняет preflight всего реестра (`Tools/VR Battlegrounds/Maps/Map Bootstrap/Rebake Catalog`
делает то же вручную).

**Клиент** ничего не создаёт: `MapBootstrap` связывает заспавненный координатор со станциями
своей сцены по `netId` из descriptor и сверяет отпечаток содержимого карты с сервером
(несовпадение — разные сборки, `GameLog.Error`).


---

## Система игровых режимов

Режим разделён на **два слоя**:

| Слой | Тип | Где живёт | Что делает |
|---|---|---|---|
| `GameModeData` | ScriptableObject | В `Assets/Data/GameModes/` | Данные для UI: название, иконка, `modeId`, команды, префаб логики |
| `GameMode` | NetworkBehaviour | Спавнится `MapReferee` при старте карты (разминка) и по «Начать матч» | Полная логика режима: управляет собственной структурой матча |

**Поток жизни режима:**
```
GameModeData.modePrefab
  → Instantiate + GameMode.Initialize(data)   ← до спавна (MapReferee.ServerSwitchTo)
  → NetworkServer.Spawn
  → GameMode.BeginWhenReady()  ← команды по политике, пополнение стен, CanBegin()
  → GameMode.Begin()           ← режим сам управляет раундами/таймером
  → GameMode.Finished event       ← режим объявил победителя
  → MapReferee.OnModeFinished → Finished (серия) → ServerSwitchTo(разминка)
  → NetworkServer.UnSpawn + Destroy (MapReferee.CleanupGameMode)
```

**Почему `GameMode : NetworkBehaviour` а не `MonoBehaviour`:**
режим сам держит реплицируемое состояние матча — счёт команд, номер раунда, фазу раунда
(`SyncVar`, `SyncList`, `SyncDictionary`) — и сам шлёт `ClientRpc`. Без `NetworkBehaviour`
клиентам не досталось бы ничего из этого.

> `RoundPhases` при этом **не** сетевой и даже не `MonoBehaviour`: это обычный C#-объект,
> создаваемый через `new` в `EliminationMode.InitializeActiveGame`, и тикает его
> `EliminationMode.ServerTick`. Счёт карты и переход «раунд → раунд» — в самом режиме, фазы
> раунда — в `RoundPhases`. Всё это покрывают EditMode-тесты через `ServerTick`. (Раньше здесь было написано, что они
> спавнятся через `NetworkServer.Spawn` — это никогда не соответствовало коду.)


**Что умеет каждый режим самостоятельно:**

| Режим | Структура | Победа | Респавн |
|---|---|---|---|
| `EliminationMode` | Карта → две половины → раунды | Больше раундов за карту (досрочно — больше половины всех) | ❌ нет |
| `RespawnMode` | Один длинный матч (таймер) | Больше фрагов | ✅ всегда |
| `WarmupMode` («Разминка») | Не режим матча: карта без запущенного матча или с матчем на паузе | — | — (смерти нет) |

### Разминка и совместимость режимов с картой

Разминка (`WarmupMode`) — **не режим матча, а состояние карты**: «режим матча не запущен
или на паузе». Её включает `MapReferee` сам — на старте карты (и лобби, и боевой), на паузе
и после конца матча. Её не выбирают, у неё нет команд и победителя.

**Почему всё же объект-режим, а не «режима нет».** Правила разминки — ответы на те же вопросы,
что задают любому режиму: стрелять ли, открыт ли арсенал, рисовать ли зоны, можно ли сменить
команду. Стена, оружие, HUD и планшет спрашивают `ActiveGameMode` и не знают его типа. Если
бы разминка была «отсутствием режима» (`null`), правила разминки пришлось бы знать каждому
потребителю по отдельности. Отличается разминка жизненным циклом — его и ведёт `MapReferee`.

**Где лежит.** Отдельное поле `GameModeRegistry.warmup`, а не элемент `modes`: в каталог режимов
матча (вкладки `MenuSessionSetup.TabModes`, `MatchModes`) и в списки карт она не входит, а по
`modeId` ищется тем же путём, что и режимы матча (`GameModeRegistry.GetById` →
`SessionManager.FindModeData`). Разминка ли режим — `GameMode.IsWarmup`, свойство класса
(`WarmupMode`), а не флаг данных.

**Совместимость — у карты** (`MapData.supportedModes`), выбор из списка — чистые правила
`MapModeRules`:

| Карта | `supportedModes` | Старт | «Начать матч» |
|---|---|---|---|
| Лобби (`MapData_Lobby`, `MapRegistry.lobby`) | `[]` | разминка | отказ: режимов матча нет |
| `TestMap1`, `TestMap2` | `[elimination, respawn]` | разминка | выбор админа, если совместим, иначе первый совместимый |
| Сцена не из реестра | — | разминка | выбор админа или первый режим матча реестра |

Поле «режим сцены» (`MapReferee._sceneGameMode`) и `GameModeCatalog` удалены.

### Смена режима на месте

`MapReferee.ServerSwitchTo`: текущий режим останавливается (`ForceStop`, если он
не закончился сам), снимается с сети, новый инициализируется **до** спавна (modeId и команды
уезжают клиенту начальным состоянием, HUD хоста сразу знает режим) и спавнится. Сцена
не перезагружается, `MapReferee` тот же.

Что уходит вместе с режимом: его правила-компоненты (уборка пола, карман, раундовые магазины),
счёт на карте (`_teamScores` — раунды Elimination), машина раундов. Что делает новый режим
на старте: пол чистый (`ModeStartCleanup` на префабе каждого режима, на каждой машине),
пустые слоты стен пополнены (режим поднимает `ArsenalRefillRequestedServer` в
`BeginWhenReady`), стена дальше сверяется с его `ArsenalRules`. Что **не** трогается:
команды игроков, общий счёт серии (`Series`), статистика (`PlayerSession.Kills/Deaths/Score`).
Снаряжение в руках, кобурах и карманах остаётся у игрока (см. развилку в CHANGELOG 2026-09-27).

### Снаряжение не переживает переходов

При любой смене режима на карте (`ServerSwitchTo`: разминка → матч, матч → разминка, пауза,
«Продолжить») и перед сменой карты (`Series.Load`) сервер зовёт
`EquipmentStrip.ServerStripAll`: у каждого игрока руки отпускают всё, оружие и магазины из рук
и кобур **уничтожаются** через сеть (не роняются — упавшее стало бы ничьим), карман магазинов
очищается, ничьё с пола убирается. Путь общий с `AvatarTeardown` (руки → снаряжение), но
предмет исчезает, а не падает. Планшет и жетон только отпускаются (`EquipmentStrip.IsEquipment`).
Раундовые магазины Elimination (`RoundMagazineRefill`) выдаются как раньше. Первый режим карты
(старт сцены) снимать нечего. Сопутствующий патч SDK 13: команда захвата, дошедшая до сервера
уже после уничтожения предмета, разрывала соединение (`Docs/UltimateXR/sdk-patches.md`).

### Пауза и «Продолжить»

> Экономика (T-45): компоненты префаба режима с интерфейсом `IPauseSnapshotPart` (`MatchEconomy`) кладут своё
> в снимок — деньги и счётчики поражений **на начало прерванного раунда**; «Продолжить» их возвращает.

**Решение — снимок, а не приостановленный экземпляр.** На паузе карта в разминке, а режим на
карте один: живой Elimination рядом с разминкой отвечал бы на те же вопросы (оружие, арсенал,
урон) и держал бы подписки. Снимок (`PauseSnapshot`) — несколько чисел: счёт команд режима (у Elimination — раунды
за карту **без** прерванного раунда, `_scoresAtRoundStart`), номер прерванного раунда,
остаток таймера (Respawn). Хранит его `MapReferee` карты — пауза
это состояние матча на этой карте, со сменой карты она теряет смысл.

- Режим объявляет `GameMode.SupportsPause`, `CaptureSnapshot`, `RestoreSnapshot` (зовётся после
  `Initialize`, до спавна). Elimination и Respawn умеют, разминка — нет.
- Прерванный раунд не засчитывается ни в счёт карты, ни в общий счёт серии: серия получает раунд
  только по окончании раунда (`EliminationMode.TickRounds` → `RaiseRoundWon` на
  `CycleCompleted`), а не в момент, когда победитель стал известен.
- «Продолжить» — `EliminationMode.InitializeActiveGame` со снимка: раунд с тем же номером заново,
  стороны по номеру раунда (вторая половина — поменяны).
- **Решение пользователя:** убийства, смерти и ассисты, случившиеся в прерванном раунде,
  **остаются** в статистике — раунд не засчитывается, но то, что в нём произошло, не откатывается.
- `EliminationMode.Begin` больше не сбрасывает состояние: корутина старта зовёт его
  кадром позже, и сброс перезапускал продолженный сет с раунда 1 (найдено в Play mode).

### Сквозная статистика серии

Живёт в `Series` (объект `SessionContext`) и переживает смену режима, паузу и смену карт.
Ведётся по сессии (`Series.PlayerKey`: токен устройства, без него — `netId` сессии), а не по
аватару. Команды — выигранные раунды по картам (и выигранные карты), игроки — убийства, смерти,
ассисты по картам; TOTAL — сумма строк (`SeriesStatsTable`).

**Карта без серии** (прямая `MapLoader.LoadMap` — отладка, E2E). Статистика ведётся и тогда:
неявная серия из одной текущей карты (`Series.IsAdHoc`, `IsRecording`), одна строка карты
и TOTAL, равный ей; экран «Статистика» показывает её с пометкой «Карта без серии».

- *Почему неявная серия, а не отдельный режим записи:* хранилище, репликация, геттеры и экран
  те же — различие одно: серия не идёт (`IsRunning = false`), поэтому нет перехода к следующей
  карте, нет лобби, нет кнопки «Стоп» и конец серии не отпускает команды. Отдельный режим записи
  дублировал бы строки, геттеры и экран ради того же результата.
- Начинается с первого события статистики на карте (раунд, гибель) — сцена берётся у текущего
  `MapReferee`; в лобби событий нет, и строка там не появляется.
- **Другая карта без серии начинает статистику заново** (та же сцена — продолжает). Обоснование:
  прямые загрузки — отладка и E2E, между собой не связаны, и накопление смешало бы разные
  прогоны. Альтернатива — копить до явного сброса; развилка отмечена в CHANGELOG. Начало серии
  (`ServerBegin`) очищает статистику без серии.

**Кто убил.** Пуля приходит в `UxrActor.ReceiveImpact` с актором стрелка
(`UxrWeaponManager`: `ProjectileSource.TryGetWeaponOwner()`), тот едет в
`UxrDamageEventArgs.ActorSource`. `PlayerController.OnDamageReceiving` (урон не отменён режимом)
пишет источник в `DamageLedger`; смертельный урон тоже проходит через `DamageReceiving`
(а `DamageReceived` — нет), поэтому к `Die` последний источник — убийца. `Die` передаёт
убийцу и ассистентов (прочие ранившие) в `MapReferee.OnPlayerDied` → режим
(`OnPlayerKilled` — фраги Respawn) и событие `PlayerKilled` → серия. Самоубийство и урон без
источника (`ReceiveDamage(float)`) убийства не дают, смерть — дают.

### Правила, которые объявляет режим

Системы вне режима не знают его конкретного типа — спрашивают базовый `GameMode`
у `MapReferee.Instance.ActiveGameMode` (статический дубль `GameMode.Current` удалён):

| Свойство / событие | Кто читает | `EliminationMode` | `WarmupMode` |
|---|---|---|---|
| `WeaponsEnabled` | `MapReferee.Update` → `UxrWeaponManager` | только в `Combat` | всегда |
| `ArsenalRules.IsOpen` | `ArsenalWallController.ApplyModeRules` (сервер) | только в `Equipment` | всегда |
| `ArsenalRules.UsesReadinessTag` | стена, каждая машина | `RoundStartRule == Readiness` | нет |
| `ArsenalRules.ReplacesLostWeapons` | стена, сервер | нет | да, через 2 с |
| `ArsenalRefillRequestedServer` (событие **экземпляра**) | стена, сервер (переподписка по `ActiveGameModeChangedLocal`) | на старте режима и на входе в `Setup` | на старте режима |
| `OnPlayerDied(player)` | `MapReferee.OnPlayerDied` | условие победы раунда | — (смерти нет) |
| `PlayersTakeDamage` | `PlayerController` на `UxrActor.DamageReceiving` (отмена урона) | да | нет |
| `TeamChoiceLocked` | `TeamChangeRules`, планшет | после старта матча | нет |
| `IsWarmup`, `ModeData` (`modeId` SyncVar) | планшет, политика команд, минимум игроков, спавн | `Elimination_GameModeData` | `Warmup_GameModeData` |
| компонент `MatchEconomy` на префабе (`MatchEconomy.Current`) | стена арсенала, правило хвата, табло, часы | есть: деньги CS2, покупки, владельцы стен, стартовый пистолет (T-45) | нет — всё бесплатно |
| `RoundBeganServer(round, firstOfHalf)`, `RoundScoredServer(winner)` (события **экземпляра** `EliminationMode`) | `MatchEconomy` (сервер) | начало раунда (сброс денег в начале половины), исход раунда (выплаты) | — |

Свойства — состояние, стена сверяется с ним каждый кадр; событие — разовое пополнение пустых
слотов, потому что фаза `Setup` бывает короче кадра. Событие стало событием экземпляра:
режим на карте меняется на месте, и статическое событие пришлось бы разбирать, чей это запрос.

### Раздача команд режимом

Политика — одно перечисление в данных режима (`GameModeData.teamAssignment`), ветка по нему —
в `GameMode.ServerAssignTeams`. Интерфейс `ITeamAssignmentPolicy` и `PlayerChoiceTeamPolicy`
удалены: точка расширения без второго потребителя. Чистый расчёт `TeamAutoBalance.Plan` остался.

| `teamAssignment` | Что делает | Где |
|---|---|---|
| `AutoBalance` | все без команды режима — в самую малочисленную (`TeamAutoBalance.Plan`) | сейчас нигде (и разово — кнопкой админа) |
| `PlayerChoice` | никого: выбирает игрок в планшете или выдаёт админ; матч ждёт, пока команда будет у всех | `Elimination_GameModeData`, `Respawn_GameModeData` |

Режим без данных (EditMode-тесты) — `PlayerChoice`; в тестах значение подменяется
присваиванием `GameMode.TeamAssignment`. Политика применяется при старте режима
(`BeginWhenReady`) и при каждом подключении (`PlayersManager.SessionConnected`).

**Входы смены команды** — статический сервис `TeamChangeRequests` (раньше жили в `MapReferee`),
правила — `TeamChangeRules`, право админа — `SessionPermissions.IsAdmin` (Player), исполнение —
`SessionTeamAssigner`:

| Кто | Вход | Правило |
|---|---|---|
| Игрок (планшет) | `PlayerSession.CmdRequestTeamChange` → `TeamChangeRequests.ServerPlayerRequest(mode, …)` | скин в своей команде — только в разминке (смена аватара изымает снаряжение, T-35); команда — только из активного режима и пока `TeamChoiceLocked == false` |
| Админ (экран «Игроки и команды») | `PlayerSession.CmdAdminAssignTeam` → `TeamChangeRequests.ServerAdminAssign(mode, admin, target, teamId)` | право админа (хост или `IsAdmin`), любая команда `TeamRegistry`, в любой момент |
| Админ, разово | `PlayerSession.CmdAdminAutoBalance` → `TeamChangeRequests.ServerAdminAutoBalance(mode, admin)` | автобаланс игроков без команды режима; политику режима не меняет |
| Режим | `GameMode.ServerAssignTeams` | по `teamAssignment` |
| Конец серии | `Series` → `SessionTeamAssigner.ClearBeforeSceneChange` | команда снимается у всех, в лобби — киборгом |

**Почему выбор закрывается стартом матча.** После старта смена стороны — это выход из
раунда посреди боя: составы уже разыграны (счёт раундов, смена сторон), а перебежчик ломает
баланс. Опоздавшему команду выдаёт админ. `TeamChoiceLocked` у Elimination —
`_state != WaitingForPlayers`, у Respawn — идёт ли матч; это SyncVar-состояние,
поэтому планшет клиента знает его сам.

**Планшет в разминке.** Своих команд у разминки нет, поэтому планшет предлагает команды матча —
выбранного на серию режима, иначе реестра: Военные и Повстанцы
(`MenuTeamSelection.ResolveAvailableTeams`). Выбрать можно и игроку без команды, и сменить —
игроку с командой, пока матч не начался.

**Матч ждёт команд.** `GameMode.AllPlayersHaveModeTeam()` — у каждого подключённого игрока
(не зрителя) есть команда режима; состав берётся у `PlayerRoster`. Elimination проверяет его
в `IsPlayersReady` вместе с минимумом игроков из своих данных (`MinPlayersToStart`).
Respawn — в `CanBegin`.

**Исполнение** (`SessionTeamAssigner`): поднимает `TeamChangeRequests.TeamChangeRequested`
(хуки режима); нет аватара — пишет команду в сессию (спавн сам возьмёт зону и скин);
аватар жив — `AvatarManager.ChangeAvatar`, **на том же месте**. Скин по выбору игрока либо
сохраняется (`TeamData.IndexOfAvatar`) для админа, политики и конца серии.

**Состояние нового аватара.** Выбывание — состояние сессии (`PlayerSession.IsEliminated`, T-35), тело —
его представление: выбывшему стратегия выдаёт призрака, живому — скин команды. Пересоздание тела одно —
`AvatarManager.ReplaceBody`: смена скина или команды (`ChangeAvatar`, снаряжение изымается) и смена тела по
состоянию (`ServerReconcileBody` — смерть, выбывание, возрождение; снаряжение роняется). После спавна
`AvatarManager` зовёт `GameMode.ServerAdmitAvatar(avatar, continuesPrevious)`; замена переносит здоровье
живого (`CarryLifeState`), и `continuesPrevious = true`; аватар без прошлого (смена карты, первый
вход) начинает жизнь заново — `false`. Elimination в матче делает такой аватар выбывшим и назначает возрождение в зоне
(подготовка, закупка); аватары, созданные до старта режима, проходят то же правило в `Begin`.

**Обнаружение прохода через стены (T-40).** `PlayerSession.Awake` добавляет `WallPassMonitor` и
`WallPassFeedback`: состояние и законная сторона стены переживают замену скина. Сервер начинает
наблюдение после первого снимка калибровки и первой сетевой позы камеры. Первичная калибровка
передаётся в `GamePlayerConnectMessage` до спавна сессии; при переподключении `SessionSnapshot`
восстанавливает пол, рост глаз и признак калибровки целиком через `PlayerSession.ServerAcceptCalibration`.
Подключение к идущему бою поэтому сохраняет прежние пропорции и пол;
окно ожидания — 5 секунд, затем используются исходные значения. Позднее изменение калибровки
живым игроком в бою отклоняется. Боты не ждут снимка клиента. Смена карты, режима, смерть и
разрешённый серверный перенос сбрасывают историю; замена живого тела её сохраняет.
Пороговые значения и ограничения — [T-40](tasks/T-40-wall-pass-penalty.md).

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
| `Warmup_GameModeData.asset` | `warmup` | Разминка | `WarmupMode.prefab` | [] — поле `GameModeRegistry.warmup`, не в `modes` |

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
`SessionManager` и `Series`. В Inspector `SessionManager` назначить:

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
  → AdminMapCommands.ServerStartSeries → SessionManager.SetSeries + StartSession
  → Series.ServerBegin(maps, режим) → MapLoader.LoadMap(maps[0])

[Карта — разминка]  MapBootstrap → MapReferee.ServerStartRun → разминка (server Ready)
  команды сохраняются, игрок без команды остаётся без неё (киборг); арсенал открыт, урона нет

Админ «Начать матч» → MenuMatchManager → PlayerSession.CmdAdminMapCommand(GoLive) → AdminMapCommands.ServerExecute → MapReferee.GoLive
  → режим из MapRunConfig.MatchIntent (согласован при загрузке карты) → режим матча на месте
  → игрок без команды матча выбирает её в планшете (или выдаёт админ)
  → матч ждёт, пока команда режима будет у всех (GameMode.AllPlayersHaveModeTeam)

[Матч кончился]  GameMode.RaiseFinished → MapReferee.Finished
  → Series.ServerRecordMapResult (общий счёт)
  → карта — снова разминка, серия ждёт
  → админ: «Следующая карта» (MapCommand.NextMap) → Series.ServerAdvance
       → следующая карта серии (стартует в разминке)
       → или, после последней: команды снимаются, LoadMap(лобби)

[Экран «Матч» у админа] кнопки — PlayerSession.CmdAdminMapCommand → AdminMapCommands.ServerExecute
  «Начать матч» — разминка → матч        (видна: матча нет, карта допускает матч)
  «Пауза»       — Pause             (видна: идёт матч)
  «Продолжить»  — Resume            (видна: пауза)
  «Стоп»        — Series.ServerEnd → лобби (видна: идёт серия)
```

---

## Этапы инициализации

Жизненный цикл системы при старте:

1.  **Avatar Setup**: `UxrAvatar` пробуждается и устанавливает `LocalAvatar`.
2.  **Registration**: Генерируется событие `LocalAvatarChanged`.
3.  **Precaching**: `UxrManager` ловит активацию аватара и запускает `TryPrecaching()`.
    - Все объекты с `IUxrPrecacheable` создаются перед камерой на несколько кадров.
    - Экран в этот момент затемнен через `UxrCameraFade`.
4.  **Gameplay Start**: Когда аватар готов и сервер разрешил, `MapReferee` запускает логику режима.

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

> Менять `MapReferee`, `RoundPhases` и сцены карт **не нужно**.

---

## Частые ошибки

| Ошибка в логе | Причина | Решение |
|---|---|---|
| `Матч не начат: на карте '…' нет совместимого режима матча` | «Начать матч» в лобби или на карте без режимов матча в `supportedModes` | Штатно для лобби; для карты — добавить режим в `MapData.supportedModes` |
| `GameModeData '...' не содержит modePrefab` | Поле `modePrefab` не заполнено в asset режима | Назначить префаб в Inspector `GameModeData` |
| `Префаб режима '...' не содержит компонент GameMode` | В префабе отсутствует компонент-наследник `GameMode` | Добавить `RespawnMode` / `EliminationMode` на GO префаба |
| `GameModeData '...' содержит менее 2 команд` | Поле `teams[]` не заполнено в `GameModeData` | Назначить два `TeamData` asset-а в поле `teams[]` |
| `карта не выбрана` / `режим не выбран` | `StartSession()` вызван до `SetSeries()`/`SetSession()` | Порядок: сначала `SetSeries`, потом `StartSession` |
| Боты и Blaze | BotDirector сохраняет сессии/фазы/закупку; BotCombatDriver создаёт Blaze-риг в Combat, вне боя — только оружейный Animator-риг; BotGunner исполняет shootEvent после Manipulation | [Контракт и приёмка](tasks/bots-blaze-integration.md) |
| BotCombatStand | Собственный диагностический сервер/реестр; акторы удаляются только через server-authority API BotDirector.RemoveBot(session). Наблюдатели не владеют движениями или хватами; конфигурация редактора восстанавливается после Stop собственного Play | [Запуск и ограничения](tasks/bot-combat-stand.md) |
| `На карте '…' нет разминки` | Пустое поле `warmup` в `GameModeRegistry` | Назначить `Warmup_GameModeData` в поле `warmup` |

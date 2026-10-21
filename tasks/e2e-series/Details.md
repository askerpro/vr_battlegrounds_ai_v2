# e2e-series — подробности

## Разведка (2026-10-21, консультации launch-infra и gameplay, только чтение, база 670741f53)

### Права админа клиенту стенда

- Сервер решает одним правилом `SessionPermissions.IsAdmin`: SyncVar `PlayerSession.IsAdmin` или хост
  (`LocalConnectionToClient`). Все сессии создаются с `IsAdmin=false` (`PlayersManager`), клиентский флаг не выдаёт
  прав. Единственный писатель `IsAdmin=true` — `DebugModeNetwork.HandleAdminRequest` по `DebugAdminPolicy`
  (`Debug.isDebugBuild` или `-vrb-debug-admin`); запрос шлёт `DebugClientSync`, когда в процессе включён `DebugMode`
  (жест «оба стика 2 с» `DebugGestureInput` или меню). `HostIsAdmin` стенда влияет только на UI/Status хоста.
- Вариант A (рекомендация launch-infra): клиент-админ включает режим отладки своим вводом, права выдаёт штатный
  `DebugModeNetwork`; стенд, PlayLaunch, контракт `play-launch-control`, heavy и Run-E2E не меняются. Вариант B (поле
  `AdminParticipant` в конфигурации) требует второго писателя `IsAdmin` и ревизии контракта — не рекомендуется.
- Побочные эффекты режима у админа: вкладка «Отладка», отладочные оверлеи, в плеере — подробные логи UXR.

### Команды админа и серия

| Команда | Доступна | Делает |
|---|---|---|
| GoLive | Warmup и у карты есть режимы матча | `MapReferee.GoLive()` → Live |
| Pause | Live и режим поддерживает паузу | снимок, разминка `MapState=Paused`, снаряжение изъято, все живы |
| Resume | Paused | новый режим из снимка, раунд переигрывается, деньги — на начало раунда |
| Stop | `Series.IsRunning` | `Series.ServerEnd()` → лобби |
| NextMap | Warmup и `Series.IsRunning` | `Series.ServerAdvance()` → след. карта / лобби на последней |

- Путь: `PlayerSession.CmdAdminMapCommand` → `AdminMapCommands.ServerExecute` (право → `IsAvailable` → исполнение,
  лог `[AdminMapCommands] <имя>: <cmd>.` / `не админ` / `не имеет смысла`). Серия: `CmdAdminStartSeries(mode, maps)`
  (планшет «Новая серия»).
- Стенд на TestMap2 стартует серию из одной карты (`ServerStartupRoute` → `Series.ServerBeginStartup`): NextMap
  на ней ведёт в лобби. Для смены карты — `CmdAdminStartSeries("elimination", [TestMap2, TestMap1])` в разминке.
- Смена карты: `MapLoader.LoadMap` → `ServerChangeScene`; `PlayerSession` переживает (команда, права), аватары
  пересоздаются, на новой карте разминка, деньги 800. E2ERunner и барьер переживают смену сцены (шаги сервера
  переотправляются после готовности); оракул заново ищет `MapReferee`/режим. Лобби: `TeamIndex=0`, серия не идёт,
  статистика сохранена.
- Риск, найденный чтением (дефект gameplay): повтор NextMap/Stop во время перехода — `IsAvailable` не учитывает
  `MapLoader.IsLoading`, а `Series` меняет индекс/команды до того, как загрузчик принял запрос (пропуск карты,
  игроки без команд на карте 2). NextMap посреди матча запрещён правилом (продуктовое решение).

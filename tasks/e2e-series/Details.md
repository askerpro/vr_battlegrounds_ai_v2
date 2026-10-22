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

## Итерация 1 — клиент-админ (2026-10-22)

- Операции скрипта: `admin-rights` (`DebugMode.Set(true)` → ждёт реплицированный `IsAdmin`), `admin-command`
  (ждёт `AdminMapCommands.IsAvailable`, шлёт `CmdAdminMapCommand`, ждёт `MapState`), `admin-command-denied`
  (не-админ шлёт ту же команду, состояние карты не меняется `seconds`). Список команд `InputScript.AdminCommands`
  совпадает с `MapCommand` (тест).
- Оракул `scripted-admin-pause` (только чтение): права ровно у одного клиента; Live без серверного вызова GoLive;
  отказ не-админу (лог `Pause отклонена: <имя> не админ`, карта Live, режим тот же); пауза — `WarmupMode`
  с `MapState=Paused`, все живы, руки пусты, `MatchEconomy.Current == null`; продолжение — новый режим (другой
  netId), раунд тот же, деньги = снимок начала раунда, готовность сброшена. Лог исполнения `<имя>: <cmd>.`
  (Info канала Match) — только диагностика.
- Прогон: worker ticket 997, run `ae5e9b0d69014d8195765cf4ff664502` — сервер и оба клиента зелёные, логи
  без ошибок (кроме шума Package Manager у клонов). Сбой стенда до этого (`LaunchNotReady`, MPPM Idle,
  ticket 991) — infra_issue 11375/11439.
- Ревью (2026-10-22): launch-infra и gameplay — approve (11481, 11485). Не блокеры:
  - проверка отказа держится на Warning-логе `AdminMapCommands` (единственное доказательство, что Command дошёл);
    при уровне канала Match ниже Warnings проверка покраснеет; для агрегатора логов это ожидаемый Warning;
  - пауза в раунде 1 не отличает восстановление снимка экономики от свежей (800, 0:0) — снимок
    денег/счёта/LossCounters проверит пауза в раунде 2+ (кандидат в следующую итерацию).

## Итерация 2 — серия и следующая карта (2026-10-22)

- Операции скрипта: `admin-start-series` (`CmdAdminStartSeries(target, maps)`, ждёт реплицированную серию),
  `expect-map` (новый оркестратор карты `target`, разминка, ввод на аватаре), `repeat` у `admin-command`
  (повтор кнопки одним кадром). Сценарий `scripted-series-next-map`: серия [TestMap2 → TestMap1] (TestMap2
  загружается заново), «Следующая карта» ×2 одним кадром, GoLive на TestMap1, клиенты в своих зонах.
- Дефект пойман (worker 1008, run `95576a42…`): второй NextMap приходит, пока загрузчик занят, `IsAvailable` видит
  старую карту в разминке → `ServerAdvance` на последней карте уходит в «конец серии»: `_running=false`, команды
  сняты, Lobby отклонён загрузчиком, а TestMap1 всё равно грузится — серия «не идёт, 0/2», `TeamIndex=0`, админ
  смог начать «матч» без команд.
- Исправление (`Series.cs`, `AdminMapCommands.cs`): переход серии — одна транзакция с загрузкой. Состояние
  (карты/индекс/«идёт»/снятие команд) меняется в `onAccepted` `MapLoader.LoadMap`; отклонённая загрузка не меняет
  серию; `ServerBegin`/`ServerAdvance`/`ServerEnd` возвращают отказ, `Stop` в `ServerExecute` — по итогу.
  Перепрогон (worker 1010, run `962f6ca8…`) — все проверки зелёные, логи без исключений.
- Не меняли: кнопка NextMap на планшете во время перехода по-прежнему видна (клиент не знает о загрузке);
  сервер отклоняет повтор. Info-лог `<админ>: NextMap.` пишется и для отклонённого повтора, следом
  Warning `[Series] … серия не изменена`.

## Итерация 3 — «Стоп» → лобби и замечания ревью итерации 2 (2026-10-22)

- Сценарий `scripted-series-stop` (S+2, карта стенда — серия из одной карты): админ начинает матч, клиенты в своих
  зонах, посреди матча «Стоп» ×2 одним кадром; клиенты ждут `expect-map Lobby`. Оракул: лобби в разминке, серия не
  идёт (`CurrentIndex = -1`), команды распущены (`TeamIndex = 0`), экономики нет; повтор отклонён, серия
  остановлена ровно один раз (первый «Стоп» сразу останавливает серию — повтор отсекает правило кнопки «не имеет
  смысла»); сессии и права те же, карты серии видны до новой серии. Worker 1023/1028 — зелёный, логи чистые.
- Игра (замечания ревью итерации 2): `AdminMapCommands` — «Следующая карта»/«Стоп»/«Новая серия» во время смены
  карты (`MapLoader.IsLoading`) отклоняются с причиной «идёт смена карты» до исполнения (Info об исполнении не
  пишется); `SessionManager.StartSession` возвращает итог, «Новая серия» при отказе загрузчика сообщает отказ;
  `Series.Transition` — изменение серии и изъятие снаряжения изолированы (сбой одного не отменяет другой).
  Оракул `scripted-series-next-map` требует лог отказа повтора, проверяет сессии после каждой карты.
- Сценарии для `Tools/e2e/suites/default.json` (владелец e2e-heavy-windows): `scripted-admin-pause`,
  `scripted-series-next-map` (timeout 420), `scripted-series-stop`.

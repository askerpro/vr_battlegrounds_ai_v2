# Ход доводки map-runtime-bootstrap

| Цель | Мы здесь | Осталось выполнить | Технический документ |
|---|---|---|---|
| Закрыть задачи 5 и 8 плана, перевести всех потребителей на MapBootstrap и удалить неуправляемый путь MapReferee | Пункты 1, 3, 4, 5 сделаны, проверены пользователем и влиты в `dev` | Старт карты мимо лобби, мелкие правки; задача 7 ждёт API сборщика генератора | [Дизайн](map-runtime-bootstrap-design.md), [план](map-runtime-bootstrap-plan.md), [handoff](map-runtime-bootstrap-handoff.md) |

**Общий статус:** проверено пользователем в редакторе (сетевые пункты приняты по e2e), влито в `dev`. AndroidCompileGate PASS (после rebase на
424bf5f4); EditMode 2132 теста, 122 падения — все воспроизводятся на чистом dev, новых 0; temporal probe RED 2/2 на dev →
GREEN 2/2 на ветке; e2e двух процессов с поздним клиентом и перезагрузкой карты GREEN. **Текущий шаг:** завершён.
**Следующий шаг:** см. «Следующие шаги» в конце документа. Задача 7 — после API сборщика (arsenal-generator, шаг 3b).

Ветка `claude/map-runtime-bootstrap` в linked worktree `F:\CodexWorktrees\map-runtime-bootstrap\Vr_Battlegrounds_ai`,
база `origin/dev` 97ff122c. Изменения не коммитятся: коммит делает пользователь после проверки в шлеме (AGENTS.md).
Порядок вливания согласован в `.agent-state/coordination/pipeline.md`: «arsenal-generator 2b, 2c, 3 ∥ map 1–4 по
готовности; map 5 — после arsenal 3». Порядок ждёт подтверждения пользователя.

## Чек-лист проверки в шлеме

**Решение пользователя (2026-10-07):** второго игрока пока нет, сетевые пункты 3 и 4 приняты как есть по e2e двух
процессов (шаг 8). Общая проверка игры — после вливания работ всех агентов. В редакторе проверяются пункты 1, 2, 5
(на хосте), 6, 7.

Для пунктов 3–4 в шлем нужна сборка APK из этой ветки: запрос начального состояния несёт ключ запуска и номер,
старая сборка с хостом из ветки несовместима.

1. Хост из редактора: Play с Offline → лобби. Обзор → «Выбрать серию» → режим и TestMap1 → «Начать». Экран «Матч»:
   «Начать матч» → «Пауза» → «Продолжить» → «Стоп» → лобби. «Пауза» видна, только если режим её поддерживает.
   На каждом шаге стены и магазины пополняются, аватар появляется, режим совпадает с выбранным при старте серии.
2. Смена режима идущей серии. Отдельной функции «сменить режим текущей карты» нет: режим выбирается только вместе со
   стартом серии, переключение режима на экране «Новая серия» до «Начать» остаётся в меню и на сервер не уходит.
   Проверка: на карте открыть «Новая серия» (Отладка → Разделы или экран «Матч»), выбрать другой режим, переключить
   его и выйти без «Начать» — на карте ничего не меняется. Затем выбрать другой режим и карту, «Начать» — серия
   перезапускается, карта грузится с новым режимом.
3. Шлем как удалённый клиент к хосту в редакторе: вход в лобби, затем на карту — оружие со стен берётся только после
   загрузки (короткая пауза до свежего снимка), чужие захваты и стрельба видны сразу после неё.
4. Поздний вход шлема на уже идущую карту: позиции предметов, взятые другим игроком стволы и магазины совпадают с
   хостом; ничего не «телепортируется» обратно на стену. Нужен второй игрок, который брал оружие до входа шлема;
   без него — проверить совпадение стен и предметов в руках хоста.
5. Перезагрузка той же карты: отдельной кнопки нет. На карте «Новая серия» → та же карта → «Начать»: серия
   перезапускается и грузит ту же сцену. Одну карту дважды в очередь не поставить (второй клик убирает её).
   «Lobby → карта → Lobby»: старт серии, затем «Стоп» на экране «Матч». После каждой смены захват работает, в консоли
   нет «адресаты не зарегистрированы» и «Снимок запуска ... отброшен» в установившемся состоянии.
6. Play прямо из открытой сцены карты при выключенной галочке «Start from Offline Scene»
   (`Tools/VR Battlegrounds/Debug/Start from Offline Scene`) стартует с Offline и
   (при включённых Bootstrap Settings) загружает эту карту.
7. Стенд ботов: `Tools/VR Battlegrounds/Bots/Stand/Run Smoke` — стенд стартует через MapBootstrap и доходит до
   вердикта.

## Внешние зависимости

- **Генератор арсенала — задача 7 ждёт API Composer от arsenal-generator (их задачи 2–3).** В `origin/dev` есть только
  чистый фундамент генератора: `ArsenalCompositionCatalog`, `ArsenalStationDescription`, `ArsenalStationResolver`,
  `ArsenalStationCompositionBinding`. `ArsenalStationComposer`, `ArsenalReadyReport`, `ArsenalAdmissionToken`, манифест ID
  и `NetworkUxrIdentity.PrepareGeneratedIdentities` в dev нет. Станции остаются Authored, заглушек Composer нет.
  Барьер Relay спрашивает только `MapBootstrap.IsLocallyReady(MapRunKey)`, поэтому generated registrations добавятся
  внутри MapBootstrap без переделки Relay. `NetworkUxrIdentity.cs` не меняется: его меняет генератор.
- **SDK-патч 51 уже в `origin/dev` (512bb1d8).** Точная регистрация UniqueId и окончательная отмена регистрации
  экземпляра: поздний `OnDestroy` старой сцены больше не стирает регистрацию новой. На этом поведении держится
  перезагрузка той же сцены; в коде bootstrap обходов нет. LoadMap той же карты с патчем прогнан e2e двух процессов (шаг 8) — GREEN.
- **Сессия оружия (WeaponSystem).** Правки в `PlayerLoadoutManager`, `ArsenalMagazineSupply`, `ArsenalWallController`,
  `BotGunner`, `GrabRules` — только проверки допуска. Стенды оружия не содержат MapReferee и не затрагиваются.

## Журнал шагов

### Шаг 0 — аудит (2026-10-07, утро)

- `git fetch`: ветка перемотана fast-forward на `origin/dev` (сначала 512bb1d8, затем 03da356c); пересечений файлов не было.
- Прочитаны AGENTS.md (включая новый раздел «Вливание работы и конвейер агентов»), `.agents/rules/*.md`, документы
  задачи и генератора, код владельцев.
- Unity MCP в сессии агента не подключён. Доступ к worker — через собственный stdio-клиент к
  `Tools/agents/unity_mcp_proxy.py`, то есть через тот же прокси брокера; чтение проверено (список инструментов).
- Потребители неуправляемого пути `MapReferee`: сцена `Assets/Scenes/Debug/BotCombatStand.unity` (сценовый судья) и
  EditMode-тесты, которые спавнили судью без запуска карты (`MatchFlowTests`, `MatchPauseTests`) или писали его
  `_currentState` (`OverviewStateReaderTests`). Остальные тесты только регистрируют активный режим у незаспавненного
  судьи и от неуправляемого старта не зависят.
- Выдача `NetworkUxrIdentity.CreateInstance` в игровом коде: стена, склад магазинов, карман игрока (магазины и
  стартовое оружие), покупка бота; вне игрового кода — стресс-тест (Debug).

### Шаг 1 — барьер начального состояния Relay (код написан, не проверен)

- `InitialStateBarrier` — чистый клиентский протокол: снимок просится только для локально готового запуска, каждый
  запрос получает номер, ответ применяется лишь при совпадении номера и `MapRunKey`; ответ старого запуска не
  применяется и канал не открывает; инкременты до открытия канала отбрасываются; Closing отменяет ожидаемый ответ,
  открытый канал живёт до смены сцены.
- `NetworkStateRelay`: запрос `CmdRequestInitialState(epoch, sequence, request)` уходит после
  `MapBootstrap.IsLocallyReady` (событие `MapBootstrap.LocalReadinessChanged`, смена сцены, страховочный опрос пока
  канал закрыт). Сервер отвечает только на текущий собранный запуск активной сцены (`ServerAcceptsInitialRequest`),
  ответ несёт ключ и номер. Хост второй запрос не делает, выделенный сервер ничего не ждёт. Пересинхронизация боезапаса
  идёт тем же протоколом.
- `InitialStateInventory` — инвентарь адресатов снимка; незарегистрированный адресат — `GameLog.Network.Error`, а не
  молчаливый пропуск SDK.
- `MapBootstrap` клиента — единственная точка «run известен»: своя сцена, статус CompositionReady/Ready, LoadSequence
  больше уже принятого клиентом в этой сессии (`AcceptsClientRun`). `LocalRunKey`, `IsLocallyReady(key)`; координатор
  связывается только с принятым run.
- `MapRunAdmission.IsLocalPlayable`: сервер — server Ready; удалённый клиент — принятый run и открытый канал снимка.
  Потребитель — `GrabRules`: до LocalPlayable новый захват запрещён (удерживаемое не отпускается).
- `HeadlessPrecacheGuard.RegisterAfterComposition` — повторная регистрация выключенных UniqueId после сборки состава на
  машине без графики.
- Файлы: `Assets/Scripts/Network/{InitialStateBarrier,InitialStateInventory}.cs` (новые),
  `Network/{NetworkStateRelay,HeadlessPrecacheGuard}.cs`, `Maps/Runtime/{MapBootstrap,MapRunAdmission}.cs`,
  `Player/GrabRules.cs`.
- Проверка: не выполнялась (ни компиляции, ни тестов).

### Шаг 2 — транзакция загрузки, direct Play (код написан, не проверен)

- `MapLoader.LoadMap(scene, onAccepted)`: номер загрузки `LoadGeneration`; разрушительное действие вызывающего (изъятие
  снаряжения серией) выполняется только под принятую загрузку; `IsLoading` держится до конца смены сцены Mirror либо
  отмены; остановка сервера или выключение загрузчика отменяют загрузку событием `MapLoadCancelled`; запоздавший шаг
  старой загрузки новую не трогает. `Series.Load` перенесён на `onAccepted`.
- `MapBootstrap`: отмена загрузки после Closing — именованный отказ `Load.Cancelled`.
- Direct Play: `PlayModeStartFromOffline.ResolveStart` — карта с MapRoot всегда стартует через Offline (процессный
  корень), независимо от личной галочки «Start from Offline Scene»; стенды Dev и временные сцены — как раньше.
- Файлы: `Managers/{MapLoader,Series}.cs`, `Maps/Runtime/MapBootstrap.cs`, `Editor/VR_Battlegrounds/Debug/PlayModeStartFromOffline.cs`.
- Проверка: не выполнялась.

### Шаг 3 — выдача предметов только через допуск (код написан, не проверен)

- `MapRunAdmission.CreateMapItem(scene, prefab)` / `CreateActiveMapItem(prefab)` — проверка допуска и создание
  экземпляра одной операцией. Переведены все игровые вызовы: стена, склад магазинов, карман (магазины, стартовое
  оружие), покупка бота. Правки в чужих областях — по одной строке.
- Файлы: `Maps/Runtime/MapRunAdmission.cs`, `Arsenal/{ArsenalWallController,ArsenalMagazineSupply}.cs`,
  `Player/PlayerLoadoutManager.cs`, `Bots/BotGunner.cs`.
- Проверка: не выполнялась.

### Шаг 4 — удаление неуправляемого пути MapReferee, стенды (код написан, не проверен)

- `MapReferee`: нет `_currentState` SyncVar, нет старта разминки в `OnStartServer`, нет `GoLive` по выбору меню и
  вложенного под судью режима. Состояние карты — только из descriptor `MapRunAuthority`. Судья принадлежит запуску
  (`IMapRunHost`: MapBootstrap в игре, `TestMapRun` в тестах); судья без запуска пишет `GameLog.Error` и режимов не
  запускает. «Начать матч» до привязки к запуску (сигнал из Awake) откладывается до разминки.
- Каталог: `MapRuntimeCatalog.DebugMaps` — отладочные стенды (kind Debug) вне меню реестра, тот же запуск.
  `MapBootstrapMigration.ApplyDebugStands()` переводит их сцены на MapRoot/MapBootstrap и перезапекает каталог;
  `BotCombatStandBuilder` новый стенд сразу привязывает к своему паспорту и мигрирует.
- `Assets/Scripts/AssemblyInfo.cs` — `InternalsVisibleTo` для EditMode-тестов (внутренний контракт запуска без рефлексии).
- Файлы: `Managers/MapReferee.cs`, `Maps/Runtime/{IMapRunHost,MapRuntimeCatalog}.cs`,
  `Editor/VR_Battlegrounds/Maps/MapBootstrapMigration.cs`, `Editor/VR_Battlegrounds/Bots/BotCombatStandBuilder.cs`,
  `Debug/Bootstrap/DebugOrchestrator.cs` (комментарий).
- Сцена `BotCombatStand.unity` ещё не мигрирована: нужна аренда worker (`ApplyDebugStands`).
- Проверка: не выполнялась.

### Шаг 4а — постоянные тесты (написаны, не запускались)

- Новые: `Network/InitialStateBarrierTests` (9), `Network/InitialStateInventoryTests` (4),
  `Maps/MapBootstrapClientRunTests` (4), `Maps/MapRunAdmissionTests` (6, включая класс «выдача предмета мимо допуска»
  сканом исходников), `Managers/MapLoaderTransactionTests` (3); помощник `Maps/TestMapRun` и методы запуска карты в
  `Network/MirrorTestHarness`.
- Переписаны на управляемый запуск: `MatchFlowTests` (+3 теста: отложенный «Начать матч», судья без запуска, выбор
  меню не меняет режим запуска), `MatchPauseTests`, `OverviewStateReaderTests`; описания `RpcCarriesNoStateTests`.
- `GameModeWiringTests.В_сцене_лобби_есть_оркестратор_режима` заменён на `Оркестратор_режима_лобби_создаёт_запуск_карты`.
- Проверка: не выполнялась.

### Шаг 4б — перенос probes в постоянные тесты, новый temporal probe (2026-10-07 11:10; не запускались)

- `Maps/MapRunContractTests` (14): контракт config/resolver/scope и единственного писателя descriptor — перенос
  `MapRunContractProbe` (18 проверок) и `Tools/Probes/MapRuntimeBootstrap/teardown.cs`.
- `Maps/MapCatalogIntegrityTests` (3): каталог против зарегистрированных префабов, отпечатки, MapRoot каждой карты
  каталога и отсутствие сценового MapReferee — перенос root-preflight/catalog-adversarial.
- Новый temporal probe `Tools/Probes/MapRuntimeBootstrap/relay-and-direct-play.cs`: ответ снимка чужого запуска не
  открывает канал Relay; Play из карты при выключенной галочке не стартует без Offline. Один код для RED на чистом
  dev и GREEN на ветке.
- Удаление старых probes и `MapRunContractProbe.cs` — после GREEN перенесённых тестов.
- В `map-runtime-bootstrap-handoff.md` обновлена верхняя таблица и раздел «Действующий срез»: всё прежнее уже в dev,
  ссылка на этот документ.

### Шаг 4в — релей без статики, документация (2026-10-07 11:25)

- Найдено чтением: `NetworkStateRelayTests.NetworkStateRelay_держит_состояние_канала_в_экземпляре` запрещает статику в
  релее, а шаг 1 добавил статическое событие `InitialStateChanged`. Событие удалено (потребителей не было:
  `IsLocalPlayable` вычисляется покадрово). В checkpoint №71 событие ещё есть — этот тест там ожидаемо упадёт.
- Документация текущего состояния: `Docs/game-manager.md` (неуправляемого судьи нет, клиентский поток, транзакция
  загрузки, барьер Relay, единая точка выдачи предметов, `IsLocalPlayable`), `Docs/README.md` (реестр классов
  `MapReferee`/`MapBootstrap`/`MapRunAdmission`/`IMapRunHost`/`InitialStateBarrier`, `ClientSceneChanged`, direct Play),
  `Docs/session-architecture.md` (таблица времён жизни), `Docs/scene-hierarchy.md` (сценового судьи нет нигде).
- Проверка: документация, без Unity.

### Шаг 4г — e2e-сценарий двух клиентов с поздним входом (2026-10-07 11:45; не собран, не запускался)

- `Assets/Scripts/Debug/E2E/Scenarios/MapRunRelayBarrierScenario.cs` (`map-run-relay-barrier`), регистрация в
  `E2ERunner`. Выделенный сервер, первый клиент, серия из одной карты, server Ready; второй клиент входит на идущую
  карту; затем перезагрузка той же карты. Клиенты проверяют: LocalPlayable и открытый канал Relay ровно для ключа
  своего запуска (и descriptor), после перезагрузки — для нового большего ключа, ноль незарегистрированных адресатов.
- `Tools/e2e/Run-E2E.ps1`: параметр `-LateClientDelay` (пауза перед последним клиентом); `Docs/testing.md` дополнен.
- Для прогона нужен e2e-плеер (`E2EPlayerBuilder.Run()` в аренде worker, ~5 мин).
- Проверка: не выполнялась.

### Шаг 4д — вычитка: переподключение к той же сессии (2026-10-07 11:55)

- Найдено вычиткой: «наибольший принятый ключ» клиента был на весь процесс, и после переподключения к той же
  сессии сервера клиент не принял бы идущий запуск (LoadSequence не больше уже принятого) — барьер не открылся бы
  никогда. Теперь ключ привязан к соединению (`NetworkClient.connection`): новое соединение начинает счёт заново.
  Файл: `Maps/Runtime/MapBootstrap.cs`. Проверка: не выполнялась.

### Шаг 5 — аренды worker (2026-10-07, ждут человека)

- Checkpoint `8fc3f433` (шаги 1–4а), заявка №71 на базе 03da356c. Предложение пришло сразу, но `claim` отказал:
  в worker после перезагрузки несохранённая безымянная сцена. Брокер перевёл очередь в HUMAN (пауза, уведомление
  пользователю); вместе со мной ждёт arsenal-generator. Сцену worker сам не трогаю.
- Подготовлен пакет: refresh с компиляцией, ошибки консоли, AndroidCompileGate, `ApplyDebugStands` (стенд
  BotCombatStand), EditMode `VrBattlegrounds.Tests.EditMode`.
- 12:00: заявка №71 отменена — её вход устарел (шаги 4б–4д). Новая заявка №73 на checkpoint `1e5b6a4e` (всё
  текущее), за ней №74 — чистый dev 03da356c для сравнения EditMode и RED probe.
- 12:20–12:35 аренда №73 (результат `8ae2daa1`, получен `receive`):
  - компиляция: 0 ошибок в консоли; **AndroidCompileGate PASS**;
  - `MapBootstrapMigration.ApplyDebugStands()`: passed, 0 ошибок — `BotCombatStand.unity` переведён на MapRoot +
    MapBootstrap (сценовые MapReferee и координатор удалены, сегменты границы зон перенесены), `BotCombatStandMap`
    kind = Debug, каталог получил `_debugMaps` и запёк отпечатки (все отпечатки сменились: `GetAssetDependencyHash`
    учитывает изменённые скрипты; сборка перезапекает их сама); preflight каталога passed;
  - temporal probe `relay-and-direct-play`: **GREEN 2/2** (ответ чужого запуска канал не открыл; Play из TestMap1 при
    выключенной галочке стартует с Offline);
  - EditMode всей сборки: 2116 тестов, статус failed; MCP вернул только первые 25 падений — в них ни одного нового
    теста ветки, зато шум worker: `Invalid serialized file header ... TestMap2/LightingData.asset` (LFS-указатель
    вместо данных) роняет тесты сцен непойманным логом. Полный список и сравнение с dev — шаг 6.
  - получены `.meta` новых файлов, сцена стенда, `BotCombatStandMap.asset`, `MapRuntimeCatalog.asset`.

### Шаг 6 — EditMode по группам на ветке (2026-10-07 13:00–13:35, аренда №75)

- Checkpoint `673b83ba` (всё, включая полученное из №73). Драйвер гонит 15 групп по пространствам имён, группу,
  упёршуюся в лимит 25 падений, — по классам. Компиляция: 0 ошибок.
- Итог ветки: **2117 тестов, 122 падения**; упёрся в лимит только `Prefabs.GrabPoseCoverageTests` (25+, позы рук).
  Без единого падения: `Network` (165, включая `InitialStateBarrierTests`, `InitialStateInventoryTests`,
  `NetworkStateRelayTests`), `Modes` (78: `MatchFlowTests` с тремя новыми, `MatchPauseTests`, `GameModeWiringTests`),
  `Managers` (18, включая `MapLoaderTransactionTests`), `Bots`, `Interaction`. В `Maps` (237) 14 падений — ни одного в
  `MapRunContractTests`, `MapRunAdmissionTests`, `MapBootstrapClientRunTests`, `MapCatalogIntegrityTests`; это
  `LobbyLayoutTests`, `LaserGridMapsTests`, `MapPrinciplesTests`, `CoverClassTests`, `LevelDesignBlockTextureTests`.
- Шум worker: `Invalid serialized file header ... TestMap2/LightingData.asset` роняет часть тестов сцен.
- Сравнение с чистым dev — заявка №78 (№74 истекла, пока разбирались результаты №73).

### Шаг 6б — EditMode чистого dev и сравнение (2026-10-07, аренда №78, база 03da356c)

- Тот же драйвер и те же 15 групп на чистом dev: **2072 теста, 123 падения**; в лимит упёрся тот же
  `Prefabs.GrabPoseCoverageTests`.
- **Ветка против dev: новых падений 0.** Исчезло одно: `GameModeWiringTests.В_сцене_лобби_есть_оркестратор_режима`
  (заменён на `Оркестратор_режима_лобби_создаёт_запуск_карты`, зелёный). Остальные 122 падения совпадают по именам
  (позы рук, балансные таблицы реестра оружия, геометрия и разметка карт, лобби, шум LightingData.asset worker).
  На ветке на 45 тестов больше — новые тесты задач 5 и 8, все зелёные.
- Temporal probe на dev (RED) в этой аренде не выполнился: после перезагрузки домена вызов вернул
  «Instance ... not found» (worker переподключался к хабу). Сопровождающий брокера (98) подтвердил: повтор безопасен.
  Драйвер теперь повторяет такие вызовы; RED probe — отдельной короткой арендой на dev.

### Шаг 6в — rebase на origin/dev 424bf5f4 (2026-10-07)

- `origin/dev` ушёл вперёд: 573ddab1 (WeaponSystem этап B, не подключён), a32c72d8 (манифест UniqueId генератора,
  задача 2b — чистый расчёт, Composer по-прежнему нет), 424bf5f4 (T-50, единый владелец калибровки игрока).
- Временный WIP-коммит → `git rebase origin/dev` → `reset --soft` и снятие индекса: правки снова незакоммичены.
  Конфликтов нет. Пересечения с T-50: `OverviewStateReaderTests` (T-50 сменил калибровку сессии в `CreatePlayer`,
  моя правка — в `CreateLiveMatch`, обе сохранились), `Docs/{README,game-manager,session-architecture}.md` —
  слились автоматически, мои разделы на месте. `PlayersManager`/`PlayerSession`/`GamePlayerConnectMessage` из T-50
  с допуском аватаров (`AvatarManager` → `MapRunAdmission`) не пересекаются.
- Результаты EditMode шагов 6–6б получены на базе 03da356c; после rebase нужна повторная компиляция и прогон
  затронутых групп.

### Шаг 6г — проверка после rebase (2026-10-07, аренда №99, база worker 424bf5f4)

- Checkpoint `4b5d6110` (ветка поверх 424bf5f4, удалённые probes включены). Компиляция: 0 ошибок в консоли.
- EditMode по тем же группам: **2132 теста, 122 падения** (+15 тестов T-50 из dev). Против dev 03da356c: **новых
  падений 0**, исчезло одно (заменённый `GameModeWiringTests`). Новые тесты ветки зелёные.
- AndroidCompileGate в этой аренде вернул пустой отказ (`success=false`, без сообщения): вызов пришёлся на
  компиляцию после смены базы, до готовности редактора. Повтор — следующей арендой.
- Активная платформа worker — StandaloneWindows64: e2e-плеер собирается без переключения платформы.

### Шаг 6д — RED temporal probe на чистом dev (2026-10-07, аренда №100, вход = база 424bf5f4)

- `relay-and-direct-play.cs` на dev: **RED 2/2** — `OldRunSnapshotOpensChannel`: ответ снимка без ключа запуска и
  номера запроса открыл канал (`channelOpenAfterForeignSnapshot=true`); `DirectMapPlayWithoutProcessRoot`: Play из
  TestMap1 при выключенной галочке стартует саму сцену карты без Offline.
- Тот же probe на ветке (аренда №73): **GREEN 2/2**. Пара RED → GREEN закрывает требование «начни с RED» для барьера
  ответа и direct Play.

### Шаг 8 — AndroidCompileGate после rebase, e2e двух процессов (2026-10-07 13:55–14:15, аренда №106)

- Вход `dbf9c9f0` (ветка поверх 424bf5f4). Компиляция 0 ошибок, **AndroidCompileGate PASS**.
- `E2EPlayerBuilder.Run()` занял главный поток; MCP вернул пустой отказ по таймауту, `finish` отказал («request не
  завершён, не повторять»), аренда ушла в «требуется проверенное восстановление». Отчёт отправлен сопровождающему
  брокера (2f); выполнен штатный `recover` своей аренды — DONE, очередь свободна. Сборка не повторялась.
  Результат восстановления `339fe26d` не получен (accepted=false): в нём только побочные эффекты сборки —
  `PC.asset`, `ProjectSettings.asset` и перезапечка отпечатка стенда в `MapRuntimeCatalog.asset`.
- Плеер собран в worker (`Build/e2e`, 14:08), в `VrBattlegrounds.dll` есть `MapRunRelayBarrierScenario` и
  `InitialStateBarrier`.
- `Run-E2E.ps1 -Scenario map-run-relay-barrier -Clients 2 -LateClientDelay 40` (выделенный сервер + 2 клиента, второй
  входит на идущую карту, затем перезагрузка TestMap1): **GREEN** — сервер 5/5, client-1 4/4, client-2 4/4. Хронология
  каждого клиента: принят запуск /2 → запрос 1 → снимок /2 применён → Closing → принят /3 → запрос 2 → снимок /3
  применён; ошибок «адресат снимка не зарегистрирован» 0, ошибок и исключений в логах 0. Артефакты (локально):
  `Tools/e2e/results/20261007-140957-map-run-relay-barrier/`.
- Не покрыто прогоном: удалённый клиент, долго стоящий в лобби (client-1 застал лобби лишь до старта серии), отмена
  загрузки при живом сервере, шлем.
- После прогона исправлен косметический дефект сценария: поле Summary писало «есть красные проверки» до вердикта
  раннера. Проверки не менялись; пересборка плеера после правки не выполнялась.

### Шаг 7 — удаление заменённых probes (2026-10-07 13:40)

- Удалены: `Assets/Editor/VR_Battlegrounds/Maps/MapRunContractProbe.cs` (+ `.meta`) → `MapRunContractTests`;
  `Tools/Probes/MapRuntimeBootstrap/{teardown,root-preflight,catalog-adversarial}.cs` → `MapRunContractTests`,
  `MapCatalogIntegrityTests`; `{baseline,lifecycle-baseline}.cs`, `capture-baseline.ps1` — замеры legacy-поведения до
  реализации, их инварианты закреплены тестами допуска, режима и Closing.
- Оставлены: `native-inventory.cs` и `index-compile.cs` (инструменты, не проверки), `relay-and-direct-play.cs`
  (direct Play — editor-код, сборка EditMode-тестов его не видит; часть Relay закреплена `InitialStateBarrierTests`).
- Проверка: тесты-замены зелёные в аренде №75.

### Шаг 9 — rebase на 97ff122c и заявка на проверку пользователем (2026-10-07)

- Ветка перенесена на `origin/dev` 97ff122c (WIP-коммит → rebase → reset). Конфликт только в `Docs/CHANGELOG.md`
  (обе записи сверху), разрешён сохранением обеих. В dev пришли задачи 2b/2c генератора (выдача UniqueId,
  `NetworkUxrIdentity`) — с нашими файлами не пересекаются; компиляция будет проверена в аренде.
- Чек-лист переписан под реальные кнопки меню: пункт 2 — смены режима текущей карты нет, режим выбирается только
  стартом серии; пункт 5 — та же карта через «Новая серия».
- Сетевые пункты 3–4 приняты пользователем как есть (см. чек-лист).
- Checkpoint входа `e610d413`, заявка на worker — билет 129 (`map-bootstrap-user-check-1`), база worker 06624cb9.

### Шаг 10 — проверка пользователем и вливание (2026-10-07)

- Аренда 129: вход `e610d413` на worker, компиляция 0 ошибок. Пользователь прошёл в редакторе пункты 1, 2, 5 (на
  хосте), 6, 7: основное работает, остались мелкие правки. Сетевые пункты 3–4 приняты как есть (решение выше).
- Работа принята к вливанию; коммиты — поверх свежего `origin/dev`.

## Следующие шаги

- **Play из сцены карты идёт Offline → Lobby → карта.** Лобби даёт `onlineScene` сетевого менеджера
  (`--- MANAGERS ---.prefab`), `DebugOrchestrator` грузит карту только после него. Предложение: Offline остаётся единственной
  точкой инициализации процесса, а при заданной карте хост стартует сразу в неё (Offline → карта; путь «первый onlineScene»
  `MapBootstrap` уже поддерживает). В Bootstrap Settings — выбор «карта из настроек / открытая сцена»: сейчас
  `PlayModeStartFromOffline` при каждом Play из карты перезаписывает карту сценария открытой сценой. Галочка «Start from
  Offline Scene» для карт больше ничего не меняет.
- Мелкие правки по итогам проверки пользователя — список ждёт пользователя.
- Задача 7 (генерируемые станции) — после handoff API сборщика от arsenal-generator (их шаг 3b); точка установки слотов —
  `ArsenalWallController.InstallGeneratedSlots`.


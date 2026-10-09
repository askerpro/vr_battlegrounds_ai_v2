# map-runtime-bootstrap — подробности

Полный текущий анализ задачи. Статус, план и следующий шаг — в [Readme.md](Readme.md); машинный план — [plan.json](plan.json).

## Цель и мотивация

Автор карты кладёт в сцену один `MapRoot`: паспорт `MapData`, корни Environment/Gameplay,
`PhysicalArenaLayout`, зоны и станции. Служебные системы собирает `MapBootstrap` по центральному каталогу.
Сервер разрешает одно неизменяемое описание запуска, а все потребители читают его или производные от него
значения. Пропущенная зависимость становится именованным отказом до выдачи оружия, спавна аватара и старта режима.

Что было раньше:
- менеджеры раскладывались по сценам вручную;
- `ManagerBootstrap` объявлял готовность даже при частичном составе;
- судья сам стартовал разминку в `OnStartServer`;
- `GoLive` читал текущий выбор меню вместо режима серии;
- предметы выдавались мимо допуска;
- клиент применял снимок состояния UltimateXR раньше, чем карта у него была готова.

Задача делает запуск предсказуемым для сетевой игры и даёт генератору станций арсенала один стык подключения.

## Границы

**Входит:**
- `Assets/Scripts/Maps/Runtime/**`;
- редакторские инструменты карт: `MapBootstrapMigration`, `MapCatalogBuildStep`, `MapRunPreflight`;
- владельцы запуска: `MapLoader`, `MapReferee`, `Series`;
- проверки допуска в чужих писателях, по одной строке: стена, склад магазинов, карман игрока, покупка бота, `AvatarManager`, `GrabRules`;
- барьер начального снимка: `InitialStateBarrier`, `InitialStateInventory`, `NetworkStateRelay`;
- адаптер генерируемых станций;
- постоянные тесты и локальные пробы этой работы.

**Не входит:**
- код генератора арсенала: `ArsenalStationComposer`, Resolver, Preset/Style/Binding, `NetworkUxrIdentity*`;
- часть `_arsenalComposition` ассета `MapRuntimeCatalog.asset`;
- перевод лобби и карт на генерируемые станции. Это задача arsenal-generator;
- SDK UltimateXR и Mirror. На SDK-патч 51 из `dev` мы опираемся, но не меняем его;
- правила режимов: раунды, урон, экономика, команды.

## Архитектура

### Владельцы и данные

| Что | Владелец | Правило |
|---|---|---|
| `MapRunConfig` | создаёт `MapRunResolver`, издатель — `MapRunAuthority` | Неизменяемое значение одной загрузки. Содержит `MapRunKey` (SessionEpoch + LoadSequence), сцену, отпечаток содержимого, версию состава, режим разминки, `MatchIntent` (`NoMatch` или `Resolved(modeId)`), пресет арсенала и отпечаток, `MapStationConfig` станций. По сети идут только ID и хеши |
| `MapRunSnapshot` | `MapRunAuthority` на `SessionContext` — единственный издатель | Целое значение, без набора отдельных SyncVar. Содержит config, `Revision`, статус (`Preparing`, `CompositionReady`, `Ready`, `Failed`, `Retiring`, `Closing`), `MapState`, `ModeEpoch`, активный режим (ID и netId), netId судьи и координатора, код отказа. Запись: `BeginRun` → `CommitPrepared` → `CommitMode` → `Close`/`Retire`/`Fail`, с проверкой ревизии |
| `MapRunScope` | запуск одного ключа | Отмена, владение объектами и подписками, разборка в обратном порядке. `Close()` отменяет без разборки, `Dispose()` разбирает |
| `MapRoot` | автор сцены | Только ссылки. `StationKey` хранится только в `ArsenalStationCompositionBinding`, вид карты (Lobby/Combat/Debug) — только в `MapData` |
| `MapRuntimeCatalog` | один на проект, назначен в `GameNetworkManager` | Типизированные префабы судьи, координатора и режимов, отпечатки карт, `DebugMaps` (стенды вида Debug вне меню), `ArsenalComposition` (каталог ресурсов генератора) |
| `MapReferee` | спавнит `MapBootstrap` и он же им владеет (`IMapRunHost`) | Своего SyncVar состояния нет: состояние берётся из descriptor. Режим — отдельный сетевой корень. Судья без запуска пишет `GameLog.Error` и режимов не запускает |
| Режим серии | `Series.CapturedModeId` | Фиксируется в `Series.ServerBegin(maps, modeId)` |
| Снимок паузы | `MapReferee` | Привязан к ключу и эпохе, в config не входит |

### Поток сервера

`MapBootstrap` опрашивает предусловия в `Update`. Поэтому одинаково работают смена карты, `onlineScene` и
старт без `OnServerSceneChanged`: Mirror спавнит объекты сцены раньше этого колбэка.

```text
MapRunAuthority готов + Mirror догрузил сцену
→ MapRoot.ValidateBindings(includeSceneScans: false)
→ Станции арсенала: MapArsenalCompositionAdapter.Describe + Apply (описание генератора в config до публикации)
→ MapRuntimeCatalog.Resolve      (режим: Series.CapturedModeId, иначе выбор админа; лобби — NoMatch)
→ MapRunAuthority.BeginRun       (новый ключ, Preparing)
→ Станции: PrepareComposition + Activate в scope запуска; стадия ComposingStations, пока все не Passed
→ MapReferee и ArsenalEquipmentCoordinator из каталога → в сцену карты → NetworkServer.Spawn
→ CommitPrepared                 (CompositionReady: gameplay ещё закрыт)
→ MapReferee.ServerStartRun → разминка → CommitMode (server Ready: допуск открыт)
→ отложенные аватары (по одному запросу на сессию)
```

Стадии `MapBootstrap`: `WaitingPrerequisites` → [`ComposingStations`] → `CompositionReady` → `Ready`, либо `Failed`.

**Коды отказа.** Отказ пишется в `GameLog.Error` с картой, кодом и не более чем 10 причинами. После отказа ни
оружие, ни аватары, ни режим не выдаются.

| Код | Когда |
|---|---|
| `MapRoot.Invalid` | Ошибки привязок `MapRoot`. Причины: `MapRoot.Count`, `.Inactive`, `.Transform`, `Map.Groups.Overlap`, `Map.LegacyServices.Present` |
| `Map.Resolution` | Отказ resolver или каталога. Причины: `Map.Scene.Invalid`, `Map.Unknown`, `Map.Kind.Invalid`, `Map.Mode.*`, `Map.MatchMode.Missing`, `Map.Lobby.HasMatchModes`, `Map.ContentFingerprint.*`, `Map.Bindings.*`, `Map.Arsenal.*`, `Mode.Component.Missing`, `Mode.Teams.Invalid` |
| `Run.BeginRejected`, `Run.CompositionCommitRejected`, `Run.ReadyNotCommitted` | Authority отверг запись: устаревший ключ или ревизия |
| `Composition.Exception`, `Mode.WarmupFailed` | Исключение при сборке; разминка не стартовала |
| `Load.Cancelled` | Загрузка отменена после Closing |
| `Arsenal.Generated.Description` | Описание станции не построено. Причины: `Station.Generated.CatalogMissing:<StationKey>`, `Station.Generated.<вид отказа генератора>:<StationKey>` |
| `Arsenal.Generated.ConfigMismatch` | Клиент: своё описание не совпало с config. Причины: `Station.Generated.ModeMismatch`, `NotPublished`, `DecorationMismatch`, `LayoutMismatch`, `IdentitySchemaMismatch` |
| `Arsenal.Generated.Compose` | Исключение сборщика (`ArsenalComposer.*`, `NetworkUxrIdentity.Generated.*`) |
| `Arsenal.Generated.NotReady` | `ValidateReady` вернул Failed во время сборки |
| `Arsenal.Generated.Lost` | Сервер: станция потеряла готовность после CompositionReady |

Closing — это не отказ: Pending-станции при Closing тихо прекращают ожидание.

### Поток клиента

`MapBootstrap` клиента — единственная точка, где «запуск известен». Сборка станций не стартует из
`OnStartClient`, `Awake` станции или подписок генератора. Запуск принимается при четырёх условиях:
- сцена descriptor — своя;
- статус `CompositionReady` или `Ready`;
- `SessionEpoch` совпадает с текущим;
- `LoadSequence` строго больше наибольшего ключа, принятого **на этом соединении**.

Счёт ключей привязан к соединению, а не к процессу. Иначе после переподключения к той же сессии клиент не
принял бы идущий запуск и барьер не открылся бы никогда. Принятый ключ виден в `LocalRunKey`. Дальше клиент:
1. связывает координатор со станциями по netId из descriptor;
2. сверяет отпечаток содержимого карты; расхождение означает разные сборки;
3. собирает Generated-станции в собственном `MapRunScope` ключа;
4. отвечает барьеру `IsLocallyReady(key)`.

Host вторую сборку не делает, выделенный сервер не ждёт локального игрока.

### Допуск (`MapRunAdmission`)

Допуск — производное значение без собственного флага:
- `CanActivateMapGameplay(scene)` — сцена без `MapBootstrap` (стенд) открыта; управляемая сцена открыта
  от server Ready до Closing;
- `CanActivateActiveMap` — то же для писателей вне сцены карты;
- `IsLocalPlayable` — сервер и host после server Ready, удалённый клиент после принятого запуска и
  применённого свежего снимка. До этого `GrabRules` не даёт взять новое, удерживаемое не отпускается.

Сетевой предмет карты создаётся только через `CreateMapItem` (сцена карты) или `CreateActiveMapItem`
(карман, кобура, покупка бота): проверка допуска и создание выполняются одной операцией. Обход ловит скан
исходников `MapRunAdmissionTests.Сетевые_предметы_игры_создаются_только_через_допуск_карты`. Аватары до
server Ready стоят в очереди, по одному запросу на сессию.

### Барьер начального снимка Relay

`MapRunSnapshot` — описание запуска. Снимок UltimateXR (`SaveStateChanges`/`LoadStateChanges`) — другая
структура, и готовность первого не доказывает, что адресаты второго зарегистрированы.

- Клиент просит снимок (`CmdRequestInitialState(epoch, sequence, request)`) только после
  `MapBootstrap.IsLocallyReady(LocalRunKey)`: по событию `LocalReadinessChanged`, по смене сцены и по
  страховочному опросу, пока канал закрыт.
- Сервер отвечает только на текущий собранный запуск активной сцены. Ответ несёт `MapRunKey` и номер запроса.
  Ответ другого ключа или номера не применяется и канал не открывает.
- Инкременты до открытия канала отбрасываются. Снимок свежий, а всё более позднее идёт после него тем же
  надёжным каналом. Объекты, созданные сервером до снимка, приходят раньше ответа. Ещё не созданный
  аватар в снимке не упоминается и барьер не держит, поэтому цикла «аватар → Ready → снимок» нет.
- Готовность определяет `MapBootstrap`: станции готовы, когда все handles сборщика дали Passed. Отдельного реестра участников нет: забытый участник открыл бы барьер раньше времени.
  Готовность стены не используется как замена этой проверки.
- Пропущенный адресат снимка — `GameLog.Network.Error` от `InitialStateInventory`, а не молчаливый пропуск SDK.
- Closing отменяет ожидаемый ответ. Открытый канал живёт до смены сцены. Пересинхронизация боезапаса идёт
  тем же протоколом.

Логика барьера вынесена в чистый `InitialStateBarrier`. Состояние канала хранится в экземпляре релея: статику
в релее запрещает `NetworkStateRelayTests`.

### Closing и разборка

`MapLoader.MapLoadStarted` вызывает `MapRunAuthority.Close`: статус `Closing`, `scope.Close()` отменяет токен.
С этого момента закрыты:
- допуск аватаров;
- коммит режима;
- любые выдачи предметов: стена, склад магазинов, карман, покупка бота;
- RPC владельцев, работающих по `MapRunScope.Cancellation`.

Иначе бесконечный карман разминки досыпал бы магазины после изъятия снаряжения серией, и они уехали бы на
следующую карту. Разборка выполняется позже, на выгрузке сцены: `Retire` → `Dispose` поддерева станций и снятие
служебных объектов. Удерживаемые и купленные предметы не уничтожаются через родителя станции. Клиент,
увидев `Closing` своего ключа, закрывает свои писатели так же. Запуск этой сцены, который ещё не начался,
после `MapLoadStarted` уже не начнётся.

### Транзакция загрузки (`MapLoader`)

- `MapLoader.LoadMap(scene, onAccepted)` остаётся единственной точкой смены карты.
- Разрушительное действие вызывающего выполняется только под принятую загрузку (у `Series.Load` это изъятие снаряжения). Отклонённый или повторный запрос его не вызывает и номер `LoadGeneration` не меняет.
- `IsLoading` держится до конца смены сцены Mirror.
- Остановка сервера или выключенный загрузчик отменяют загрузку событием `MapLoadCancelled`. Запуск, который уже закрыт этой загрузкой, получает отказ `Load.Cancelled`.
- Запоздавший шаг старой загрузки новую не трогает.

### Адаптер генерируемых станций (`MapArsenalCompositionAdapter`)

Адаптер вызывает только `MapBootstrap`, на сервере и на каждом удалённом клиенте одинаково, с одним `MapRunKey`.

- `Describe`: `ArsenalStationBuildInput.Capture(stationKey, preset, catalog, visual, placement)` →
  `ArsenalStationResolver.ResolveDescription`. Входы:
  - preset — `MapData.arsenalPreset`;
  - catalog — `MapRuntimeCatalog.ArsenalComposition`;
  - visual — `binding.VisualRequest`;
  - placement — мировая поза `ArsenalEquipmentPoses` и конверт `ArsenalStationAnchor.RaisedBoundsWorld`.
- `Apply` (сервер, до публикации config): записывает в `MapStationConfig` оформление, fallback, layout hash и версию схемы ID.
- `Verify` (клиент): строит своё описание и сверяет его с config. При расхождении станция не собирается и подмены нет.
- `Compose`: Prepare всех станций, затем Activate всех.
- `Poll`: сводная готовность по приоритету Failed > Pending > Passed. `IsServerReady` и `IsLocallyReady` требуют Passed.
- Шов `IArsenalStationComposer` (экземплярное свойство `MapBootstrap`) позволяет EditMode-тестам проверить
  ожидание и отказы без Play Mode. В игре используется `RuntimeArsenalStationComposer`.
- Авторского формата станций больше нет (решение пользователя 2026-10-08, удалён этапом arsenal-generator
  `composer-presentation`): все станции всех карт собирает генератор, отказ сборки — отказ запуска.
- `ArsenalStationComposer` сообщает только, что станция собрана. Выдачу предметов решает `MapRunAdmission`.

### Стык с генератором

API сборщика влит в `dev` 34d3ccb0, namespace `VrBattlegrounds.Arsenal`:

- `ArsenalStationComposer.PrepareComposition(description, MapRunScope scope, ArsenalStationCompositionBinding station)` → `ArsenalCompositionHandle`.
  - Условия: Play Mode; scope открыт; `description.Success`; `station.Mode == Generated`; ключи совпадают; у станции есть Controller, `ArsenalStationPresetBinding` и `EquipmentPoses`; слоты ещё не установлены.
  - Собирает выключенный `GeneratedSlots`, выводит ID из `scope.Key` и передаёт handle во владение `scope.Own`.
  - Повтор с тем же description и scope возвращает тот же handle. Иначе — `InvalidOperationException` `ArsenalComposer.AlreadyComposed:<key>`.
  - Прочие отказы — `ArsenalComposer.*` и `NetworkUxrIdentity.Generated.*`; при сбое поддерево откатывается.
- `Activate(handle)` включает поддерево. Токена допуска нет.
- `ValidateReady(handle, out reason)` возвращает Pending (не включено или ID не зарегистрирован), Passed или
  Failed (разобрано, scope закрыт, ID занят чужим). `scope.Close()` переводит в Failed без разборки,
  `scope.Dispose()` разбирает.
- **Генератор применяет стиль как есть** (решение пользователя, в `dev` с 5aafcbc4):
  - позы задаёт человек в стиле представления, генератор не знает размеров оружия;
  - `PlacementOverflow` и расчёт габаритов удалены, корпус выбирается по числу слотов;
  - `LayoutFingerprint` хеширует входы, а не вычисленные позы: числа с плавающей точкой на ПК и Quest расходились бы.

  Для нас это означало одну правку: адаптер берёт `binding.VisualRequest` вместо пустого запроса. Сигнатуры `ArsenalPlacementInput` и `RaisedBoundsWorld` не изменились.
- Факт пробы: роли могут регистрироваться ещё на выключенных компонентах, поэтому присутствие ID в реестре —
  не признак готовности. Барьер опирается только на `ValidateReady` = Passed.

## Этап `startup-route`: старт сервера сразу в целевую сцену (проект)

**Зачем.** Пользователь (2026-10-07/08): лобби не обязательно в пути старта; Play из сцены карты должен идти
Offline → карта. Механизм запуска Play (окно, конфиг, директор) ведёт задача `vr-test-stand`; первая смена сцены
сервера — область запуска карты, поэтому точка входа — здесь, а директор её только вызывает (контракт
`map-startup-route`).

**Факты Mirror** (`ThirdParty/Mirror/Core/NetworkManager.cs`): `StartServer` → `OnStartServer` → `ServerChangeScene(onlineScene)`;
`StartHost` → `ServerChangeScene(onlineScene)` → (сцена загружена) → `FinishStartHost` → `OnStartServer`. Порядок у
сервера и хоста разный, поэтому совет NET-21 «перенести переход в `OnStartServer`» для хоста лобби не убирает. Общая
точка обоих путей — виртуальный `ServerChangeScene`, переопределённый в `GameNetworkManager`.

**Решение.**
- `ServerStartupRoute` (Managers, процессный, только сервер): `TryRequest(scene, modeId, owner, out handle, out error)`
  до `StartServer`/`StartHost` возвращает `StartupRouteHandle : IDisposable` с собственным `RequestId`; один живой запрос,
  второй — отказ `StartupRoute.Busy`. `modeId`, сцена и владелец неизменны с момента запроса. Проверка сразу: сцена есть в
  `MapRuntimeCatalog` (карты и `DebugMaps`) и в списке сборки (`StartupRoute.SceneNotLoadable`), режим совместим
  (`StartupRoute.ModeIncompatible`). Отказ — именованный, без тихого отката на Lobby.
- `GameNetworkManager.ServerChangeScene`: первая смена сцены сервера при активном запросе грузит цель вместо
  `onlineScene`. Поле `onlineScene` в префабе не меняется (NET-21: оно же — конфиг меню); без запроса поведение прежнее.
- `GameNetworkManager.OnStartServer` (у сервера — до смены сцены, у хоста — после): при `modeId` запрос начинает серию
  из одной карты без повторной загрузки (`Series` — новый вход «серия уже грузит первую карту»), чтобы `MapBootstrap`
  взял режим из `CapturedModeId`. У хоста `MapBootstrap` ждёт `MapRunAuthority` с `SessionContext`, который появляется
  в том же `OnStartServer`, — оба порядка сходятся до Resolve.
- **Жизнь запроса:** `Requested` → (первая смена сцены сервера) `SceneConsumed` → (захват режима в `OnStartServer`)
  `Completed`, запись удалена. Без `modeId` запрос завершается сразу после выбора сцены. `handle.Dispose()` и
  `Cancel(requestId, owner)` снимают только свой запрос и только в `Requested`; после `SceneConsumed` запрос уже
  исполняется и доводится до захвата режима (Dispose лишь освобождает handle). Неудачный старт (сеть не поднялась,
  `ServerChangeScene` не вызван) оставляет `Requested` — владелец снимает его своим Dispose; `StopServer` при неактивном
  сервере (`OnStopServer` не вызывается) на запрос не опирается. `OnStopServer` снимает запрос в `SceneConsumed`
  (сервер остановлен до захвата режима). Чужой новый запрос не удаляется: снятие сверяет `RequestId`.
- Клиенты не меняются: Mirror присылает им текущую сцену сервера.
- Готовность наблюдается существующими средствами: `MapBootstrap.IsServerReady`, снимок `MapRunAuthority`.

**Проверено (2026-10-09, аренда worker 243, вход 05cd7867, finish принят, лишних изменений нет):** компиляция 0 ошибок,
AndroidCompileGate PASS; EditMode Managers/Maps/Network/Modes — 549 тестов, 5 падений только в тестах содержимого сцен
(эмбиент, текстуры блокаута, раскладка лобби, LD20, LD23), `ServerStartupRouteTests` 13/13. Play: хост с запросом
TestMap1/elimination — загружены только Offline → TestMap1, серия с режимом `elimination`, server Ready; выделенный
сервер с запросом TestMap2 без режима — Offline → TestMap2, без серии, server Ready; запрос на стенд ботов —
`StartupRoute.SceneNotLoadable` (стенд не в списке сборки); без запроса — Offline → Lobby как раньше; ошибок консоли 0.
Запрос ставился в обработчике загрузки Offline до `GameNetworkDiscovery.Start` — так же будет звать директор.

**E2E двух процессов, `map-run-startup-route` (2026-10-09, плеер из аренды 253): GREEN.** Выделенный сервер запросил
TestMap1/elimination до старта сети и стартовал сразу в карту (сцены сервера: TestMap1, без Lobby), серия
`elimination`, server Ready; первый клиент и поздний (через 40 с) вошли в идущую карту, LocalPlayable и снимок Relay
для ключа запуска, адресаты снимков зарегистрированы; перезагрузка той же карты — новый ключ у сервера и обоих клиентов.
Артефакты (локально): `Tools/e2e/results/20261009-033112-map-run-startup-route/`.

**Окно запроса.** До загрузки Offline (`BeforeSceneLoad`) `GameNetworkManager` ещё нет — каталог карт недоступен, запрос
получает `SceneNotLoadable` с текстом «звать после загрузки Offline». Правильное окно — после `Awake` менеджеров Offline
и до `Start`, который поднимает сеть (`SceneManager.sceneLoaded` первой сцены); E2E вызывает подготовку сервера там же
(`IE2EServerStartup`).

**Ограничение среды:** e2e и Play в worker делят порты 7778/47777 — прогон во время чужого Play с сетью падает
`SocketException`; параметра порта у e2e нет.

**Не входит:** окно, конфиг, директор, стенды, `LocalMenuManager` (лобби по-прежнему определяется по `onlineScene` и
остаётся верным: в карте меню — игровое).

**Проверка этапа:** EditMode — разбор запроса, отказы, cancel до старта → новый запрос, неудачный старт → Dispose →
новый запрос, чужой Dispose не снимает новый запрос, хост: выбор сцены → режим сохранён до `OnStartServer` → захват; Play на worker — хост и выделенный сервер стартуют сразу в
TestMap1 и в стенд ботов, с режимом и без, server Ready, без промежуточного Lobby; без запроса — прежний Offline → Lobby;
поздний клиент попадает в карту сервера.

## Этапы `series-smoke` и `series-smoke-e2e`: сквозной смок сессии

**`series-smoke` (EditMode, сделан 2026-10-09).** `SeriesSmokeTests.Серия_из_двух_карт_от_старта_до_лобби`: настоящие
`Series`, `MapReferee` (запуск — `StartMapRun`, как у `MapBootstrap`) и `EliminationMode`, матч крутится его `ServerTick`.
Цепочка: разминка → «Начать матч» → все шесть фаз раунда → раунды подряд → смена сторон после 3-го раунда → конец карты
по большинству (4 из 6) → разминка, счёт серии, стороны на месте → «Следующая карта» (`AdminMapCommands`) → закрытие и
снятие запуска первой карты (Closing → Retire) → вторая карта в разминке с сохранённым счётом и командами → её матч с
первой половины → «Следующая карта» на последней → лобби, серия остановлена, команды распущены. Проверено на worker
(аренда 281): группа Modes 84/84.

**Границы EditMode-смока.** Аватаров нет (реестр игроков — заглушка), рендеров нет, клиентов и сериализации Mirror между
процессами нет. Поэтому он не ловит дефекты тел и сети, например регрессию рендереров Cyborg (задача
`avatar-renderer-regression`: исключение `SetAvatarRenderMode` внутри `BeginSync` после конца раунда, утечка глубины sync
и развал синхронизации).

**`series-smoke-e2e` (план, после `vr-test-stand/play-launch-integration`).** Тот же путь живьём: выделенный сервер,
клиенты с настоящими аватарами, серия из двух карт на сценах, укороченные таймеры режима. Помимо переходов — общие
критерии на всё время прогона: ноль исключений и ошибок в логах всех процессов (кроме явного списка известного шума);
клиенты остаются подключены, `LocalPlayable` открыт в каждой карте, нет ошибок синхронизации (mismatch, глубина sync);
аватары живы и переживают конец раунда, смену сторон и смену карты. Именно эти критерии ловят класс дефектов вроде
регрессии рендереров. Порты — по механизму `vr-test-stand`.

## Принятые решения и причины

1. **Явный `MapRoot` + типизированный каталог + зарегистрированные сетевые префабы** вместо обязательного
   сценового префаба служб. Новые карты не копируют служебную иерархию, а client/host/dedicated проходят один
   контракт. Цена — перенос ранних побочных эффектов существующих компонентов за допуск.
2. **Один издатель состояния карты** — `MapRunAuthority`. У судьи нет SyncVar состояния, а
   `SessionManager.Selected*` — выбор будущей серии, а не текущий config. Клиент принимает режим только с netId из
   descriptor, поэтому поздний `OnStartClient` старого режима текущий не перебивает.
3. **Режим фиксируется при старте серии** (решение пользователя). Выбор в меню во время Warmup, Live или Paused
   относится к следующей серии. Resume восстанавливает режим из снимка паузы, лобби запускается с `NoMatch`.
   Отдельной функции «сменить режим текущей карты» нет.
4. **Транзакции смены режима со «спящим» кандидатом нет.** Каталог проверяет все префабы режимов до старта карты,
   поэтому между уничтожением старого режима и коммитом нового отказов не остаётся.
5. **Closing отделён от разборки**: писатели закрываются при принятой загрузке, разборка идёт на выгрузке (см. выше).
6. **Барьер Relay без ACK и очереди**: свежий запрос после локальной готовности, корреляция по ключу и номеру.
   Дополнительный протокол вводить только по конкретной падающей пробе.
7. **Сканы всей сцены выполняются только при подготовке**: preflight сборки, миграция, `MapRunPreflight`. В рантайме
   `UxrManager` навешивает `UxrCanvas` с пустым id на world-space канвасы (known-issues, Issue 36). Поэтому
   `MapBootstrap` проверяет только привязки и станции, а целостность сцены подтверждает отпечаток из каталога.
8. **Карта с `MapRoot` в Play стартует через Offline** (процессный корень) независимо от галочки
   «Start from Offline Scene».
9. **Генерируемые станции описываются до публикации config.** Клиент сверяет config, а не выбирает сам.
   Отказ сборки — отказ запуска; host вторую сборку не делает.
10. **Постоянные тесты написаны до приёмки в шлеме** по прямому поручению пользователя. Это исключение из правила AGENTS.md.
11. **Сетевые пункты проверки приняты по e2e двух процессов** (решение пользователя 2026-10-07): второго игрока нет.
    Общая проверка игры — после вливания работ всех агентов.

## Проверки и пределы

**Влитые этапы**, база 424bf5f4:
- AndroidCompileGate PASS.
- EditMode: 2132 теста, 122 падения. Все падения воспроизводятся на чистом dev, новых нет.
- Проба `relay-and-direct-play`: RED 2/2 на dev → GREEN 2/2 на ветке. Ответ чужого запуска не открывает канал; Play из TestMap1 при выключенной галочке стартует с Offline.
- E2E `map-run-relay-barrier`:
  - состав: выделенный сервер и два клиента, второй входит на идущую карту (`-LateClientDelay 40`), затем перезагрузка TestMap1;
  - итог GREEN: сервер 5/5, клиенты 4/4 и 4/4;
  - ошибок «адресат не зарегистрирован» 0;
  - артефакты (локально, игнорируются Git): `Tools/e2e/results/20261007-140957-map-run-relay-barrier/`.
- Play Mode на хосте: Lobby → TestMap1 → GoLive → Pause → Resume → Lobby, `Ready` на каждом шаге. TestMap2,
  TestMap3, ServiceYard и ReferenceMap04 доходят до `Ready`. Миграция 6/6, preflight каталога 6/6.

**`generated-stations`**, база 2c845870:
- Компиляция без ошибок, AndroidCompileGate PASS.
- Play Mode-проба 32/32:
  - условия: без сети, настоящий сборщик, тестовая станция во временных сценах, пресет — копия FullDemoArsenal в памяти со стилем IndustrialPegboardPresentation;
  - покрыто: описание → config, Pending до Activate и Passed после, клиентская половина `MapBootstrap`, Closing, выгрузка;
  - `GeneratedSnapshotOldRunResponse`: тот же StationKey в новом запуске получает новые ID, ответ старого ключа отвергнут;
  - `ConfigMismatch` даёт одну именованную ошибку;
  - реестр после выгрузки чист.
- EditMode, группы Arsenal, ArsenalWall, Managers, Maps, Modes, Network, UI:
  - ветка — 741 тест, чистый dev — 730 тестов и 21 падение;
  - новых падений игрового кода нет;
  - `MapArsenalCompositionAdapterTests` 11/11.

**Не проверено:**
- После переноса на 5aafcbc4 не проверено ничего. Есть только офлайн-сверка точек стыка чтением.
- Серверная половина `TryBeginRun` с Generated-станцией: нужна сеть и полный каталог. Общий с клиентом код проверен, склейка проверена только чтением.
- E2E с Generated-станцией: нет карты с такой станцией без игрового ассета.
- `GeneratedDescriptorSpawnSnapshotPermutations` и `GeneratedLateNetworkItemArrival` в части сетевых предметов: проба покрыла только порядки descriptor, снимка и ключа.
- Шлем.
- Удалённый клиент, долго стоящий в лобби.
- Отмена загрузки при живом сервере.

**Известные шумы тестов и харнесса:**
- `Invalid serialized file header ... TestMap2/LightingData.asset` на worker: вместо данных LFS-указатель. Роняет часть тестов сцен, иногда `MapCatalogIntegrityTests`. Код задачи эти тесты не затрагивает.
- MCP возвращает не больше 25 падений. Прогонять группами по пространствам имён и сравнивать с чистым dev той же базы по именам. В `Prefabs.GrabPoseCoverageTests` (позы рук) падений больше лимита.
- AndroidCompileGate сразу после смены базы может вернуть пустой отказ: редактор ещё компилирует. Нужен повтор.
- «Instance ... not found» после перезагрузки домена: повтор безопасен.
- Выход из Play через `EditorApplication.Exit` блокирует фильтр MCP; выходить через `EditorApplication.isPlaying = false`.
- `E2EPlayerBuilder.Run()` занимает главный поток: MCP отваливается по таймауту, аренда уходит в recovery. Побочные эффекты сборки — `PC.asset`, `ProjectSettings.asset` и перезапечка отпечатка в `MapRuntimeCatalog.asset`.
- Отпечатки каталога (`GetAssetDependencyHash`) меняются при любой правке скриптов. Сборка перезапекает их сама.
- У сцены EditMode-прогона пустое имя, и resolver отвергает такой config (`Map.Scene.Invalid`). Фикстуры задают имя явно, например `GeneratedTestMap`.
- Mirror пишет `Error` на `[ClientRpc]` вне сервера (AGENTS.md); это шум теста, а не логики.

**Пробы** (временные, под Git): [tools/](tools/); их отчёты — локально в `tasks/map-runtime-bootstrap/reports/`. Каждый файл — тело одного вызова
`execute_code` на worker под своей арендой (после `guard`). Результаты пишутся в
`tasks/map-runtime-bootstrap/reports/` checkout worker. Чтение ответа MCP — `Tools/UnityMcp/compact-result.js`.

| Проба | Как запускать |
|---|---|
| `relay-and-direct-play.cs` | Редактор без Play и сети; один вызов. Одинаковое тело даёт RED на dev и GREEN на ветке |
| `generated-stations-1.cs.txt` → `-2` → `-3` | Play Mode без сети (временная стартовая сцена `TeamSelection_Screen`). Три последовательных вызова через кадры, состояние хранится в `AppDomain` (`GenProbe7`). Ответ «ещё выгружается — повторить вызов» означает повторить тот же вызов |
| `native-inventory.cs` | Редактор без Play: инвентарь сетевых и UXR ID шести карт без сохранения сцен |
| `index-compile.cs` | Редактор без Play: экспорт входов компилятора для офлайн-сверки индекса |

Отчёты прежних срезов лежат локально в основном checkout: `Docs/tasks/report/map-runtime-bootstrap/`.

## Известные ограничения

- **Все карты на генераторе** (arsenal-generator, «данные нового флоу на всех картах», 4107a18b; каталог
  `MapRuntimeCatalog.ArsenalComposition` назначен). Серверная половина адаптера и e2e двух процессов на настоящих картах
  перепроверены после этого вливания (2026-10-09, аренда 285, база eb0f0906): хост Lobby (2 станции, Passed) →
  TestMap1 (8 станций, Passed) → перезагрузка той же карты → Lobby, server Ready на каждом шаге, ошибок консоли 0;
  E2E `map-run-startup-route` (выделенный сервер + 2 клиента, поздний, перезагрузка карты) на картах со
  сгенерированными станциями — GREEN, в логах сервера и клиентов 0 отказов `Arsenal.Generated.*`/`Station.Generated.*`
  (клиентская сверка с config прошла), 0 исключений и ошибок. Артефакты (локально):
  `Tools/e2e/results/20261009-060047-map-run-startup-route/`.
- Проверка станции требует непустых UXR id у всех компонентов внутри неё. World-space канвас на станции дал бы
  ложный отказ; сейчас таких нет.
- Настроенный локальный SDK root не перенесён в startup процесса: процессный корень по-прежнему даёт Offline.
- Play из сцены карты идёт Offline → Lobby → карта: Lobby задаёт `onlineScene`, а `DebugOrchestrator` грузит карту только после лобби.
- Перезагрузка той же сцены держится на SDK-патче 51 (точная регистрация и отмена регистрации UniqueId). Обходов в
  коде bootstrap нет.
- `MapBootstrapMigration` и `MapRunPreflight` пишут отчёты в старый игнорируемый каталог
  `Docs/tasks/report/map-runtime-bootstrap/details/`. Перенос в `tasks/map-runtime-bootstrap/reports/` — правка кода, в этот этап не входит.
- Предмет другого игрока, лежащий в слоте, уничтожается вместе со сценой, если он в этой сцене.

## Критерии приёмки `generated-stations`

- На базе не старше 5aafcbc4 компиляция проходит без ошибок, AndroidCompileGate PASS.
- `MapArsenalCompositionAdapterTests` зелёные. В группах Arsenal, ArsenalWall, Managers, Maps, Modes, Network, UI
  нет новых падений против чистого dev той же базы (сравнение по именам).
- Пробы `generated-stations-1..3` проходят полностью.
- Play на хосте: Offline → лобби → серия на TestMap1 → «Начать матч» → «Стоп» → лобби. Стены и магазины
  пополняются, в консоли нет `Arsenal.Generated.*` и `Station.Generated.*`.
- Документация актуальна: Readme.md и этот документ, `Docs/game-manager.md`; запись изменений — в [changelog/](changelog/).
- Пользователь поручил проверить через MCP и влить. Отдельной проверки в шлеме этот этап не требует: видимого изменения в игре нет.

## Координация

`needs` этапа пуст сознательно: на 2026-10-08 в реестре координации нет ни одного контракта. Фактические зависимости
`generated-stations`:
- **API сборщика станций** (владелец — задача arsenal-generator): `ArsenalStationComposer.PrepareComposition / Activate /
  ValidateReady`, `ArsenalStationBuildInput.Capture`, `ArsenalStationResolver.ResolveDescription`, `ArsenalPlacementInput`
  (шесть аргументов), `ArsenalStationCompositionBinding.VisualRequest`, `ArsenalStationAnchor.RaisedBoundsWorld`. Сверено
  с dev 5aafcbc4; генератор подтвердил, что сигнатуры не меняет. Когда владелец зарегистрирует контракт, этап получит
  `needs` на его ревизию.
- **Барьер MapBootstrap/Relay** (`MapBootstrap.IsLocallyReady`, `LocalReadinessChanged`, `InitialStateBarrier`) — владелец
  эта задача; потребители — генератор (готовность станций) и Relay.

Будущие изменения арсенала, затрагивающие перечисленные сигнатуры или порядок Prepare → Activate → ValidateReady, влияют
на этап; изменения раскладки, стиля и декора — нет (адаптер только переносит LayoutFingerprint в конфиг).

Общие индексы и журналы (`Docs/README.md`, `Docs/CHANGELOG.md`) задача не пишет: записи изменений — в собственном
[changelog/](changelog/). Из кода с другими задачами этап не пересекается.

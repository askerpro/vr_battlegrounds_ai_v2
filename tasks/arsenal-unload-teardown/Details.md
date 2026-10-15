# Детали: разборка станций арсенала при выгрузке карты

2026-10-13, этап `top-level-detach`.

## Кто вызывает `ArsenalCompositionHandle.Dispose`

Handle регистрируется в scope запуска (`ArsenalStationComposer.PrepareComposition` → `scope.Own(handle)`), поэтому
`Dispose` зовёт только `MapRunScope.Dispose` (в обратном порядке регистрации). Откат неудачной сборки уничтожает
слоты напрямую (`DestroyImmediate` в `catch`), без handle. Пути до `MapRunScope.Dispose`:

| Путь | Где | Сцена карты в этот момент |
|---|---|---|
| `MapBootstrap.OnDestroy` → `RetireServerRun` → `MapRunAuthority.Retire` → `CommitTerminal` → `DisposeScope` | сервер / host | **уничтожается** (смена сцены Mirror `ServerChangeScene` из `MapLoader.LoadMap`, выход) |
| `MapBootstrap.OnDestroy` → `DisposeLocalScope` | клиент | **уничтожается** (клиент грузит сцену по `SceneMessage`, выход) |
| `MapBootstrap.FailRun` → `MapRunAuthority.Fail` → `CommitTerminal` → `DisposeScope` | сервер | живёт (отказ запуска) |
| `MapBootstrap.FailLocalRun` → `DisposeLocalScope` | клиент | живёт (отказ запуска на клиенте) |
| `MapRunAuthority.OnStopServer` → `CommitTerminal(Retiring)` / `DisposeScope` | сервер | живёт: Mirror `StopServer`/`StopHost` (и из `OnApplicationQuit`) зовёт его до смены на offline-сцену |
| `MapRunAuthority.OnDestroy` → `DisposeScope` | сервер | обычно scope уже снят в `OnStopServer`; иначе — уничтожение `SessionContext` |
| `MapRunAuthority.BeginRun` → `DisposeScope` | сервер | живёт; после `CommitTerminal` scope уже `null` — на практике пусто |

Смена запуска на той же сцене (сервер перезапущен в сцене карты, `ResetServerRun` → новый `BeginRun`) разбирает
прежний scope в `OnStopServer`/`OnDestroy` старого authority — при живой сцене.

## Признак выгрузки

Явный, от владельца сцены: `MapBootstrap.OnDestroy` до разборки scope вызывает
`ArsenalStationComposer.ReleaseWithScene(gameObject.scene)`. Сборщик (владелец реестра живых сборок `Live`)
помечает сборки станций этой сцены (`ArsenalCompositionHandle.ReleasesWithScene`; сцена запомнена при сборке —
находится, даже если объект станции уже уничтожен). Затем scope разбирается как раньше, и `Dispose` помеченной
сборки предметы не отцепляет.

Почему так:
- `OnDestroy` компонента `MapBootstrap` на корне карты — ровно момент «сцена карты (или сам корень карты)
  уничтожается»; других игровых вызовов `Destroy` для `MapRoot` нет. Все пути с живой сценой идут не через него.
- Причину нельзя передать через `MapRunAuthority.DisposeScope`: scope (`MapRunScope`) хранит только `Action`
  без причины, а разборка станции — не дело издателя состояния карты. `MapRunAuthority` не менялся и остаётся
  единственным публикатором; `MapRunConfig`, ключ и порядок сборки не тронуты.
- Эвристика по Unity (`gameObject.scene.isLoaded == false` в `OnDestroy` при выгрузке) широко применяется, но
  в документации Unity порядок состояния сцены во время выгрузки не описан, а при выходе из приложения сцена
  остаётся «загруженной». Явный признак от владельца не зависит от этого и от порядка `OnDestroy` в сцене.
- Шов `IArsenalStationComposer` не расширен (файл адаптера вне writes этапа): сборки тестовых сборщиков в `Live`
  не попадают, для них поведение прежнее.

## Что происходит с предметами

«Чужой» — хватаемый предмет (`UxrGrabbableObject`) под слотом, которого не было в слоте при сборке: оружие стены
(`ArsenalSlotController.AssignNetworkItem`), магазин склада (`ArsenalMagazineOffer.Assign`), вложенные в них
части. Свои объекты слота (якоря, опоры, карточка) хватаемых предметов не содержат.

| Случай | Верхний уровень (нет чужого хватаемого предка до слота) | Вложенный (стартовый магазин в оружии, затвор) |
|---|---|---|
| Сцена живёт | отцепляется в корень своей сцены (`SetParent(null, true)`), остаётся жить | остаётся в родителе и уезжает с ним |
| Сцена уходит | не переподвешивается, уничтожается со сценой | уничтожается с родителем |

Сетевой / нет:
- Под слотом объект всегда в сцене карты (родитель в сцене карты; предмет из DontDestroyOnLoad, положенный в слот,
  тоже переходит в её сцену). При выгрузке сетевой предмет уничтожается со сценой, и Mirror сам снимает его:
  `NetworkIdentity.OnDestroy` на сервере → `NetworkServer.Destroy` → запись уходит из `NetworkServer.spawned`.
  Второго писателя нет: разборка ничего не спавнит и не уничтожает сверх слотов.
- При живой сцене отцепление локально на каждой машине (каждая собирает и разбирает свою станцию по одному ключу);
  это не синхронизированное действие, авторитет не требуется. Несетевые предметы ведут себя так же.
- Раньше отцепление во время выгрузки рождало корни, переживавшие выгрузку вне сцены (`!scene.IsValid()`), с живым
  `NetworkIdentity` в `spawned` и `AnchoredItemCollisionIgnore`, трогающим коллайдеры вне сцены.

Правило вложенности уточнено: предком-«перевозчиком» считается только чужой хватаемый предмет. Свой хватаемый
предок уничтожался бы со слотом и вложенное не спас бы; в текущем префабе слота своих хватаемых нет, поэтому на
игру это не влияет.

Попутно: `ArsenalStationComposer.Forget` снимал запись `Live` только при живой станции (`Station != null` —
Unity-null). При выгрузке станция бывает уничтожена раньше разборки, и запись разобранной сборки оставалась в
реестре. Теперь проверка по ссылке (`ReferenceEquals`), запись снимается всегда.

## Проверки

- Офлайн-компиляция `VrBattlegrounds` (Assets/Scripts, Roslyn Unity 6000.4.1f1, ссылки из csproj основного
  checkout) — 0 ошибок; `VrBattlegrounds.Tests.EditMode` против новой сборки — 0 ошибок. Тесты не запускались.
- Приёмка в Unity: перезагрузка той же карты через `MapLoader.LoadMap` на выделенном сервере и host — 0
  `UxrGrabbableObject` с `!scene.IsValid()`, нет «not cleaned up», 0 «Tried to ignore collisions»; прогон
  launch-infra `map-run-startup-route`; отказ запуска / перезапуск сервера в сцене — предметы в слотах не
  уничтожены, вложенные магазины остаются в оружии.

## Этап accepted-tests, возобновление 2026-10-14

Механика top-level-detach принята пользователем и влита; ограничение «тесты после приёмки» выполнено.
Независимая подготовка регрессионных тестов поручена отдельному worker с единственным writable-путём
`Assets/Tests/EditMode/Arsenal/ArsenalCompositionTeardownTests.cs`. Продуктовые исходники не меняются.

План: синтетическая станция и слот; проверка верхнего чужого предмета при живой сцене,
сохранение вложенного чужого предмета в его родителе, отсутствие переподвешивания при ReleaseWithScene,
сохранение владения своими вложенными transforms и корректная очистка Live при Unity-null станции.
GUID нового .meta создаст Unity в последующей аренде. Затем scoped EditMode-прогон и приёмка пользователя.

Unity-прогон пока не начат: собственная заявка catalog-fingerprint 625 имеет blocker выдачи capability,
infra_issue 6704; сопровождающий разбирает его. Подготовка тестов не является PASS этапа.

Блокер 625 затем снят через исходный stdout claim; аренда завершена. Тестовый файл подготовлен:
8 случаев, статическое ревью без новых блокеров. Roslyn Unity 6000.4.1f1 скомпилировал этот единственный
файл против 284 текущих ссылок worker: exit_code=0, compiler.txt пустой. Это compile-only, не NUnit PASS.
SHA256 исходника 08a46006a5b136cc5ace422033d338f8aacf718d0bd0b112fdb64b4fcea1ccee.
Локальный отчёт: [reports/compile-tests/report.json](reports/compile-tests/report.json), вне Git.

Подготовлена и поставлена в FIFO заявка 633, input 0ae82425decd984c604382a4807e20f447a685c9;
выход — Unity-generated .meta тестового файла. В аренде: scoped NUnit class, снимок состава сцен
и UxrGrabManager до/после (включая cleanup), terminal job → немедленный finish, затем отчёт.
Попытка coordination begin дала UNORDERED_OVERLAP с отложенным arsenal-generator/acceptance;
это внутренняя проверка CLI. В действующем общем mode off begin ради допуска инструментов не нужен;
пакет исполняется штатным legacy-маршрутом, с сохранением владельца и аренды Unity.

## Фактическая попытка 640

Заявки 633 и 636 не исполнялись: OFFERED истёк; 636 задержала автоматическая проверка разрешения.
Infra_issue6803 передан сопровождающему. Task-local acquire-test-lease.py объединяет только штатные
request/watch-ticket/claim/begin/guard после предварительного разрешения; capability берётся из собственного
claim stdout приватно, не из БД и не записывается в отчёты.

Аренда640, input1fdc3046f611e7ba3cae94265a3e2680c48c954d: run_tests потерял MCP plugin session при ожидании
ответа; job_id не получен. Повторного запуска не было. До/после: UxrGrabManager=0, Live=0, preview=0,
setup=[], Editor idle. Это не NUnit PASS. Finish640 DONE, result8447d05683d269207a7838469103d490cfe8a8af,
unexpected=[]; receive получил единственный Unity-generated .meta, guid ca7b39726bc307049a5bb7c37cf7d2f5.
Отчёт [reports/attempt-640.json](reports/attempt-640.json),
SHA256 f52a6762ff156ea62bbe807658a2539bf481a1a174a571120a047141e233cef5; infra_issue6863.

В текущем MCP package TestJobManager сохраняет StartJob в SessionState до запуска Unity TestRunner:
MCPForUnity.TestJobsV1 и MCPForUnity.CurrentTestJobIdV1. Следующая собственная аренда сначала читает эту
durable запись, устанавливает исход640 и только затем решает о новом прогоне. Чужие jobs не очищаются.

Подготовленный повторный input дополнен полученным .meta: 2bf166035a08dc6899bb1885a80744781b63e0df,
GUID сохраняется. Собственная QUEUED649 отменена до claim, watcher завершён; замена653 QUEUED.
В хабе accepted-tests RUNNING; у отложенного arsenal-generator/acceptance записан порядок после этих
регрессий (rev21), оба этапа касаются Arsenal tests. Этот порядок соответствует приоритету пользователя.

launch-infra6892 подтвердил отдельный E2E на87643327108f с принятой механикой: map-run-startup-route14/14,
strict enforce GREEN exit0, full logs, unexpected/defects/native/dropped=0. Их изменение known-noise пока
не принято/не влито. Это проверка запуска/перезагрузки карты, не результат нового NUnit fixture.

Principal прочитал исходные summary.json и log-verdict.json в launch-infra checkout 2026-10-14 17:38 UTC:
14/14, exit0, enforce=green, unexpected/defects/native/dropped=0, rawLogs present для server/client-1/client-2.
SHA256 summary8958d7b77d3766c77cd826205d3165e6b09bb558711f830896ad4b689a274d48,
log-verdict a368e3f4736eb17041f6a4c92fdd50a9f5607ecb17f9f1ed2b0e7a1b841b26af.
Evidence root: F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai/tasks/quest-perf-pipeline/reports/
2026-10-14-status/runtime/20261014-195744-map-run-startup-route/.

## Попытка653 и прежний блокер runner

Guard653 подтвердил RUNNING, worker41776, вход2bf166035a08 с сохранённым GUID, Editor idle.
Старый640 не найден среди retained jobs при фильтре его времени / имени fixture; полный исход восстановить
нельзя. Новая изолированная попытка653 снова потеряла plugin session при run_tests. После reload: idle,
currentjob=null; фильтр SessionState jobs по времени вызова вернул0. Это не доказательство достижения handler
и не NUnit verdict. Оба ключа raw после попытки и время самого Editor ещё нужны для строгой корреляции.
До/после managers/live/previews0, setup[]. Finish653 DONE, result1eb1727e2b87ce3bcce49b9fad0e1ba75aabf6ab,
paths=[]/unexpected=[]; GUID сохранён. [reports/attempt-653.json](reports/attempt-653.json),
SHA25694f045927bbb2b191e28cfbdfb6c7f9ec7a342b36bbd94fbdd8537e6ef9be449; infra_issue7002 (follow-up6863).

Read-only диагностика физического worker package: RunTests.cs49 → StartJob339–342 сохраняет SessionState
до lazy TestRunnerService345 и native Execute249. Обычный TestRunner reload сам по себе не объясняет отсутствие
совпавшей записи. Persist подавляет исключение с warning296–298; dispatcher держит RAM-очередь до editor update.
Возможен reload до handler; конкретная причина не доказана. Нужны native log около18:16:38Z, оба raw SessionState
keys, Editor UTC и loaded assembly MVID/Location. Shared package/infra не изменялись, собственных аренд нет.

## Подтверждённый NUnit после восстановления, 2026-10-15

После canonical worker_ready7257 выполнена новая изолированная заявка668; прежние неизвестные640/653
не повторялись. База54673ba496688efbb5c0dd1aaba6287cbe8feb4a, входbf104fcb45e3152be18f6d5a883000bc6823f0c8,
Unity6000.4.1f1, worker41776, broker epoch564. Runtime VERSION eb4ab1d981654f3bcd64cd4eb546cbcc950370d6.

Scoped job c47d9b1953e24feaafbb0eaea853438e завершён succeeded: total8/passed8/failed0/skipped0,
NUnit duration0.73529s. До/после managers/live/previews0, setup[], Editor не в Play и не компилируется.
Finish668 завершён до анализа: result faf8227276fc13dab06609803d935f51d95dee6d, paths[]/unexpected[].
Receive не нужен; исходный GUID ca7b39726bc307049a5bb7c37cf7d2f5 сохранён.

Полный scoped MCP отчёт — [reports/attempt-668.json](reports/attempt-668.json),
SHA2561574f41b307caf7b1af1c3bb435f8dd3e29d308163784bf0accbd6ebbc4a00ab. Это Unity EditMode регрессия
принятой механики; headset/network приёмку не расширяет. Тестовый код принят пользователем 2026-10-15.

Begin accepted-tests записал epoch3, но вернул publication PUBLICATION_BUSY. Infra_issue7295 передан
сопровождающему через хаб и подтверждённый Codex канал; последующее сообщение опубликовано успешно.
Самостоятельный ремонт/обход infra не выполнялся.

## Приёмка тестового кода, 2026-10-15

Ответ пользователя на отдельный вопрос arsenal-unload-teardown/accepted-tests: «Принять и влить
(рекомендуется)». Разрешены только 8 тестов, их Unity-generated .meta и документация этого этапа;
тесты каталога и task-local CLI helpers в принятый коммит не входят. Перед интеграцией подтверждены
неизменный SHA256 теста08a46006… и отсутствие изменений Arsenal/MapRunAuthority/UltimateXR между
проверенной базой54673ba4 и свежим origin/dev. Нового runtime доказательства не заявляется.
Подготовленная owner-spec.v1 — исторический immutable пакет данных7375, не production executor schema.

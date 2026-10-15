# Подробности задачи debug-menu-refactor

## Факты и границы

Состояние на `26129282b4` (origin/dev, 2026-10-14):

- `Assets/Scripts/UI/Menu/MenuDebug.cs` строит экран одной колонкой через `MenuKit`:
  строка статуса; «Оверлей кадра» (кадр, попадания); «Касание меню» (лучи пальцев UI);
  «Разделы» (переходы `Links`: «Перф-тесты» всем, «Матч», «Игроки и команды», «Новая серия» — админу);
  «Боты» (админу); «Детализация лобби» (только при `LobbyDetailTuner.Current`); «Телепорт» (сетка 4 колонки);
  «Режим» (выключить режим отладки).
- Пересборка раз в 0,5 с и только при смене снимка (`_lastSnapshot`); статус обновляется на месте.
- В `MenuKit` нет элемента вкладок/сегментов (есть Row, Grid, Button, Section, TableRow и т. д.).
- Ссылки на `MenuDebug`: `MenuPerfTests.cs`, `Assets/Tests/EditMode/UI/MenuWiringTests.cs`.
- Префаб экрана: `Assets/Prefabs/UI/Menu/Screens/Screen_Debug.prefab` (вариант `Screen_Base`).

## Архитектурный анализ

- Владелец выбранной вкладки — сам `MenuDebug` (локальное UI-состояние, не матчевое, не реплицируется).
- Инварианты: экран — вариант `Screen_Base`, меняется только `Content`; элементы — из `MenuKit`
  по токенам `MenuTheme`; админ-UI — только `MenuPermissions.ShowAdminUi`.
- Альтернатива «вложенные экраны через `Push`» отвергнута как основная: она добавляет экраны и
  шаги назад; вкладки дают один экран с переключением без стека. Окончательно — на этапе `design`.

## Координация

- level-design убрал `Assets/Scripts/UI/Menu/MenuDebug.cs` из writes `lobby-decoration` (ред. 14) и
  `scene-detail` (ред. 3); событие 6493. Секцию детализации сцены level-design делает в своей зоне
  (`VrBattlegrounds.Maps.Detail.SceneDetailDebugSection`: статический `Build(RectTransform content, Action invalidate)`,
  `Snapshot()`; видна, когда в сцене есть `SceneDetailApplier`). ui вызывает её на вкладке «Сцена»
  и включает Snapshot() в снимок пересборки. Условия ui (событие 6495): только элементы MenuKit,
  без `Clear`/`Title`, контейнер вкладки принадлежит ui. Если ui вольётся раньше — остаётся блок
  `LobbyDetailTuner`, а после `scene-detail/core` ui вносит однострочную замену.
- Чужие зоны не затрагиваются: бот-запросы, телепорт, `LobbyDetailTuner`, `DebugMode` вызываются как есть.

## Проверки и расследования

2026-10-14: awake/status и inbox обновлены, coordination off, адресных сообщений нет.
Свежий origin/dev `d3c62ca5e`; checkout linked, ветка `claude/ui`, исходно чистая.
`SceneDetailDebugSection` в этой базе отсутствует: сохраняется интеграция `LobbyDetailTuner`.

## Пакет реализации

- `MenuKit.TabStrip(parent, labels, selectedIndex, onSelected)` — равные по ширине кнопки темы,
  акцент выбранной кнопки; локальное состояние остаётся в MenuDebug.
- `MenuDebug`: шесть категорий, Боты/Админ только по ShowAdminUi, Сцена только при Current tuner.
  Исчезнувшая выбранная категория возвращает на Оверлеи; повторный клик не пересобирает экран.
- Выбор категории немедленно обновляет содержимое; обычный polling сохраняет снимок раз в 0,5 с.
- Preview использует те же категории и образцы телепортов. Кнопка выключения режима удаляется.
- Проверки: компиляция текущего дерева Unity и снимки/раскладка в ограниченной аренде;
  существующие применимые UI-проверки, без изменения тестов до приёмки пользователя.
- Решение: использовать записанные названия категорий при продолжении; цена ошибки — переименование
  или перестановка вкладок по результату приёмки. Runtime/Quest приёмка этим решением не подменяется.

## Выполненная реализация и проверка исходников (2026-10-14)

Изменены только `MenuDebug.cs`, `MenuKit.cs` и материал задачи. Префабы, тема, игровые
механизмы и тесты не менялись. Таблица Links сохранена для существующего MenuWiringTests.
Независимый read-only reviewer `ui_review` (gpt-6.1-sol high) не нашёл P0–P2 ошибок;
это source-review, не доказательство компиляции или поведения в шлеме.
`git diff --check` прошёл; проверка физических пяти изменённых файлов — LF, 0 issues.

Технический вход Unity `ff4301666f3df30f2faaad62783370dd977e9c73`, не принятый коммит.
Билет 624: CANCELLED (истёк OFFERED до claim); аренда не захватывалась.
Билет 627: заявлен тем же входом и новым key, ожидает очереди. Другие аренды не трогаем.
Инфра-предупреждение EDITOR_DELAYCALL_STALLED передано сопровождающему, событие 6673.
Сообщение 6695 разрешило штатные заявки после возобновления очереди; ресурсный MCP
discovery вернул Session terminated, но read_console затем ответил. Ни этот ответ,
ни 0 ошибок чужого Editor не подтверждают нашу компиляцию.

Диагностический пакет `tools/debug-menu-preview.cs.txt`: один Tablet.prefab, шесть категорий
с образцовым неактивным tuner, контроль ширины/границ/выделения, callbacks вкладок и
повторный клик. Не меняет Current, права, QualitySettings; очищает preview scene в finally.
Ревью пакета выполнено, запуск пока не подтверждён.

На 19:00 МСК: Roslyn C#9 compile-source.py прошёл (exit 0, ошибок 0, 411 reference DLL).
Компилировались только MenuDebug.cs и MenuKit.cs; reference Assembly-CSharp.dll из основного
checkout (только чтение), SHA256 `d29384db3c94a570bd0d71b0e0ce5814cfe08b92f8abbc4687696f06d1b6002a`.
Подавлено только ожидаемое предупреждение CS0436 о двух исходных типах поверх reference DLL.
Полный отчёт и лог: `reports/managed-compile/`. Это семантическая проверка исходников,
без Unity import, Mirror weaving, Play и Quest. Проверка скрипта превью остаётся открытой.

Hub begin и последующее чтение inbox получили database is locked; запись infra_issue
о блокировке доставлена сопровождающему событием 6749. Самостоятельного ремонта shared
инфраструктуры не выполняли. Билет 627 остаётся QUEUED, собственной аренды нет.
Принятый коммит/вливание не выполнялись, все изменения сохранены в working tree.

19:59: fresh awake/status/inbox; маршрут ui очищен собственной CAS-публикацией epoch4.
Прочитано 6805 worker_ready_resume: штатные заявки разрешены. 627 terminal CANCELLED без
claim/begin. Fresh fetch/rebase origin/dev97a320c88 (autostash применён), исходники не изменились.
Входящее 6807 scene-detail: core WIP не принят/не опубликован; остаёмся на согласованном маршруте
замены UI после принятого merge core. До него применяется принятый LobbyDetailTuner.

20:03: новое обычное input44f9cc7b891cbb8064b2be9d948e053304b4f7e9, заявка642 QUEUED.
Register свежей базы снова вернул database is locked/state_may_have_changed=true; status после
отказа показал hub revision4. Свежий отказ доставлен сопровождающему, событие6836.
Core-приёмка ждёт UI (6841), поэтому ui согласовал подготовку отдельной технической композиции
(6857) после обычной проверки642. Полный маршрут и границы записаны в
contracts/2026-10-14-scene-detail-technical-input.md; source runtime capability [1] подтверждена.
Borrowed input пока не создан/не заявлен: нет exact grant и recorded own dependency proof.

## Результат аренды642 (20:17–20:21)

Input44f9cc7b891cbb8064b2be9d948e053304b4f7e9, B97a320c88, RUNNING epoch541, process41776.
Guard подтвердил worker root и input. Unity preflight загрузил TabStrip и поле category,
не было активного compilation/Play. Preview6 PASS failures0: equal width225,33, selected1,
без переполнения полосы и обрезки текста; non-admin без tuner 3 таба; callbacks и повторный
клик прошли. Шесть PNG после finish просмотрены — видимых дефектов раскладки не найдено.
Scope — один Tablet.prefab, образцовый inactive tuner; это не Runtime/Quest приёмка.

run_tests EditMode (6 UI classes) 20:18:49 вернул disconnected plugin session991f49af-...
без job ID; success/passed suite не установлены. Hint retry проигнорирован. После ошибки
durable guard: ready=true, tests/compiling/updating/playing=false, leftover preview0.
Снимки/отчёты скопированы, затем сразу finish; R e93c4f3d10462ab783b3697558393db91923dfc4,
paths0/unexpected0, штатный receive выполнен. Accepted в capture — техническая целостность,
не принятие пользователем. Полные MCP responses — reports/unity-ticket-642/.
Инфра-отчёт доставлен сопровождающему событием6916.

Read-only исследование test_outcome: proxy dispatch есть, фактический suite result неизвестен;
package хранит последние10 jobs в SessionState, ключи MCPForUnity.TestJobsV1 и
MCPForUnity.CurrentTestJobIdV1; Result в persistence не сохраняется. Editor PID41776 тот же,
наблюдались recompile/domain reload/new plugin session. Нет доказательства RunFinished.
Подготовлен tools/read-ui-test-state.cs.txt, inputbbab0916192d92ad7f442c2ec5e0e6493c503090,
короткая заявка652 только на чтение metadata. Тесты повторно не запускаются до terminal/no-start proof.

## Актуальное состояние на 21:18 МСК

Хаб rev5: design VERIFIED epoch3, SHA63dbf45f56b67a396f6f05a30dc992e3fb2795d7;
implementation RUNNING epoch1. Scope реализации — два UI C# и материал своей задачи.
Собственный native route очищен CAS epoch5 при resume; используются canonical inbox и wait-inbox.
Согласованный consumer binding для SceneDetail отправлен событием6964; exact provider grant
ещё не получен. Helper tools/compose-input.py создан и просмотрен: CLI/AST/LF проверены,
ветка композиции не выполнялась, чужие рабочие файлы не менялись.

Аренда652: inputbbab0916192d92ad7f442c2ec5e0e6493c503090, RUNNING epoch549, PID41776.
Прочитаны только SessionState metadata и записан свой отчёт. Current job пуст, storedJobs5,
matchedJobs0 по времени17:18:49UTC. Все сохранённые jobs terminal; три до UI-вызова
и два после. Обычное вытеснение исключено (лимит10, записей5), но best-effort persistence
при ошибке могла потерять job после reload. Это не результат suite; проверяются warnings.
Guard ready=true, tests/Play/compile/update=false; предупреждение delayCall stalled сохранилось.
Отчёт скопирован и сразу finish выполнен, R7b0a1b48aab8194790c9171d779d6e7158fa81c0,
paths0/unexpected0. Отчёты — reports/unity-ticket-652/; никаких тестов повторно не запускали.

Receive652 выполнен. Узкий read-only check: Failed to persist/restore SessionState в текущем
worker Editor.log отсутствуют; exact keys в package встречаются только в TestJobManager.
Normal StartJob сохраняет job до RunTestsAsync, ClearStuckJob не удаляет записи. Вместе с
current empty, terminal jobs, неизменным PID и guard это допускает новый независимый прогон.
Первоначальный результат остаётся unknown; абсолютного доказательства незапуска нет.
Fresh fetch origin/dev не изменил B97a320c88. Новый прогон657 QUEUED, inputbbab0916192d92ad7f442c2ec5e0e6493c503090,
key debug-menu-refactor-ui-suite-independent-20261014-1; пакет — шесть существующих UI classes,
job ID, ожидание terminal, cleanup/finish/receive. Новых тестов/clear_stuck нет.

## Повторяемый блокер UI-suite (аренда657)

657 выполнена: RUNNING epoch554, прежние input/B и PID41776. Fresh guard ready=true,
tests/Play/compile/update=false; delayCall stalled warning до вызова. run_tests EditMode
по шести классам снова вернул disconnected plugin session ffe7c0d3-cc1e-475f-822b-44c7d388298b,
без job ID. Это UNKNOWN outcome, не failed tests и не PASS. Старый исход642 не восстановлен.
После ошибки durable guard ready=true, tests/Play/compile/update=false, leftover preview0.
Немедленный finish до диагностики: Rda47c7f57c764be9be8e97e8d29e08b3c5f94f77,
paths0/unexpected0. Полный ответ — reports/unity-ticket-657/mcp-test-start.json.
Receive657 выполнен; infra issue7037 с воспроизведением доставлен сопровождающему worker/agent-infra. Shared repair,
clear_stuck и третьего запуска не выполняем; ждём его диагноза/исправления.
Source/preview checks остаются применимы: production C# после642 не менялся и совпадает с input.
Exact SceneDetail grant и пользовательская приёмка в шлеме по-прежнему ожидаются.

## Эскалация инфраструктуры (2026-10-15)

Canonical событие7160 maintainer_escalation_policy прочитано. Прямое распоряжение пользователя
проверено через read_thread актуального чата сопровождающего: при worker/broker/MCP проблемах
сразу infra_issue из своей задачи/owner в agent-coordination-protocol, затем внутреннее Codex
уведомление с task-id/event_id после свежей проверки обоих endpoints через list_threads/read_thread.
Разрешения пользователя повторно не спрашиваем; capabilities/tokens остаются приватными,
неизвестные Unity-мутации не повторяем. Это не ACK и не передача владения игровыми задачами.
Прежний infra_issue7037 дополнительно доставлен во внутренний чат сопровождающего.
Recovery664 выполняет только назначенный сопровождающий; ui не запускает recover/Refresh.
Глобальный desktop discovery теперь доступен; свой route обновлён на проверенный thread ID.

## Возобновление после worker_ready7257 (2026-10-15, 01:15 МСК)

Recovery664 завершён отдельно (7202), worker_ready7257 разрешил штатные подготовленные заявки.
Сопровождающий установил SDK unity-mcp2 и в своей667 доказал native prebuild24,9с и полный
Failed Result; это инфраструктурная проверка, не наш UI suite. Старые642/657 остаются UNKNOWN.
Вопрос о маршруте нового независимого UI verification на свежей базе отправлен7261 через
hub и проверенный Codex endpoint. Никаких replay прежних request keys/repair/обходов нет.
Fresh fetch/rebase с autostash выполнен: B54673ba496688efbb5c0dd1aaba6287cbe8feb4a,
HEADa1dda03905da1dbee393d6ead9c133599d498f38. Tracked/untracked сохранены, production C#
Git blobs прежние: MenuDebugd8e1fa11b, MenuKit3b7a5dfd4. Runtime VERSIONeb4ab1d981654f3bcd64cd4eb546cbcc950370d6.
7189 сообщил отдельный provider checkpoint blocker; exact grant пока отсутствует. После новой
B прежний6964 binding требует актуализации, borrowed input не собираем до exact grant.

Registration rev6 успешна, design остаётся verified, implementation running. Технический
checkpointc3fbe124559b26192b3681bf6cbe6978928bea9b построен штатно onto-baselineB54673ba49.
В нём два production C# и собственный материал задачи/эксперта из прежнего WIP; CLI --path
не исключает уже committed HEAD delta. Diff проверен: чужих runtime/assets нет. Уточнение7261
ожидается до нового run_tests; старая неизвестная операция не повторяется.

Ответ7303 разрешил новый независимый прогон после old leases terminal/native not busy.
Status подтвердил642/657 DONE/IMPORTED; результаты старых suite остаются UNKNOWN.
Новая заявка672 QUEUED: inputc3fbe124559b26192b3681bf6cbe6978928bea9b, B54673ba49,
key debug-menu-refactor-fresh-sdk2-ui-20261015-1. Только один штатный run_tests в своей
RUNNING/guard; job ID и полный terminal Result сохраняются до finish. При потере start
или null Result — сразу infra_issue и native уведомление, без clear_stuck/альтернативного клиента.

## Результат672: подтверждённый сбой инициализации

RUNNING epoch568, inputc3fbe1245/B54673ba49/PID41776; native not busy подтверждён guard.
Один run_tests по прежним шести UI classes с init_timeout120000 вернул job ID
d29d2ebb455c4b55910e8741478fa04b. Start receipt и каждый poll сохранены в reports/unity-ticket-672/.
Terminal: failed, completed0, totalnull, resultnull, error Test job failed to initialize
(tests did not start within timeout). Started1792017256270, finished1792017401795;
MCP не получил RunStarted/RunFinished callbacks; PASS/failed/skipped counts не получены. Полный terminal response
mcp-test-result-3.json сохранён ДОfinish. Native guard: readytrue, tests/Play/compile/updatefalse,
leftover0, warning EDITOR_DELAYCALL_STALLED135.1s; причину warning не объявляем причиной сбоя.
Немедленный finish и receive выполнены: Re3474218ab098b09cd073c618a264807acbba3e0,
paths0/unexpected0. Namespace/class каждого из6 fixtures проверены в source и совпадают с filters.
Infra_issue7327 доставлен в hub и проверенный Codex чат сопровождающего. Runtime VERSION прежняя
eb4ab1d981654f3bcd64cd4eb546cbcc950370d6. Repair, replay, clear_stuck, альтернативного клиента нет;
ждём диагностики сопровождающего. Старые642/657 остаются UNKNOWN, это отдельный новый исход.

## Подготовка CLI-spec по infra_status7375

FIFO не ставится на плановую паузу ремонта; существующие leases/ownership/guards сохраняются.
Новые независимые разрешённые пакеты допустимы, но product ui не чинит и не обходит worker,
broker/proxy/hooks/MCP. Production CLI executor ещё не выпущен, альтернативный клиент не запускаем.
Schema/path offline CLI-контракта запрошены событием7386. До ответа подготовлен
contracts/ui-test-intent-v1.json: неисполняемый source-bound caller intent с шестью прежними
fixtures, точными Git blobs, XML/editor.log/receipt, проверкой полноты и cleanup через брокер.
Прежний inputc3fbe1245 закреплён как исходная точка; перед будущим submit требуется новая свежая
B/input/binding и адаптация к опубликованной схеме. Это не ready CLI request и не игровой PASS.
SHA256 intent v1: e683d8fcea259cf32b739051ad3690a491e3700f930a30d7affc6d1553ce8869;
JSON parse/6 fixtures/LF прошли. После фиксации hash файл v1 не переопределяем: новая схема
или B/input оформляются новой версией. Ответ7397 подтвердил provider blocker SceneDetail,
свежий epoch/permit/B/TTL будет согласован до grant; старый6964 binding не используется.

Ответ7404/7409 дал offline contract schema1: platform/filters/categories/assemblies и точные
ticket/epoch/owner/task/request_key/B/A/project_root/Unity/UTF поля; capabilities запрещены.
contracts/ui-cli-offline-spec-672-v1.json использует реальные исторические данные завершённой672
для строгой offline-проверки, НЕ новую аренду/admission. GUI worker root в нём тоже исторический;
его нельзя запускать CLI при открытом Editor. Перед production нужны новый ticket/epoch/key,
свежие B/A и зарегистрированный закрытый CLI checkout. Только contract.py help/plan без Unity launch.

Существенное уточнение7404: сопровождающий сообщил native RunStarted/RunFinished/XML по сохранённому
Editor.log672; после reload MCP receiver не восстановил callbacks. Поэтому прежнее заключение
«тесты не выполнялись» по нулевому MCP progress было избыточным. У нас пока нет полного
source-bound результата с подтверждёнными counts: игровой PASS не заявляем. Native XML/evidence
запрашивается у сопровождающего отдельно, старые неизвестные операции не повторяем.

Strict offline plan прошёл schema1, production_readyfalse; request_id85942f3fa2d9414572a97e13bc1b4212,
fingerprint922f673d25bae22f72bdd39d26ec27c2c079a1d0e057875b717339f1c1fea3f5.
Полный plan сохранён в reports/ui-cli-offline-672-v1/plan.json (ignored). CLI spec SHA256
554a63666da629b2a8a819ce8f32bbdea0be6f11efc4e7457ec47d113d65b004;
contract.py SHA2564dd850167ee4d736e6795235a635d972047eb39c996d4cbfae66631246f8e0af.
Это проверка формы/argv без записей в shared state и без запуска Unity; историческая672 не
переоткрывалась. XML/editor log/source-bound proof672 запрошены отдельно, UI PASS пока не заявлен.

## Свидетельства7430 проверены (2026-10-15)

Скопированы только адресуемые redacted log и manifest из ownmaintenance reports сопровождающего
в reports/ui672-native-correlation/. Log SHA25699f64ab2be5669c4441418bd76c55f5402524f70e9c371331b55c834bb01a1aa
совпадает с7430/manifest. Manifest SHA25611084e1636d48ece7f87306ab95f07ca2793b6f9fc1240a7e98b260997d02b62.
Источник Editor.log, строки978680–979230, rawchunk SHA25687b975ac7e4ecb9fef1ebb2deebb4c23667f3ed1cd3aefe70731f8d3b1275d04.
Фрагмент содержит native RunStarted/RunFinished, запись общего TestResults.xml, reload и timeout
точного jobd29d2ebb455c4b55910e8741478fa04b. Это log correlation/callback gap, без восстановления
fixture coverage, counts или ticket-specific XML. Общий XML уже перезаписан другими runs;
его не присваиваем672 и не читаем как результат своего пакета. xml_recovered=false/ui_accepted=false.
Тестовый исход шести UI classes остаётся неподтверждённым; source-review/preview scope прежний.

## Новый независимый пакет после7457

7457: runtimef6387f9dd19ff4547e7f98c53e6e4718d5735933 и pinned MCP10.2.0+vr2012.de5035a57e7f
проверены сопровождающим в681 через native prebuild25,5с и midrun reload с полным Result3.
Это infra proof, старые UI результаты не принимает. Новая подготовка: explicit refresh/compile,
проверка reload/import/native readiness, tools/ui-discovery.cs.txt (async, без блокировкиEditor),
discovery6 exact fixtures/nonzero cases/assembly loaded, затем один штатныйrun_tests.
Полный terminal summary/results сохраняются доfinish, progress.completed может быть неполон.
Fresh fetch/rebase с autostash сохранил tracked/untracked; production2C# blobs неизменны.
На момент checkpoint accepted Bbb28046079f0ad3251ab3bbc22988027bf00c107 совпала с baselinebroker;
HEAD e45f1e7012dc4d559502ff12d9dfcc1bc2027f0e, input3e7ca345cf4e98d6593c5c0cb253345c19018fe7.
Input содержит2UI source и свой ранее committed материал задачи/эксперта, foreign runtime delta нет.
Новый discovery snippet статически сверял read-only explorer, вUnity ещё не исполнялся.

## Продолжение по7492 и актуальный статус воркера

Хаб зарегистрировал план rev7 на базеbb2804607; собственный Codex route epoch12 после свежего native listing.
Пакет686 QUEUED, input3e7ca345cf4e98d6593c5c0cb253345c19018fe7; это новый независимый запуск,
старые UNKNOWN не повторяются. Основной test-results repair подтверждён сопровождающим7457.
После одного разрешённого read-only debug_request_context discovery ресурсов успешно:
resources19/templates6. Raw в reports/resource-discovery-20261015.json; canonical7520 и Codex доставлены.
Результат discovery не означает PASS нашего suite. Новых тестов, gameplay/assets и infra не меняли.

## Приёмка пользователем (после Unity-проверки)

1. Открыть «Отладка» на VR-планшете. Проверить читаемость ряда, выделение и попадание
   пальцем/лучом по каждому табу; переключение сразу меняет только категорию.
2. «Оверлеи»: только попадания. «Касание»: лучи и пояснение. «Телепорт»: прежние точки и запросы.
3. «Перф-анализ»: общий кадр/FPS, GPU — тест сцены (MapOnly) и доступная детализация;
   CPU — тесты нагрузки аватаров (Standard). Навигация не запускает тест; активный прогон не меняется.
   Эти режимы не изолируют процессоры: оба пишут CPU/GPU, общий FPS не выдается за CPU/GPU timing.
4. «Админ → Игроки и команды»: добавить/убрать ботов, число обновляется; без admin guard кнопок нет.
   Исчезновение SceneDetailApplier убирает только секцию детализации, вкладка перф остаётся доступной.
   В GPU изменить настройку детализации и сохранить профиль: состояние/результат сохранения
   обновляются без потери вкладки; выход в сцену без аплайера не оставляет старые элементы.
   Дублирующей вкладки «Админ» и кнопки выключения режима в «Отладке» нет.
5. Закрыть/открыть меню и вернуться из вложенного экрана: выбор локальный, стек планшета
   прежний; проверить скролл и отсутствие сброса наведения в покое.

Новые проверки категорий/размеров в suite добавляются только после этой приёмки.
Существующие tests проверяют общую проводку/дизайн/права; их успешный прогон не покрывает
новые runtime-переходы прав/сцены, выбор режима через GPU/CPU и физическое касание вкладок.

## Уточнения пользователя 2026-10-15: GPU/CPU и боты

MenuDebug теперь4 категории. MenuPlayersTeams владеет UI управления ботами, до существующего
teams.Length0 fallback, но после admin guard. Серверная политика DebugAdminPolicy/SessionPermissions
не изменена. MenuPerfTests.Open(mode) выбирает локальный mode лишь вне активного прогона и открывает
экран настройки; серверные команды старта не вызываются навигацией. В GPU — MapOnly (куклы по карте),
в CPU — Standard (нагрузка аватаров); пользователь может выбрать остальные существующие режимы в тестах.
Старые preview6 и compile2 относятся к исходной группировке. Текущая compile охватывает4UI source.
686 cancelled offer_expired7547, Unity не начиналась; claim не повторяется.
Новый source provider SceneDetailDebugSection.Build/Snapshot в собственной базе отсутствует.
Функционал main-checkout проверен read-only: это настройка LOD/mip/мелочи/профиля, а не отдельный
runtime профайлер. Он размещается в GPU-секции после accepted merge или immutable grant.
Editor-only SceneDetailSetup и внешний Tools/Perf/quest_gpu_breakdown.py не переносятся в планшет.

## Текущая Unity-проверка697: 50 PASS

Inputf90de663e5ce689895694e845021941bb7e0f80f, parentB1812dc5bf22860e8302e858b1637e92b1df636e9;
delta9 собственных paths, source blobs4UI совпадают с working tree. Claim однократный, epoch592,
workerPID41776, sourceRootF:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai.
Явный refresh compile=request завершился reload (новая generation6941973 после6873488),
state advice readytrue/stalefalse/dirtyfalse; native guard readytrue, busy flagsfalse.
Async RetrieveTestList завершён:6 exact fixtures,50 cases, всеRunnable, assemblyVrBattlegrounds.Tests.EditMode.
Один standard run_tests, job58eb3537d1bc41dfa04692261b4987ee: terminal succeeded,
Result.summary total50/passed50/failed0/skipped0, duration1.8713007s;50 полных results сохранены ДОfinish.
Покрытие fixtures: DesignRules10, KitLogic16, Wiring14, Tabs4, Permissions4, Fonts2.
Отчёты reports/unity-ticket-697/: refresh-request, discovery-request/ui-discovery,
mcp-test-start/mcp-test-result-1, native guards. Post guard Play/tests/compile/updatefalse, leftover0.
Немедленный finish/receive: R91d2a6c8147e85d0cb7843daa0f4f5289d1da5d2, paths0/unexpected0.
Discovery raw архивирован брокером, sha2564fc637206a062fdac7fca4676865847ddb98addd48cf94ee7e39b9912dbc013a.
Scope — реальные Unity EditMode checks текущих4UI исходников. Это не physical Quest,
не настоящие действия AddBot/teleport и не проверка отсутствующего providerSceneDetail.
Старый render helper tools/debug-menu-preview.cs.txt имеет ожидания6старых категорий и теперь устарел;
его не запускали/не переписывали, актуальные4категории отдельно нужно визуально проверить.
На момент проверки697 source provider ещё не был передан; размещение GPU принято7639.

## Подключение SceneDetail после697

Exact provider source30901528d984fe18b5ad01f03ddb06a827f70c21 получен7681/7683.
MenuDebug: один Snapshot за Refresh, снимок участвует в invalidation и доступности секции;
Build вызывается внутри GPU, callback инвалидирует UI. Legacy LobbyDetailTuner удалён из consumer.
Provider static review и final consumer review: P0–P2=0. Это не runtime-проверка.
Managed compile:4UI+3точных provider blobs,411refs,exit0/errors0;2старых CS0618 warnings.
Source309/Profile141aa8dc8/Applier031b42fad/DebugSectionb645b5928 зафиксированы в
reports/managed-compile/report.json. Unity import/weaving/Quest этим не подтверждены.

Checkout штатно обновлён до accepted B d449f19832b1c2b0f6bb9c9ada3b9db4a4a2b299 с autostash.
Hub consumer12/implementation epoch3; свежий собственный permit TTL1792032379.6763787.
Grant rev1 consumer10/B1812 устарел; rebind запрос7766 отправлен владельцу SceneDetail.
Технический own input70840de6ea56d2107f166f1fdff008bc02f111ab содержит ровно4UI source
поверхB, без experts/foreign/docs. HEAD, настоящий index и working tree сохранены.
Composer готов, новый Unity request не создан до свежего canonical grant.

## Снимки текущего меню714 — 2026-10-15

Прямой запрос пользователя выполнен:7 PNG в reports/714/snapshots, все просмотрены.
Inputb97f528fe7a0cecef0ddcac6998dfdbe7a605891, B04fc9de8875d390ab2fb1ddb4116536aa288a01d;
grant3 source18d8e91289c7af778aa8ac71e6b11a62967fc5c0,152readonly entries.
RUNNING epoch606/PID41776, native root/input совпали. MCP instance выбран по guard,
project/info подтвердил workerRoot. Explicit refresh/compile завершил reload0730393;
readytrue/stalefalse/dirtyfalse, native compile/import/tests/playfalse.
Один execute_code capture: captureCompletedtrue,errornull,snapshotCount7.
PNG отражают4Debugкатегории, общиеFPS+GPU/CPU, настройку теста, Admin, PlayersTeamsBots.
Права/preset/Current и игровые кнопки не менялись; стандартные данные BuildPreview.
SceneDetail.Current в EditMode отсутствует, provider controls скрыты — это не runtime proof.
Cleanup: own preview scene/камера/RenderTexture удалены, native leftover0/idle.
Немедленный finish/receive R3c4ea2f50b6a3034f4e3bd468de873be0ab48a4f:
paths=[],unexpected=[],immutable_changed=[], received. Worker освобождён.
Raw MCP/guards/report/hashes/finish/receive: reports/714/.
Perf PNG SHA25629b044b63aa6d8ea714847030e20efbf2069ce8e99f3df55d7c9fd138cbbac0f;
PlayersTeamsBots SHA256397e0c350c14f7d0128f9116b43837af1bcdcb176f7ec3d19eb533d18c2a9c54.
Warning delayCall сохранился46s/probes5/fired0, но capture/cleanup завершились штатно;
это не объявляет infra дефект исправленным. Новых suite/tests/accepted commits нет.

## Постоянное время кадра и принятая зависимость — 2026-10-15

После снимков714 пользователь поручил: «вместо \"показать время кадра\" сразу показывай
там всегда время кадра на планшете. и вливаем». В GPU/CPU экране строка времени кадра
всегда присутствует и обновляется через DebugPerfReadout.Format() каждые0,5с.
Существующий sampler уже работает независимо от Visible; второе состояние или sampler
не добавлены. Числа не участвуют в composition snapshot, кнопки при обновлении не пересоздаются.
Пустой первый sample показывается как «Время кадра: идёт замер…».
Source-review итогового provider+inline варианта: P0–P2=0.

Автоматическая проверка разрешений отклонила расширение writes для новых UI tests до
headset validation. Команда изменения scope не выполнилась, новые tests не записаны.
Проверяем существующие6fixtures/50cases, import/compile и изображения. Пробелы покрытия:
обновление чисел без пересборки в живом кадре; касание вкладок в шлеме;
настоящие AddBot/RemoveBots и переходы CPU/GPU при активном прогоне.
Они не объявляются пройденными по статике или EditMode preview.

Владелец scene-detail получил отдельное указание пользователя на публикацию core.
Согласование7979/7983: GPU, один Build/Snapshot, полная замена legacy; source first, UI second.
Core74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d принят и опубликован владельцем в origin/dev7993;
UI выполнил fetch/rebase с autostash. Provider blobs Profile141aa8dc8, Applier031b42fad,
DebugSectionb645b5928 совпадают с прежней технической композицией18d.
Временный legacy запрос718 отменён до аренды; grants3/4 не используются.
Последняя managed Roslyn проверка4UI+3accepted provider+capture: PASS, exit0/errors0,411refs.
MenuDebug SHA256953a9c06a788d6da8b1945ffb0a4dec263c06bce91ab6c32285513fee44486c6.
Это ещё не свежая Unity-проверка. Финальный technical checkpoint
be8c55d5ea154fb8e71b19a28b597c6bebfe79d5 parent74bd, delta20own/foreign0;
обычный HEAD/index/working tree не заменены, старый experts/ui/backlog WIP исключён.

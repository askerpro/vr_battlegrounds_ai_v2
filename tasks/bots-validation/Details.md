# Проверки ботов и двух внешних клиентов

Для продолжения читать единый Handoff.md: он заменяет чтение этой хронологии.
На момент консолидации2026-10-19 HEAD48d38b9b2 ещё не rebased на новую
origin/deva62039a26 (Player/Ghost migration); request832 уже DONE, brokerbaselinea620,
workeridle. Старые записи о QUEUED/равенстве HEAD и origin относятся к своим датам.

## Независимый выпуск и исследование, 2026-10-19

После прямого поручения публиковать auto-review отклонил коммит пола из-за
неподтверждённой userQA. Пользователь затем явно разрешил «выпуск пола и helper
без QA». Исключение записано в changelog и accept source; QA не выдумывалась.

Floor commit080f137aaade0d31c2ae0d1e7a8f1d8810e6f796:3product paths + journals/changelog.
Exact fresh reviews d15bdc1a8b11489a(bots) и9d401def8a544fea(gameplay), approve,
дословные verdict записаны через reviewer-output. Verify: source blobs равны native818,
existing99/99+catalog7/0, EOL6/6; merge2174db9b9318476a86a7f6b7fb614799 state merged.
Helper commit48d38b9b2885b771f9cc961b4b640eaa7f74ca9e:helper/input+journal/changelog.
Exact launch-infra review4393717961484c8a approve/ACK; неизменённый helper hash,
PS5.1/PS7/principal reaggregation evidence, EOL4/4; merge9af713880acf4cd4858519ed7df39afd.
Оба remote SHA проверены ls-remote. HEAD=origin/dev48d38b9b2, index пустой;
trace product files остаются dirty/untracked. Worker base request832 QUEUED.

Субагент продолжил read-only диагностику: mppm-initialization-analysis.md сравнил
main/clones824/829 и доказал main70–76s reload/29–31s map уже в успешном bot0;
причина workload пока не выделена. User scope уточнён: устойчивый startup любой
роли при длинной init, не успешная перестановка main/clone. В
startup-architecture-options.md сверены server-active/bind/map dependencies,
explicit connect owner и per-peer KCP policy. Полный map-ready требует server graph;
Mirror.listen=false может сохранить active/handlers без bind, но hot-enable не
проверен в runtime. Отложенный advertise один не покрывает explicit до bind;
увеличенный timeout не устраняет SocketException. Новый startup contract пока не принят.
Новых behavior changes/tests/Unity после829 нет. Физический Quest и матрицы открыты.

## Уточнение пользователя, 2026-10-19

Пользователь напомнил: сценарий2клиентов раньше работал; возможно, замедление
появилось после декорирования Lobby и долго оставалось незамеченным.
Дизайн initial-connect admission НЕ подтверждён и не реализуется до решения.
Сначала различить стоимость Editor reload, конкретной сцены и startup workload;
нельзя считать найденный timeout доказательством новой сетевой регрессии.

Проверка уже сохранённых raw829: в localhost Asset Pipeline Refresh79.531s,
Domain Reload Profiling75.607s; Imports0/Asset File Changes0. Внутри reload:
InitializeOnLoadAttributes22.516s, InitializeOnLoadMethodAttributes14.019s,
AwakeInstancesAfterBackupRestoration12.348s. Это не измерение загрузки Lobby.
Managed маршрут Offline→TestMap1, отдельная загрузка Lobby в interval не записана.
TestMap1 отдельно31.103s, ThreadWait26.785s. Косвенное влияние Lobby assets,
загруженных зависимостей или общего Editor состояния пока не исключено.
Нужны сравнимые замеры текущего startup по фазам и сравнение с рабочей ревизией;
влияние тяжёлого декора,3Editor процессов и диагностического логирования не разделено.

## Продолжение 2026-10-18: подтверждённые helper и trace829

Пользователь утвердил оба шага. Build-Game.ps1 исправлен; PS5.1/PS7/principal
фактически выполнили reaggregation без Unity build, reports/helper-fix/
{ps7-final,ps51-final,principal-final}/build.json. Полный inventory3+237,
hashes, attempts/ancestry/UXR проверены; UNKNOWN/dirty сохранены, old failed pair
не изменён. Launch-infra approve requestfc5f8a0419674a40, candidate31b6c5ae,
prompt6d379e1dc552c3f96f408900667775ccdfc63b836d13dcffa2807fefaffd2f7e.
Дословный verdict записан через review-ack --reviewer-output, zone_ackwritten;
scope только quest/helper. UserQA остаётся отдельным gate.

Trace opt-in NetworkStartupDiagnostics добавляет editor-only hooks до KCP Awake,
transport events, UTC/Stopwatch JSONL и редкие gap>=1s. Владелец IDisposable scope
участника; cleanup снимает свои hooks/подписки и восстанавливает debugLog/Log.Info.
SDK/таймауты/readiness/retry не менялись. Новых assertions/tests/expectations нет.
Старые ProtocolHarness/EditorBinding не исполнились из-за отсутствующих типов/stubs;
их не переписывали. Unity compilation: fresh5877d882, AndroidCompileGate Passed/Errors0.

Ticket829/epoch702: только2 случая tools/external-diagnostic-cases.json.
Discovery8 run0b3cf3a8c7f947f8b66b9ab2b3f83d4d: сервер TestMap1/8bots,
клиенты Offline epochs0/1. Localhost8 run4af7525cc39d4dc4b36d483d2780e064:
сервер TestMap1/8bots, клиенты Offline epochs0/0. Readyfalse в обоих случаях.
Оба Cancel завершились Idle/CleanupPassedtrue/Playingfalse/Ownerempty;
по3 полных process logs и3 независимых JSONL сохранены до finish.
Bridge23:38–23:49 local сохранён reports/trace829/bridge-2338-2349.log.

Localhost: RawReceive SocketException после connect примерно за80s доlisten.
Discovery: KCP timeout внутри server longframe с12.330586s между late hooks.
MapPlayable marker появляется внутри кадра; сам по себе barrier недостаточен.
Подробные timestamps и границы выводов — reports/trace829/timeline-analysis.md.
Исправление startup-поведения пока не внесено; диагноз не является runtime PASS.

Finish829 отправлен один раз сразу после cleanup/архивирования и завершён;
receive=true, R5fa499be: только Unity-generated trace.cs.meta, unexpected0.
GUIDcf7e53aea46679045b48fefd80d1e0c5; worker освобождён.
Чужой828 восстановил сопровождающий (reply9778); самостоятельного repair/recovery нет.
UserQA helper/Floor, внешние Player/Spectator/матрицы/Quest ещё открыты.
Далее сохранены исторические результаты824 и исходный план.

## Продолжение 2026-10-18: external824

Новая ведущая сессия `01a15069-c158-7da0-ab1d-8ee6b32c4dd6` приняла Handoff,
опубликовала route epoch2, обработала inbox. Native proxy подтверждён с правильным
`--repo F:/CodexWorktrees/bots-validation`. Fetch подтвердил HEAD=origin/dev=B
`2e1b6a2fc342e894d4b8526287b804fa5e3093b0`; rebase не требовался.
Dirty3 product paths и untracked task docs сохранены техническим checkpoint
`5dcb91e30c19e67ac6bbd4bfb9c1ca8f18c02ba4`, HEAD/index не переключались.

Пакет tools/external-cases.json выполнен через PlayLaunch Plan→Play→Status
в собственной аренде824/epoch698. Все4 плана Passed, Game/Offline→TestMap1;
роли PC/Spectator, seed20261018, MinPlayers8, readiness map-playable,
портовая политика auto и timeout180 сохранены. Метаданные case не передавались
в JsonUtility конфигурации. Новых tests/expectations не создавали.

| Case | RunId | Факты |
|---|---|---|
| discovery-bot0 | e0824aafa03a40dc8b2ee8149d673398 | Ready, оба клиента epoch1, bodyless/team0. Player2 disconnect→Offline, второй остался жив; reconnect→epoch2/session175, сервер снова2 сессии. |
| discovery-bot8 | ba6cac2621db4c3394328d32ec1e3c9c | Сервер TestMap1/8 bot sessions; оба клиента Offline, epochs0/1. Оба лога содержат KCP silence10000ms. Ready не достигнут. |
| localhost-bot0 | 439f5fc3f2ca44dbbebd8dcbadc2c801 | Штатный LaunchNotReady; все процессы остались вне Play/epoch0. Это не проверка сетевого подключения. |
| localhost-bot8 | 100f0249fb8b4b3e8c62606bd17f6640 | Сервер TestMap1/8 bot sessions, оба клиента Offline/epoch0. Вызовы StartClient есть; transport причина разрыва не записана. |

У discovery8/localhost8 серверная загрузка TestMap1 после запуска сети заняла
соответственно29256.418/28958.852ms, включая ThreadWait25340.961/24757.768ms.
Это сильная гипотеза паузы сетевого тика, но TotalOperationTime не доказывает
отсутствие NetworkEarlyUpdate все29s. Нет UTC/монотонных меток сетевых событий,
поэтому точное перекрытие с timeout пока UNKNOWN. Early UDP refusal в localhost
также не доказан. Отсутствие server-ready barrier/retry объясняет сохранение
Offline после первой неудачи, но ещё не установило первичный сбой.

Полные статусы, планы, command terminal, roster и по3 process logs каждого
случая сохранены в reports/external824/. Main-архив ограничен общим Editor.log;
дополнительно сохранены его хвосты. Причинный отчёт с точными ссылками —
reports/external824/diagnosis.md. Startup callbacks/reload временами останавливали
heartbeat; неотправленные proxy Status не повторяли до responsive guard.
Infra_issue9752 и проверенное native-уведомление доставлены сопровождающему;
ответ9756 подтвердил нормализовавшийся delayCall и отсутствие его вмешательства.
Нового launcher, обхода, самостоятельного ремонта и recovery не было.

После каждого случая CleanupPassed=true, Playing=false, Owner пустой.
Guard перед finish подтвердил отсутствие Play/compile/update/tests.
Finish/receive824 завершены, R `e1130da7142c317d9207d820fed71c3bad7f58d1`,
paths0/unexpected0. Worker освобождён; техническое accepted в receipt не является
пользовательской приёмкой продукта. Dirty продукт сверён с checkpoint5dcb91e3.

Диагностика provenance PS5.1/PS7, manifests/inventory и контрольного пути helper
сохранена отдельно: reports/build3d24/provenance-diagnosis.json. Оба native
artifact inventory соответствуют SHA256/размерам (Quest3, Server237).
Helper не исправлен, исходный pair success=false сохранён. Quest-scope плана
расширен на Tools/release/Build-Game.ps1, revision4; новый task не создавали.

Следующий ограниченный дизайн helper: типизированный разбор DateTimeOffset/
DateTime и invariant ISO-строк; проверки происхождения/свежести сохраняются.
Повторная агрегация должна использовать тот же валидатор, исходные attempt
intervals, hashes manifests/artifacts/console/helper и явно помеченное derived
exit evidence; создать новый manifest, сохранив original failed pair и UNKNOWN.
Нового Unity build для этого не требуется. Реализация ещё не начиналась.
Следующий runtime-пакет требует только raw временных меток сети/scene loading
и штатной KCP диагностики до Awake; таймауты/retry/readiness не менять.

2026-10-18. Продолжение проверок после принятой и закрытой итерации bots-fix.
Пользователь подтвердил в Host ходьбу, стрельбу и удержание оружия; это не приёмка
внешних ролей, физического Quest или полного сравнительного performance-сценария.

Принятая база новой задачи: 2e1b6a2fc342e894d4b8526287b804fa5e3093b0.
Оперативная передача новому чату: [Handoff.md](Handoff.md), обновлена 2026-10-18
после terminal Quest/server. Детальные результаты остаются в ignored reports/.
Она содержит последующую принятую иерархию аватаров/кистей; старые5489 evidence
и APK53 сохраняют только историческую область и не переносятся на новый input.

Первый этап — причинная диагностика сохранённых запусков808 и минимальный новый
прогон по штатному PlayLaunch. До доказательства причины исправления не вносятся.
Для нового/изменённого поведения тесты с expectations — после пользовательской
проверки; сейчас допустимы compilation, неизменённые существующие проверки и raw
диагностика. Инфраструктуру при необходимости ремонтирует назначенный сопровождающий.

## Сохранённый отказ 808: установленные факты

Архив: `F:/UnityProjects/Vr_Battlegrounds_ai/.agent-state/editor-broker/artifacts/808/Logs/VRBattlegroundsTestStand/`.
Receipt прежней задачи: `F:/CodexWorktrees/bots/tasks/bots-fix/reports/manual-editor-prepared/finish808.json`.
Проверены размер и SHA всех восьми полученных артефактов. Исходный Editor.log
сохранён сжатым; никаких повторных Unity-операций ради чтения логов не выполнялось.

| Запуск | Факт | Ограничение вывода |
|---|---|---|
| `3af969a568fb40febc48d94dab05a00f` | Dedicated + 2 PC Spectator, TestMap1, 8 ботов, discovery. Оба нашли `192.168.1.245:61112`. Player2 оборвался до authentication; Player3 загрузил TestMap1 и отправил E2E hello, затем оборвался после authentication. KCP silence timeout — 10000 ms. Сервер продолжил раунды. | Discovery не является общей причиной. Нет временных меток каждого события: блокировку главного потока на 10 s эти логи не доказывают. |
| `fb051014273e437aab0c4bfa5c0ff720` | Те же роли с explicit `127.0.0.1`. В первом статусе оба клиента уже `Client=false`, connection epoch 0; сервер продолжил раунды. | В клиентских архивах нет KCP timeout/error/exception. Это отдельный ранний отказ, а не доказанный повтор первого timeout. |
| Исторический 797 | Dedicated + 2 VR Spectator, 8 ботов: подключение, 10 сессий, disconnect/reconnect сохранил работоспособность. | Иные DeviceType и Readiness; это контроль существовавшей работоспособности, а не доказанная причина различия с 808. |

Точные места первого запуска: `Player2-273464.log:1052,1106–1120`,
`Player3-153228.log:1133,1139,1203–1217`; сервер `main-41776.log:5559,22093,22241–22256,40524`.
Во втором запуске клиентские стеки `OnStartClient → StartClient → ConnectToAddress → ApplyRole`
на строках 960–974. Общая низкая частота кадров не доказывает конкретную длинную паузу;
69.965 s AssetPipelineRefresh произошёл до старта сервера.

### Цепочки исходников

- `PlayModeTestStand.StartConfigured` запускает manifest участников одновременно;
  server-ready barrier до клиентов нет. Readiness наблюдает состояние и таймаут,
  не задаёт последовательность старта и не повторяет соединение.
- `GameNetworkDiscovery` после discovery делает один `StartClient`; explicit address
  вызывает его сразу. Простая строка `127.0.0.1` использует настроенный KCP port,
  гипотеза ошибочного URI-порта не подтверждается.
- `KcpClient.RawReceive` при SocketException пишет только Info и отключается;
  при `KcpTransport.debugLog=false` Info выключен. UDP refusal при слишком раннем
  localhost connect согласуется с молчаливым ранним отказом, но для 808 не доказан.
- После контролируемого отключения `GameNetworkManager` вызывает
  `NotifyClientStopped(true)`, а `GameNetworkDiscovery` удерживает Offline.
  Штатный `RequestClientConnect` умеет снять hold, но stand reconnect требует
  connection epoch >= 1. Никогда не подключавшийся epoch 0 не восстановить этим
  фасадом. Это установленное ограничение восстановления, не доказательство причины разрыва.
- KCP receive/send исполняется в Unity main loop, timeout использует Stopwatch.
  Синхронное создание ботов/сцены/прогрев может задерживать обработку; доказательств
  фактического runtime precache на dedicated/Spectator или конкретного stall пока нет.

## Следующий причинный прогон

Одинаковый fresh input: Dedicated + 2 PC Spectator, TestMap1, seed 20261018,
AutoGoLive, MinPlayers 8, Readiness map-playable, одинаковые порты и quality.
Сначала discovery с **0**, затем с **8** ботами. Отдельно повторить explicit localhost
для отличия discovery от раннего connect. Не добавлять одновременных изменений
таймаутов, retry, DeviceType или readiness; не форсировать Ready/команды/HMD.

Для каждого процесса сохранить полный log и сырые статусы: Playing/Active,
Server/Client/Connected, connection epoch, scene/map readiness, roster,
GameRole/DeviceType/Team/avatar, первое listen/connect/hello/disconnect,
фактические временные метки, frame timing и сообщения ошибок. Отдельно проверить
отключение/повторное подключение уже достигшего epoch1 клиента. Роли Spectator
должны быть bodyless/team0; Player/team0 не подменяет S8.

Если штатных данных недостаточно, минимальная runtime-диагностика добавляется
после чтения AGENT.md затронутых зон; это raw trace, без новых тестовых ожиданий.
Запуск server-first с поздним добавлением клона нынешним фасадом не поддержан:
manifest заморожен. Не выдавать такой сценарий за готовую команду.

После каждого подготовленного пакета: штатный Stop, известное завершение операций,
чистые сцены/Prefab Stage, finish/receive и проверка артефактов. Не удерживать worker
для анализа, переписывания кода или ожидания человека.

## Актуальные тела и оружие

`044888e1b` — предок текущего HEAD; diff `3a3a73402..2e1b6a2fc -- Assets/Scripts`
пуст. Отсутствие исторических имён BotAvatarSource/IsBotControlled не означает
утрату принятого core. Действующая цепочка: BotDirector → BotSkin.PickHittable →
SessionTeamAssigner → TeamAvatarStrategy → TeamRegistry/TeamData/AvatarData.

| Команда | AvatarData GUID | Активный prefab GUID |
|---|---|---|
| CounterTerrorists, teamIndex1, AvatarIndex0 | `4d1f2ac2b7326a144a7aff3486e766fe` | `60604d8891226f64592f880c9b2da883` — MEF_cyborg_hands_Player_Black |
| Terrorists, teamIndex2, AvatarIndex0 | `294e4428ec16fb84d9dd876e1cbb523a` | `2aa0ab6714e2413f9d829637b62df0c0` — MEF_cyborg_hands_Player |

Оба наследуют CyborgHandsBase GUID `71b74b0d08444f49bbb7aa2828d0890a`,
PlayerBase GUID `eee4c6960159a24459810d9c0851279c`. Cyborg fallback не является
текущим team bot roster. Старые Optimized_MEF_Player перемещены в Unused.

| Стендовый selector | WeaponInfo GUID | Prefab GUID | CyborgHands primary/support poses |
|---|---|---|---|
| PPK_Weapon, Rifle=false | `e656ffaededf94447b00dfe8561f5caf` | `53b2af502e94d13489397cef90c86925` | `ccf318eb50b9edc409d829df964fafe7` / `cf6043152865be24eb9592b01d01c275` |
| AK105_Weapon, Rifle=true | `7d9a79e589f45c74e9585bdbed93ce76` | `732e5b48d3557594583580ed383cc981` | `a5d40ee130b373d4eb6e46a55ecf2ea3` / `a8f0c12373c44a14581020cf060e1d22` |

Root input включает сцену/registry/map стенда, team registry/data/avatar strategy,
оба активных тела с базами и FBX, WeaponInfo/weapon/magazine/poses, proxy controller
и действующие scripts/asmdefs плюс dependency closure. Таблица GUID — не полный
dependency manifest. FBX материализован, весь closure ещё не проверен.

## Доступ к Unity

Аренда817 штатно завершена/получена. Собственной действующей аренды нет;
перед новым прогоном перечитать состояние брокера. MCP этого Desktop-чата
по-прежнему имеет `--repo F:/CodexWorktrees/bots`. После uvrestart9698 чтение
debug_request_context/read_console успешно, но mutations новой задачи из старого
чата не выполнять. По ответу сопровождающего9664 поддерживаемого rebind нет:
продолжить существующую задачу в чате проекта `bots-validation`, без task-new.
Маршрут сопровождающего восстановлен; уведомление9708 доставлено, ответ9718
подтвердил самостоятельное завершение UPM Git fetch. Самостоятельных правок
proxy/config, recovery или чужого finish нет.

Существующий `Tools/TestStand/worker_probe.py --compile-only` использует штатный
stdio proxy своего root и проверен для AndroidCompileGate в своей
аренде817. Полный probe запускает MPPM напрямую и содержит иные проверки; целиком
не запускать вместо требуемого PlayLaunch. Нового альтернативного клиента не писать.

## Фактический результат817

Технический input `9a112a8869844dd8276ccb08f333938f413e26e0`, base2e1b6a2fc.
Guard подтвердил правильный worker41776, исходный Editor session
`76bce245b47648e5ad5f75c8afbd65c5`, не Play/compile/update/tests.
Исполнитель подтвердил правильный root. Затем native MCP plugin session
`9c338ce0-fe7a-432b-8844-f883d2daf1b9` отключился при ожидании command_result.
Raw JSON содержит passed=false/transport error; его не исправляли задним числом.

Отдельное фактическое доказательство: сохранённый `reports/android-compile/editor-log-tail817.txt`
строка2163 — `ExitCode: 0 Duration: 4s530ms`; строка2170 —
`[AndroidCompileGate] PASS`. После этого guard подтвердил Editor idle,
без Play/compile/update/tests, modal/progress и лишних preview scenes.
Повторного вызова gate не было. Это Android script compilation, не APK/Quest/runtime PASS.

Сразу выполнен штатный finish817, затем receive:
R `2e28fbf7a71217272c8a4973da2b4671e007563f`, paths0/unexpected0.
Worker освобождён. Полные receipts: `reports/finish817.json`, `reports/receive817.json`;
исходный JSON и transport log сохранены рядом с Editor evidence. Последний guard
также показывал delayCall stalled509 s; этот симптом передан сопровождающему,
делегаты/фокус/процессы/конфигурация не менялись.

SHA256 evidence:
- result.json: `e98a334e65e03a9aa5f2623b62cf83c21e50420604aeafd2311c717217c4d4f0`;
- editor-log-tail817.txt: `a591c2a19edd1dc38a190eb922135ba3e29d2ea6228c63ee8f1aa5c05a3c604b`;
- transport817: `e197b38321ce16e16924cacc72e9d66d699b24e3825c05745c3f598f3f35fffb`.

## Передача исправления пола, события9609/9615

2026-10-18 пользователь поручил передать исправление Floor стенда агенту ботов.
Hand-rig-quality сообщил об этом в прежний inbox bots-fix, event9609; собственный
новый inbox bots-validation в момент проверки был пуст. Передача принята ответом9615
из bots-validation; адрес будущих сообщений уточнён. После отдельного явного
разрешения пользователя уведомление9615 доставлено native в hand-rig-quality.

Источник: `F:/CodexWorktrees/bot-stand-teleport-floor`, checkpoint
`f762ead8e0f7fe43f003c48e8eedca8087266b27`. Прочитан diff: только назначение
Ground Floor в Build и NormalizeFixture, плюс материалы исходной задачи.
Требуются штатное сохранение сцены и RebakeCatalog, без смены teleport masks/tests.

Свежий broker status на момент передачи показал **818 RUNNING**, owner
bot-stand-teleport-floor. Чужую аренду не отменять, не recover/finish и не выполнять
дублирующее SaveScene/Rebake. Дождаться terminal/receive с известным исходом,
сверить результат, затем перенести минимальный diff в свою ветку и продолжить
проверки по актуальному source. Это исправление предпосылки уже запланированных
AvatarLoadout checks; новая самостоятельная продуктовая цель не добавлена.

Исходный baseline: Floor Default0, teleport mask Ground3; прежние проверки99:
96 passed, три Floor failures. Это переданные исторические данные, не наш fresh PASS.
Accepted commit исправления требует userQA телепорта на свободный пол и визуального
качества; существующие неизменённые проверки можно выполнить для диагностики.

Ответ9620 от hand-rig-quality: исходный исполнитель не обработал STOP9607/9612
до выполнения пакета. Native Floor Default0→Ground3 и RebakeCatalog завершены,
7 maps/0 errors; job `bcf8fdcb974540f588f399bb8f54c825` succeeded99/99
(AvatarLoadout/AvatarHandPoseChain/PrefabComposition), тесты неизменны.
Это сообщение источника; raw артефакты и result SHA ещё ожидаются после finish/receive818.
Повторных действий по полу не запускать.

Передача9623 завершена: finish/receive818 exit0; native R
`83a0789fbe184d23c0cace8ee56d1d8a7324fde3`. После проверки diff в свою ветку
перенесены ровно три product paths (builder, scene, catalog), 5 insert/4 delete.
Scene меняет только Floor.layer0→3; builder задаёт Ground при Build/NormalizeFixture.
Tag/геометрия/коллайдер Floor совпадают в native before/after. Tests не менялись.
Все три физические файла LF; diff против R по этим путям пуст.

Проверены исходные native артефакты, сохранены byte-for-byte в reports/floor818:
test job succeeded,99 total/99passed/0failed/0skipped,6.7003316s;
AvatarHandPoseChain19, AvatarLoadout63, PrefabComposition17. Native catalog
passed7maps/failureCount0. Console query success и0 записей — только снимок запроса.
SHA256: tests-full `9a52b69a752c672eceb3766a04a0cdaa2a6fc7081d8509a1b5a0f67dcfdf4c4d`,
native-floor `7ef7b8575025ab3d1ac094dae82abd5919e4393685d51b998b94be46f09fdaa8`,
test-starts `dbb936e39e28614cc29c7c297d6b4169079588a8c43c983bc45344fcdab014b6`,
console `8d9b8b436830829e99b28d0feae1569ca8835a99e2f33bf7c6fac55abc4af48e`.

Дополнительный Lobby fingerprint в нативном каталоге сохранён: независимая
read-only сверка алгоритма MapRunPreflight с исходными байтами base2e доказала
`a7553c0bced6c0480c188709fae131f47c4a34e5c5972a8f83f3f3e8bfa4c0c9`.
Catalog `2380eef1...` отставал после изменения самой Lobby.unity в принятом
`74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d`; MapData/modes/meta неизменны.
Это не dependency hash аватаров: текущий алгоритм использует scene/map/modes
source+meta с нормализацией CRLF для YAML. Сцену Lobby задача не меняет.
Новый userQA Floor ещё не проведён; accepted commit/merge не выполнялись.

## Сборки для Quest: подготовленный следующий шаг

Штатная пара Test: `F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai` и
`F:/CodexWorktrees/build-test-android/Vr_Battlegrounds_ai`. Сборки отдельно от
интерактивного worker, только существующими Build-Game/Run-QuestE2E.
Свежий immutable SHA после переноса Floor должен быть одинаковым для обеих платформ.
Команда из своего root:

```powershell
pwsh -NoProfile -File Tools/release/Build-Game.ps1 -Targets 'quest,server' -Config Test -Win64Worktree 'F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai' -AndroidWorktree 'F:/CodexWorktrees/build-test-android/Vr_Battlegrounds_ai' -Ref '<полный immutable SHA>'
```

Read-only preflight18.10.2026 16:12:33UTC: нет matching Unity/director процессов,
Temp/UnityLockfile не обнаружены; оба role=build, HEAD
`53e3a85fd6c2f88d7593ffe2a9aa94bd5e9eb1a2`. Оба checkout dirty:
`Assets/Data/Maps/MapRuntimeCatalog.asset`, `Packages/packages-lock.json`;
Win64 дополнительно `Assets/URPDefaultResources/PC.asset`. Поэтому -Ref сейчас
не запускать: свобода процесса не означает пригодность к переключению.
Файлы не очищались и не перезаписывались, происхождение dirty не установлено.
Full LFS closure не проверен. Перед реальным запуском повторить полный preflight.

Release evidence: обе полные build directories с APK/server/uxr-ids/build.json,
pair manifest `Build/manifests/build-<id>/build.json`, полные Logs/build-*.log,
SHA/hash/command/UTC/exit/verdict. Android script compile817 не заменяет это.
Run-QuestE2E использует отдельный Windows e2e output; у него нет -Ref/-Config,
пара сначала выравнивается Build-Game. Для device run нужны конкретный свободный
Quest serial/device, LAN и готовые приложения; никаких самовольных StopLink.
Обязательны run.json/summary/log-verdict, server/client/logcat и build statuses;
без run.json фактического device-прогона нет.

Актуальные источники: Docs/release.md37–88, actual split helpers.
Docs/testing.md508–525 описывает старый single-worktree/worker маршрут и не
используется вместо текущей схемы. Helpers Get-Help прочитаны без запуска сборок.

## Сохранённый отказ 808: установленные факты

Архив: `F:/UnityProjects/Vr_Battlegrounds_ai/.agent-state/editor-broker/artifacts/808/Logs/VRBattlegroundsTestStand/`.
Receipt прежней задачи: `F:/CodexWorktrees/bots/tasks/bots-fix/reports/manual-editor-prepared/finish808.json`.
Проверены размер и SHA всех восьми полученных артефактов. Исходный Editor.log
сохранён сжатым; никаких повторных Unity-операций ради чтения логов не выполнялось.

| Запуск | Факт | Ограничение вывода |
|---|---|---|
| `3af969a568fb40febc48d94dab05a00f` | Dedicated + 2 PC Spectator, TestMap1, 8 ботов, discovery. Оба нашли `192.168.1.245:61112`. Player2 оборвался до authentication; Player3 загрузил TestMap1 и отправил E2E hello, затем оборвался после authentication. KCP silence timeout — 10000 ms. Сервер продолжил раунды. | Discovery не является общей причиной. Нет временных меток каждого события: блокировку главного потока на 10 s эти логи не доказывают. |
| `fb051014273e437aab0c4bfa5c0ff720` | Те же роли с explicit `127.0.0.1`. В первом статусе оба клиента уже `Client=false`, connection epoch 0; сервер продолжил раунды. | В клиентских архивах нет KCP timeout/error/exception. Это отдельный ранний отказ, а не доказанный повтор первого timeout. |
| Исторический 797 | Dedicated + 2 VR Spectator, 8 ботов: подключение, 10 сессий, disconnect/reconnect сохранил работоспособность. | Иные DeviceType и Readiness; это контроль существовавшей работоспособности, а не доказанная причина различия с 808. |

Точные места первого запуска: `Player2-273464.log:1052,1106–1120`,
`Player3-153228.log:1133,1139,1203–1217`; сервер `main-41776.log:5559,22093,22241–22256,40524`.
Во втором запуске клиентские стеки `OnStartClient → StartClient → ConnectToAddress → ApplyRole`
на строках 960–974. Общая низкая частота кадров не доказывает конкретную длинную паузу;
69.965 s AssetPipelineRefresh произошёл до старта сервера.

### Цепочки исходников

- `PlayModeTestStand.StartConfigured` запускает manifest участников одновременно;
  server-ready barrier до клиентов нет. Readiness наблюдает состояние и таймаут,
  не задаёт последовательность старта и не повторяет соединение.
- `GameNetworkDiscovery` после discovery делает один `StartClient`; explicit address
  вызывает его сразу. Простая строка `127.0.0.1` использует настроенный KCP port,
  гипотеза ошибочного URI-порта не подтверждается.
- `KcpClient.RawReceive` при SocketException пишет только Info и отключается;
  при `KcpTransport.debugLog=false` Info выключен. UDP refusal при слишком раннем
  localhost connect согласуется с молчаливым ранним отказом, но для 808 не доказан.
- После контролируемого отключения `GameNetworkManager` вызывает
  `NotifyClientStopped(true)`, а `GameNetworkDiscovery` удерживает Offline.
  Штатный `RequestClientConnect` умеет снять hold, но stand reconnect требует
  connection epoch >= 1. Никогда не подключавшийся epoch 0 не восстановить этим
  фасадом. Это установленное ограничение восстановления, не доказательство причины разрыва.
- KCP receive/send исполняется в Unity main loop, timeout использует Stopwatch.
  Синхронное создание ботов/сцены/прогрев может задерживать обработку; доказательств
  фактического runtime precache на dedicated/Spectator или конкретного stall пока нет.

## Следующий причинный прогон

Одинаковый fresh input: Dedicated + 2 PC Spectator, TestMap1, seed 20261018,
AutoGoLive, MinPlayers 8, Readiness map-playable, одинаковые порты и quality.
Сначала discovery с **0**, затем с **8** ботами. Отдельно повторить explicit localhost
для отличия discovery от раннего connect. Не добавлять одновременных изменений
таймаутов, retry, DeviceType или readiness; не форсировать Ready/команды/HMD.

Для каждого процесса сохранить полный log и сырые статусы: Playing/Active,
Server/Client/Connected, connection epoch, scene/map readiness, roster,
GameRole/DeviceType/Team/avatar, первое listen/connect/hello/disconnect,
фактические временные метки, frame timing и сообщения ошибок. Отдельно проверить
отключение/повторное подключение уже достигшего epoch1 клиента. Роли Spectator
должны быть bodyless/team0; Player/team0 не подменяет S8.

Если штатных данных недостаточно, минимальная runtime-диагностика добавляется
после чтения AGENT.md затронутых зон; это raw trace, без новых тестовых ожиданий.
Запуск server-first с поздним добавлением клона нынешним фасадом не поддержан:
manifest заморожен. Не выдавать такой сценарий за готовую команду.

После каждого подготовленного пакета: штатный Stop, известное завершение операций,
чистые сцены/Prefab Stage, finish/receive и проверка артефактов. Не удерживать worker
для анализа, переписывания кода или ожидания человека.

## Актуальные тела и оружие

`044888e1b` — предок текущего HEAD; diff `3a3a73402..2e1b6a2fc -- Assets/Scripts`
пуст. Отсутствие исторических имён BotAvatarSource/IsBotControlled не означает
утрату принятого core. Действующая цепочка: BotDirector → BotSkin.PickHittable →
SessionTeamAssigner → TeamAvatarStrategy → TeamRegistry/TeamData/AvatarData.

| Команда | AvatarData GUID | Активный prefab GUID |
|---|---|---|
| CounterTerrorists, teamIndex1, AvatarIndex0 | `4d1f2ac2b7326a144a7aff3486e766fe` | `60604d8891226f64592f880c9b2da883` — MEF_cyborg_hands_Player_Black |
| Terrorists, teamIndex2, AvatarIndex0 | `294e4428ec16fb84d9dd876e1cbb523a` | `2aa0ab6714e2413f9d829637b62df0c0` — MEF_cyborg_hands_Player |

Оба наследуют CyborgHandsBase GUID `71b74b0d08444f49bbb7aa2828d0890a`,
PlayerBase GUID `eee4c6960159a24459810d9c0851279c`. Cyborg fallback не является
текущим team bot roster. Старые Optimized_MEF_Player перемещены в Unused.

| Стендовый selector | WeaponInfo GUID | Prefab GUID | CyborgHands primary/support poses |
|---|---|---|---|
| PPK_Weapon, Rifle=false | `e656ffaededf94447b00dfe8561f5caf` | `53b2af502e94d13489397cef90c86925` | `ccf318eb50b9edc409d829df964fafe7` / `cf6043152865be24eb9592b01d01c275` |
| AK105_Weapon, Rifle=true | `7d9a79e589f45c74e9585bdbed93ce76` | `732e5b48d3557594583580ed383cc981` | `a5d40ee130b373d4eb6e46a55ecf2ea3` / `a8f0c12373c44a14581020cf060e1d22` |

Root input включает сцену/registry/map стенда, team registry/data/avatar strategy,
оба активных тела с базами и FBX, WeaponInfo/weapon/magazine/poses, proxy controller
и действующие scripts/asmdefs плюс dependency closure. Таблица GUID — не полный
dependency manifest. FBX материализован, весь closure ещё не проверен.

## Доступ к Unity

Новая аренда не запрошена. Текущие MCP proxy фактически запущены с прежним
`--repo F:/CodexWorktrees/bots`, поэтому не используются для mutations новой задачи.
Сопровождающему отправлен canonical infra_issue **9578**: нужна штатная перепривязка
или выпущенный backend. Native route адресата cleared, native-уведомление недоступно.
Попытка штатного attach_worktree вернула unmanaged checkout, ничего не меняя.
Самостоятельных правок proxy/config, HTTP/stdio обходов, recovery или чужого finish нет.

Существующий `Tools/TestStand/worker_probe.py --compile-only` использует штатный
stdio proxy своего root и применён для AndroidCompileGate в своей
аренде817. Полный probe запускает MPPM напрямую и содержит иные проверки; целиком
не запускать вместо требуемого PlayLaunch. Нового альтернативного клиента не писать.

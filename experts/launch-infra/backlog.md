## Текущий итог — 2026-10-15

GPU diagnostics по8600: r2 agreed после ACK8729/8735 и provider ACK, planrev32.
Runtime, transport и build provenance реализованы в собственном scope; независимое review
и исправления продолжаются. Launcher review выявил пропущенные early role/scene guards
и full inventory check; build review проверяет source snapshot и terminal inventory.
Unity/GPU/runtime проверки новой реализации ещё не выполнены. Hub stage planned из-за
legacy ordering; local source работа разрешена mode off, Unity требует собственной lease.
Performance observer8797 подготовлен отдельно; bridge ждёт immutable provider и grant/proof,
чужой WIP не копируется. Правила r2 и границы источников метрик сохраняются.
Base753 DONE/published ed8b79f17 подтверждён8537/nativewatch; прежнее QUEUED историческое.

quest-perf-pipeline: матрица4checkout завершена,8/8builderPASS,3последнихpipelineexit0;
исходный Testcoldexit1 сохранён как дефект старогоreader. Testrepeat14мин11с,
Prodrepeat17мин45с. Все4root clean/free/no-lock, Library/Build сохранены; Testcache hashes
и creationtimes до/послеProd совпали. Fullresults и694проверенных архивных файлов в taskreports.
Heartbeat checkout штатно остановлен после матрицы. Readerfix18/18/reviewclean принят15.10,
FF влит e83af8413, remoteSHA проверен; финальная fixture18/18, EOLrange issues0.
DevTools129/129 принят и FF влит6669db54f, публикация737DONE; hub lifecycle merged,
plan30: DevTools merged → build-checkouts; приёмка reader и публикация e83af8413 выполнены.
Рецепты самостоятельных Test и Windows-rendered E2E
переданы scene-detail8313/8325; новых GPUзапусков/профилей нет. Общий AGENTScandidate8039
и оценка build-брокера у сопровождающего; нового build-механизма нет.

Ниже — историческая передача, её старые running/RED/route значения не являются текущим статусом.

## Обещания другим агентам (передача Codex 2026-10-14)

Возобновление 2026-10-14: хаб подтверждает `vr-test-stand` rev36 (всё merged),
`quest-perf-pipeline` rev26 (`e2e-smoke-kit` принят и merged be120d280; `build-checkouts` running: проверка и инструкции),
`map-runtime-bootstrap` rev23 (`series-smoke-e2e` planned, остальные этапы merged).
Текущий WIP `6a95b322e`; сборочная инфраструктура `93d7dfbc3` уже в `origin/dev = d3c62ca5e`.
Объявление 6695 сняло остановку FIFO; заявка e2e-smoke-kit **637 DONE**, input `87643327108f`.
AndroidCompileGate PASS, Windows build PASS; selftest 19/19 GREEN, map-startup 14/14 GREEN.
Арсенал 24/24 функционально, enforce RED: четыре teleport-state-unknown-target, network уведомлён 6850.
Пользователь принял проверенный Windows-каркас и launcher9/9 2026-10-14;
физический Quest, реальные четыре сборки/кеши и сетевой дефект остаются открытыми.
632 CANCELLED после истечения OFFERED (claim отказал), Unity-действий не было.
629 отменена до claim для включения двух диагностических hello-логов; поведение протокола не менялось.
Отдельный MCP-клиент через штатный proxy прошёл initialize/read (48 инструментов).
Старый route map-runtime-bootstrap в другом checkout оставляет owner inbox частично view-only;
это не ACK и не допуск, собственные текущие routes очищены.

- network: итог передан 6829 — один hello на соединение, повтор ready после смены карты,
  0 «Unknown message id», потерь/дублей журнала нет (6425, 6437/6441/6445).
- gameplay: сообщить о вливании `e2e-smoke-kit` → они заводят задачу series-smoke и шлют id → снять
  `map-runtime-bootstrap/series-smoke-e2e` (6403/6408). Прогон `map-run-startup-route` с полным вердиктом по логам на базе
  с исправлением арсенала ec0887cf5: 14/14 GREEN, полный вердикт чистый; дефект
  `anchored-ignore-collision-prefab` удалён из `Tools/e2e/known-noise.json`; результат передан 6892 (6367).
- network: после исправления `teleport-state-unknown-target` — перезапуск `dedicated-server-arsenal`, удалить дефект (6054/6062).
  Ответ 6866: candidate 01e8b7748d, worker643 ждёт собственных gates; accepted SHA ещё нет.
- manipulation: прогон карты с их защитой AnchoredItemCollisionIgnore (6079) чистый;
  ошибочная ветка guard не сработала, её поведение этим прогоном не доказано; уведомление 6896.
- weapon-system: сценарий shot-cost — дать zone-ack на строку в `E2ERunner.cs`, когда пришлют zone-request; их этап после
  `e2e-log-verdict` (6198/6206); сцена — вариант (A) Lobby или доработка пары для BotCombatStand (6221).
- bots/avatar-ik: контракт `xr-input` r1 (contract-propose) до кода — 5337/5341, условия avatar-ik 5295.
  Напоминание legs-ik 6758: отдельного r1 пока нет; ревью по control-modes-plan.md81+,
  ACK до кода, один writer камеры, штатный UxrControllerTracking запястий, без костей/BodyIK/калибровки.
  e2e-head-pose/quest-device-run r5 не заменяет xr-input. Кандидат не выбран, код ввода не начат.
- performance/agent-infra: четыре `build-checkouts` развёрнуты на 93d7dfbc3, чистые и LFS hydrated;
  lock preflight и ошибки LFS исправлены, fixture 9/9. Настоящие сборки/прогрев и замеры performance
  ещё не выполнены (6455/6457); этап running и упорядочен после e2e-smoke-kit.
  2026-10-15 пользователь уточнил: проверять уже существующие checkout/дирижёры и обновить
  инструкции всех агентов. Свежий audit4/4 clean,8828LFS,missing0/pointer0; три документированные
  dry-run Test/Prod/QuestE2E exit0. Общая инструкция и допуск общей пары — maintainer7288;
  собственные Docs/release/AGENT обновляются. До назначения пары только dry-run безRef/Unity.
  Решение пользователя15.10 заменило ожидание pair reservation7514: dedicated batch без
  брокера/очереди, fresh process/log/lock preflight; при занятости повтор по таймеру20–30мин.
  Сопровождающий8007+Codex получил запрос общего AGENTS и оценки дешёвого build-брокера;
  новый механизм сейчас не внедряем. Первая Test реально запущена на accepted74bd42f57,
  сначала Android затемWin64; результат ещё не получен. DevTools-правки явно согласованы.
- performance: перенести тесты `Release/Tests/**` в `Assets/Tests/EditMode` при удобном случае (5496).
- agent-infra: обращение 6653 — прежний Claude route другой задачи и view-only owner inbox;
  встроенная MCP-сессия Codex завершена, отдельный штатный proxy-клиент работоспособен.

Обновлено 2026-10-14. Кандидаты вне хаба; порядок — приоритет. VTS = `tasks/vr-test-stand/Details.md`,
MRB = `tasks/map-runtime-bootstrap/Details.md`,
PH = `tasks/vr-test-stand/pilot-handoff/snapshot.json`.

Порядок пересмотрен 2026-10-11 — `journal/2026-10-11-e2e-path-priority.md`.
`player-launch` взят в хаб: `vr-test-stand/e2e-run-isolation`.

0. `quest-perf-pipeline` — взят в работу 2026-10-11 по поручению пользователя: автоматический конвейер на шлеме —
   сборка Android → adb install → запуск перф-теста → закрытие → сбор логов и перф-результатов. Конвейер, сборка и
   запуск — launch-infra; содержание замеров, метрики и бюджеты — performance (контракт формата результатов).
   Блокер сборки 2026-10-12: Android-сборка из редактора показывает модальное «Unsupported Input Handling on
   Android» (Active Input Handling = Both, ключ opt-out `AndroidInputHandlingWarning`) — worker простоял ~1 ч.
   Симптом закрывает этап `quest-build` (opt-out перед сборкой + предупреждение в отчёте); сопровождающему —
   infra_issue 4176 (детект модальных окон в брокере).
0a. `input-handling-new-only` — корень: перейти на Input System only. В рантайме сторонних пакетов есть legacy
   `Input`: UXR `UxrGamepadInput`/`UxrKeyboardInput`, FImpossible `LegsAnimator`, Mirror `GUIConsole`/
   `RemoteStatistics`/`ToggleHotkey`, KINEMATION `KShooterMenu`/`TacticalShooterPlayer`. Нужен аудит путей
   вызова на Quest с avatar-ik (ввод, ноги) и manipulation; `ProjectSettings` — моя зона, решение — совместное.
0b. `stand-multiprocess-sceneid` — находка bots 5080 (2026-10-12, bots-fix net-round1 attempt-2, тикет 517): BotCombatStand в
   мультипроцессе PlayLaunch (S2C, H1C) — на каждом клиенте 21 «Spawn scene object not found for <sceneId>» / «Could not spawn
   assetId=0». Гипотеза bots: сервер открывает Assets/Scenes/Stands/Bots/BotCombatStand.unity напрямую, клиенты — через Offline→карта,
   sceneId расходятся. Связано с `stands-migration`. Плюс: логи клонов MPPM (Library/VP/*/Logs/Editor.log) перезаписываются
   каждым Play — PlayLaunch должен архивировать лог клона на RunId/фазу (брокер --output Library/VP не принимает).
   Отчёты: worktree bots tasks/bots-fix/reports/net-round1/runtime/attempt-2/.
0c. `stand-deferred-client` — просьба bots 5068 (не блокер): «новый игрок входит в идущий бой». Выбран вариант (б):
   `PlayLaunchConfiguration.DeferredClients` — участник стартует отключённым (не входит в Ready) + адресное действие
   `connect` (StandRequest). Вариант (а) connect-fresh со сменой токена посреди прогона отклонён: второй путь к
   идентичности/сессии участника. (в) H1C с хостом в клоне — пока не нужен (Role=host, ClientCount=1 достаточно).
   Порядок: после `stand-multiprocess-sceneid`.
0d. `stand-kcp-timeout` — находка bots 5185 (низкий/средний): S2C p2 (bots-fix net-round1 attempt-4, тикет 525) — клиенты нашли
   сервер через Discovery (kcp://192.168.1.245:<port>), но Client/Server Transport Error Timeout 10000 ms, PlayLaunch Failed
   LaunchNotReady; повтор часто проходит. Гипотезы: блок главного потока при входе клона (спавн «призрачных» объектов из
   preview-сцен worker — §16), потеря первого рукопожатия KCP. Проверить после рестарта worker-редактора; если останется —
   разбор логов bots attempt-4/logs/S2C-p2-*.log. Порядок: после stand-preview-preflight.
0e. `stand-back-to-back` — аренда 539: S2C сразу за предыдущим прогоном не уложился в TimeoutSeconds=90 — клоны MPPM
   стартовали через ~70 с (ждали выхода клонов прошлого прогона), LaunchNotReady. Для серий ботов: до Start ждать выхода
   клонов прошлого RunId или отсчитывать таймаут от появления клонов. Порядок: после вливания stand-preview-preflight.
0f. `e2e-smoke-kit` — решение пользователя 2026-10-13: смок-тест серии матчей (сценарий и критерии) пишет gameplay
   сам, launch-infra готовит каркас; предложение gameplay — 5863. Каркас: вердикт по логам (этап e2e-log-verdict),
   `-Arg` → `-e2eArg` в Run-E2E.ps1 (как в Run-QuestE2E), руководство «мультипроцессный сценарий» в Docs/testing.md,
   плюс что попросит gameplay. Ответ gameplay 5869 (согласен; сценарий, критерии и шум сценария — их этап). Нужно от
   каркаса по важности: (1) барьер между процессами — именованные шаги, сервер ждёт N клиентов и наоборот, таймаут шага
   — именованный провал с ролью и шагом; (2) сценарий и сбор логов переживают смену карты (MapLoader/ServerChangeScene),
   вердикт после финала серии; (3) роль/идентичность процесса (server/client N/late) и сводный результат по процессам;
   (4) длинный таймаут и параметры режима через -Arg; (5) журнал шагов сценария с временем и ролью в итоге;
   (6) поздний клиент учитывается барьерами отдельно. Не нужно: админ-команды, отдельный детектор sync-ошибок.
   Сначала проверить, что из этого уже есть (E2EContext.Role, -Timeout, -LateClientDelay, MapRunRelayBarrierScenario).
   Снять `map-runtime-bootstrap/series-smoke-e2e` с моего плана, когда gameplay сообщит id своего этапа.
0h. Хвосты `e2e-smoke-kit` (вне writes этапа): `tasks/vr-test-stand/tools/managed_e2e_worker_probe.py:247` ждёт роль
   `client`, теперь `client-N`; `-e2eArg` и поздний клиент в PlayLaunch (поля StandRequest, `StandProtocol.cs`);
   `Run-QuestE2E.ps1` не передаёт клиенту `-e2eClients` (безвредно, сверка по N сервера).
0g. Перезапуски по вердикту логов (обещано владельцам): `map-run-startup-route` после вливания gameplay
   `arsenal-unload-teardown/top-level-detach` (критерии 6071: 0 предметов вне сцены, нет «not cleaned up», 0 «Tried to
   ignore collisions»; защита manipulation 6079 — 0 её Error); `dedicated-server-arsenal` после исправления network
   `teleport-state-unknown-target`. Записи `kind=defect` в `Tools/e2e/known-noise.json` удалять после исправления.
1. `e2e-log-verdict` — принят 2026-10-13 (журнал `2026-10-13-e2e-log-verdict-accepted.md`). Остаток: настоящий прогон
   `Run-QuestE2E` на шлеме с вердиктом по логам (проверено только на фикстурах) — при свободном шлеме.
2. `async-player-build` — `E2EPlayerBuilder.Run()` блокирует главный поток (таймаут MCP), пачкает `PC.asset`, `ProjectSettings.asset`, `MapRuntimeCatalog.asset`; ложное «устарел» — `Docs/testing.md` «Шероховатость: проверка свежести билда». Диагностика 2026-10-11 (чтение кода): `PC.asset` — URP
   `ShaderBuildPreprocessor` под Standalone+Development, мусор, зона performance; `ProjectSettings.asset` — снимок
   Player Settings посреди сборки (XR `preloadedAssets`), мусор, моя зона (`GameBuilder.cs:181-203` восстанавливает,
   E2E-сборщик нет); `MapRuntimeCatalog.asset` — `MapCatalogBuildStep` перезапекает отпечаток намеренно, для Git шум,
   зона gameplay. E2E-сборщик без `XrBuildSettingsScope` — Oculus-лоадер в Windows-плеере (связь с `xr-startup`).
   Ложное «устарел»: `Run-E2E.ps1` берёт `Assets\Editor` в корни свежести + сравнение по mtime → штамп по
   содержимому (`build-stamp.json`, SHA-256 runtime-исходников). Неблокирующая сборка: основной путь — batch-Unity
   на своей копии; в редакторе — delayCall + статус-файл (главный поток всё равно занят, renew аренды) и откат
   трёх файлов с согласия performance/gameplay. Аренда 421: сборка пачкает ещё `ProjectSettings/GraphicsSettings.asset`
   и `Assets/Settings/Graphics/UniversalRenderPipelineGlobalSettings.asset`; обход MCP-таймаута через `delayCall` +
   статус-файл работает (`tasks/vr-test-stand/tools/e2e-isolation-build.cs.txt`, 75 с).
   (`SessionRecoveryOnReconnectScenario.ConnectDirectly` мимо шва портов — передан network: `e2e-reconnect-port-seam`,
   inbox 3701.)
3. `xr-startup` — запуск без аппаратного XR для сетевых проверок, production-путь не трогать — PH, VTS.
4. `xr-input` — один адаптер поз/кнопок/стиков, ревью avatar-ik — VTS §5, §7, §14.
6. `stands-migration` — BotCombatStand сам правит EditorBuildSettings/Bootstrap/registry, ловит `StartupRoute.SceneNotLoadable`; перевести на общий запрос, порядок с bots-fix — VTS.
7. `worktree-isolation` — EditorPrefs профиля (`DebugBootstrapSettings.KeyPrefix`) и UXR focus-pause общие между worktree — VTS.
8. `headless-roles` — клиент `-nographics` считается сервером; общий запуск стресс-теста/сервера/e2e с performance — `Docs/testing.md` «Подводные камни, найденные прогоном».
9. `docs-sync` — `Docs/testing.md` «Уровень 3 · Multiplayer Play Mode» устарел (MPPM-теги вместо PlayLaunch); с gameplay сверить «Play из сцены карты идёт Offline → Lobby → карта» — MRB «Известные ограничения».
10. `interaction-scenarios`, `match-e2e`, `network-faults` — каркасная часть; содержание у manipulation/gameplay/network — PH.

Ограничения: native Oculus boundary, UPM/MPE -noUpm, licensing fallback, Git config lock — отдельные границы;
full suite, клик toolbar, Quest, synthetic XR/gameplay E2E не приняты; предупреждения TeamRegistry из аудита383 —
с gameplay; `Tools/e2e/Run-E2E.ps1`, `Tools/release/`, `ProjectSettings/VirtualProjectsConfig.json` вне writes
этапов — только новым этапом.

- `troubleshooting-link` — запись в `Docs/troubleshooting.md` (раздел «Сборка и инструменты»): Link не подключается
  после `-StopLink`, помогает только перезапуск среды Meta Quest Link на ПК (2026-10-12). Пока описано в
  `Docs/testing.md`; в troubleshooting — когда этап `hand-rig-quality/documentation` освободит файл.

- `quest-manifest-oculus-keys` — в манифесте APK (GameBuilder Quest/Test, 2026-10-12, aapt) нет Oculus-ключей
  (`vr_only`, `supportedDevices`, VR-категория), хотя XR работает (OpenXR-сессия до VISIBLE). Вероятно,
  `OculusBuildProcessor` не видит лоадер из-за смены в памяти в `XrBuildSettingsScope`. Влияние: категория в лаунчере,
  список устройств, проверка LateLatching/SpaceWarp дирижёром (`renderCheck=unknown`). Сессия в прогонах доходит до
  VISIBLE, не FOCUSED — проверить связь с prox/фокусом.

- `build-checkouts` (этап хаба `quest-perf-pipeline/build-checkouts`, rev 22, после e2e-smoke-kit) — решения пользователя
  2026-10-14: Prod — отдельный checkout (повтор 6449); Test-пара — отдельный Quest-checkout на Android (AskUserQuestion),
  сервер Test — в editor-worker без переключения платформы. Library worker 37 ГБ → два checkout ~60–75 ГБ. Размещение,
  перевод на SHA, очередь — agent-infra (запрос 2026-10-14). Моя часть: `-ProjectPath`, проверка закрытого редактора,
  SHA в отчёте, пара = сервер (worker) + APK (Quest-checkout) со сверкой ревизий, Docs/release.md.
- `prod-build-route` — решение пользователя 2026-10-13 (через performance, 5410): Prod-сборки — на отдельном checkout
  со своей Library, чтобы не сбрасывать кэш Test (IL2CPP Master против Release, общий Library/Bee). Моя часть (ответ
  2026-10-13): `Build-Game.ps1 -ProjectPath`, проверка закрытого редактора, SHA в отчёте, `Docs/release.md`,
  предупреждение GameBuilder о Prod в общей Library. agent-infra — размещение prod-checkout, перевод на SHA, очередь.
  Порядок: после stand-preview-preflight. Замер для `Docs/release.md` (5416, аренда 537, 5d614df4, Ryzen 9 5900X):
  Test пара 396 с (сервер 72 с, Quest APK 150 с инкрементально); Prod Quest — шаг APK 355 с (2578 МБ), стена 634 с.
  Prod-манифест: VR-категория, supportedDevices quest2, записей рук нет. Цена сброса кэша Test после Prod не мерена.
  Решение пользователя 2026-10-13: Quest 3 (TargetQuest3) пока не включаем — предупреждение валидатора остаётся.

- Запуск на Quest без контроллеров — передан performance (решение пользователя, 5181): этап controller-free-launch задачи
  perf-stress-scenario; launch-infra — стык (признак Test/e2e-сборки в GameBuilder, ревью, zone-ack), заметки — 5183.

- `xr-input` уточнён bots (5206→ответ, 2026-10-12): общий тестовый XR-адаптер для e2e и ботов «бот = настоящий клиентский
  Local-аватар, ввод от Blaze/скрипта» — голова + оба контроллера + grip/trigger (+ стики или MoveAvatarTo), в плеере И в
  редакторе/MPPM-клонах. Не блокер; состав и сроки bots пришлёт после проекта архитектуры режимов. Условия avatar-ik
  (4459/4877): один источник в слоте ввода до Manipulation, без записи в кости/пивот/калибровку; база — E2EHeadPose.
  Состав от bots 5252 и условия avatar-ik 5295 приняты (ответ 5337/5341, 2026-10-12): уровень UXR (скриптовые
  UxrTrackingDevice/UxrControllerInput вместо UxrUnityXR*), голова 6DoF в пространстве трекинга, оба контроллера 6DoF,
  grip/trigger/Button1-2/стики, `IXrInputSource.TryRead(frame, out XrInputFrame)` без аллокаций, ParticipantInputs в
  play-launch-control (новая ревизия), двойная защита релиза + тест. Порядок: после stand-preview-preflight — контракт
  xr-input r1 на ACK bots/avatar-ik до кода; срок — к device-executor bots (после их clip-cutover).

- Стык controller-free-launch: zone-ack на GameBuilder.cs дан (5330), условия — 5343 (Arm/Disarm в try/finally, Prod
  без изменений, порядок 10001/10002 с валидатором, diff до merge-request). После вливания принять
  `QuestControllerFreeManifest.cs` и `QuestManifestValidator.cs` в owns. Ревью 64d2546ef (5352 → ответ): убрать
  ArmedConfig, который переживает Disarm, и передавать config в CheckApkManifest явно; проверить, что Prod-манифест без
  HAND_TRACKING. После вливания — моя правка `Docs/release.md` (порядок 10001 вставка / 10002 валидатор). Позже —
  ошибка вставки через LastError/result.Fail вместо голого BuildFailedException. Финальный 3cabcc1ab0 одобрен (5496);
  тесты в `Release/Tests/` — перенести в `Assets/Tests/EditMode` при передаче owns.

- `docs-build-open-map` — находка lobby-decoration 5476: сборка из открытого редактора падает, если открыта карта
  каталога (повторный OpenPreviewScene → дубликат sceneId). Gameplay сообщил 6907: preview-loaded-scene
  принят и влит в origin/dev97a320c88, preflight3/3 и Windows build с открытой Lobby PASS.
  Own Docs/testing.md актуализирован: чистая загруженная карта проверяется без preview, dirty даёт отказ;
  обход пустой сценой не требуется. Доказательства gameplay, наш runtimeinput87643327108f старше фикса.

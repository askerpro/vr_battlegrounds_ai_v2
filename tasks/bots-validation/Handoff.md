# bots-validation — единый документ продолжения

Обновлено 2026-10-19, Europe/Moscow. Документ предназначен новому ведущему агенту:
его достаточно для восстановления цели, решений, состояния и оставшегося плана
без чтения прежнего чата. Исходный код и сырые evidence читать по указанным путям
по мере необходимости. Правила корневого AGENTS.md по-прежнему обязательны.

## 1. Задание новому агенту

Продолжи СУЩЕСТВУЮЩУЮ задачу bots-validation, owner bots-validation, в worktree
F:/CodexWorktrees/bots-validation, ветка task/bots-validation. Сохрани tracked,
staged и untracked изменения. Повторный task-new не нужен. Не запускай параллельно
другого писателя в этом checkout и не коммить весь dirty.

Цель: завершить оставшуюся проверку принятого поведения ботов, исправить
подключение двух клиентов при долгой инициализации и пройти функциональную,
сетевую, performance и физическую Quest-приёмку. Это продолжение принятой bots-fix,
а не переписывание ботов с нуля.

Ближайший приоритет: общий порядок подготовки сервера/карты, открытия порта и
первого подключения клиента. Нельзя считать решением удачную перестановку ролей
main/clone или просто успешный единичный запуск.

Новый агент принимает ведущую сессию: после свежего native listing обновляет
собственный route в хабе, читает inbox, сверяет план и продолжает. Исходная сессия
Codex: 01a15069-c158-7da0-ab1d-8ee6b32c4dd6; её route epoch3 — исторический адрес,
не адрес нового агента. Не копируй этот UUID в --as от своего имени.

## 2. Проверенный снимок состояния — не подменять его будущим live status

| Объект | Состояние при составлении |
|---|---|
| Checkout | F:/CodexWorktrees/bots-validation, linked worktree |
| Ветка / HEAD | task/bots-validation / 48d38b9b2885b771f9cc961b4b640eaa7f74ca9e |
| origin/dev после fetch и ls-remote | a62039a26f1d2d454b69e134aeb6eea38670042a |
| Отставание checkout | Один принятый коммит: «Упорядочить Player prefabs и сделать Ghost самостоятельным» |
| Rebase на a620 | НЕ выполнен: на этом шаге собирался документ, dirty сохранён |
| Index | Пустой; незакоммиченные изменения перечислены ниже |
| Координация | mode off; task plan revision7, brief revision13 |
| Этапы | floor-release и build-helper-release merged; остальные6 не завершены |
| Worker base request832 | DONE, result_sha48d38b9b2885b771f9cc961b4b640eaa7f74ca9e |
| Фактическая broker baseline | Уже a62039a26f1d2d454b69e134aeb6eea38670042a |
| Worker | active=null, blocked=false, recovery_required=false |
| Собственная Unity-аренда | Нет. Старые824/829 завершены; capability не использовать |
| Новый startup behavior | Не реализован; изменён только диагностический opt-in trace |

Коммит a620 затрагивает Player/Ghost prefab hierarchy, позы/карманы и avatar
Editor-инструменты. Поэтому до нового runtime надо сохранить dirty, обновить
свою ветку от свежей origin/dev и заново проверить фактические тела/ссылки/input.
Старые результаты818/824/829 относятся к своим SHA, а не автоматически к a620.

Снимок может устареть. Перед работой повторить status/fetch/проверку baseline.
Не понижать worker до старого48d38b9b;832 уже выполнен, повторять его не нужно.

## 3. Что уже опубликовано и что разрешил пользователь

### Пол BotCombatStand

Commit080f137aaade0d31c2ae0d1e7a8f1d8810e6f796 уже в origin/dev.
Три product paths:
- Assets/Editor/VR_Battlegrounds/Bots/BotCombatStandBuilder.cs;
- Assets/Scenes/Debug/BotCombatStand.unity;
- Assets/Data/Maps/MapRuntimeCatalog.asset.

Floor переведён Default0→Ground3 в сцене и Build/NormalizeFixture.
Геометрия, collider, tag, ссылки и IDs не менялись. Каталог пересчитан Unity;
дополнительно исправлен отставший Lobby fingerprint после принятого74bd42f57,
сама Lobby не менялась. Product diff5insert/4delete совпал с native R818.

Доказательства native818: existing EditMode99/99, catalog7maps/0errors.
Новые exact approve/ACK: bots d15bdc1a8b11489a, gameplay9d401def8a544fea.
Merge2174db9b9318476a86a7f6b7fb614799 completed; remote SHA отдельно проверен.
Физическая проверка телепорта на пол пользователем ещё не подтверждена.

### Сборочный helper

Commit48d38b9b2885b771f9cc961b4b640eaa7f74ca9e уже в origin/dev.
Изменены Tools/release/Build-Game.ps1 и вход
tasks/bots-validation/tools/reaggregate-input.json, добавлены task changelog/zone journal.

Исправлен locale-sensitive разбор DateTime/DateTimeOffset/ISO timestamps PS5.1/PS7.
Добавлен -ReaggregateInput / -ReaggregateOutputDirectory: без новой Unity build
проверяет исходные attempts, SHA/config/profile, pinned legacy helper/console,
полный inventory файлов, размеры/SHA256, safe paths/reparse guards и UXR.
Новый manifest создаётся отдельно; старый failed manifest не перезаписывается.
UNKNOWN/sourceKind/dirty не скрываются; старый process outcome помечен
control-flow-derived, exitCode=null, а не выдуманный raw0.

Реальные PS5.1/PS7/principal reaggregation: exit0, pair success=true,
Quest3/Server237 файлов, UXR ea5574049cff8620. Parser errors0.
Новая обычная Unity build после этой правки отдельно не проверена.
Exact launch-infra approve/ACK4393717961484c8a,
merge9af713880acf4cd4858519ed7df39afd completed; remote SHA проверен.

### Исключение QA и его границы

Пользователь прямо поручил «вливай что можно влить и продолжай исследование
проблемы с подключением клиентов с субагентом». Первый commit был остановлен
автоматической проверкой из-за правила userQA. Затем пользователь ЯВНО ответил:
«Разрешаю выпуск пола и helper без QA».

Это исключение относится к этим двум уже выпущенным исправлениям.
Оно не означает, что headset/helper QA состоялась, не принимает новые startup
изменения и не отменяет QA-before-tests для дальнейшей работы.
Новых test expectations до соответствующей QA не писать.

## 4. Что осталось незакоммиченным и как это сохранить

Четыре modified tracked source paths:
- Assets/Scripts/Debug/DebugBootstrapGate.cs;
- Assets/Scripts/Debug/Bootstrap/PlayLaunchConfiguration.cs;
- Assets/Scripts/Network/GameNetworkManager.cs;
- Assets/Editor/VR_Battlegrounds/Testing/PlayModeStandParticipant.cs.

Два untracked source/Unity paths:
- Assets/Editor/VR_Battlegrounds/Testing/PlayLaunchNetworkDiagnostics.cs;
- Assets/Editor/VR_Battlegrounds/Testing/PlayLaunchNetworkDiagnostics.cs.meta.

Meta создан Unity, GUID cf7e53aea46679045b48fefd80d1e0c5; не генерировать заново.
Также untracked собственные Readme/Details/Handoff/plan/contracts/tools задачи.
Changelog пола/helper и соответствующие zone journals уже committed.

Это opt-in NetworkStartupDiagnostics=false по умолчанию. Scope managed участника
включает KCP debug до Awake, пишет UTC/Stopwatch/run/participant/session/PID/frame,
scene/network/transport события и редкие >=1s gaps. Dispose снимает свои подписки,
восстанавливает debugLog/Log.Info. Network timeouts/readiness/retry не менялись.
GameNetworkManager hooks editor-only; SDK trace не патчился.

Сеть/trace НЕ опубликованы в dev и не приняты пользователем.
Полные локальные backups:
- 9136590af0adddeca4c6d08c8a3cffb741be32b4 — post-release source+task snapshot поверх48d38b9b;
- ab2736790634e633e70eaa7b7ea302efd11a629f — более ранний полный snapshot;
- 5877d8825edd6a77fe14a8a4f23839fc067835fd — точный Unity input829;
- 5fa499be04d3bcb43990651af1a54a2ef7a034de — result829, только Unity-generated meta.

Это technical checkpoints, не новые accepted commits. Backuprefs локальные.
Перед rebase сделать новый checkpoint и при необходимости обратимый stash,
сохранив untracked; не применять reset/clean/force checkout. После восстановления
сверить код/meta/diff, не потерять trace или материалы нового документа.

Reports исключены из Git. Они физически находятся в
F:/CodexWorktrees/bots-validation/tasks/bots-validation/reports.
Не force-add. При передаче на другую машину документ не переносит APK/логи:
нужны эти локальные artifacts и source diff/checkpoint отдельным архивом.
На том же компьютере использовать существующий checkout.

## 5. Последние реальные прогоны: что подтверждено

Все перечисленные времена ниже UTC; пользовательское время Europe/Moscow.
Старые input/base — 2e1b6a2fc342e894d4b8526287b804fa5e3093b0 плюс свой checkpoint.
Не выдавать их за fresh acceptance новой avatar базы a620.

### Пакет824 — без нового подробного trace

| Case | RunId | Факт |
|---|---|---|
|discovery0|e0824aafa03a40dc8b2ee8149d673398|Ready: dedicated+2PC Spectator, bodyless/team0. Player2 disconnect→reconnect epoch1→2, новая session175; второй клиент остался connected.|
|discovery8|ba6cac2621db4c3394328d32ec1e3c9c|Server TestMap1/8bot sessions, оба клиента Offline, epochs0/1; KCP silence10000ms.|
|localhost0|439f5fc3f2ca44dbbebd8dcbadc2c801|LaunchNotReady до Play всех процессов; сетевой исход UNKNOWN.|
|localhost8|100f0249fb8b4b3e8c62606bd17f6640|Server TestMap1/8bot sessions; клиенты Offline/epoch0, raw transport причина тогда отсутствовала.|

Все4 cleanup=true, Playing=false. Finish/receive824 выполнены,
R e1130da7142c317d9207d820fed71c3bad7f58d1, paths0/unexpected0.

### Пакет829 — exact input5877d882, opt-in trace

Discovery8 run0b3cf3a8c7f947f8b66b9ab2b3f83d4d:
- server-listening20:44:32.860; загрузка TestMap1 началась сразу после него;
- server-scene-end20:45:07.415, затем создаются8ботов;
- Player3 hello/connected20:45:07.485, timeout20:45:17.508;
- server late-hook20:45:07.472→20:45:19.803: gap12.330586s;
- Player2 connect20:45:03.790, handshake timeout20:45:13.795;
- MapPlayable20:45:11.878 находится ВНУТРИ длинного frame4, не после его завершения;
- terminal: server TestMap1/8sessions, clients Offline epochs0/1, Ready=false.

Localhost8 run4af7525cc39d4dc4b36d483d2780e064:
- clients connect20:46:49.563/20:46:50.811;
- KCP RawReceive SocketException20:46:52.439/20:46:53.498, затем disconnect;
- server начинает слушать только20:48:10.588, примерно на80s позже попыток;
- terminal: server TestMap1/8sessions, clients Offline epochs0/0, Ready=false.

Текст SocketException: «Удаленный хост принудительно разорвал существующее
подключение». Specific WSA/ICMP code не записан; не объявлять его доказанным
ConnectionRefused. Увеличенный silence timeout не предотвращает RawReceive disconnect.

Оба829 завершены Idle/CleanupPassed=true/Playing=false/Ownerempty.
Finish/receive829 выполнены; R5fa499be только разрешённый meta, unexpected0.
Для каждого case сохранены3 process logs и3 независимых startup JSONL.
Свежий existing AndroidCompileGate на input829: Passed=true, Errors0.
Android817 также PASS; отдельный raw MCP disconnect817 не отменяет доказанный
результат из Editor.log, и не является причиной сетевого отказа.

### Сравнение main Editor и clones

Успешный824/discovery0 уже имел main reload70.214s и TestMap1 load29.438s
(ThreadWait25.319s), clones reload~5s и map1.46/1.82s.
С8ботами main reload71–76s и map29–31s. Значит, эта тяжёлая инициализация
не создана BotCount8 или диагностикой829 и сама по себе не достаточна для отказа.

829/localhost: Refresh79.531s, Domain Reload Profiling75.607s.
Внутри: InitializeOnLoadAttributes22.516s, Methods14.019s,
AwakeInstancesAfterBackupRestoration12.348s; загрузка assemblies~0.46s.
Imports0, AssetFileChanges0, compile~1ms. Это не новый импорт декора.
Маршрут Offline→TestMap1, отдельной Lobby load в interval нет.
Косвенное влияние общих ресурсов, Editor state, JIT и3процессов не исключено.
Конкретный workload, блокирующий frame, ещё НЕ выделен; BotDirector не обвинять
по одной близости логов. Trace AutoFlush/KCP logs могли усиливать задержку.

## 6. Решения пользователя о startup и границы исследования

Пользователь напомнил, что2клиента раньше работали. Предложил гипотезы:
после декора Lobby всё стало медленнее; либо длительная MPPM инициализация
проявила старую неподготовленность архитектуры. Затем явно уточнил:
успешный role-swap не доказывает найденную причину и не является решением;
архитектура должна позволять подключиться при долгой инициализации сервера.
Варианты для изучения: больший timeout и/или открытие listen после готовой карты.

Раннее предложение только MPPM initial-connect admission НЕ было одобрено.
Нельзя трактовать его как уже согласованный дизайн или автоматически внедрять.
Допустимы продолжение исследования и подготовка общего runtime дизайна.
Точный новый lifecycle/таймауты/initial attempts согласовать до реализации.

Read-only субагент external_failure_analysis уже выполнил анализ source/logs.
Его текущие отчёты: timeline-analysis.md, mppm-initialization-analysis.md,
startup-architecture-options.md в reports/trace829.
Он ничего не менял в продукте/SDK, не запускал Unity и не ремонтировал infra.
Role-swap пакет подготовлен на бумаге, НО НЕ ВЫПОЛНЕН; это лишь опциональная
локализация main-vs-server стоимости. Не планировать его как конечную приёмку.

## 7. Найденные архитектурные зависимости — основа следующего решения

Нужно различать четыре состояния: logical server-active, socket-bound,
startup-ready, advertised. В текущем ServerOnly пути порядок:
GameNetworkDiscovery.ApplyRole → NetworkManager.StartServer → SetupServer →
NetworkServer.Listen → Transport.ServerStart → GameNetworkManager.OnStartServer →
SessionContext/handlers → ServerStartupRoute/ServerChangeScene → LoadSceneAsync →
MapBootstrap/composition/mode Ready. Advertisement вызывается рано после StartServer.

### Владельцы и пути

| Ответственность | Единственный владелец / основные исходники |
|---|---|
|Server lifecycle и доступность сети|Assets/Scripts/Network/GameNetworkManager.cs|
|Client connect/disconnect/initial waiting и advertisement|Assets/Scripts/Network/GameNetworkDiscovery.cs|
|Первый frozen target/mode request|Assets/Scripts/Managers/ServerStartupRoute.cs|
|Live map load|MapLoader.Instance.LoadMap(sceneName), Assets/Scripts/Managers/MapLoader.cs|
|Map composition/descriptor/run-key|Assets/Scripts/Maps/Runtime/MapBootstrap.cs, MapRunAuthority.cs, MapReferee и GameMode|
|Управляемая топология и frozen configuration|Assets/Editor/VR_Battlegrounds/Testing/PlayLaunch.cs, PlayModeTestStand.cs, PlayModeStandParticipant.cs, StandProtocol.cs|
|Роль/launch seam|Assets/Scripts/Debug/DebugBootstrapGate.cs и Debug/Bootstrap/PlayLaunchConfiguration.cs|

Уточнить реальные пути через rg --files: ссылки на классы/строки в отчётах
относятся к исследованному input и могут сдвинуться после rebase.

Полный существующий MapBootstrap при NetworkServer.active=false не готовится:
нужны SessionContext/MapRunAuthority epoch и authority.isServer/CanWrite,
NetworkServer.Spawn служб/режима/ботов. Просто LoadSceneAsync до StartServer
может обойти владельца первой сцены и вызвать повторный map run; так не делать.

Перспективная source-возможность без немедленного SDK patch:
NetworkServer.listen=false перед штатным StartServer сохраняет active=true,
handlers и серверный graph, но пропускает Transport.ServerStart.
Можно исследовать server preparation до внешнего UDP bind.
При этом listen=true само сокет НЕ открывает. Повторный Listen не является
безопасным hot-enable; нужен один owner-controlled переход фактического transport
bind, проверка ServerActive и rollback/cancel/bind-in-use/shutdown.
Это ещё НЕ проверенный runtime lifecycle. Host/local client имеют особый порядок,
ServerOnly рецепт нельзя переносить на них вслепую.

MapPlayable не означает конец startup: descriptor/mode commit предшествует
части BeginWhenReady, teams, arsenal, deferred GoLive. Нужны явные receipts
завершения соответствующих работ и корректная сетевая frame boundary.
Mirror.OnLateUpdate вызывается ДО actual NetworkServer.NetworkLateUpdate;
одного этого hook недостаточно для утверждения, что send завершён.
Нынешний trace marker server-listening стоит в OnStartServer. При delayed bind
он должен быть разделён на logical-active и подтверждённый socket-bound.

Для explicit client до открытия порта нужен определённый initial waiting/attempt
lifecycle с общим deadline, отменой и named failure. Его владелец —
GameNetworkDiscovery. PlayLaunch может дать run-bound readiness своего сервера,
но не становится вторым writer Mirror/повторных соединений.
Remote explicit не может узнать о появлении UDP bind без попытки/ready-протокола.
Stand reconnect требует epoch>=1; initial attempt до первого соединения — отдельная
фаза, не обход этого guard и не поддельное повышение epoch.
Пользовательский disconnect/cancel обязан прекращать любые initial attempts.

### Таймауты: что действительно доступно

KcpTransport.Timeout копируется в config в Awake; поздняя запись только в поле
не меняет уже созданные peers/config. KcpPeer.timeout публичен, но transport
refs protected; product adapter/subclass может получить эти refs.
Однако peer.kcp и kcp.dead_link internal. Публичного live setter MaxRetransmits
нет; config влияет на будущие Reset/new peers, не на живой Kcp.
Reset сбрасывает cookie/FSM/очереди/таймеры; применять его к живому соединению
для смены policy нельзя. Поэтому полное startup→operational переключение
retransmit limit через текущий public API не доступно.
Не обещать180s синхронизацию изменением одного silence timeout.
Если реально понадобится SDK capability, отдельно определить минимальный scope,
зарезервировать patch номер через coordination.py и получить zone review.

Предпочтительное направление для согласования: server preparation до external
publication + определённое ожидание initial-connect клиента; отдельно решить
необходимость per-peer sync budget. Не ослаблять глобально уже играющих peers
ради нового клиента. Server main-thread stalls после готовности тоже требуют
ограниченной/cooperative работы либо отдельной map-transition policy.

### Критерии будущей ручной приёмки

- Долгая server preparation >10s, client начинает до bind; затем Online либо
  bounded named failure, без ложного Ready и зависшего Offline без действия.
- Любое назначение main/clone; затем эквивалентные built/remote сценарии.
- Медленная client scene/snapshot sync после handshake: обе стороны соблюдают
  согласованный startup budget, затем operational policy. Этот сценарий входит
  в проверку; способ реализации budget ещё предстоит выбрать.
- Отсутствующий сервер, failed composition, bind занят, cancel/disconnect:
  известный terminal; нет бесконечных attempts или самовольного reconnect.
- RunId/ProcessSession/MapRunKey receipts не принимаются от старого запуска.
- Один bind, один SessionContext/map run/mode, без повторного spawn.
- Host local и remote client проверены отдельно; live смена карты через MapLoader.
- Role-swap PASS, Editor Ready и compile PASS не заменяют gameplay/Quest QA.

## 8. Полный оставшийся план в порядке выполнения

### A. Обновление базы и startup — ближайший блок

- [ ] Сохранить dirty/checkpoints, fetch/rebase свежей origin/dev; сверить новые
    avatar/Ghost ссылки и зарегистрировать актуальную task revision.
- [ ] Подготовить согласованный дизайн server preparation → final readiness →
    socket bind → advertisement и client initial-connect lifecycle.
- [ ] Выбрать startup/sync deadline policy на основании обоих классов отказа,
    не менять timeout/retry/readiness одновременно без проверяемого решения.
- [ ] Реализовать выбранный lifecycle с одним владельцем каждого state.
- [ ] Получить actual compilation/существующие применимые проверки и raw runtime.
- [ ] Повторить discovery/localhost,0/8ботов,2PC Spectator, длинную init,
    cancellation/failure и затем built/remote coverage.
- [ ] Отдельно разобраться с localhost0 LaunchNotReady до Play, не называть его
    сетевым отказом. Продолжить локализацию main reload/workload по необходимости.

### B. Внешние Player и Spectator

- [ ] Dedicated+2PC Spectator: bodyless/team0, карта/позы/оружие ботов через
    обычный receive путь; disconnect/reconnect на fresh input.
- [ ] Remote Player: S2C/H1C, latejoin, owner self-skip, руки/кобура/оружие,
    смерть/новое тело, reconnect. Клиенты не создают bot-компоненты/второй lifecycle.
- [ ] S8 допускается после функционального Spectator; Player/team0 его не заменяет.
    Earlier824 discovery0 — частичный proof, не закрытая вся network матрица.

### C. Функциональность ботов, lifecycle, оружие и контакт

- [ ] PPK/AK105: штатные Equip/Prepare/Holster/Unarmed/Purchase/Death/Ghost/Respawn,
    одна цепочка владельцев, стационарная цель. Стендовая покупка не доказывает
    Director→Buy→Offer; ghost штатно пишет rest/travel через BotAvatarDriver,
    но не Hands/Aim/Combat/grab/shot.
- [ ] Blaze proxy A: существующие T01/T06/T08, normalizedTime, Move/footpath,
    missing states. Generic1s вариант B нужен только при доказанном провале A;
    он не самостоятельная обязательная feature.
- [ ] Director M2:4 scored rounds, admin Stop и второй матч на тех же Session/Mind,
    покупка/экономика/context. firstNaturalProof=true значит4раунда до Stop,
    а не доказанный natural match end.
- [ ] Pause/resume, reload той же/другой карты, respawn/new life, natural Offer refusal.
    continuousDirectorEquip подготовить до AddBot; normal UI/MapLoader.
    Записывать Session/Mind/body/MapRunKey/modeEpoch/shopToken/economy/inventory.
    sampled maxOwned1 не доказывает ровно один spawn; notExercised остаётся UNKNOWN.
- [ ] Каждая активная body×weapon пара: actual emission/outcome/readiness,
    ActorSource/session damage credit, death/restore/cleanup.
    Пассивный observer не готовит бота и не стреляет вместо него.
- [ ] Runtime right/left hand + whole body: точные GUID/pose/frame, контакт и
    закрытая часть хвата, визуальная Quest проверка. HandFit analysis_incomplete
    не PASS; универсальный допуск±2mm пользователем не утверждён.
- [ ] SDK registry: fresh startup/next map/abort/rejoin/terminal cleanup,
    отсутствие duplicate IDs/leaks. getterErrors0 и +150MB mono не leak-free proof.
- [ ] Применимые unchanged Editor/managed/Android checks на fresh input;
    obsolete fixtures отмечать отдельно, не подгонять до userQA.

### D. Сравнительная производительность

Сначала валидировать методику общего H/C сравнения. Здесь H — историческая
Hybrid reference composition, C — clip-cutover candidate; буквы не обозначают
Host/Client. Не возвращать Hybrid в production ради baseline.
Исторические common36 H/C snapshots — только упаковка, не принятое целое H.
Нужно подтвердить сопоставимые frozen source/dependencies/common probe на новой базе.

- [ ] H8/C8: Host+8ботов+1Player; H10/C10: Host+10ботов+1Player.
- [ ] RH: dedicated+8ботов+1remote Player; исключить его session/avatar из bot metrics.
- [ ] S8: dedicated+8ботов+2Spectator после functional acceptance.
- [ ] Одинаковые actual seed до bots, hardware/quality/warmup/windows/duration/frames,
    полный roster, console counters каждого процесса и valid recorders.
- [ ] Combat p95 C8 не хуже свежего H8; GC/scaling без выдуманного допуска;
    bot bytes/s не больше сопоставимого remote human.
    Сохранять allocations bytes/frame и bytes/s, Gen0 rate, frame duration и counters.

Старые H800 zero shots, C801 cap600/completedfalse, H′804 diagnostic —
не baseline. analyze.py из старого cc3 исключён из приёмки: он мог считать
missing lifecycle/participants/counters нулями и дать ложный PASS.
До userQA читать raw вручную; после — исправить анализатор/регрессии в этой задаче.
Полный watch.completed, identity/roster/seed receipts, Idle/Playfalse/CleanupPassed,
отсутствие cleanupWarning и неизменность чужих заявок обязательны.

### E. Физический Quest

- [ ] Fresh согласованные APK+Dedicated build из одного immutable input.
- [ ] Quest-client bot0/1/9 + PC dedicated frame9. Это действующий план вместо
    старого Quest-host1/8/16. Проверить качество, хват, сеть и производительность
    против сопоставимых remote dummies.
- [ ] Сохранить exact build SHA/input/device/роль/scene/Hz/FFR/CPU/GPU/quality,
    полные metrics и необходимые снимки/видео. Simulator/Editor/Android compile
    не заменяют фактическое устройство.

### F. QA, регрессии и завершение

- [ ] QA уже опубликованных пола/helper остаётся открытой, несмотря на разрешённый выпуск.
- [ ] После проверки нового/изменённого поведения пользователем добавить/обновить
    соответствующие тесты, включая Editor tools и временные probes с expectations.
- [ ] Для final diff получить review каждой затронутой зоны; ACK относится к exact
    SHA либо stable patch-id после rebase без изменения diff.
- [ ] Публиковать принятые небольшие блоки штатно FF в origin/dev, затем request-base;
    проверить actual result/baseline, не считать QUEUED публикацией.
- [ ] После фактического завершения всей задачи — task-close с текущей revision.
    Не отмечать задачу завершённой по одним compiled/Ready/частичным историческим PASS.

## 9. Тела/оружие: стартовые ориентиры, не свежая инвентаризация a620

На исследованной базе2e1b:
- CT AvatarData4d1f2ac2b7326a144a7aff3486e766fe,
  MEF_black prefab60604d8891226f64592f880c9b2da883;
- T AvatarData294e4428ec16fb84d9dd876e1cbb523a,
  MEF prefab2aa0ab6714e2413f9d829637b62df0c0;
- PPK WeaponInfoe656ffaededf94447b00dfe8561f5caf,
  prefab53b2af502e94d13489397cef90c86925;
- AK105 WeaponInfo7d9a79e589f45c74e9585bdbed93ce76,
  prefab732e5b48d3557594583580ed383cc981.

Перед fresh run проверить цепочку BotDirector→BotSkin→SessionTeamAssigner→
TeamAvatarStrategy→TeamRegistry/TeamData/AvatarData и фактические prefab/poses/FBX.
После a620 пути/наследование менялись; не использовать старый список как manifest.
Root input обязан включать dependency closure, а не только scene/scripts.

## 10. Проверки, сборки и известные устаревшие рецепты

Существующие .NET ProtocolHarness/EditorBinding не исполнились: отсутствуют
E2EDiagnosticSession/Input и kcp2k/TransportError stubs. Это не Unity compile failure.
Не переписывать ожидания/stubs ради зелёного результата до соответствующей QA.

Старые pf-round5/b0-preflight.cs ожидают удалённые BotBodyMode.PlayerFlow/
AddBot(mode)/BotStandRunOptions.PlayerFlow и старые bodyGUID. tools/net-round1
тоже содержит удалённый PlayerFlow field. Нельзя просто заменитьGUID и объявить
runtime defect. Непинованный cc3 B0 давал CS0123 из-за optional аргумента
FinishActive против Func<string,string>. LedgerSpawnReady obsolete expectation
ранее исключался; это не пройденный/обновлённый тест. M1 не обязан стартовать
при менее двух участников/команд.

Старые точные функциональные инструменты искать в tasks/bots-fix/cc1-plan.md,
tasks/bots-fix/tools/cc3-perf/acceptance.md и
tasks/bots-fix/tools/cc3-perf/package.json; проверить совместимость
с нынешним source. Исторические proof5489/G805/802/старые APK не свежая приёмка.

Сборки3d24f53080fe853cbfc8a1de82170c70cedc5b7a: Quest+Server native builder PASS,
старый wrapper exit1 был именно timestamp bug. Архивы reports/build3d24/Quest
и Server, old pair-build.json success=false. В reports/helper-fix/
ps7-final, ps51-final, principal-final лежат новые success=true manifests.
UXR старой пары ea5574049cff8620,8714 IDs/356files; это исторический cross-profile
match, не ожидаемый hash новой avatar базы. Quest APK не установлен/не проверен.

Штатные build roots:
F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai и
F:/CodexWorktrees/build-test-android/Vr_Battlegrounds_ai.
Стандартный Build-Game.ps1 из своего root, Config Test, quest+server,
один immutable Ref. Dedicated builds идут отдельно от interactive worker.
Перед Ref/Unity проверить процессы, UnityLockfile, progressing logs и dirty
обоих build roots. Ранее они были dirty; текущую свободу/чистоту проверить заново.
Не перетирать чужое, не делать clean/force checkout.

Для повторной агрегации без Unity выбрать НОВЫЙ output directory:
~~~
powershell.exe -NoProfile -File Tools/release/Build-Game.ps1 -ReaggregateInput tasks/bots-validation/tools/reaggregate-input.json -ReaggregateOutputDirectory tasks/bots-validation/reports/helper-fix/user-qa-new
~~~
Этот режим требует сохранённых локальных archives/console из input; существующий
каталог не перезаписывается. Не повторять Unity build ради старого failed wrapper.

## 11. Воспроизводимая исходная конфигурация сетевой диагностики

Это baseline829, не новый исправленный дизайн. Discovery использует пустой
ClientAddress, localhost —127.0.0.1; для0ботов изменить только BotCount на0.
Метаданные case/id не передавать в configuration JSON.
~~~
{
  "SchemaVersion": 1,
  "Enabled": true,
  "SceneSource": "scene",
  "ScenePath": "Assets/Scenes/Maps/TestMap1.unity",
  "Role": "server",
  "ClientAddress": "",
  "NetworkPortPolicy": "auto",
  "ClientCount": 2,
  "AdditionalPlayerRoles": ["client", "client"],
  "HostIsAdmin": false,
  "DeviceType": "PC",
  "GameRole": "Spectator",
  "ModeId": "elimination",
  "AutoGoLive": true,
  "MinPlayers": 8,
  "BotCount": 8,
  "UseRandomSeed": true,
  "RandomSeed": 20261018,
  "PauseOnFocusLoss": false,
  "ScreenshotOnButtonB": false,
  "AllowUnmarked": false,
  "Readiness": "map-playable",
  "TimeoutSeconds": 180,
  "NetworkStartupDiagnostics": true
}
~~~
Флаг trace есть в незакоммиченном input, а не в опубликованном48d38b9b.
Readiness map-playable в bot0 не доказывает начавшийся матч/четыре раунда.
ServerOnly Editor с графикой не считать доказательством dedicated build/Quest.

## 12. Порядок запуска и координации для нового агента

1. Прочитать корневой AGENTS.md и этот документ; проверить cwd/branch/git status,
   absolute-git-dir и common-dir. Здесь linked worktree; main checkout read-only.
2. Свежий native listing → собственный route-publish CAS → inbox --unread --as
   СВОЙ адрес; повторять только при pagination.has_more=true. Обработать notices.
   Адрес прежнего чата не заимствовать. Новый task не регистрировать повторно.
3. Сохранить dirty, fetch/rebase fresh origin/dev без потери untracked/meta.
   Сверить actual base/аватарные изменения, task status/brief revision и mode.
   Register обновлённый существующий Readme/plan с текущей CAS revision.
4. До правок читать AGENT.md затронутых зон: network, launch-infra, gameplay,
   bots, avatar-grip/IK, performance по фактическому scope. expert owns — routing
   ревью, не запрет task agent работать в нужных зонах.
5. Уточнить/согласовать общий startup дизайн. Не считать старое MPPM admission
   согласованным, не начинать с глобального timeout или обхода epoch guard.
6. Перед Unity подготовить точный immutable пакет и release conditions, затем
   checkpoint → request → watch-ticket → claim → begin → guard → execute/wait →
   finish → receive. Stage admission не заменяет broker lease.
   MCP должен быть привязан к source root bots-validation, а guard — к ожидаемому
   worker/input. Раньше старый чат ошибочно имел proxy --repo bots; проблема
   решена переходом в этот проект. Не использовать такой foreign proxy и не
   чинить launcher/config самостоятельно, если новый клиент снова привязан неверно.
7. Play только PlayLaunch.Plan → PlayLaunch.Play с exact PlanHash → Status.
   Один Play на case. Сохранять полные JSON ДО return, выводить только compact
   summary/путь. Не использовать прямой EditorApplication.isPlaying=true.
8. По наблюдённому своему RunId вызвать Cancel(runId,"bots-validation").
   Подтвердить cleanup/Playingfalse/Ownerempty, архивировать process logs/JSONL.
   После подготовленного пакета немедленно finish/receive до анализа/ожидания человека.
9. После QA — регрессии и final zone review. Integration: fetch/rebase →
   exact approve/ACK → merge-request/merge-execute → request-base → actual DONE.
   Не force-push и не писать в main вручную.

Mode off снимает coordination gate для local tools, но не Unity leases/guards,
не ownership и не права чинить инфраструктуру. Смена mode — не задача этого агента.
При hub write в Codex readonly DB нужна штатная escalation, не обход БД.
Если новый клиент не адресует native peers, очистить собственный route штатно;
межзадачные сообщения сначала в canonical inbox. Инфра issue отправлять
agent-coordination-protocol с пометкой для сопровождающего worker/agent-infra.

Unknown outcome Unity mutation: не replay/retry/clear_stuck/recover/finish
активной операции. Сначала durable response/archive/journal и status.
executionCompleted=true значит действие уже выполнено.
Не прерывать human Play/dirty scenes/Prefab Stage или чужие leases.
Token держать приватно, не класть в этот документ; у старых leases он не передаётся.

Runtime правила: GameLog вместо Debug.Log; серверные map changes через MapLoader;
authority gates для synchronized actions; один state owner.
Editor source в Assets/Editor/VR_Battlegrounds; Unity YAML/meta только native save.
SDK patches только с reservation и task-local changelog.
Managed source/tools/docs и task md/json — LF. Перед checkpoint проверить только
свои physical paths/staged blobs существующим Tools/agents/check_text_eol.py.
Не трогать общие indexes/changelogs и не нормализовать чужие файлы.

## 13. Где лежат доказательства и что читать только при необходимости

Все следующие пути относительно tasks/bots-validation/:
- reports/trace829/timeline-analysis.md — точные временные цепочки обоих отказов;
- reports/trace829/mppm-initialization-analysis.md — main/clones и контроль bot0;
- reports/trace829/startup-architecture-options.md — source dependencies/варианты/API limits;
- reports/trace829/{discovery-bot8,localhost-bot8}/logs — полные process logs+startup JSONL;
- reports/trace829/{android-compile,finish,receive}.json — exact technical receipts;
- reports/external824/ — четыре исходных case, reconnect0 и raw statuses/logs;
- reports/floor818/ — tests-full/native-floor/test-starts/console;
- reports/helper-fix/diagnosis.md и final manifests — helper checks/ограничения provenance;
- reports/build3d24/ — APK/Server/original manifests, console рядом;
- reports/*release* / *merge* / floor-review* — exact new reviews/ACK/accept/merge receipts;
- reports/base832-handoff.json — DONE832; reports/broker-handoff-current.json —
  снимок idle worker; текущую baseline сверять заново;
- reports/handoff-archive/Handoff-before-consolidation-2026-10-19.md — прежняя
  хронологическая передача, для основной работы больше не нужна.

plan.json — machine scopes/stages; Readme.md — короткая карточка; Details.md —
исторический журнал, в котором ранние формулировки могли устареть. Этот документ
заменяет необходимость читать всю хронологию, но не сами raw artifacts при проверке.
Ссылки на historical profiles/SHA не дают права повторять операции или принимать
старые результаты на новой базе.

## 14. Готовая стартовая формулировка

«Продолжи существующую bots-validation в F:/CodexWorktrees/bots-validation.
Сначала прочитай tasks/bots-validation/Handoff.md и корневой AGENTS.md.
Сохрани все незакоммиченные изменения, повторный task-new не нужен.
Пол/helper уже влиты с явным исключением QA; trace не опубликован.
Обнови checkout от origin/dev с сохранением dirty и продолжи с общего порядка
server preparation / socket bind / client initial-connect, затем оставшейся
матрицы. Role-swap не является исправлением. Новые tests — после userQA».

Документ составлен по текущему checkout, хабу и broker status. Полная задача
не закрыта: опубликованы только два самостоятельных исправления.

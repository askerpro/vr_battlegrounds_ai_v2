# Боты: player-flow и Blaze AI

Обновлено: 2026-10-18. Задача `bots-fix`, owner `bots`, ведущая сессия
`01a14cd3-0777-7292-8802-d68a8df74022`. Worktree `F:/CodexWorktrees/bots`,
ветка `bots-fix` (продолжение существующей задачи).
[Машинный план](plan.json), [план завершения и зависимости](completion-plan.md),
[архитектура](player-flow-plan.md), [исторический статус](changelog/2026-10-18-status-before-consolidation.md).

## Цель и порядок работы

Завершить переход ботов на штатный поток игрока: обычный аватар и SDK-хват,
один владелец каждого состояния, решения ИИ через адаптер намерений.
По решению пользователя 2026-10-18 ведущая сессия и её субагенты делают нужные
изменения во всех областях; эксперты подключаются для ревью. Чужие worktree
сохраняются; перенос из WIP не означает приёмку или закрытие чужой задачи.

## Текущее состояние

Последний technical candidate — `53e3a85fd`: SDK88 и Mirror7,64owned/stagedLF,
HEAD/настоящийindex сохранены. Network/gameplay/launch-infra approve записаны
для этого SHA. Host/rejoin/регистрация53 провереныв798; Spectator795–797
относитсякf8. Quest/server53 обаGameBuilderPASS, UXRсовпал; общийwrapperexit1
из-заразбораUTCподru-RU, infra9318передан. Полныйаудитbuild/UPMlockниже.
Врабочемдеревепосле53 добавленадресныйклиентскийrequest-team-change;
егоsource/review/compile/runtimeиcommon36впроцессе, приёмкинет.

Common34 H=`1a20c093a`, C=`1b8497e67`, frozen85/cutover48/probe2:
каждый actualoffline12сборок+5payloads errors0/freshMirror/noFOREIGN.
Они намеренно используют одинаковую pinnedprobe2fa11b. В отдельной проверке
currentprobe самого53 игровые12сборок errors0, а cc3 B0 несовместим с current
FinishActive API (CS0123): этот payload предназначен для H/C pinned API.
Это ограничение сохранено как FAIL, исходники/ожидания под него не переписаны.
Семь rawHostpayloads отдельноскомпилированы с actualcurrentprobeDLL/errors0.

- После fetch: `origin/dev=50c27ae71`, HEAD перед консолидацией `f10735c1c`,
  0 позади / 24 впереди, дерево было чистым. WIP `bca9e3632` содержит только отчёт;
  реализация cutover находится в более ранних checkpoints.
- Хаб r64: pf-prototype/equipment/network MERGED; clip-cutover RUNNING epoch17
  после учёта парных meta registry/proxy/cutover и ACK use-context r2. Остановленный epoch4 исторический.
  Binding r3 AGREED; r2, use-context r2, shooter r1 и grab-lifetime r2 ACTIVE.
- Seed Variant B уже принят в `192eb34f8` и присутствует в текущей базе.
  Фактическийreceiptдоspawn полученв795/797/798/799; самseedнепрепятствуетстарту.
- R08/SDK82 и Revolver авторизованы в Unity768/776, сохранены в оружейном
  WIP `88c664f09`; это исторические технические результаты, runtime-приёмки нет.
  Spectator находится в gameplay WIP `2453262ed` и network `7b67ce051`.
  SDK81 Host precache — technical checkpoint `6a0fde1e0`, не влит.
- Прогоны 738/747/751 и офлайн-компиляция исторические. Хваты/source identity,
  свежие R08, Host H/C, S8 и Quest не приняты.
- Собраны SDK81/82, R08/Revolver, Spectator и SDK84 selector. Офлайн11 сборок и
  два пакета по5 C#6 payloads — errors0; visual также с текущей пробой матча.
  [Состав и проверка](changelog/2026-10-18-unified-source.md).
- Unity783 DONE/receive: input43b58afd4, result354cee7e1, source paths0/unexpected0.
  Контроллер и preflight выполнены; G old/new сняты, cleanup подтверждён.
  192 артефакта сохранены в `reports/received783/`, SHA256 копий совпали192/192.
  Hand Fit четырёх рук — analysis_incomplete; качество пока не принято.
- Замечания avatar-ik/grip исправлены: EventArgs создаются только при смене позы,
  обход наследования без yield, положительный ParentPrefab cache. Нужные Docs/expert
  paths включены в план. Повторные ревью исходников одобрены; runtime GC ещё не измерен.
- Source r3: EventArgs/yield устранены; ParentPrefab использует положительный cache,
  живые pose/blend/default читаются каждый кадр. Сетевое P1 исправлено: handshake
  Spectator не назначает IsAdmin по клиентскому флагу. Повтор compile11+5 errors0.
- В783 подтверждены pose/source identity обеих кистей до/после respawn, 22 выстрела,
  13 событий урона, ghost shots0. Контакт пальцев требует пользовательской оценки.
- Unity785 DONE/receive: result6c35e0224, paths0; 70 артефактов reports/received785,
  SHA256 копий совпали70/70. R08 Fired92/hits65, Revolver Fired34/hits28, fixture ready,
  errors0, cleanup/Idle/profileRestored подтверждены. Это MEF и не полный Actor/Source trace.
  Сценарии выполнены на snapshotdf0b97be2 до source r3.
  B0 timeout разрешён через get_history: execution success/ready=true, 747 hashes,
  фактическое выполнение64с. Replay не было. Новые source r3 в worker не подменялись.
- SDK86 исправил повторный discovery legacy readiness-provider; install/ingress сохраняют
  принудительную проверку. Compile11+5 cc3 payloads errors0, повторное weapon review approve.
- Source48b27cf2 получил approve avatar-ik/network/gameplay/performance; weapon changes
  привели к SDK86. Позднейшие diff требуют нового финального ревью.
- Точные H/C готовы: H `bcdc4e62d`, C `836cb1d8d`, одна база50c27ae71, общие26 assets,
  pinned probe, diff48. Оба офлайн11 сборок +5 payloads errors0, без foreign fallback.
  Старый analyzer исключён из приёмки; launch review нового маршрута approve.
- Unity788 DONE/receive: input8dd49cc9, resultf4cf96341, paths0/unexpected0.
  Неизменённые применимые EditMode49/49; R08/Revolver G со смертью и respawn завершены,
  обе пары кистей сняты; exact Idle/CleanupPassed/profileRestored подтверждены.
  Полный архив425 файлов SHA256425/425 в `reports/native788/full-worker-artifacts/`;
  центральный97/97 — `reports/received788/`. [Разбор788](reports/native788/analysis.md):
  R08 Fired133/damage104, Revolver51/40; old/ghost/new одна session179,
  null/foreign/mismatch0, ghost shots/grabs0. Recorder пропускает первый выстрел
  до подписки, periodic shots — последний; raw Fired/trace и metrics не смешиваются.
  Startup warnings/OVR boundary error сохранены, runtime consoleErrors0 не означает
  чистоту полного лога. Все8 fit analysis_incomplete, пользовательская оценка ожидается.
  Spectator functional — ниже; Host/latejoin/abort, H/C perf и Quest ещё не проверены.
- Unity790 DONE/receive: inputaa3b75968, result8346840f5, paths0/unexpected0,
  архив28/28 SHA256. Два Spectator без тел, serverSessions10→9→10,
  Player8; P2 reconnect epoch2/session311, P3 сохраняет206. Трафик принят обоими.
  Найден дефект:12 получателей начального снимка не зарегистрированы на клиентах,
  relayErrors2/1 и server UxrComponentNotFound3. S8 остаётся закрыт.
  Все12 — Pin статических гранат ShootingRange; причина исследуется. MinPlayers9
  ещё не запускался. Cleanup/профили восстановлены, собственная probe-заявка удалена
  по exact исходному SHA256 после подтверждения только CRLF/LF-различия копии.
- Исправления после790: HeadlessPrecacheGuard регистрирует disabled scene-компоненты
  независимо от render precache; SDK87 оставляет derived proxy availability локальной
  с Replay, публичный IsGrabbable сохраняет Network|Replay. Compile11+6 errors0;
  network/manipulation/launch-infra approve записаны для candidate491bf8ec.
  Common H/C расширен26→29, cutover48 прежний. Exact H36d3f350/Caa0956c5:
  каждый11 сборок+5 payloads, errors0, без FOREIGN fallback; HEAD/index сохранены.
  Unity792 DONE/receive417c5d171 paths0: существующие EditMode40/40 PASS,
  P3 применил830 компонентов/32137Б, прежние12missing/3UniqueIdError отсутствуют.
  P2 завершился transport timeout10000ms и осталсяOffline; два клиента/reconnect
  не проверены. Cleanup Idle/CleanupPassed/requestCleared;20артефактов SHA25620/20
  сохранены в reports/native792 и reports/received792. Infra9258: причина таймаута
  не установлена, задержки редактора не доказывают причинность. Unity793 — Host/Player,
  отдельный подготовленный пакет прогрева/late rejoin. Старые manifests/progons
  не подтверждают новые исправления.
- Unity793 DONE/receive dcd567efb paths0,21артефакт SHA25621/21. Обычный Player
  переподключён тем же процессом: epoch1→2, session189/avatar190→191/192.
  Host сообщает Ready, но LocalAvatar/session отсутствуют, UsePrecaching=false.
  Причины baseline: Mirror ждёт onlineSceneLobby вместо загруженной TestMap1;
  guard принимает pendingHost за ServerOnly. Mirror SDK6 и guard исправлены в source;
  новый compile со свежим Mirror/новый runtime впереди. Это не регрессия Spectator.
- Unity794 DONE/receive f22ee6acb paths0,18артефактов SHA25618/18. MinPlayers9
  не удержал автостарт: TryGoLive пропускал override при пустом SelectedGameModeData
  и брал default1. Live разрешён до подключения Spectator; фильтр Player корректен.
  Расчёт explicit override исправлен в том же владельце, проверка нового source впереди.
- Новый technical C*=f8aaf9283:58owned/stagedLF, HEAD/index сохранены; Mirror6,
  Host guard и MinOverridefirst. Exact offline12сборок+5payloads errors0,
  freshMirror75sources, без FOREIGN fallback/mainMirrorDLL. Network/gameplay/
  launch-infra approve записаны для f8. Common30 H=d5933476e/C=e1d95e8c2
  упакованы: frozen81/cutover48/probe2, исходные blobs/modes сверены;
  отдельный exact H/C compile завершён: каждый12сборок+5payloads errors0,
  Mirror собран первым, foreign0. Это не Unity/Quest-приёмка.
- Unity795 DONE/receive180463621 paths0,21артефакт SHA25621/21. Inputf8:
  настоящий Host session189/avatar190, ClientReadytrue/SDK LocalAvatar,
  UsePrecachingtrue, завершённый latch, coroutine отсутствует, временных копий0.
  Обычный remote Player epoch1→2/session191→193/avatar192→194; Host сохранён.
  Два post-rejoin снимка через63.956с: transforms25790/avatars10/CameraFade1,
  контейнер пуст. Это стабильность числа объектов, не memory/Quest acceptance:
  mono вырос150626304Б, полный registry прерван caught MissingReference UxrLaserPointer.
  Probe runtime errors0/exceptions0; pre-probe OVR/UPM ошибки в полных логах сохранены.
  Cleanup Idle/CleanupPassed/requestCleared/profileRestored подтверждён.
  [Полный разбор и границы795](reports/native795/analysis.md).
- Unity796 DONE/receivef0761dd0b paths0: тот жеf8, MinPlayers9/8bots attempt4.
  Warmup131.360с,128.657с послеSpectator; Live/rounds/shots0, raw8/9 дважды.
  P2session189/avatar0 получил862компонента/32054Б; P3KCPtimeout10с/epoch0/Offline.
  Полный2client/reconnect не проверен. Старые12+3 ошибки не наблюдались;
  P2probe periodic, не final. Cleanup Idle/Passed/requestClear/profilesRestored,
  stale heartbeat не выдаётся за новый teardown снимок.
  Существующие40EditMode passed40/failed0/skipped0. Первый запрос auto-review
  отклонил; read-only сверка доказала прежние6классов/фильтры и только смену
  outputroot launcher; повтор разрешён и выполнен послеPlaycleanup.
  Worker18+test2 и central20 SHA256 совпали. [Разбор796](reports/native796/analysis.md).
- Quest+server Test buildf8 запущен штатным Build-Game после свежего preflight
 06:25:16Z в свободной dedicatedпаре; обаcheckout переведенынаf8/fullLFS.
  Завершилсяexit1 доAPK/server:49станций×2preflightошибки=98. ДисковыеIDвалидны;
  причинаbaselineMirror5:обнулениеsceneIdнаOpenPreviewScene. Полныйlog/manifest/
  preflight сохранены3SHA3 в reports/android-f8-build. Это не AndroidPASS.
- Unity797 DONE/e764d6e3c, durableIMPORTED14180: обаSpectatoravatar0,
  P2samePID/processsession epoch1/session206→epoch2/session318; P3сохранил207.
  Snapshots830/830/982; P2послеrejoin930liveincrements, P3total2090.
  Runtimeerrors/exceptions/relay0, старые12+3неповторились. Матч2scoredrounds0:2,
  третийCombat,78shots/50hits/18kills. Сохранена атрибуция bot-session;
  точный actor тела здесь не записывался. Per-shot GUID/ledger не является
  требованием действующих контрактов. В788 ActorSource проверен напрямую
  для104попаданийR08 и40Revolver; AK105 этим evidence не покрыт.
  Cleanup/профили восстановлены; старыеpeerheartbeat54.8снеживойснимок.
  Worker23 иcentral23 SHA256совпали. [Разбор797](reports/native797/analysis.md).
- SDK88/Mirror7 реализованы для двухподтверждённых blockers: managedreceipt
  регистрацииаватараочищаетсявactualOnDestroy; previewMirrorоставляетserializedID.
  Ассеты/порогвалидаторанеизменялись. Source4путивпланеr63; комментариииownedLF
  проверены. Технический checkpoint53e3a85: accepted=false,64ownedLF;
  network/gameplay/launch дали source APPROVE. Actual53:12игровыхсборок
  безошибок;7Hostpayloads отдельно скомпилированы с actualprobe. Старый
  cc3B0 несовместим с actualprobe (CS0123), его FAIL сохранён; H/C используют
  закреплённую совместимую probe. SDK88 destroy/rejoin runtime ещё предстоит.
  Common34/frozen85 упакованиофлайнскомпилирован, cutover48/probe2сохранены;
  SDK88 destroy/rejoin проверен ниже в798; свежиеH/C Combat/perf ещёнеполучены.
- SDK88/Mirror7 build53: Quest иServer GameBuilderPASS/errors0, UXRодинаковый
  e8e5c50b5d92312e. Aggregateexit1/provenanceRED: ошибкаразбораUTCвpwsh7/ru-RU;
  инфра9318 передана сопровождающему. APK805101200Б SHA3de9bf4e… текущий,
  ноUPMавтоматическисогласовалоднуlockзаписьMCPснеизменнымmanifestGitpin.
  [Полныйаудит](reports/android-sdk88-build/artifact-audit/Readme.md):8копийSHA8.
  Это build evidence, а не запускQuest/qualityacceptance.
- Unity798/input53: existingEditMode50 —48passed/0failed/2ignored; двеLobby
  fixtureотказалисьзакрыватьисходныенесохранённыесцены. Host189/190actualLocal,
  ClientReadytrue, precacheзавершён/dynamic0/контейнерпуст. OrdinaryPlayer2
  samePID266676/process6db9a474… epoch1/session191/avatar192→epoch2/193/194,
  Hostсохранился. РеестрUxrComponent6767→6518→6767→6767; дваpostrejoin
  снимкачерез74.389с, errors[]пусты. Managedmemory+90337280Б — неmemoryPASS.
  CleanupIdle/Passed/requestCleared/profilesRestored; staleheartbeat12.9с
  неfreshremoteabsence. Finish/receive011af75dbfcb2a09ae0cf64836bb65700c4c8782,
  durableIMPORTED14201: sourcepaths0/unexpected0; central23SHA23.
  [Полныйразбор798](reports/native798/analysis.md):13sourcehashes53 совпали,
  main/remoteprobeerrors0; UPM/IPC preprobe noise сохранён отдельно.
- Common34H8 Unity799: actualHost+8bots,seedдоspawn,precacheзавершён,
  GC/markersvalid. РеальныйCombatненачался:195сwatch Live/round0/Setup,
  hostPlayerteam0/inModeTeamfalse;8botsраспределены1/2. ШтатныйPlayerChoice
  ждётвыборакомандычеловеком (AllPlayersHaveModeTeam), неистёкшийAIтаймаут.
  Остановленсfinaltrue/completedfalse/0rounds/0shots/0hits, error0/warnings12.
  Idle/cleanupPassed/requestClear,14fullartifactsсохранены; finishидёт.
  ДляновойпопыткиготовитсяобычныйCmdRequestTeamChange(1,0), какпланшет:
  безforcedReady/телепорта/прямойзаписиTeamIndex/измененийигровыхправил.

## План

- [x] Найти WIP, проверить чужие worktree, контракты и accepted ancestry.
- [ ] Собрать необходимые source/authoring изменения в этой задаче.
- [ ] Проверить компиляцию и неизменённые применимые проверки.
- [ ] Выполнить свежие функциональные и perf/network прогоны через broker.
- [ ] Получить пользовательскую проверку качества, затем обновить тесты.
- [ ] Ревью зон, принятое вливание, активация binding r3 и закрытие задачи.

Новые/переписанные тесты до пользовательской проверки не создаются. Unity запускается
только в собственной аренде; чужие заявки не прерываются.

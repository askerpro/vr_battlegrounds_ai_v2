Обновлено 2026-10-15 (Codex recovery). Кандидаты вне хаба; порядок — приоритет. `tasks/bots-fix/` — в worktree владельца.

## Текущий статус

- Единственная зарегистрированная задача owner bots — `bots-fix`, revision52. clip-cutover RUNNING epoch3;
  pf-prototype/pf-equipment/pf-network MERGED; pilot-split/script-brain/pf-quest PLANNED.
- Ветка после возобновления6805 rebased на origin/dev97a320c88, HEADfe1bc9121. Проба матча и cc1-round1 сохранены;
  все свои правки восстановлены из точного stash64bbfad3 (резерв сохранён). Семь сборок и семь C#6 payload повторно проверены офлайн;
  B7 с явными test-фильтрами отдельно скомпилирован без ошибок.
  Финальная офлайн-проверка: 7 сборок без ошибок; ранний CS0117 исчез после обновления общей DLL.
- Проверялка cc1-round1 исправлена: отсутствие Move и неполные журналы не дают PASS;
  полная синтетическая fixture проходит, 24 ошибочные отвергнуты. Gameplay не изменялся.
- Исторические infra6691/6750 и CANCELLED630 сохранены в task changelog. Inbox6805 снял ночной STOP;
  FIFO и native MCP фактически отработали в644/656. GripPoseEncoder CS0117 после обновления DLL не воспроизвёлся.
- 644 DONE/receive: input2d652723f, Unity-контроллер получен, B0/B1 PASS; T1 disconnect, исход unknown.
- 656 DONE/receive: inputf24eb91e1, resultaa814d5ea paths0/unexpected0, B0/B1 PASS,
  targeted EditMode49/49 PASS (полный Result,8 нужных классов). p1 E/PE — NeedsReview, H прервана,
  p2 не запускался: CameraFade2→108→256 потребовал остановку. Playfalse/launchIdle/ownedfalse.
  Аренда656 завершена; отчёты reports/received656/ и reports/cc1-round1/native656-*.json.
- Native route epoch26: codex-app,01a13af7-2278-70a0-8070-8bbc42ec5300; свежая native listing проверена.
  Infra6970/6994/7039: native adapter/SessionState/delayCall симптомы переданы сопровождающему, SDK не менялся.
- Runtime CameraFade7035 передан uxr-precache-once: продолжение даже single-bot цепочки требует
  accepted guard либо explicit immutable grant с own dependency proofs. Замены входа646 не было.
- Exact SDK81 grant выдан7058; после исправления current scope/admission r51epoch3 нужен regrant7073.
  Аудит54 current paths/32 исключения/5 exact proofs; own_verified entries0. Сборщик A готов, stale grant отвергнут;
  native7073 отклонён auto-review, canonical запрос доставлен. Вопрос пользователю только о native уведомлении.
- Пользователь разрешил native7073: доставлен; новые accepted B498/39af учтены, HEADе4764a8a1 rebased0behind13ahead,
  stash6fe283466 сохранён. Grant3/consumer r52epoch3/B39af актуален; Afe95dfead создан отB, 55current/2immutable pins.
  Строгая664 DONE после recovery: исходный Refresh terminal, bots по7178 выполнил единственный recover,
  session32034 exit0; Rb624a8bcc сохранён, paths0/unexpected0/immutable_changed0, accepted=false.
  Worker вернулся наB39af, Git чист, blocked=false/recovery_required=false/active=null; Lobby/ready=true/compile_errors[].
  Receive требует ревью аварийного результата, force accept не было. B0..B8 не запускались; reports/cc1-round2-input/recovery664.md.
  Infra7142/7152/7154/7157 и terminal7193/7195 переданы maintainer,
  native route cleared; пользователь подтвердил ручного адресата «Ответить, кто ты» (agent-infra),
  уведомление7142/7154/7157 доставлено через send_message_to_thread. Policy7160 обработана;
  прямое поручение пользователя проверено в чате сопровождающего. Новые infra_issue + native уведомления без вопросов.
  Canonical7202 независимо подтвердил DONE и открыл очередь; R можно оставить AVAILABLE/accepted=false без force receive/ACK.
  Publication665 DONE/B54673ba4; branch9ee56ea37 rebased0behind13ahead, stashaa28b01c6 сохранён.
  Off-line7assemblies+9payloads errors0 повторно на новой базе; analyzer fixture/rejections PASS.
  Exact SDK81 grant4 наB54673ba4 получен7213/7214, consumerr52/epoch3 unchanged, expiry04:21:57МСК.
  Audit55current/34excluded/5proofs/2pins PASS, own_verified0; A52790c32 создан separateindex/HEADunchanged.
  666 DONE/receive, R3c344e1a7 paths0/unexpected0/immutable_changed0. B0/B1 PASS;
  T1 job78b1247567f341a683acf172d55267ce wrappertimeout/completed0 на том снимке, не assertion failure.
  По7237 native IPrebuildSetup ещё выполнялся, потом поздний RunStarted; фактический assertions outcome UNKNOWN.
  B6/B5/guard сообщили idle передfinish; поздний native старт этими flags не исключён. WorkerB546/Gitclean,
  brokeractive=null/blocked=false/recovery_required=false на post-finish snapshot. p1/p2 не запускались.
  Infra7229+native доставлены; vendor unity-mcp2 fix/регрессии в реализации, ещё не live. Заявки для обхода не подавать;
  validation после review/установки выполняет сопровождающий, игровые приёмки не меняются.
  Update7257: unity-mcp2 установлен, maintainer trueprebuild24,9s иFailedResult2 проверены; workerready/B546.
  669 DONE/receive, technicalA94f63a5e/treeffc475c8/B546/grant4, R08865a569 paths0/unexpected0/immutable_changed0.
  B0/B1 PASS, nativeIsRunActivefalse до/после; standardrun_tests потерялsession11381b03-fcb4-4b04-b8aa-beef4987caef доjob_id.
  SessionState currentempty/jobCount10 неизменен, незапуск не доказан, outcomeUNKNOWN. No directB7/retry/clear_stuck.
  Cleanup/finishexit0/receive=true; свояleaseосвобождена, p1/p2не запускались. Infra7305/7316 canonical+native.
  После release rebase наaccepted8eb877915: HEADf1905a35e0behind13ahead, stash76ba11b3a retained, ownLFbackupunique.
  Runtime VERSIONeb4ab1d981654f3bcd64cd4eb546cbcc950370d6. Old666UNKNOWN сохранён; новая игровая validation ещё не выполнялась.
  Policy7257: no self repair/bypass инфраструктуры; infra_issue+native сразу сIDs/VERSION/evidence, только назначенный maintainer чинит.
  Newhumanretry: grant5/B8eb/r52epoch3, A8f3ae332/tree66b8787c,55current/36excluded/5proofs/2pins/56delta.
  Admission failedmissing providerReadme, no ticket returned; infra7352/provider7354 canonical+native.
  Owner7357 восстановилownReadme/plan, grant5unchanged. Readonlysnapshot677records/key0matches.
  Maintainer7377 подтвердил key0/no-created-ticket и runtime-only p1/p2;679 DONE/receive после strict admission.
  R80b16a72a, paths0/unexpected0/immutable_changed0. B0/B1 PASS; native runner до Play=false.
  Обе очереди завершены:9/9 случаев, InvalidFixture0, ошибки/анимационные предупреждения/CameraFade0.
  E80/80, PE67/68; H/U/P/G pf-equipment PASS. Target height/root и proxy gates FAIL; все NeedsReview.
  Cleanup Playfalse/Idle/ownedfalse/bots0, finishexit0/receive=true; своей аренды нет.
  11 B8 snapshots:container0, память без экспоненциального роста; IsPrecaching=true/dictionary absent,
  поэтому SDK acceptance не заявлен. Evidence7436 canonical+native владельцу; отчёты received679/cc1-round5.
  Owner7439: consistent WAIT_LOCAL, не finished warmup; LocalAvatar не измерен, consumer SDK fix не нужен.
  Target audit679:5602rows/6initialbad/5596stable, каждый jump только rows0→1;
  все312Fired имеют stable target pose. Это окно готовности fixture, не объяснение PE67/68.
  T1API не запускался пока knownrace чинится; oldUNKNOWN неизменены. Разбор target/proxy trace делегирован read-only.
  Proxy audit679: actual Idle виден, request currentState initiallynull; точный Play timestamp не записан.
  T06/T08 содержат реальные отмены Move без displacement за0,242/0,282с; allMove FAIL сохранён.
  Captured stableNT растёт402/402,390/390,309/309, но fullcoverage отсутствует; fullgate не пересчитан.
  Причина navigation cancel UNKNOWN; clipfallback не обоснован. Таблица уточняет max только успешных Move.
  Worker_ready7457/publication7492: ownHEAD e812a2dfa rebased на acceptedbb280460, retainedstasha7921d04c.
  Grant6 получен7494/7497, consumer r52/epoch3 прежний; fresh55current/37excluded/5proofs/2pins.
  A40c4c0a2/tree85950aeb создан separateindex;687 DONE/receive для новой независимой T1, без Play/replayUNKNOWN.
  Resource-list32600→infra7495; один разрешённый native read восстановил resource listing, result7501.
  Передrun_tests завершить import/compile/discovery; fullterminalResult сохранитьдоfinish, counts изResult.summary.
  687 actual discovery8classes/49Runnable; standard job1c504f58a64f40b5b32dbda47eb3efb0 →49/49PASS, failed0/skipped0.
  Все49 details/coverage совпали сdiscovery; full terminal raw archived SHA70856d216bfaa599ccef35155f298ecb8afd8ae041a01048533edad9c03d28d8.
  R0f4ecfed2, paths0/unexpected0/immutable_changed0; cleanup runner/Play/compile/importfalse, finish/receive=true.
  T1 Console1 Error: UxrActor.Destroy вEditMode damage case, SilenceMirrorNoise глобальноignoreFailingMessages=true;
  не Consoleclean PASS, не Gameplay/SDK acceptance. Harness/SDK/test changes не выполнялись.
  Собственная lease освобождена, полныеотчёты received687/cc1-round6 иcc1-round6/native687-*.
  После resume собственный native route epoch29. По7614 общий runtime1432984/B5435 уже новее,
  но687 доказан именно наf6387f9/A40/Bbb; автоматического повторного T1 не требуется.
  PPK-tail679: последний Fired за1кадр/34,7748мс до закрытия probe, AK105 за14кадров/402,1682мс;
  SDK first update skip допускает censoring, identity missing shot UNKNOWN,67/68FAIL неизменён.
  Готового устойчивого stop-fire command нет; owner contract requested7648 weapon-system.
  DevTools7592/688139/141: scene/asmdef/BuildSettings invariant уже вB; release resolver исключает
  debug scene, automation включает явно. Boundary request7645 launch-infra/vr-test-stand, reply7647 performance.
  Два отчёта вreports/cc1-round6-input/{ppk-tail679,stand-boundary7592}.md; чужие assets/tests не менялись.
  Native recipient routes cleared/channel_mismatch, canonical доставка выполнена без native обхода; это не ACK.
  CLI selector/acceptance draft подготовлен contracts/cc1-t1-cli-selectors.json; production backend не запускался.
  Readonlydiagnosis664.md подтверждаетwaiting-journal placeholder и actual compilation, причина удержанияflag неизвестна.
- E/PE656: попадания10/22 и25/28, root цели скачок3,06м/height0–1,65м; время скачка не сохранено.
  B4 дополнен target trace, C#6 compile PASS; фактически выполнен в679, свежие трассы разбираются.
  Оружейная часть7046 получила bounded static review7176: SDK random spread для AK105/PPK не найден,
  atShot измерен до ExecuteRequestedShot и не доказывает commit/projectile orientation; raycast miss допускается.
  Причина непопаданий UNKNOWN; следующий ряд — gate collider → commit pose → projectile forward → collision по shot-id.
  Read-only дизайн reports/cc1-round3-input/shot-trace-design.md: per-component CommitShotSynced/Shoot дают commit/emission;
  exact projectile/collision public hooks отсутствуют, отдельный diagnostic endpoint request передан weapon-system.
  Owner7282 выбрал PARTIAL сexplicitattemptscope непосредственно вокруг TryToShootRound; exactprojectile/collision UNAVAILABLE,
  безSDKpatch/измененияnestedoptions/cadence/PASS. Подробный endpoint-review прочитан, implementationне выполнялась.
  Не скрывать начальную позу или неполное покрытие ради PASS.
- Сторож precache-server-guard в хабе PLANNED; обязательные M8 и late-client замеры ещё не выполнены.
  До принятой защиты не запускать ≥8 ботов. Исторические задачи клипового пути ниже не являются
  разрешением вернуть удалённый гибрид; ближайшая работа — cc-1 и пользовательская приёмка.

0. `npc-anim-architecture` — исследование перехода на готовые решения (Final IK, KINEMATION, Animation Rigging и др.) вместо самописной калибровки: опорная рука, крен, завал, отдача; решение пользователя 2026-10-11, pose-fit остановлен — `tasks/bots-fix/research/`.
1. `grip-fit` — support-кисть вне цевья, Rifle вверх (MEF 23.58°, Cyborg 36.25°), хваты Cyborg; отложено пользователем — `tasks/bots-fix/Readme.md` «Блокеры и следующие действия», `grip-contact-plan.md`.
2. `stand-coverage` — все входы/клипы, T02/T04/T09, V01/L01 Unsupported, эталоны поз, защита; выстрел NPC в Unity не подтверждён — `Docs/tasks/bot-combat-stand.md` «Наблюдения», `bots-validation-stand-plan.md` «Этапы реализации» 3–5.
3. `stand-launch-request` — содержимое стенда после ухода `BotCombatStandEditor` от записи EditorBuildSettings/Bootstrap/registry (механизм — launch-infra `stands-migration`), порядок с clip-cutover не согласован — `tasks/vr-test-stand/Details.md`.
4. `team-body-catalog` — Team1 только MEF Black, Team2 обычный MEF, Cyborg нет в TeamRegistry; с gameplay/avatar-ik — `tasks/bots-fix/diagnosis/mef-team-catalog-coverage.md`.
5. `bots-quest-perf` — CPU/GC Quest при N ботах, синхронная запечка NavMesh, нечитаемые MeshCollider в плеере; с performance — `Docs/tasks/T-48-bots-match.md` «Что не проверено (2026-10-01)».
6. `docs-sync` — после clip-cutover обновить прежний гибрид в `Docs/gameplay.md` «Матч с ботами (T-48)», `Docs/tasks/bots-blaze-integration.md`, строках ботов `Docs/game-manager.md`.
7. `low-crouch` — присед ботов после приёмки native legs (avatar-ik) — `Docs/tasks/bots-blaze-integration.md` «Приёмка».
8. `heavy-bot` — судьба Heavy, исключённого из клиповой схемы — `tasks/bots-fix/Details.md` «Цель и мотивация».

- `dev-hybrid-no-fire` — в origin/dev боты матча (гибрид BotGunner.FireRequestedShot) не стреляют из AK105/PPK и большинства стволов: профиль готовности ищется в WeaponData (там null), а он на WeaponSystem префаба; замер pf-round1/2 (база 0 выстрелов). Закрывается clip-cutover (переход матча на player-flow) или точечным фиксом гибрида по решению пользователя — `tasks/bots-fix/player-flow-plan.md`.

- `scripted-client-smoke` — смоук-тесты всей игры скриптовыми клиентами (решение-идея пользователя 2026-10-12): Blaze заменяется детерминированным скриптом намерений (тот же BotInputFrame), исполнитель — локальный аватар в процессе клиента через ввод на уровне устройств UXR (кандидат xr-input vr-test-stand, зона avatar-ik), сетевой путь как у живого игрока; сценарии ярус C (раунды/фазы/экономика/урон/смерть/призрак/счёт/конец матча/поздний вход/reconnect), затем шлем. Зоны: bots (скрипт, слой намерений/наведения), avatar-ik (xr-input), launch-infra (E2E-каркас), gameplay (что проверять). Слой А — матч ботами Blaze + наблюдатели — после clip-cutover — `tasks/bots-fix/player-flow-plan.md`.

- `purchase-rubber-arms` — при покупке ствола руки бота «резиновые»: вытягиваются далеко к стволу (сторона арсенала), берут его и возвращаются (демо pf-network demo-2, 2026-10-13, наблюдение пользователя; сценарии P/LH). Задуманно покупка появляется у кисти (разовая начальная поза до SpawnServerObject, manipulation 4259). Гипотезы: у клиента ствол сначала спавнится в позе префаба/арсенала, а хват копии наблюдателя (KeepGripsInPlace) тянет кисть к нему до прихода позы; либо на сервере начальная поза не применяется до хвата. Проверить: зазор кисть↔ствол у клиентов и на сервере в момент покупки (BotStandNetObserver RightGapMm, серверный pf-probe), видео/кадры; зоны bots/manipulation/network — `tasks/bots-fix/reports/net-round1/runtime/demo-2/`.

- `stand-shot-cost-case` — случай стенда для weapon-system shot-pipeline-budget (6191): один бот player-flow на ПК-сервере, AK105 в руке, прицел в точку стены, N=90 выстрелов с темпом R через штатный путь (BotGunner → WeaponSystem), шлем-клиент наблюдает; после вливания clip-cutover cc-1; B2 (сценарий стреляет сам) отклонён — второй стрелок.

## Обещания другим агентам (на 2026-10-14)

- manipulation6898: worker641 сообщил 5 прежних AvatarLoadoutTests.Teleport floor failures:
  BotCombatStand/Floor layer0, у пяти аватаров ValidTargetLayers mask8. Сцена не менялась в manipulation;
  проверить авторинг пола/генератор в отдельном scoped исправлении бот-стенда. Это не результат T1 текущего cc1-round1.
- avatar-ik6902: новый непринятый input patch81 — 6a0fde1e0ad9771075cd47e047fe319f30bc84a7 на97a320c88,
  старый b00 исторический; worker646 DONE:2976 tests processed/failed, полный Result недоступен, capped25.
  Patch81 не принят; M8/late-join и повтор cc1 после CameraFade требуют отдельного immutable input.
- weapon-system6986: synthetic capture648 PASS; реальный shot-cost driver ждёт принятого cc-1/API/dependency proof.

- network 6324/6330: после вливания сторожа precache-server-guard (f645d72f9) — регрессия «матч 8 ботов на сервере с графикой» (память ровная, childCount UxrManager и копии прогрева не растут, строка [HeadlessPrecacheGuard] в логе).
- avatar-ik 6344/6348: замер «до» SDK-патча прогрева — поздний клиент к серверу с 8 ботами (childCount UxrManager, *(Clone) прогрева, IsPrecaching, PrecachingStarting, память; контроль UsePrecaching=false), результат avatar-ik и network.
- network 6187/6192: проверить PPK_mag@MagAnchor у опоздавшего в ближайшем сетевом прогоне (наблюдатель пересобирает якоря на каждом проходе).
- weapon-system 6191/6197/6211/6217: драйвер shot-cost (вариант A, лобби, BotGunner → WeaponSystem, N=90, R=10/с) — после вливания cc-1; нужно подтверждение gameplay, что Lobby допускает сессию/тело бота.
- infra 6208/6465: сообщены зависание worker (тикет 600) и остановленная очередь брокера (606) — ответа нет.

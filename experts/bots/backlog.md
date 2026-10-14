Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет. `tasks/bots-fix/` — в worktree владельца.

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

- network 6324/6330: после вливания сторожа precache-server-guard (f645d72f9) — регрессия «матч 8 ботов на сервере с графикой» (память ровная, childCount UxrManager и копии прогрева не растут, строка [HeadlessPrecacheGuard] в логе).
- avatar-ik 6344/6348: замер «до» SDK-патча прогрева — поздний клиент к серверу с 8 ботами (childCount UxrManager, *(Clone) прогрева, IsPrecaching, PrecachingStarting, память; контроль UsePrecaching=false), результат avatar-ik и network.
- network 6187/6192: проверить PPK_mag@MagAnchor у опоздавшего в ближайшем сетевом прогоне (наблюдатель пересобирает якоря на каждом проходе).
- weapon-system 6191/6197/6211/6217: драйвер shot-cost (вариант A, лобби, BotGunner → WeaponSystem, N=90, R=10/с) — после вливания cc-1; нужно подтверждение gameplay, что Lobby допускает сессию/тело бота.
- infra 6208/6465: сообщены зависание worker (тикет 600) и остановленная очередь брокера (606) — ответа нет.

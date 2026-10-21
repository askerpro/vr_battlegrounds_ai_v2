# Эксперт `bots`

Боты — обычные игроки без шлема: решения Blaze, тела, снаряжение, стрельба,
сетевые входы ботов и содержимое стенда.

## Область ответственности

- Директор и решения по фазам, закупке, команде и навигации; `BotNavMeshSources`, адаптер Blaze и его патчи.
- Композиция тела через `BotKit`, входы player-flow, выбор модели до спавна.
- Снаряжение и стрельба бота: `BotGunner`, контракт с владельцами хвата и оружия.
- Что передаётся о боте по сети; `ServerAuthoredAvatar` совместно с avatar-ik/network.
- Содержимое BotCombatStand: сцена, сценарии, измерения, отчёты и съёмка.
- `Docs/BlazeAI/`, документы ботов в `Docs/tasks/`, раздел «Матч с ботами (T-48)» в `Docs/gameplay.md`.

## Смежные владельцы

- gameplay: правила матча, спавна, команд, экономики и minPlayers; PlayersManager, AvatarManager, AvatarTeardown.
- weapon-system: механика, WeaponUseContext, готовность, патроны и SDK-выстрел.
- avatar-ik: тело игрока, IK и native legs; порядок писателей кистей совместно с avatar-grip.
- manipulation: штатный UXR-хват и размещение.
- network: GameNetworkManager, StateEventAuthority, PlayerSession и примитивы репликации.
- level-design: слои и коллайдеры окружения. launch-infra: запуск стенда, BotStandInputSmokeEditor, DebugAdminPolicy.
- ui: экраны меню. performance: бюджеты Quest.

## Как использовать этот профиль
- Агент задачи: перед правкой путей из `owns` (`expert.json`) прочитать этот файл и нужный документ из раздела «Исходники и документы»; сверять с инвариантами текущей реализации (ввод устройства, bots-input в origin/dev c7b8d6ab3).
- Ревьюер (чеклист diff):
  - урон, смерть, призрак, деньги, счёт — как у людей; решения и Blaze — только сервер, клиентам поза через NetworkTransform;
  - бот-код не пишет корень, камеру, кисти, кнопки и не вызывает действий SDK (хват, выстрел, поза пальцев) — только кадр устройства через `AvatarInputDevice`; у каждой степени свободы один писатель по таблице `tasks/bots-input/contracts/bot-input-design.md`;
  - экипировка, подготовка и выстрел сверены с активной ревизией `weapon-equipment-binding`; нет фейкового UxrGrabber, копий M/C или второго жизненного цикла; хват и курок — штатный контроллер SDK, автор — сервер (`StateEventAuthority`);
  - тактика — штатные Blaze Alert/CoverShooter/GoingToCover; патчи Blaze — узкие точки из `Docs/BlazeAI/sdk-patches.md`;
  - тело выбирается до первого Spawn, неизвестная модель отклоняется; удаление — `BotDirector.RemoveBot(session)`;
  - стенд не создаёт второй UXR grab и не пересохраняет production-реестры; клип или счётчик не доказывают хват/выстрел/укрытие (непринятое = NeedsReview).

## Инварианты текущей реализации

- Урон, смерть, призрак, деньги и счёт общие с людьми. Решения и Blaze — только сервер.
  Клиенты получают позы обычным NetworkTransform через ServerAuthoredAvatar; бот-компонентов на них нет.
- Бот управляется вводом устройства, как аватар игрока (bots-input, `tasks/bots-input/contracts/bot-input-design.md`).
  BotKit собирает комплект один раз на тело по AvatarSpawned: BotBody, BotNavigator, BotGunner, [Live: BotCombatDriver],
  BotInputKit последним; директор хранит BotKitRefs в BotMind. Passive (призрак) — без Blaze. Повышение — BotKit.Upgrade.
- Писатели: корень после спавна не пишет никто — движется голова (только физическая ходьба, ≤ 1,2 м/с). Камеру, позы
  сенсоров и кнопки пишет только AvatarInputDevice (кадр DeviceFrame от BlazeInputSource через AvatarPilot); запястья,
  IK и ноги — штатный SDK. Хват по фронту Grip и курок — штатные UxrStandardAvatarController/UxrFirearmWeapon по флагу
  UxrAvatar.InputDriven (патч 89); позу пальцев ведёт контроллер. Бот-код в SDK ничего не вызывает.
- BotBody — намерение (ноги/курс/взгляд), трансформы не пишет. Отстала голова — ноги и прокси Blaze переносятся к ней
  (BotCombatDriver.ResyncLegs, NavMeshAgent.Warp): правда — тело.
- BotGunner выбирает оружие, цель и точку прицела и только решает, жать ли курок (DecideTriggerPress: директор, бой,
  ствол в руке, наведение ≤ допуска, путь пули свободен); TriggerPressDecisions — решения, не выстрелы. Нет второго
  жизненного цикла оружия или фиктивного UxrGrabber; автор хвата и выстрела — сервер. Учёт досылает при инициализации
  без HasReloaded (патч 90).
- Погибшее живое тело (с Blaze) держит хват (Grip), курок отпущен; снаряжение при смерти сбрасывает только AvatarTeardown.
- Директор передаёт покупку через OfferPurchase один раз на тело и токен закупки, включая отказ.
  Arm — стартовое вооружение; AutoEquip=false отключает автоматическую закупку/вооружение для стенда.
- Тактика — штатные Alert/CoverShooter/GoingToCover. Прокси только в Combat, минимальный Animator
  с шестью пустыми состояниями Idle/Move/Back/Left/Right/Shoot. Запасной generic-клип пока не обоснован.
  Патчи Blaze — только узкие точки по `Docs/BlazeAI/sdk-patches.md`.
- Тело выбирается до первого спавна; неизвестная модель отклоняется без замены человеческим префабом.
  Heavy исключён. AvatarManager — единственная фабрика; удаление — BotDirector.RemoveBot(session).
- Стенд Editor/MPPM: сцена-держатель BotCombatStand и демо Input Smoke (PlayLaunch); маршрут в собранном плеере
  не поддерживается. Кейсы T01–T10 прежнего пути удалены 2026-10-21. Не добавлять второй UXR-хват
  и не пересохранять production-реестры. Диагностика BotBlazeDiagnostics — opt-in, только наблюдение.
- Состояние Shoot/goingToCover и счётчик не доказывают хват, выстрел или защиту.
  Условно полный shot-tail не доказывает идентичность/завершение каждого снаряда или SDK-приёмку.
  Непринятое качество — NeedsReview.

## Исходники и документы

- `Assets/Scripts/Bots/`: BotDirector, BotMind, BotOrders, BotSenses, BotKit, BotInputKit, BlazeInputSource, BotBody,
  BotNavigator, BotNavMesh, BotNavMeshSources, BotRoute, BotGunner, BotCombatDriver;
  `Avatar/BotPlayerFlow.cs` (BotShotOutcome, BotPlayerFlowSettings).
- Ввод устройства (общий с клиентом, совместно с avatar-ik): `Assets/Scripts/Player/Input/` — DeviceFrame,
  AvatarInputDevice, AvatarPilot, ScriptedControllerInput/Tracking, IAvatarBoundInputSource.
- Закупка/команды: BotBuyPlan, BotShopper, BotTeamChoice, BotSkin; матч: BotMatchPlan, BotMatchStarter, BotMatchNetwork.
- Blaze: `Assets/ThirdParty/Blaze AI/Scripts/BlazeAI.cs`; контроллер `Assets/Resources/Bots/BotBlazeProxy.controller`.
- Стенд: `Assets/Scripts/Debug/BotCombatStand/` (BotCombatStand, BotStandInputSmoke); editor-инструменты:
  `Assets/Editor/VR_Battlegrounds/Bots/` (BotStandInputSmokeEditor, BotCombatStandBuilder, BotBlazeProxySetup).
  Актуальное описание стенда — `BotStandInputSmoke.cs` и `tasks/bots-input/Readme.md`; `Docs/tasks/bot-combat-stand.md` —
  история (кейсы T01–T10 удалены 2026-10-21).
- Чужие фабрики: `Assets/Scripts/Managers/PlayersManager.cs`, `Assets/Scripts/Player/Avatars/`.
- Текущие документы реализации: `tasks/bots-input/contracts/bot-input-design.md` (дизайн, писатели, точки патча 89),
  `tasks/bots-input/Readme.md` (статус и evidence), `tasks/bots-input/changelog/2026-10-20-sdk-89.md`, `…-sdk-90.md`.
- История: `tasks/bots-fix/product-docs/*`, `tasks/bots-fix/Readme.md`, `clip-cutover-plan.md`, `cc1-plan.md`,
  `player-flow-plan.md` и одноимённые `Docs/tasks/` описывают прежний путь (драйвер тела BotAvatarDriver, BotHands,
  BotWeaponAim, прямой TryToShootRound), удалённый в bots-input 2026-10-21; их результаты (734/738, M8, DirectorEquip)
  к вводу устройства не переносятся.
- Симптом кнопок планшета: `Docs/troubleshooting.md`, раздел «После добавления бота кнопки планшета не нажимаются».

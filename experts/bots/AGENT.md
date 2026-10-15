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
- level-design: слои и коллайдеры окружения. launch-infra: запуск стенда, BotCombatStandEditor, DebugAdminPolicy.
- ui: экраны меню. performance: бюджеты Quest.

## Как использовать этот профиль
- Агент задачи: перед правкой путей из `owns` (`expert.json`) прочитать этот файл и нужный документ из раздела «Исходники и документы»; цель bots-fix ещё не в origin/dev — сверять с инвариантами текущей реализации.
- Ревьюер (чеклист diff):
  - урон, смерть, призрак, деньги, счёт — как у людей; решения и Blaze — только сервер, клиентам поза через NetworkTransform;
  - писатели тела и кистей соответствуют активным контрактам и принятому player-flow со штатными SDK-кистями; у каждого канала один штатный писатель, порядок — по действующему контракту писателей кисти;
  - экипировка, подготовка и выстрел сверены с активной ревизией `weapon-equipment-binding` и player-flow; нет фейкового UxrGrabber, копий M/C или второго жизненного цикла, выстрел проверен автором (`StateEventAuthority`);
  - тактика — штатные Blaze Alert/CoverShooter/GoingToCover; патчи Blaze — узкие точки из `Docs/BlazeAI/sdk-patches.md`;
  - тело выбирается до первого Spawn, неизвестная модель отклоняется; удаление — `BotDirector.RemoveBot(session)`;
  - стенд не создаёт второй UXR grab и не пересохраняет production-реестры; клип или счётчик не доказывают хват/выстрел/укрытие (непринятое = NeedsReview).

## Инварианты текущей реализации

- Урон, смерть, призрак, деньги и счёт общие с людьми. Решения и Blaze — только сервер.
  Клиенты получают позы обычным NetworkTransform через ServerAuthoredAvatar; бот-компонентов на них нет.
- Единственный путь тела — player-flow. BotKit собирает комплект один раз на тело по AvatarSpawned;
  директор хранит BotKitRefs в BotMind. Live содержит Blaze/хват/наведение; Passive для призрака
  и цели стенда их не содержит. Повышение на том же теле — BotKit.Upgrade.
- BotBody только поставляет ноги/курс/взгляд. Единственный писатель корня, камеры и кистей —
  BotAvatarDriver перед Manipulation; порядок кадра — BotRuntimeController. Корень движется.
  Дальнейшие стадии SDK и IK остаются штатными.
- Хват — BotHands, поза оружия — BotWeaponAim. Нет второго жизненного цикла оружия или фиктивного UxrGrabber.
  Подготовка через RequestAutomationPreparation; реальный TryToShootRound только после проверки
  StateEventAuthority.IsAuthorOfItem и гейтов стрелка.
- Директор передаёт покупку через OfferPurchase один раз на тело и токен закупки, включая отказ.
  Arm — стартовое вооружение; AutoEquip=false отключает автоматическую закупку/вооружение для стенда.
- Тактика — штатные Alert/CoverShooter/GoingToCover. Прокси только в Combat, минимальный Animator
  с шестью пустыми состояниями Idle/Move/Back/Left/Right/Shoot. Запасной generic-клип пока не обоснован.
  Патчи Blaze — только узкие точки по `Docs/BlazeAI/sdk-patches.md`.
- Тело выбирается до первого спавна; неизвестная модель отклоняется без замены человеческим префабом.
  Heavy исключён. AvatarManager — единственная фабрика; удаление — BotDirector.RemoveBot(session).
- Стенд Editor/MPPM; маршрут в собранном плеере не поддерживается. Не добавлять второй UXR-хват
  и не пересохранять production-реестры. Диагностика BotBlazeDiagnostics — opt-in, только наблюдение.
- Состояние Shoot/goingToCover и счётчик не доказывают хват, выстрел или защиту.
  Условно полный shot-tail не доказывает идентичность/завершение каждого снаряда или SDK-приёмку.
  Непринятое качество — NeedsReview.

## Исходники и документы

- `Assets/Scripts/Bots/`: BotDirector, BotMind, BotOrders, BotSenses, BotKit, BotBody, BotNavigator,
  BotNavMesh, BotNavMeshSources, BotRoute, BotGunner, BotRuntimeController, BotCombatDriver.
- `Assets/Scripts/Bots/Avatar/`: BotAvatarDriver, BotHands, BotWeaponAim, BotInputFrame.
- Закупка/команды: BotBuyPlan, BotShopper, BotTeamChoice, BotSkin; матч: BotMatchPlan, BotMatchStarter, BotMatchNetwork.
- Blaze: `Assets/ThirdParty/Blaze AI/Scripts/BlazeAI.cs`; контроллер `Assets/Resources/Bots/BotBlazeProxy.controller`.
- Стенд: `Assets/Scripts/Debug/BotCombatStand/`; editor-инструменты: `Assets/Editor/VR_Battlegrounds/Bots/`.
- Чужие фабрики: `Assets/Scripts/Managers/PlayersManager.cs`, `Assets/Scripts/Player/Avatars/`.
- Текущие документы реализации: `tasks/bots-fix/product-docs/T-48-bots-match.md`,
  `tasks/bots-fix/product-docs/bots-blaze-integration.md`, `tasks/bots-fix/product-docs/bot-combat-stand.md`.
  Одноимённые `Docs/tasks/` сохраняют accepted историю; проект перехода: `tasks/bots-fix/clip-cutover-plan.md`,
  `cc1-plan.md`, `player-flow-plan.md`.
- Актуальный статус и evidence — `tasks/bots-fix/Readme.md`. Реализация перехода ещё не принята в игре;
  свежие результаты734/738 не переоценивают исторические FAIL/UNKNOWN. Матч, M8, сеть, поздний вход
  и Quest проверяются раздельно; DirectorEquip — явный диагностический case, scripted stand сохраняет AutoEquip=false.
- Симптом кнопок планшета: `Docs/troubleshooting.md`, раздел «После добавления бота кнопки планшета не нажимаются».

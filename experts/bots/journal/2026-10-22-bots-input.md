# bots-input: бот на вводе устройства, этап 2 эпика влит, 2026-10-22

Бот на сервере управляется вводом устройства, как аватар игрока: Blaze (`BlazeInputSource`) через общий пилот
`AvatarPilot` даёт кадр шлема и контроллеров, `AvatarInputDevice` подаёт его в аватар, ниже ввода — код человека
(флаг `UxrAvatar.InputDriven`, патч 89). Итерации приняты пользователем 2026-10-20/21, влито в dev c7b8d6ab3.

Для зоны:
- Прежний путь удалён: BotAvatarDriver, BotHands, BotWeaponAim, BotInputFrame, BotRuntimeController, прямой
  TryToShootRound, кейсы стенда T01–T10. Стенд — сцена-держатель и демо Input Smoke.
- Корень не пишет никто, только физическая ходьба головой; отстала голова — ноги и прокси переносятся
  (`BotCombatDriver.ResyncLegs`, `NavMeshAgent.Warp`). Grip без хвата разжимается и сжимается снова (`GripWithRetry`).
- `BotGunner` только решает, жать ли курок; `TriggerPressDecisions` — решения, не выстрелы. Категория оружия
  кешируется на выбранный ствол.
- Погибшее живое тело держит Grip при отпущенном курке: снаряжение при смерти сбрасывает только `AvatarTeardown`
  (смерть событием синхронизации SDK откладывает сброс до LateUpdate — отпущенный Grip ронял бы оружие раньше).
- Дальше: сведение пилота и стрелка со скриптовым клиентом и `ScenarioInputSource` после series (e2e-combat);
  перф на Quest-хосте (IK ботов без троттлинга, геттер `ControllerInput`).

Материалы: tasks/bots-input/Readme.md, contracts/bot-input-design.md, changelog/2026-10-20-sdk-89.md, -sdk-90.md.

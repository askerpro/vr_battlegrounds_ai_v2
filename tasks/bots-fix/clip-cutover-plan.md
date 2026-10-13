# Этап clip-cutover: матч ботов на новой архитектуре, удаление гибрида (проект, 2026-10-13)

Проект — архитектор (только чтение, HEAD 36b99f366 на origin/dev a1040a95f). Решения пользователя: player-flow —
единственная архитектура ботов; «от простого к сложному»; подготовка — automation, ручная процедура позже.

## Главные находки

1. Blaze-прокси без Animator не ходит (`[RequireComponent(Animator)]`, BlazeAI.cs:13; движение ждёт
   normalizedTime анимации, BlazeAI.cs:881-893, 1277). Минимум: голый GameObject + BlazeAI + один generic-контроллер
   `Resources/Bots/BotBlazeProxy.controller` (Idle/Move/Back/Left/Right/Shoot), без humanoid, копии locomotionRig,
   BotCombatAssets и клипов Mixamo. Растёт ли normalizedTime у состояния без клипа — A/B на стенде; запасной — 1-с
   generic-клип.
2. Контрольная цель стенда стоит прямо только благодаря гибридному BotBody → ей нужен пассивный комплект драйвера.
3. BotStandRecorder.SourceHashes открывает BotPistol/BotRifle.controller — после удаления упадёт.
4. BotAuthorityTests ищет `_firearm.SetAmmoLeft(` (только в гибридном FireRequestedShot) — сторож править в cc-1.
5. Директор на стенде сменит PPK на бесплатный ствол → `BotAddOptions.AutoEquip` (у стенда false).
6. Покупка в матчах: `OfferPurchase` — единственный вход; один оффер на пару (тело, закупка) — `PurchaseOfferedBody`.
7. В dev боты матча сейчас не стреляют — cutover матч только улучшает.
8. Гейты: manipulation 5016 (кобура у клиентов/поздний вход) и network 5510 (храповик NTU) — до вливания cutover.

## Удаляется

Целиком: BotCombatAssets.cs(+.asset), BotPistol/BotRifle.controller, BotStandHandMotionRecorder, BotStandIdleMotionProbe,
BotCombatSetup (или переписать в генератор BotBlazeProxy). Внутри: enum BotBodyMode, AddBot(mode), mind.BodyMode,
гибрид BotBody (запись головы/кистей в StageUpdated, SetWeaponHands, клиповая поза, PoseDrivenExternally — BotBody
становится источником ног/курса/взгляда, ~313→110 строк), гибрид BotGunner (StageUpdated-выстрел, GrabObject из
стрелка, FireRequestedShot/SetAmmoLeft/WeaponReadinessController, PoseRightHand, Sees, BodyOf, гибридный IsHolding),
BotCombatDriver (ассеты, копия locomotionRig, PoseAnimator, вне-боевой прокси), связь BotAvatarDriver↔BotBody,
SetPlayerFlow, опция стенда PlayerFlow. Подробные строки — отчёт архитектора (сессия 2026-10-13).

## Как матч переходит

AddBot() → CreateBotSession(..., Calibration) → SpawnAvatar → AvatarSpawned → `BotKit.Attach(avatar, passive)`
(один раз на тело, ссылки в BotMind): BotBody, BotNavigator, BotGunner, BotCombatDriver (живой), BotAvatarDriver,
BotHands+BotWeaponAim (живой), BotRuntimeController.SetKit. Призрак и цель стенда — пассивный комплект. Покупки:
`OfferPurchase` единственный вход + `Arm()`; Elimination — закупка по раундам, Respawn — бесплатно. Blaze-прокси —
только в Combat.

## Подэтапы

| # | Что |
|---|---|
| cc-0 | База гибрида + `BotStandMatchProbe` (матч 1/3/8(+10) на ПК: кадр, маркеры, GC; S8 со зрителями) — [cc0-spec.md](cc0-spec.md) |
| cc-1 | Перевод матча и удаление гибрида (атомарно), минимальный прокси, стенд, сторож BotAuthorityTests |
| cc-2 | Покупки: OfferPurchase, Arm(), BotAddOptions.AutoEquip, PurchaseOfferedBody, case DirectorEquip |
| cc-3 | Замеры стенда и матча (M1/M3/M8(+M10) ПК-хост, S8 ServerOnly+2 зрителя), ревью avatar-ik/performance, демо |
| cc-4 | Документы (T-48, blaze-integration, combat-stand, раздел gameplay.md T-48), binding r3, Readme/plan |
| cc-5 | После приёмки: EditMode-тесты; удаление bots-fix-clip-archive и parked/pose-fit |

Критерии: стенд — эталоны 100%, 5 пунктов pf-equipment, DirectorEquip (один ствол), цель стоит, 0 ошибок, комплект
один раз; матч Elimination ≥4 раундов — фазы, закупки сходятся с экономикой, стрельба при видимом враге, смерть →
призрак → возрождение, конец матча, 0 ошибок/исключений/Relay; ПК-сервер 8 ботов p95 не хуже базы cc-0, GC не
растёт с N; S8 байт/с на бота ≤ удалённого человека.

Риски: минимальный аниматор Blaze; сервер при 9 ботах (FindObjectsByType в TryHomePoint, GetComponent в Think,
GetComponentInParent в Target — кешировать); пик конца раунда (9 призраков); корень теперь движется (трафик, прыжки
ног); MaxTeamSize=4 — только автодобор BotMatchPlan; база 8 (4×4), предел 10 (5×5) — ответ gameplay 6030.

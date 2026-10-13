# План подэтапа cc-1 (clip-cutover): матч на player-flow, удаление гибрида

2026-10-13. Архитектор (только чтение, HEAD b4f06199a = origin/dev 5761bb36b по коду). Проект этапа —
[clip-cutover-plan.md](clip-cutover-plan.md); база — [cc0-spec.md](cc0-spec.md). Строки — по HEAD, пути от `Assets/`.

## Решения (главный агент bots, 2026-10-13)

- Прокси Blaze — генератор `BotBlazeProxySetup` (из `BotCombatSetup.cs`), ассет `Resources/Bots/BotBlazeProxy.controller`
  коммитится (UnityEditor.Animations в плеере нет).
- Поле стенда `PlayerFlow` удаляется полностью; старые пакеты `tools/*` не компилируются — новые `tools/cc1-*`.
- Масштаб прокси — как сейчас (`lossyScale`), поведение Blaze не меняем в этом подэтапе.
- Запасной 1-с клип в состояниях прокси — только если A/B провалит пустые состояния (коммит cc-1d).
- Документы (`Docs/README.md`, `experts/bots/AGENT.md`, `combat-stand.md`) — в cc-4.
- Сторож `BotAuthorityTests` правится в cc-1 — решение пользователя 2026-10-14 (исключение из правила «тесты после
  приёмки»): автор — до `RequestAutomationPreparation` и `TryToShootRound`, `SetAmmoLeft` нет.
- Боты засчитываются в `minPlayersToStart` (EliminationMode.IsPlayersReady → PlayersManager.Sessions, бот — сессия);
  матч из 1 бота не стартует по правилу (минимум 2 и обе команды не пусты) — подтверждено замером cc-0 M1/M3.

## 1. Удаляется

Файлы: `Scripts/Bots/BotCombatAssets.cs` (+`Resources/Bots/BotCombatAssets.asset`), `Resources/Bots/BotPistol.controller`,
`BotRifle.controller`, `Editor/VR_Battlegrounds/Bots/BotStandHandMotionRecorder.cs`, `BotStandIdleMotionProbe.cs`
(+ .meta). `BotCombatSetup.cs` → генератор прокси.

Внутри:
- `Bots/Avatar/BotPlayerFlow.cs`: enum `BotBodyMode` (5–18); остаются `BotShotOutcome`, `BotShotSample`, `BotPlayerFlowSettings`.
- `BotBody.cs` 313 → ~110: `EyeHeight` (37), поля головы/кистей (43–45, 51–55, 62–68), `PoseDrivenExternally` (71–79),
  `TryGetWeaponPose` (91–101), `SetWeaponHands` (104–115), `RightHand` (118), `Set/ClearRightHandPose` (157–169),
  подписка `StageUpdated` (177–185) и `OnStageUpdated` (193–224), захват кистей в `PrepareFrame` (235–241),
  `TryGetRestHands` (264–273), `CaptureHands`/`Place(Quaternion,…)` (290–311). `SetCombatPoseSource` →
  `SetCombatLegsSource(Transform)`. Остаются Feet/Yaw/BodyRotation/Place/WalkTo/StopWalking/LookAt/PrepareFrame/
  HeadRotation/Turn; BotBody ничего не пишет в трансформы.
- `BotGunner.cs` 783 → ~560: поля хвата (90–93), `_playerFlow`/`SetPlayerFlow` (103, 115–127), условия `_playerFlow`
  (139, 215, 413, 483, 532, 552 → безусловно), `OnEnable/OnDisable/OnStageUpdated` (241–277), гибридный `IsHolding`
  (333–335 → только `HeldByOwnHand()`), `_body.ClearRightHandPose()` (355), хват из стрелка в `TryTakeWeapon` (386–397),
  гибридный прицел (494–497), `FireRequestedShot/BodyOf/PoseRightHand/Sees` (655–768, с `SetAmmoLeft` и
  `WeaponReadinessController`), `MuzzleOf` (776–781).
- `BotAvatarDriver.cs`: связь с BotBody (31, 35 RequireComponent, 41, 77, 92–93, 100).
- `BotRuntimeController.cs`: комментарий 16–19; `SetGhostKit` (151–158) → `internal SetKit(bool passive)`;
  `_gunner.SetPlayerFlow` (178, 185).
- `BotMind.cs`: `BodyMode/LastBody/PlayerFlowBody/PlayerFlowGhostKit` (15–26) → `BotKitRefs Kit` + кеш точки базы.
- `BotDirector.cs`: один `AddBot()` с `BotPlayerFlowSettings.Calibration` (110–141); `Think` по `mind.Kit` (229–243);
  кеш `TryHomePoint` (289–305, FindObjectsByType только при смене сцены/команды); `EnsureBody` (381–421) → `AttachKit`.
- `BotCombatDriver.cs`: `_assets` (32), `PoseAnimator` (41), вне Combat — `RemoveProxy()` (59–65, 94–100),
  `CreateProxy` (103–142) по п.3, кеш `Target` (37–38) и `UxrAvatar` в фильтре (232), `SetCombatLegsSource` (253, 276).

Не трогаем: BotHands, BotWeaponAim, BotArmRestGeometry, BotHandTrajectory, BotInputFrame, BotNavigator, BotSenses,
BotShopper, BotOrders, BotNetwork, BotMatchStarter; покупки (`OfferPurchase`, `AutoEquip`) — cc-2.

## 2. Сборка бота

`AddBot()` → `CreateBotSession(name, token, Calibration)` (PlayersManager.cs:208, без правки) → `AvatarSpawned`
(AvatarManager.cs:168/521) → `BotDirector.OnAvatarSpawned` → `AttachKit` → `BotKit.Attach(avatar, BotKitKind.Live|Passive)`
→ `BotKitRefs` (Avatar, Body, Legs, Gunner, Combat, Driver, Hands, Aim, Runtime, Passive). Новый публичный
`Scripts/Bots/BotKit.cs`. Порядок AddComponent: BotBody, BotNavigator, BotGunner, BotAvatarDriver, [живой: BotCombatDriver,
BotHands, BotWeaponAim], BotRuntimeController последним + `SetKit(passive)`.
Один раз: ссылки только в `BotMind.Kit` (пишет директор); `ReferenceEquals(mind.Kit?.Avatar, avatar)` → no-op (двойной
AvatarSpawned при вложенном ReplaceBody, AvatarManager.cs:507–521); повышение пассивный → живой — `BotKit.Upgrade`;
`Place(mind.Feet, mind.Yaw)` после Attach (та же сцена); в `Think` — только страховка с одним предупреждением;
без GetComponent в тике. Цель стенда — `BotKit.Attach(target, Passive)`.

## 3. Минимальный прокси Blaze

BlazeAI требует Animator/NavMeshAgent/CapsuleCollider/AudioSource (13–16, добавятся сами); движение ждёт
`normalizedTime >= animT/2` текущего состояния слоя 0 (881–893, 1277); `AnimationManager.Play` без состояния — warning и
нет CrossFade. Humanoid/кости не нужны (ragdoll 2850 не используется), root motion off. Контроллер: 6 состояний слоя 0
`Idle` (default)/`Move`/`Back`/`Left`/`Right`/`Shoot`, без motion. `CreateProxy`: GameObject неактивный → позиция/курс,
layer 2, DontSave → `AddComponent<BlazeAI>` → кешированный `Resources.Load` контроллера, `applyRootMotion=false`,
`cullingMode=AlwaysAnimate` → агент/капсула/поведения как 145–251 → `SetActive(true)`.
A/B на стенде (T01, T06, T08): A — пустые состояния, B — 1-с generic-клип с константной кривой на несуществующем пути;
мерить время `Play("Move")` → смещение агента (≤ 0,2 с; первый Move от Idle сразу после создания), путь ног против
pf-round5/eq-round3, normalizedTime, 0 предупреждений «doesn't exist». Риск — пик создания 8 прокси в первом кадре Combat.

## 4. Стенд

- `BotStandRunContracts.cs`: поле `PlayerFlow` (21–25) удалить. `BotStandEquipmentScript.cs`: проверка 47–49.
  `BotCombatStand.cs`:138 — без `options.PlayerFlow`.
- `BotStandPlayerFlowProbe.cs`: параметр/поле (72, 196–199), `botBodyWritesPose` (312), `["mode"]` (981) = "PlayerFlow",
  комментарии 1060/1101 → «с базой cc-0 / pf-round».
- `BotStandSession.cs`: 42–43, 54 → `director.AddBot()`; цель — `CreateBotSession(..., Calibration)` (66) и
  `BotKit.Attach(target.ActiveAvatar, Passive)` (70); `Place` (111–117) без AddComponent BotBody (иначе InvalidFixture).
- `BotStandRecorder.cs`: 67 `driver.PoseAnimator` → `driver.Brain?.anim` (+ currentState); `SourceHashes` (161) —
  `BotBlazeProxy.controller`, `BotKit.cs`, `BotDirector.cs`; проверить ключ снимков (≥5 изображений).

## 5. Тесты

`Tests/EditMode/Bots/BotAuthorityTests.cs:21–27` — см. «Открыто». Предлагаемый сторож: в `ExecuteRequestedShot`
`IsAuthorOfItem(_firearm)` до `RequestAutomationPreparation(` и до `_firearm.TryToShootRound(`; `SetAmmoLeft(` нет.
`DevTools/BotTests.cs:162` — зелёный. Новые (cc-5): комплект один раз, повышение пассивный → живой, BotBody не пишет.

## 6. Коммиты, проверки, риски

cc-1a — BotKit/BotKitRefs, директор (AddBot, AttachKit, Think по Kit, кеш TryHomePoint), стенд (AddBot, цель с
калибровкой и пассивным комплектом, Place). cc-1b — удаление гибрида (BotBody, BotGunner, драйвер↔BotBody, BotBodyMode,
SetPlayerFlow, опция стенда, параметр пробы, сторож теста). cc-1c — прокси (генератор + ассет, BotCombatDriver), удаление
BotCombatAssets/BotPistol/BotRifle/двух проб, SourceHashes/рекордер. cc-1d — запасной клип (условно). Вливание одним мержем.
Офлайн: компиляция срезов; grep пусто `BotBodyMode|PoseAnimator|BotCombatAssets|SetPlayerFlow|SetWeaponHands|FireRequestedShot`.
Unity (аренда): компиляция 0 ошибок, EditMode Bots + DevTools.BotTests.
Стенд (`tools/cc1-round*`): E/PE 100% (AK105/PPK, ZeroAiAimSpread+NoRecoil), 5 пунктов pf-equipment, по 1 компоненту на
тело (у призрака нет Hands/Aim/CombatDriver), цель стоит (камера 1,65 ± 2 см, корень неподвижен), A/B прокси, 0 ошибок.
Матч (BotStandMatchProbe): Elimination ≥ 4 раундов, M8 хост и S8 со зрителями; фазы, Arm, стрельба > 0, смерть → призрак
→ возрождение, конец матча, 0 ошибок/исключений/Relay; p95 не хуже cc-0, GC не растёт с N.
Риски сервера 8–10 ботов: TryHomePoint (295), GetComponent в Think (231–235), Target/фильтр (37–38, 232), IsEnemy (258),
ChooseTeam (329–330, два списка за тик), пик конца раунда (8 призраков Attach+прокси), движущийся корень (трафик NT).
pilot-split: BotKit — единственная точка композиции; новых вызовов SDK в BotRuntimeController/BotGunner/BotBody нет;
BotInputFrame — граница; типы Blaze только в BotCombatDriver; `AddBot()` расширяем под `BotAddOptions` (cc-2).
Ревью зон до вливания: avatar-ik (один писатель, пассивный комплект, движущийся корень), manipulation (хват 8 ботов,
TakeHolstered, Teardown), gameplay (поток Elimination), network (S8 байт/с), performance (p95/GC), weapon-system
(уведомить: BotGunner больше не зовёт WeaponReadinessController — WeaponReadinessController.cs:8/18, WeaponSystem.cs:409).

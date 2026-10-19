# bots-control-modes — подробности

## Инвентаризация кода ботов (2026-10-19, HEAD a62039a26, только чтение)

### Слои плана control-modes против текущего кода

- L1 «мозг»: размазан — `BotDirector.Think` (BotDirector.cs:323-424) + чистая `BotOrders.Decide`, `BotShopper`,
  `BotTeamChoice`, `BotSenses`; в бою Blaze (`BotCombatDriver`, shootEvent → `BotGunner.RequestShot` :219-220),
  `BotBlazeDiagnosticBrain`. `IBotBrain` нет.
- L2 намерения: `BotIntent` нет; вызовы `legs.GoTo/Stop`, `gunner.Arm/OfferPurchase/StandDown`,
  `bot.ServerSetReady`, `BotBody.LookAt`. `BotGunner` смешивает решение (цель, разброс, гейт луча), пилот и
  исполнение (спавн покупки, SDK-выстрел).
- L3 пилот: `BotRuntimeController.BuildFrame/BuildHands` (:256-476), `BotWeaponAim`, `BotHandTrajectory`,
  `BotArmRestGeometry`, `BotNavigator`/`BotRoute`/`BotNavMesh`, `BotBody`. Выход `BotInputFrame` — семантические
  флаги (GrabPrimary/GrabSupport/Release/Stow/Trigger) и мировые позы костей, не кнопки. `frame.Trigger` (:287)
  исполнитель не читает — выстрел идёт `_gunner.ShotRequested` → `ExecuteRequestedShot` (:232-240).
- L4 исполнитель (сервер): `BotAvatarDriver` (позы, `CanDrive` :72), `BotHands` (хват),
  `BotGunner.ExecuteRequestedShot/SpawnPurchase`. `IBotExecutor` и исполнителя ввода клиента нет.

### Где бот обходит путь клиента

| # | Правило | Бот сейчас | Человек |
|---|---|---|---|
| 1 | Покупка | `BotShopper.Buy/TryPay` → `ServerTryPurchase(bot, choice, 0)` (BotShopper.cs:58,74); ствол спавнится у кисти `BotGunner.SpawnPurchase` (:335-353); в Respawn бесплатно где угодно (BotShopper.cs:41-45) | хват со своей стены → `ItemTakenServer` → `ArsenalCheckout.HandleItemTaken` (:78-112: CanTrade, ArsenalPurchaseRules.Checkout, owner/taker, отмена) → `ServerTryPurchase(owner, w, netId)` |
| 2 | Стена | `ArsenalOwnershipPolicy.ServerAssign` (:60-77) закрепляет стены за ботами, бот ими не пользуется | — (влияние на людей не проверено) |
| 3 | Готовность | `bot.ServerSetReady(true)` (BotDirector.cs:422-423), жетон не берётся | жетон → `CmdSetDogTagGrabbed` + `CmdSetReady` (ArsenalWallController.cs:1095-1096) |
| 4 | Команда | `SessionTeamAssigner.Apply(force:true)` каждый тик, игнорирует `TeamChoiceLocked` (BotDirector.cs:498-527); `BotMatchStarter.AssignHumans` автобалансит людей и вызывает `referee.GoLive()` напрямую (:175, :211-221) | `CmdRequestTeamChange` → `TeamChangeRequests.ServerPlayerRequest` + `TeamChangeRules.CanPlayerChoose` |
| 5 | Спавн/карта | точка общая; тело после смены карты — директор каждый тик `SpawnAvatar(null,null,bot)` (BotDirector.cs:308-311,541-546); сессия без conn (`PlayersManager.CreateBotSession` :218-242), вне handshake `MapLoader.ConnectionsSettled` (:229-236) | `OnServerReady` → `ChangeAvatar` (GameNetworkManager.cs:126-139) |
| 6 | Смерть/призрак | бот-призрак — пассивный комплект + `BotBody.Place` (BotDirector.cs:575-590); `BotGunner.OnDestroy` сам уничтожает ствол (:229-241) | `AvatarTeardown.Confiscate` |
| 7 | Раунд/minPlayers | общий `PlayerRoster` — без обхода; готовность — только п.3 | — |
| 8 | Подготовка оружия | `RequestAutomationPreparation` (BotGunner.cs:520, требует IsWorldAuthority) | руками: магазин, затвор |
| 9 | Хват | кисть ставится на снап (`AlignPrimaryToGrabber`, BotGunner.cs:369-381) + `UxrGrabManager.GrabObject` (BotHands.cs:203-257) | grip → `UxrStandardAvatarController` → TryGrab ближайшего |
| 10 | Огонь | `TryToShootRound(0)` (BotGunner.cs:529) со своим гейтом луча | trigger → SDK |
| 11 | Перемещение | корень пишется каждый кадр (BotAvatarDriver.cs:162), голова local XZ=0, кости кистей мирово (:188) | корень стоит, движутся камера/контроллеры в пространстве трекинга (решение 2026-10-12: подмена tracked device) |
| 12 | Ветки по conn | AvatarManager.cs:141/487 (`ServerAuthoredAvatar`), PlayerSession.InitialCalibrationReady :132, StateEventAuthority.DefaultIsAuthoredHere :190, PlayerController.RpcDevTeleport :494; `IsBot` в игровой логике нет | — |

Прямые SDK-вызовы: `UxrGrabManager` — BotHands.cs:138,147-152,189,214,246,257,282,291,368; BotGunner.cs:235,283,
316,320; BotRuntimeController.cs:396. `TryToShootRound` BotGunner.cs:529; `RequestAutomationPreparation` :520;
`UpdateExternalHandPoseFromGrabState` (SDK patch 84) BotAvatarDriver.cs:142; `CanBePlacedOnAnchor`
BotRuntimeController.cs:471. `MoveAvatarTo` боты не вызывают.

Остатки клиповых тел: `IWeaponEquipmentSocketOwner` (реализаций нет), `WeaponEquipmentBinding` на AK105/PPK
(добавляет WeaponSystemAuthoring.cs:422-426), ветки `WeaponUseKind.Equipment` (WeaponUseContext.cs:45-61,
WeaponSystem.cs:453/601, WeaponContextSensor.cs:45-47); комментарии cc-0/cc-1 в BotKit/BotAimMath/стенде.

Скриптовый ввод клиента: есть только `Assets/Scripts/Debug/E2E/E2EHeadPose.cs` — голова, только плеер Quest
(`VRB_XR_OCULUS && !UNITY_EDITOR`, `-e2eHeadPose`). `IXrInputSource`, `ParticipantInputs`, `-e2eInput`,
наследников `UxrControllerInput/UxrControllerTracking` нет; в SDK есть `UxrDummyControllerInput`.

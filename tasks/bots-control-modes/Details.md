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

## input-research: avatar-ik (2026-10-19, только чтение)

Вердикт: ввод в аватар бота тем же путём устройств UXR реализуем с патчами SDK; `AvatarMode.Local` боту нельзя
(`LocalAvatar` = первый Local, UxrAvatar.cs:144; сеттер включает камеру/AudioListener :567-585; UxrMirrorAvatar
переписывает режим :84,107; 22 файла игры опираются на `LocalAvatar`).

- Уже по аватару: `EnabledControllerInputs`/`TrackingDevices` (UxrAvatar.cs:317,328), UxrAvatarController.cs:158-197,
  UxrControllerTracking без статики (патч 54), сенсоры в пространстве корня (UxrControllerTracking.cs:77-98,259-272).
- Только для Local: стадия Update (UxrManager.cs:1366, 2453-2461), `AvatarUsingTracking` localOnly (:1999), полная
  Animation (:2070-2081); `ControllerInput` при UpdateExternally — external/Dummy (UxrAvatar.cs:247-251).
- Патчи SDK (флаг `InputDriven` при UpdateExternally, не новое значение enum): UxrManager.cs:1366,1999,2070;
  UxrAvatar.cs:247; UxrGrabManager.cs:412; UxrTeleportLocomotionBase.cs:483; UxrFirearmWeapon.cs:474-487 (курок из
  `LocalAvatarInput` — нужно ли, не проверено).
- Код игры: `BotControllerInput : UxrControllerInput`, `BotControllerTracking : UxrControllerTracking`, голова —
  local pose камеры; из BotAvatarDriver убрать запись корня/камеры/запястий и патч 84; калибровка HeightOffset только
  при ownAvatar (AvatarCalibrationApplier.cs:181-187) — ветка «автор — сервер» или FloorOffset=0. Ноги берут скорость
  головы (UxrLegLocomotion.cs:208).
- Условие avatar-ik 1 ослабить до «ровно один источник на аватар»; в клиентском процессе с xr-input без изменений.
- Остаточные отличия UpdateExternally: IK-троттлинг (UxrStandardAvatarController.Custom.cs:199), HitReaction
  (HitReaction.cs:79), render-оптимизатор; UI пальцем, UxrHandPoseVolume (:90) — только LocalAvatar.
- Сеть: NT бота сейчас ServerToClient (ServerAuthoredAvatar.cs:24-30); в tracking space форма данных как у человека.
- Перф Quest-хоста: стадия Update UxrStandardAvatarController на каждого бота — не замерено, нужен стресс-замер.
- Запасной первый шаг: BotAvatarDriver пишет в tracking space (корень стоит), кнопки пока серверным API.

## input-research: network (2026-10-19, только чтение)

Вердикт: модель «тот же сервер, другой транспорт» совместима с правилами авторитета; фиктивных соединений не делать.

- Mirror: `[Command]` на сервере без клиента не выполняется → бот зовёт серверный метод. `[Server]` не ставить
  (PlayerSession.cs:801-808). Шаблон: `Cmd*(args)` → `Server*(args)` с полной проверкой; одна точка отправки
  `PlayerSession.Request*` (`isServer && connectionToClient == null` → Server*, иначе Cmd*).
- Уже разделены: CmdRequestCalibration → ServerAcceptCalibration (:389-401); CmdSetReady → ServerSetReady (:833, но
  это запись без проверок); CmdAdmin* → TeamChangeRequests.ServerAdmin*/AdminMapCommands.
- Разделить: CmdSetDogTagGrabbed (:725-729); CmdRequestTeamChange (:638-656) → ServerRequestTeamChange;
  CartridgeIntake.CmdRequestCartridgeAdmission — право по `connectionToClient == sender` (:113,166), без клиента
  запрос отменяется (:154) — бот на выделенном сервере патрон не вставит (в рантайме не проверено).
- Жетон: `HandleTagGrabbed` отсекает по `player.isOwned` (ArsenalWallController.cs:1086) → `IsAuthoredHere(avatar)`.
- Авторство: `IsAuthor = ownedHere || (isServer && !hasOwnerConnection)` (StateEventAuthority.cs:106-108,190);
  события сервера — `RpcComponentStateChanged` origin 0, применяют все клиенты (NetworkStateRelay.cs:263-266), без
  серверного admission (:304-317). Поздний вход: хваты в снимке SaveStateChanges (:551-556). Риск — смена автора
  при передаче бота клиенту.
- Смена карты: фиктивное соединение вредно (ShouldSend отбросит события, NT ClientToServer заморозит тело, спавн
  объектов боту, ConnectionsSettled, таймауты, discovery). Рекомендация — одна функция тела
  `RestoreAvatarAfterSceneChange` через ChangeAvatar: для соединений из OnServerReady (GameNetworkManager.cs:126-139),
  для сессий без соединения из OnServerSceneChanged (:90); убрать тик директора и `SpawnAvatar(null,null,bot)`.
- Поза: наборы трансформов бота и человека совпадают (корень, камера, кисти), запись корня драйвером снять. Кнопки не
  реплицируются — реплицируются следствия; значит кнопки бота разбирает на сервере тот же конвейер UXR.
- Риски по приоритету: право по sender/соединению (CartridgeIntake); обходы TryToShootRound/SessionTeamAssigner/
  ServerSetReady; две ветки тела после смены карты; `isOwned` вместо `IsAuthoredHere` (жетон, остальные не искались);
  один Local-аватар UXR; запись корня; смена автора; неизбежные развилки по connectionToClient (PlayerSession.cs:132,
  WallPassMonitor.cs:100, AvatarManager.cs:141/487) собрать в одну точку.

## input-research: manipulation (2026-10-19, только чтение)

- Хват по вводу у UpdateExternally-аватара сейчас не сработает: стадия Update (UpdateInputDevice/UpdateTrackingDevices,
  IsGrabbing — UxrStandardAvatarController.cs:605-678) только для Local (UxrManager.cs:1366, 2452-2458); Manipulation
  для всех (:2034), но TryGrab/TryRelease по фронту IsGrabbing (:1068-1083), у внешнего всегда false.
- Local-гейты в манипуляции: UxrGrabManager.cs:412, Manipulation.cs:2093 (AlignToController), :2563 (дальний хват),
  :1686-1720 (сопротивление). Ввод проекта только Local: MagazineEjectInput.cs:44; курок SDK из LocalAvatarInput
  (UxrFirearmWeapon.cs:474-485) — зона weapon-system.
- Правила проекта от режима не зависят: PlayerGrabManager.cs:41 CanGrabDelegate, GhostGrabRule (:70), GrabRules.cs:13-55,
  применяются в GetClosestGrabbableObject (Querying.cs:52,141). PlayerGrabManager на префабе бота — не проверено.
- Покупка: Anchor.Removed на каждой машине → ArsenalSlotController.cs:501-509 → ArsenalWallController.cs:1110-1112
  (isServer) → ItemTakenServer → ArsenalCheckout.cs:77-110; покупатель через PlayerController.Session
  (ArsenalGrabRule.cs:58-63); проверок соединения нет — хват бота сработает как у человека, если у бота есть Session,
  он владелец стены (ArsenalOwnershipPolicy.cs:67-72, TeamStates.Sessions — боты там? не проверено) и CanTrade.
  BotShopper с netId 0 ломает возврат денег.
- Жетон: DogTagController.cs:169-185 срабатывает и на сервере, но HandleTagGrabbed выходит на !isOwned
  (ArsenalWallController.cs:1086) → ветка «аватар авторится здесь» → серверный метод сессии.
- R4: TryGrab берёт ближайшую разрешённую точку (Manipulation.cs:1293-1322); у внешнего аватара BodyIK в PostProcess
  после Manipulation (:704) — кость по прошлому кадру (вывод, не проверено). Меры: держать позу кадр до нажатия;
  перед нажатием read-only GetClosestGrabbableObject — расхождение логировать как баг досягаемости/правил.
- Рекомендация: SDK-флаг «внешний аватар с вводом на этой машине» (Update — UpdateInputDevice + IsGrabbing без
  локомоции/трекинга; ControllerInput — ввод бота; гейты AlignToController/дальнего хвата/курка на этот признак).
  Альтернатива без SDK — свой исполнитель grip → TryGrab, дублирует логику контроллера.

## input-research: launch-infra (2026-10-19, только чтение)

- Editor/MPPM: адресная `run-e2e` уже есть — PlayModeStandParticipant.Execute (:177-196) → E2ERunner.Begin, роль
  client-N по индексу; топология с собственным сервером, AutoGoLive=false, BotCount=0, без host; сценарий —
  StandRequest.Scenario (StandProtocol.cs:23). Ввод участника — `ParticipantInputs[]` рядом с AdditionalPlayerRoles[]
  (PlayLaunchConfiguration.cs:19; Validate :49, Copy :38), применять в PlayModeStandParticipant.Sync (:113-121) через
  PlayLaunch.ResolveParticipantConfiguration (:182). Второй IPC-канал не заводить. Пересечение с незакоммиченными
  правками bots-validation в обоих файлах.
- Плееры: Run-E2E.ps1 раздаёт файлы по ролям (:322-342); скрипт — `-Script` → `-e2eArg script=e2e/<token>/<role>/script.json`
  + `-e2eInput scripted`; разбор в E2EContext.cs:363-385 по образцу `-e2eHeadPose` (:423).
- PauseOnFocusLoss=false уже по умолчанию (PlayModeStandParticipant.cs:113, PlayLaunch.cs:286). XR-лоадер Standalone
  включён (XRGeneralSettingsPerBuildTarget.asset:30,46), в MPPM-клонах не проверено → ввод на уровне UXR (свои
  UxrTrackingDevice/UxrControllerInput, образец UxrDummyControllerInput). E2EHeadPose отказывает в редакторе
  (:150-155), на сервере/headless (:165).
- MPPM 2.0.2: ≤4 игроков на Editor → сервер + 3 клиента; больше — плееры (Run-E2E -Clients до 1024). Headless-клиент
  со скриптовым вводом — позже (не проверено).
- Вердикт: есть E2EResult (E2EResult.cs:20-159), барьер ctx.Step/ServerStep (E2EContext.cs:112-116, E2EBarrier.cs,
  ≤512 шагов), агрегация Run-E2E (summary.json, код 0/1/2), в Editor `Temp/VRBattlegroundsTestStand/<run>/e2e-<participant>.json`.
  Нет: интерпретатора JSON-шагов (один IE2EScenario «scripted-client», регистрация E2ERunner.cs:420-438), серверного
  оракула, сводки по участникам для Editor.
- Брокер: checkpoint → request → claim → begin → guard → компиляция → PlayLaunch Plan/Play/Status → run-e2e → Cancel
  → finish (Docs/test-stand.md:93-115); итерация — минуты + очередь → один пакет на аренду (компиляция, S+1, S+2, JSON).
- Минимальная веха: S+1, затем S+2 в MPPM; клиент позой головы/запястий подходит к стене, grip R → предмет в руке
  штатным UxrGrabManager; оракул: слот пуст, владелец — клиент, IsBot=false; второй клиент видит предмет; критерии
  LI-1: голова ±1 см/0,1°, запястья ±1 мм, 0 UxrComponentNotFound. Выстрел — следующим шагом. Плеер — вторым шагом.

## Итог input-research (2026-10-19)

- Клиенты (первыми): скриптовый клиент — Local-аватар, ввод через свою реализацию UXR-устройства (голова, два
  контроллера, кнопки/оси) — тот же конвейер UxrStandardAvatarController, что у шлема; SDK-патчей не нужно; курок SDK
  читает LocalAvatarInput — у Local-клиента это и есть наш ввод. Запуск — ParticipantInputs[] + run-e2e в MPPM,
  `-e2eInput scripted` в плеерах; интерпретатор JSON-шагов поверх E2E-барьера; серверный оракул.
- Боты (следом): avatar-ik и manipulation независимо рекомендуют SDK-флаг «внешний аватар с вводом на этой машине»
  (UpdateExternally + свой ввод: стадия Update — UpdateInputDevice/IsGrabbing, ControllerInput — ввод бота, Local-гейты
  AlignToController/дальний хват/курок/перенос хвата на этот признак) — 5 точек патча SDK. Тогда у бота ровно код
  человека ниже ввода. Остаточные отличия UpdateExternally (IK-троттлинг, HitReaction, render-оптимизатор) —
  презентационные. Перф N ботов на Quest-хосте — замерить.
- Общие серверные обработчики (network): шаблон Cmd* → Server* + одна точка отправки `Request*`; разделить
  CmdSetDogTagGrabbed, CmdRequestTeamChange, CartridgeIntake (право по соединению → по держателю); `isOwned` →
  `IsAuthoredHere` в HandleTagGrabbed; тело после смены карты — одна функция для соединений и сессий без соединения;
  фиктивных соединений не делать. Покупка со стены по хвату уже не зависит от соединения (manipulation).

## xr-input: реализация вехи 1 (2026-10-19, не проверено в Unity)

- Рантайм `Assets/Scripts/Debug/E2E/Input/`: DeviceFrame/IAvatarInputSource, ScriptedInputGate, ScriptedControllerInput,
  ScriptedControllerTracking (всегда компилируются), E2EScriptedInput, ScriptedPilot, InputScript, ScriptedClientRunner
  (`#if !VRBG_NO_E2E`); сценарии `Scripts/input-smoke.json`, `Scripts/wall-purchase.json`.
- Сценарий `Assets/Scripts/Debug/E2E/Scenarios/ScriptedClientScenario.cs` (`scripted-input-smoke`, `scripted-wall-purchase`),
  регистрация в E2ERunner.
- Editor: `Assets/Editor/VR_Battlegrounds/Testing/ScriptedInputPrefabSetup.cs` — меню
  «Tools/VR Battlegrounds/Testing/Скриптовый ввод в префабах аватаров» кладёт выключенный объект ScriptedInput в
  PlayerBase и GhostAvatar (сенсоры Quest 3).
- Unity MCP: прокси сессии запускается из cwd сессии (`.mcp.json` → `Tools/agents/unity_mcp_proxy.py`, агент = worktree
  скрипта). Сессия, запущенная в bots-validation, переведена в worktree задачи до первой аренды.

## xr-input: прогон 1 в worker (2026-10-19, ticket 841, вход ae2c3b3e4)

- Компиляция чистая (0 ошибок консоли). `ScriptedInputPrefabSetup.Setup()`: ScriptedInput под `Components` в PlayerBase и
  GhostAvatar, сенсоры SensorLeft/RightMetaTouchQuest3, компоненты выключены; варианты (CyborgHandsBase,
  PlayerControllersCyborgAvatar, MEF_cyborg_hands_Player(+Black), PlayerBase_NonSdkHands) наследуют, UXR дописал служебные
  `__isInPrefab/__prefabGuid`. Префабы `Unused/*` Unity пересохранил с посторонними полями рига — не взяты; результат
  принят вручную из ref R c3574c93b + `ack` (receive отказал: пути вне output roots).
- S+1 `scripted-input-smoke` (TestMap2, run d97d2d65): activate, ходьба головой 2 м + поворот 90° + 1 м — PASS; сервер
  видит голову клиента в 1,823 м от корня — PASS; запястья ≤ 0,003 мм / 1,8°; 0 UxrComponentNotFound. FAIL measure-check
  головы 5 см / 8,7° — ошибка замера (сравнение с позой пилота, обновлённой после записи кадра; ≈ один кадр при ~20 fps
  клона), исправлено: замер против применённого кадра.
- S+2 `scripted-wall-purchase` (run d69b7570): команды 1/2 выбраны клиентами через CmdRequestTeamChange — PASS; GoLive
  эквивалентом кнопки — PASS; призраки дошли головой до своих зон и возродились, фаза Equipment за 5,7 с — PASS; у каждого
  своя стена (2 и 3) — PASS; client-2 дошёл до стены (2,1 м); остановлено тем же ложным measure-check до хвата.
- Логи: `tasks/bots-control-modes/reports/smoke-1/`, `reports/purchase-1/` (вне Git).

## xr-input: прогон 2 (2026-10-19, ticket 843 → 845, вход a30d6a7c1)

- Ticket 843: MCP `refresh_unity` не вернул ответа 1800 с (мост MCP устарел после domain reload, editor/state stale),
  аренда истекла → RECOVERY_REQUIRED. Ответ worker на pending refresh найден (ok, ready, без Play/dirty) — владелец
  выполнил `recover --force` (DONE). Сопровождающему отправлены два infra_issue (нативного маршрута нет — client_mismatch).
  В 845 компиляцию сделал сам `begin`, `refresh_unity` не вызывался.
- S+1 `scripted-input-smoke` (run cbffe534): 11/11 клиент, 2/2 сервер. Голова ≤ 0 см / 0°, запястья ≤ 0,003 мм / 1,9°,
  223 кадра; сервер видит голову клиента в 1,822 м от корня; 0 UxrComponentNotFound / NRE.
- S+2 `scripted-wall-purchase` (run 7ad1722d): 16/16 у каждого клиента, 6/6 сервер. Команды 1/2 выбраны клиентами,
  GoLive, призраки → свои зоны → возрождение, Equipment за 5,7 с, свои стены (4 и 2), путь 2,2–4,3 м головой;
  хват Viper (200) по Grip Right; касса: −200 «WK-11 Viper», 800→600, чек есть, слот пуст, держит клиент с соединением;
  каждый клиент видит оружие в руке другого (0,057 м до руки). Запястья ≤ 0,017 мм, угол запястья до 7,6° (критерия
  нет; вероятно скручивание IK — на ревью avatar-ik). 0 UxrComponentNotFound / NRE.
- R прогона 2 — только пересохранённые Unity `Unused/*` (не взяты, `ack`). Логи: `reports/smoke-2/`, `reports/purchase-2/`.

## Тесты вехи 1 (2026-10-20, ticket 848/850)

- `Assets/Tests/EditMode/DevTools/ScriptedInputTests.cs`: 11/11 (встроенные сценарии и известные op, отказ на
  неизвестную op и отсутствующий файл, кадр покоя, закрытый ScriptedInputGate, регистрация сценариев, состав префабов).
- Полный EditMode на ветке: 2824 passed / 35 failed / 9 skipped из 2868. Тот же набор 12 классов на чистой origin/dev
  5a066921c: те же 35 красных поимённо (только в задаче — 0, только в базе — 0). Красные — карты/блокаут, эмбиент
  BotCombatStand, GrabPoseCoverage, выстрел в WeaponSpread/RemoteShotReplay/Shotgun/Kinemation/ShotSound, Ledger, сканер
  StateSnapshotCoverage — к задаче не относятся. XML: `reports/editmode-full-1/`, `reports/editmode-baseline-1/`.

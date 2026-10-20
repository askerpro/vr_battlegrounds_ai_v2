# bots-epic — подробности

2026-10-20. Сводка материалов для этапов эпика. Первоисточники — по ссылкам; при расхождении верен первоисточник
и текущий код.

## Синхронизация с e2e-regression (2026-10-20, хаб 10212 → 10216)

- Перевод ботов на флоу клиентов (bot-input, rules-parity, match-smoke) — в этом эпике (зона bots).
  Этап 9 e2e-regression «Боты в регрессии» — потребитель: тот же JSON-сценарий клиентов подаётся в аватар бота,
  зависит от этапа bot-input. e2e-regression ссылается на этот эпик как на исполнителя.
- e2e-regression не трогает `Assets/Scripts/Bots` до этапа 9; незакоммиченных правок нет.
- Блок C bots-validation сокращён до того, что переживёт перевод (бой, урон, смерть/возрождение, реестр SDK);
  покупку, готовность и выбор команды проверяют уже на флоу клиента (`scripted-wall-purchase`, этап 2 e2e — жетон).
- Параллельные исполнители e2e-regression: e2e-combat (`Assets/Scripts/Debug/E2E/Input/**`, `Scenarios/Scripted*`,
  `E2ERunner.cs`), e2e-heavy-editor (`Tools/e2e/**`, `Assets/Editor/VR_Battlegrounds/Testing/**`).

## Обходы правил ботом

Полная таблица с номерами строк — [bots-control-modes/Details.md](../bots-control-modes/Details.md)
(«Где бот обходит путь клиента», HEAD a62039a26). Кратко:

| # | Правило | Бот сейчас | Человек |
|---|---|---|---|
| 1 | Покупка | `BotShopper` → `MatchEconomy.ServerTryPurchase(bot, w, 0)`, ствол спавнит `BotGunner.SpawnPurchase` у кисти; в Respawn бесплатно | хват со своей стены → `ArsenalCheckout` (владелец, деньги, отмена) |
| 2 | Стена | `ArsenalOwnershipPolicy` закрепляет стены за ботами, бот ими не пользуется | — |
| 3 | Готовность | `ServerSetReady(true)` директором, жетон не берётся | жетон → `CmdSetDogTagGrabbed` + `CmdSetReady` |
| 4 | Команда | `SessionTeamAssigner.Apply(force)` каждый тик; `BotMatchStarter` автобалансит людей и зовёт `GoLive` напрямую | `CmdRequestTeamChange` → `TeamChangeRequests` + `TeamChangeRules` |
| 5 | Спавн/карта | тело после смены карты — директор каждый тик; сессия без соединения, вне handshake | `OnServerReady` → `ChangeAvatar` |
| 6 | Смерть/призрак | пассивный комплект призрака + `BotBody.Place`; `BotGunner` сам уничтожает ствол | `AvatarTeardown.Confiscate` |
| 7 | Раунд/minPlayers | общий `PlayerRoster`, без обхода | — |
| 8 | Подготовка оружия | `RequestAutomationPreparation` (IsWorldAuthority) | руками: магазин, затвор |
| 9 | Хват | кисть на снап + `UxrGrabManager.GrabObject` | grip → `UxrStandardAvatarController` → ближайший |
| 10 | Огонь | `TryToShootRound(0)` со своим гейтом луча | trigger → SDK |
| 11 | Перемещение | корень пишется каждый кадр, кости кистей мирово | корень стоит, движутся камера и контроллеры |
| 12 | Ветки по соединению | `ServerAuthoredAvatar`, `InitialCalibrationReady`, `DefaultIsAuthoredHere`, `RpcDevTeleport` | — |

Найденный дефект людей, который обход спрятал: бесплатная покупка со стены (`InstallSlots` без подписки),
исправлено `fddf7b4ed`, тесты `ArsenalWallSlotInstallTests`.

## Ввод для ботов (итог input-research, 2026-10-19)

Из [bots-control-modes/Details.md](../bots-control-modes/Details.md) «Итог input-research»:

- Клиенту SDK-патчи не нужны (Local-аватар, своё UXR-устройство) — сделано в вехе 1 xr-input.
- Боту avatar-ik и manipulation независимо рекомендуют SDK-флаг «внешний аватар с вводом на этой машине»
  (`UpdateExternally` + свой ввод): стадия Update — UpdateInputDevice/IsGrabbing, ControllerInput — ввод бота,
  Local-гейты AlignToController/дальний хват/курок → на этот признак. 5 точек патча SDK. Остаточные отличия —
  презентационные (IK-троттлинг, HitReaction, render-оптимизатор). Перф N ботов на Quest-хосте — замерить.
- Общие серверные обработчики (network): шаблон `Cmd*` → `Server*` + одна точка `Request*`; разделить
  `CmdSetDogTagGrabbed`, `CmdRequestTeamChange`, `CartridgeIntake`; `isOwned` → `IsAuthoredHere` в
  `HandleTagGrabbed`; тело после смены карты — одна функция для соединений и сессий без соединения; фиктивных
  соединений не делать. Покупка хватом со стены уже не зависит от соединения.
- Контракт ввода клиента: [contracts/xr-input.md](../bots-control-modes/contracts/xr-input.md).

## Остаток bots-validation (этап 1 эпика)

Первоисточник — [bots-validation/Handoff.md](../bots-validation/Handoff.md) §8 и
[Readme](../bots-validation/Readme.md).

- **B** внешние Spectator/Player на выделенном сервере с ботами: bodyless/team0 у зрителя, тела/позы/оружие ботов
  через обычный путь приёма; поздний вход, owner self-skip, руки/кобура/оружие, смерть/новое тело, reconnect.
  Клиенты не создают бот-компонентов. Технически подключение уже работает (startup-lifecycle).
- **C (сокращено)** бой и урон с автором (`ActorSource`), смерть/призрак/возрождение, реестр SDK без дублей и
  утечек при смене карты/abort/rejoin; оружейные пары — только огонь и урон; хват — визуально в шлеме.
- **D/E** perf и Quest — в этапах 5–6 эпика (после паритета мерить ботов на флоу клиента; исторические H/C — только
  методика).
- **F** QA пола BotCombatStand и helper сборки (влиты без QA), регрессии, закрытие.

## Связанные задачи

| Задача | Роль | Состояние |
|---|---|---|
| [bots-fix](../bots-fix/Readme.md) | принятая основа: player-flow, клиповые тела, хват/стрельба | закрыта 2026-10-18 |
| [bots-validation](../bots-validation/Readme.md) | этап 1 эпика | идёт |
| [bots-control-modes](../bots-control-modes/Readme.md) | термины, инвентаризация, input-research, xr-input веха 1 | закрыта, перешла в e2e-regression |
| [e2e-regression](../e2e-regression/Readme.md) | зонтик регрессии; этап 9 — потребитель эпика | идёт (этапы 2 «Бой», 4 «HeavyEditor») |

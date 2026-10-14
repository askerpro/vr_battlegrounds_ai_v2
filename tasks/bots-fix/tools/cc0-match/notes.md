# cc0-match — разведка (2026-10-13)

Подэтап cc-0 clip-cutover: база матча гибрида через PlayLaunch, проба `BotStandMatchProbe`.

## Как запускается матч с ботами через PlayLaunch

- `PlayLaunchConfiguration.BotCount` → `DebugBootstrapSettings.BotCount` (замороженный request) →
  `DebugOrchestrator.OnServerSceneChanged` → `BotDirector.EnsureInstance().EnsureCount(N)` при первой загрузке сцены
  сервером. Каждый `AddBot` → `PlayersManager.CreateBotSession` (событие `SessionConnected`) → `ChooseTeam(force)` →
  `SpawnBody`.
- `AutoGoLive=true`: `DebugOrchestrator.TryGoLive` при `SessionConnected` и при появлении `MapReferee`; условие
  `Sessions.Count >= MinPlayers` (боты — сессии; `MinPlayers` из конфигурации > 0 перекрывает `minPlayersToStart=2`).
  Пакет ставит `MinPlayers = люди + N`, поэтому «Начать матч» срабатывает, когда подключился последний участник.
  Если матч не стартовал, B4 через 45 с вызывает `MapReferee.GoLive()`, как админ.
- Карта `TestMap1` — первая в `MapRegistry` (её же выбирает `BotMatchStarter`), режимы Elimination и Respawn,
  `maxPlayers` 10. NavMesh ботов строится в памяти сервера по коллайдерам карты (`BotNavMesh`), так что подходит
  любая игровая карта. Пилот Blaze проходил 4 раунда как раз на TestMap1.
- Elimination: `roundsPerSet` 3 (до победы 4), подготовка 1 с, закупка ждёт готовности не дольше 45 с, отсчёт 3 с,
  бой 90 с (по таймеру ничья), итог 3 с, табло 5 с. Если никто не стреляет, раунд идёт примерно 105–110 с, 4 раунда —
  примерно 7–8 мин. Поэтому сторож останавливает прогон в начале 5-го раунда, по концу матча или через 480 с Live.

## Предел N

Жёсткого предела нет. `BotMatchPlan.MaxTeamSize=4` действует только в автодоборе «Матч с ботами»
(`BotMatchStarter.Fill` → `BotsToAdd`), а `EnsureCount` через PlayLaunch его не применяет. Команды выравнивает
`BotTeamChoice`. Ответ gameplay 6030: база — 8 ботов (4×4), верхний предел — 10 (5×5, `maxPlayers` 10 у TestMap1/2/3).

## Топология

- Хост с ботами поддерживается: боты создаются на сервере, а хост — это сервер. M* = `Role=host`, `ClientCount=0`;
  человек на хосте остаётся без команды режима.
- `GameRole=Spectator` не работает: `GameNetworkManager.OnSpectatorConnect` пишет «пока не реализовано», сессии нет
  (тикет 515, net-round1 attempt-1). Поэтому S8 заменён на S8P (решение главной сессии): клиенты — `GameRole=Player`.
  У Elimination `teamAssignment=PlayerChoice`, DebugOrchestrator команд не раздаёт, значит у людей `TeamIndex=0`,
  в составах (`RoundReadiness` по командам режима) их нет. Проба на каждой смене фазы пишет `inModeTeam` для
  каждой сессии.

## События только для чтения, на которые подписана проба

Фазы — `EliminationMode.RoundPhaseChangedServer` (статика), `RoundBeganServer` и `RoundScoredServer`
(экземпляр). Конец — `MapReferee.Finished`, `RoundWon`, `GameMode.Finished`, `ModeStarted/FinishedLocal`. Гибели —
`MapReferee.PlayerKilled`, тела (живое тело или призрак = `session.IsEliminated`) — `AvatarManager.AvatarSpawned`.
Деньги — `MatchEconomy.TransactionServer` (Purchase: `Detail` = DisplayName, цена — из `WeaponRegistry.GetByName`)
и `MoneyChangedLocal`. Выстрелы — `UxrFirearmWeapon.ProjectileShot`: оружие находится через
`UxrComponent.GlobalEnabled` и разовый поиск при загрузке карты или спавне тела, стрелок — `UxrWeapon.Owner`.
Попадания — `UxrWeaponManager.DamageReceived`. Сеть — `NetworkDiagnostics.Out/InMessageEvent`. Маркеры —
`ProfilerRecorder` по именам `ProfilerMarker` бот-кода, а также `BehaviourUpdate`, `LateBehaviourUpdate` и
`FixedBehaviourUpdate`.

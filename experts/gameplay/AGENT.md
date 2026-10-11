You are expert `gameplay`: game runtime — map lifecycle, modes, rounds, series, match, teams,
spawn/death, economy, arsenal, lobby as a session phase.

### Zone

Owns: everything under Architecture, plus contract `map-startup-route`; spawn rules incl. whether bots count
in minPlayers; meaning of `PlayerSession` commands (team, readiness, admin); `Docs/gameplay.md` (structure +
gameplay sections), `Docs/game-manager.md`, `Docs/Arsenal/**`; E2E scenario content, expected result, noise list.

Not owned:
- launch-infra — PlayLaunch, E2E framework (write scenarios on it, never edit it), noise mechanism, ports,
  builds, bot-stand launch;
- network — GameNetworkManager/Discovery, Relay, StateEventAuthority, Mirror, `PlayerSession` as session;
  what/when to replicate is yours, network reviews the network part;
- level-design — map/lobby scenes, spawn/station placement, station bodies/decor, occlusion, nav
  layers/colliders, map validators;
- bots — AI, BotBody, bot equipment/shooting; ui — menu/HUD, economy/arsenal view; weapon-system — weapon
  mechanics, gun balance; manipulation, avatar-grip, avatar-ik — grab/body; performance — budgets.

### Invariants

- Live map change only `MapLoader.Instance.LoadMap`; first server scene only via ServerStartupRoute inside
  `GameNetworkManager.ServerChangeScene`; onlineScene/offlineScene belong to Mirror (NET-21).
- Single map-state publisher `MapRunAuthority` (`MapRunSnapshot`); `MapRunConfig` immutable per load.
- Series mode captured at series start (`Series.CapturedModeId`); menu choice applies to the next series.
- Networked map items only via `MapRunAdmission`; named refusals, no fallback to Lobby or authored.
- Arsenal station built from a preset deterministically by `MapRunKey`; one slot-view writer
  `ArsenalSupportProjection`.
- Same-scene reload relies on SDK patch 51; series EditMode smoke has no avatars and no network.

### Architecture (entity — responsibility — source)

- Map start — Resolve → BeginRun → stations → Ready, admission, Relay barrier — `Assets/Scripts/Maps/Runtime/`
  (MapBootstrap, MapRunAuthority, MapRunAdmission).
- Map catalog — referee, modes, fingerprints, DebugMaps, ArsenalComposition —
  `Assets/Scripts/Maps/Runtime/MapRuntimeCatalog.cs`, `Assets/Data/Maps/MapRuntimeCatalog.asset`.
- Load/start/session — `Assets/Scripts/Managers/` (MapLoader, ServerStartupRoute, SessionManager, Series,
  MapReferee, AdminMapCommands).
- Modes/rounds — GameMode, Warmup/Respawn/Elimination, phases, readiness — `Assets/Scripts/GameModes/`.
- Teams/spawn — `Assets/Scripts/Core/TeamRegistry.cs`, `Assets/Scripts/Maps/TeamSpawnZone.cs`,
  `Assets/Scripts/Player/Avatars/` (AvatarSpawnPointResolver, AvatarManager, AvatarTeardown).
- Fade — `Assets/ThirdParty/UltimateXR/Runtime/Scripts/CameraUtils/UxrCameraFade.cs`.
- Player commands — `Assets/Scripts/Player/PlayerSession.cs`.
- Damage/death — `Assets/Scripts/Player/DamageLedger.cs`, `Assets/Scripts/Player/Corpse/`, `Assets/Scripts/Player/Ghost/`.
- Economy — `Assets/Scripts/Economy/`.
- Arsenal — `Assets/Scripts/Arsenal/` (ArsenalStationComposer, ArsenalSlotBuilder, ArsenalWallController).
- E2E — `Assets/Scripts/Debug/E2E/Scenarios/` (MapRunRelayBarrierScenario, RoundReadinessMatchScenario).

### Details (grep the heading, read that section only)

- `tasks/map-runtime-bootstrap/Details.md` (50 KB): «Владельцы и данные», «Этап `startup-route`…»,
  «Этапы `series-smoke` и `series-smoke-e2e`…», «Проверки и пределы» (**Известные шумы тестов и харнесса**,
  **Не проверено**), «Известные ограничения». Its Readme lags.
- `Docs/game-manager.md`: «Запуск карты (`MapBootstrap`)», «Первая сцена сервера», «Система игровых режимов».
- `Docs/gameplay.md` (110 KB): «Структура матча», «Машина состояний раунда…», «Готовность к раунду…»,
  «Режимы игры», «Экономика и покупки (T-45)».
- `tasks/arsenal-generator/` (Readme.md, Details.md, new-flow.md); `Docs/Arsenal/Arsenal_Code_Architecture_RU.md`.
- `Docs/troubleshooting.md`: «Матч и раунды».

# Expert `launch-infra`

Launching editor and players in all modes (Play, roles, MMP/MPPM, native, built, headless), ports,
debug mode, E2E framework, test XR input, builds.

## Owns
- PlayLaunch + frozen config; checkout profile; MMP backend, address RunId+ParticipantId+ProcessSessionId.
- Port pair; DebugBootstrapGate and normal-Play entry; MPPM config and package.
- Debug mode: `DebugMode`, `DebugAdminPolicy`, `DebugPerfReadout` as a tool. Test XR input mechanism.
- E2E framework: runner, CLI context, verdict, waits, log aggregation, known-noise mechanism.
- Builds: GameBuilder, BuildSceneResolver, E2EPlayerBuilder, `Tools/release/`, `Tools/e2e/`.
- Contract `play-launch-control`; `Docs/test-stand.md`; `Docs/testing.md` tiers C/3/4; `Docs/release.md` build/config.

## Not owned
- gameplay: map lifecycle, rounds, `map-startup-route`; E2E scenario content, expected result, noise list.
- network: GameNetworkManager/Discovery, session, authority (yours: launch hooks only).
- avatar-ik: input/tracking rules, reviews the XR input adapter. ui: debug screen. performance: budgets.
- Stand content: its expert (BotCombatStand — bots; its launch is yours). agent-infra: broker/worker/proxy.

## Invariants
- PlayLaunch alone owns request/frozen config; window and stands are no second launcher; no profile
  change during another request.
- One Editor writer applies the port pair before network start; a failure stays within its RunId.
- E2E and the director never start the network concurrently; live map change only via MapLoader.
- Network commands are addressed by epoch/avatar; a repeated RequestId does not repeat the action.
- XR input: one adapter, no second writer of UXR input/Transform/grab-state.
- You change the E2E framework; scenario experts change scenarios, you review.
- Stages list files explicitly: masks `Debug/**`, `Bootstrap/**` capture foreign menus.
- negative377 evidence stays revoked.

## Architecture
- `Assets/Editor/VR_Battlegrounds/Testing/`: `PlayLaunch.cs` — launcher; `PlayLaunchNetworkPorts.cs` — ports;
  `PlayModeTestStand.cs`, `PlayModeStandParticipant.cs`, `StandManifest.cs`, `StandProtocol.cs` — MMP; `Tools/TestStand/Program.cs`.
- `Assets/Editor/VR_Battlegrounds/Debug/`: `PlayLaunchWindow.cs`; `PlayModeStartFromOffline.cs`, `EditorFocusPauseMenu.cs` —
  normal Play; `E2EPlayerBuilder.cs`.
- `Assets/Scripts/Debug/Bootstrap/`: `PlayLaunchConfiguration.cs`, `PlayLaunchProfileStore.cs`, `DebugBootstrapSettings.cs` —
  profile; `DebugOrchestrator.cs` — role/launch part only. Seam: `Assets/Scripts/Debug/DebugBootstrapGate.cs`.
- `Assets/Scripts/Debug/DebugMode/`: `DebugMode.cs`, `DebugAdminPolicy.cs`, `DebugGestureInput.cs`, `DebugPerfReadout.cs`.
- `Assets/Scripts/Debug/E2E/`: `E2ERunner.cs`, `IE2EScenario.cs`, `E2EContext.cs`, `E2EResult.cs`, `E2EWait.cs`.
- MPPM: `ProjectSettings/VirtualProjectsConfig.json`, `ProjectSettings/MultiplayerManager.asset`.
- Builds: `Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs`, `BuildSceneResolver.cs`; `Tools/release/Build-Game.ps1`,
  `Tools/release/server/Start-Server.ps1`; `Tools/e2e/Run-E2E.ps1`.

## Details (grep the heading)
- `Docs/test-stand.md` «Профиль и обычный Play», «Локальные сетевые порты», «Адресные команды и reconnect».
- `Docs/testing.md` «Ярус C · Два процесса», «Подводные камни, найденные прогоном»,
  «Уровень 3 · Multiplayer Play Mode», «Уровень 4»; `Docs/release.md` «Как собрать», «Конфигурация: тест и прод».
- `Docs/troubleshooting.md` «Порт для сервера уже занят», «Выделенный сервер», «В Multiplayer Play Mode сыпется».
- `tasks/vr-test-stand/Details.md` (93 KB) «4. Конфликты», «5. Рекомендуемая архитектура», «13. Приёмка»,
  «14. Локальные порты»; its Readme lags.

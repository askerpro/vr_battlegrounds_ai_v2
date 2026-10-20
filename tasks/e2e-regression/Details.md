# e2e-regression — подробности

## Инвентаризация E2E-инфраструктуры (2026-10-20, origin/dev 43332888b, только чтение)

### Каркас `Assets/Scripts/Debug/E2E/` (зона launch-infra)

- E2ERunner — RuntimeInitializeOnLoad при `-e2eScenario`, реестр `All()`, файл результата всегда (заглушка,
  вердикт, timeout, exception, выход).
- E2EContext — CLI: `-e2eScenario/Role/Result/Timeout/Map/Clients/Late/ServerAddress/DeviceToken/NetworkPort+
  DiscoveryPort/Arg k=v/QuitOnFinish/HeadPose`; ошибки разбора валят прогон до сценария. `-e2eInput` нет.
- E2EBarrier — шаги `Step/ServerStep`, барьеры через Mirror (`WaitClients` с поздним клиентом), FailServer,
  ReportClientFailure, роли server/host/client-N.
- E2EResult (Declare/Set, статусы, JSON, steps, logIssues), E2EWait (`Until` с диагнозом), E2EViews (точки обзора),
  E2EHeadPose (высота глаз, только плеер), E2EPlayerPorts (пара портов), E2ELogCapture (Error/Exception/Assert →
  logIssues), E2EDiagnosticSession (RunSpec, sha256).
- `Input/` — скриптовый ввод (этап 1): DeviceFrame, ScriptedControllerInput/Tracking (выключены в PlayerBase и
  GhostAvatar), ScriptedInputGate, E2EScriptedInput, ScriptedPilot, ScriptedClientRunner, `Input/Scripts/*.json`.
  Проверено только в Editor/MPPM.

### Сценарии (19, по умолчанию S + N Windows-клиентов через Run-E2E)

Исторические «зелёные» — из `Docs/testing.md` (август), до вердикта по логам (enforce с 2026-10-13), на текущем dev
не перепроверены.

| Сценарий | Проверяет | Топология | Зона | Состояние |
|---|---|---|---|---|
| dedicated-server-arsenal | NET-06/13/07 | S+2 | gameplay | RED 2026-10-14 (24/24, но лог-дефект teleport-state-unknown-target; фикс `network-sync-hardening` не в dev) |
| avatar-swap-death-replication | NET-01/02 | S+2 | gameplay | зелёный (ист.) |
| calibration-scale-replication | VR-01 | S+2 | avatar-ik | зелёный (ист., 3/3) |
| session-recovery-on-reconnect | ARCH-01 | S+1 | — | зелёный; Editor-стенд server 10/10, client 5/5 (worker 364) |
| arsenal-item-grab | NET-16/17 | S+1 | — | зелёный (ист., 5 подряд) |
| player-death-signal | NET-04 | S+1 | gameplay | зелёный (ист.) |
| shot-pipeline-budget | T-23/24 (замер) | S+1 | performance | вердикта нет |
| round-readiness-match | T-29/RDY-01 | S+2 | gameplay | зелёный (ист., 5 подряд) |
| weapon-hit-damage | T-28 | S+1 | weapon-system | зелёный (ист.) |
| avatar-spawn-point | WPN-03 | S+1 | gameplay | зелёный (ист.) |
| calibrated-position-persists | CAL-01, смена карты | S+2 | avatar-ik | зелёный (ист.) |
| connect-place-across-map | CAL-02 | S+1 | — | зелёный (ист.) |
| map-run-relay-barrier | поздний клиент, перезагрузка карты | S+2 | gameplay | GREEN 2026-10-07 |
| map-run-startup-route | старт сервера в карту | S+2 | gameplay | GREEN 14/14 2026-10-14 (строгий вердикт) |
| e2e-kit-selftest | каркас | S+N | — | GREEN 19/19 2026-10-14 |
| device-smoke | ПК-сервер + Quest-клиент | S+Q | — | GREEN Quest 3 2026-10-12 |
| perf-stress | замер на шлеме | S+Q | performance | GREEN 14/14 2026-10-12 |
| scripted-input-smoke | скриптовый ввод, поза в сети | S+1 MPPM | — | PASS 11/11 + 2/2 (2026-10-19) |
| scripted-wall-purchase | покупка со стены вводом, касса | S+2 MPPM | — | PASS 16/16 + 16/16 + 6/6, принят 2026-10-20 |

### Конвейер плееров

- `Tools/e2e/Run-E2E.ps1` (777 строк): headless-сервер + `-Clients` 1..1024 оконных; `-Scenario/-Map/-Timeout/-Player`
  (по умолчанию `Build\e2e\VrBattlegrounds.exe`), `-LateClientDelay`, авто-пара портов, `-Arg`, `-RunSpec/-ViewsFile/
  -HeadPose`, `-LogVerdict enforce|report`, `-SkipStaleCheck`. Свежесть плеера — по mtime `.cs`, не по ревизии.
  Результаты `Tools/e2e/results/<stamp>-<scenario>/summary.json` + `log-verdict.json`; коды 0/1/2.
- `E2ELogVerdict.psm1` + `known-noise.json` (одна запись kind=defect — teleport), `E2EDiagnosticInputs.psm1`.
- `Assets/Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs`: «Собрать e2e-плеер (Windows)» → `Build/e2e/`;
  `StartPairBuild` — пара Win+APK одной ревизией (BuildRevisionStamp).
- `GameBuilder.cs:97,891`: `VRBG_NO_E2E` только в Prod; Test-сборки содержат харнесс. `Tools/release/Build-Game.ps1`
  (server/quest/tablet, Test/Prod, `-DryRun`), `Tools/release/server/Start-Server.*`.
- Build-checkouts `F:/CodexWorktrees/build-{test,prod}-{win64,android}/Vr_Battlegrounds_ai` — старая раскладка,
  detached `d5db59eaf` (отстают от dev); матрица 8/8 builder PASS 2026-10-15; dedicated batch без аренды worker.

### Editor-стенд (`Assets/Editor/VR_Battlegrounds/Testing/`)

- PlayLaunch (planner, профиль `UserSettings/VrBattlegrounds/play-launch.json`, ClientCount ≤ 16 в конфиге),
  PlayLaunchNetworkPorts, PlayLaunchNetworkDiagnostics (opt-in, 2026-10-20), PreviewSceneNetworkGuard.
- PlayModeTestStand/Participant (MPPM/MPE IPC, адрес RunId + ParticipantId + ProcessSessionId), StandProtocol
  (`create/read/remove-marker`, `state`, `disconnect`, `reconnect`, `request-team-change`, `run-e2e`), StandManifest.
- Пробы `tasks/vr-test-stand/tools/`: launch_worker_probe (56/56), managed_e2e_worker_probe, native_bot_worker_probe.
- Ограничения: MPPM ≤ 4 игроков; медленный вход в Play (OculusXRPlugin, infra 10064); внешний server + 2 Spectator не
  дошёл до Ready (infra 9410/9451); Play только через PlayLaunch; `run-e2e` вызывается вручную через MCP.

### Quest

- Контракт `tasks/quest-perf-pipeline/contracts/quest-device-run.json` (r5); `Tools/e2e/Run-QuestE2E.ps1`
  (1960 строк): ПК-сервер + шлем, adb install/wake/prox/logcat/pull, сверка ревизий APK и сервера, `-Build` пары,
  `-HeadPose eye=1.6`, aapt-проверка манифеста, `-ViewsFile`, вердикт по логам; `-Topology host` не реализована.
- Controller-free Test-APK влит (`3cabcc1ab`). На шлеме прогнаны device-smoke и perf-stress (+ view-tour).

### Юнит-тесты

- EditMode: ~248 файлов, ~2870 случаев; на dev 35 известных красных (карты/блокаут, эмбиент BotCombatStand,
  GrabPoseCoverage, выстрел WeaponSpread/RemoteShotReplay/Shotgun/Kinemation/ShotSound, Ledger, сканер
  StateSnapshotCoverage). PlayMode-тестов нет. `Tools/TestStand/ProtocolHarness.csproj` 67/67 (2026-10-14).

### Пробелы

- Нет `-e2eInput scripted` в плеере; сценарии ввода грузятся из `Application.dataPath`; взаимоисключение с
  E2EHeadPose по `Armed`.
- Нет suite-раннера (список сценариев + профиль) и общей точки входа HeavyEditor/HeavyWindows/HeavyQuest.
- Нет агрегированного вердикта по сценариям/профилям; результаты в `Tools/e2e/results/`, а не в `reports/`.
- Нет `-Build` и привязки к ревизии в Run-E2E (stale-check по mtime); build-checkouts отстают.
- Нет дирижёра Editor-стенда с JSON-вердиктом и кодом возврата.
- Исторические сценарии не перепроверены строгим вердиктом; dedicated-server-arsenal RED.
- Нет сценариев серии (бой → смена карты → лобби, пауза/стоп, админ).
- Без владельца в expert.json: ArsenalItemGrab, SessionRecovery, ConnectPlaceAcrossMap, E2EKitSelfTest, DeviceSmoke,
  ScriptedClient, `E2E/Input/**`.
- Нет host-топологии и мультишлемных прогонов на Quest; нет CI и gate «heavy перед merge».

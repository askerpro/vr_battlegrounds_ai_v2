# Expert `performance`

Quest 2/3 performance: frame/GC/draw-call/memory budgets, measurement methods, stress test,
Quality/URP, occlusion budgets and checks, perf review of other zones' stages. You set budgets and
review; optimisation in a foreign zone is done by its owner from your spec/review.

## Owns
- Stress test and metrics (`perf.log`, `summary.json`, `history.log`); `Docs/perf-stress-test.md`.
- Headset CPU/GPU levels and FFR: installers, perf fields of `GameSettings`.
- Quality/URP: `QualitySettings`, `GraphicsSettings`, `URPDefaultResources`, `Settings/Graphics`.
- Scene and occlusion budgets plus the occlusion check criterion (level-design meets them in scenes).
- Perf reviews: legs-ik `quest-perf` (writes `Docs/perf-stress-test.md`), weapon-system T-24
  `shot-pipeline-budget` (method review).

## Not owned
- level-design: scenes, occlusion bake, map validation tools. avatar-ik: avatars, their render/IK/LOD,
  UxrManager, input; avatar-grip: MEF hands. manipulation: grab. network: Mirror/messages.
- launch-infra: launch, E2E framework, debug mode, `DebugPerfReadout`. ui: «Перф-тесты» screen.
  weapon-system: weapons, projectiles.

## Invariants
- A number without conditions is not a fact: build (Development/release), device, role, mode, scene, SHA.
- Editor shows relative growth only; budgets are judged on the headset. Do not touch Editor/MCP during a run.
- The stress test only records FFR/levels, never sets them. `StressTestServer.Run` phases and `StressTestPlan` change together.
- Post-processing, HDR, render scale != 1, opaque/depth texture, renderer features push rendering past
  FFR: re-measure before changing them.
- A measuring scenario never assigns its own threshold. Unknown budget: measure and agree, never invent.

## Budgets today (sources only)
- Frame = 1000 / XR display rate; no display -> 72 Hz -> 13.9 ms (`PerfStats.BudgetMs`). The game does
  not set the rate; 72 vs 90 Hz undecided.
- Spike: frame > budget x1.5 and > level x2. Log line: change >= 1 ms and >= 15 %.
- Headset request: CPU 4 / GPU 2 (repeat <= once per 10 s), FFR 0.75. Prod load: player + 9 remote, 40 items.
- No budget yet for GC, draw calls/SetPass/triangles, memory, network, server frame (backlog `budget-baseline`).
- Last measurement (journal 2026-09-29): CPU-bound, 9 puppets p50 15.5 ms > 13.9.

## How to measure
1. Stress test: tablet -> «Отладка» -> «Перф-тесты» (debug mode: both sticks 2 s) or menu
   `Tools/VR Battlegrounds/Debug/Stress Test/…`. Subsystems only in Development; files via `adb pull`.
2. Load cost = phase minus `база` of the same run; `history.log` before/after; GPU on Quest = `ovr_gpu`.
3. On-device profiler: Development + Autoconnect, no Deep Profile.
4. Measurement logic tests (EditMode): `PerfLogicTests`, `StressTest*Tests`, `PerfMemoryCountersTests`, `PerformanceLevelPolicyTests`.

## Perf review of a foreign stage
Per-frame work per avatar/item; per-frame allocations (LINQ, lambdas, iterators, `GetComponent*`);
scene searches every frame; new SkinnedMeshRenderer/materials/shadows/transparency; post/HDR/depth
textures (FFR); geometry without occlusion re-bake, movers not excluded from occlusion. Demand a
before/after measurement or justification; reject a perf PASS without measurement conditions.

## Architecture
- Run (phases, puppets, recording): `Assets/Scripts/Debug/StressTest/StressTestServer.cs`, `StressTestClientSession.cs`, `StressTestPlan.cs`.
- Metrics (windows, spikes, ovr_*): `Assets/Scripts/Debug/StressTest/PerfFrameRecorder.cs`, `PerfStats.cs`, `OculusPerfStats.cs`, `PerfEvents.cs`.
- Entry: `Assets/Scripts/Debug/StressTest/StressTestLauncher.cs`; `Assets/Editor/VR_Battlegrounds/Debug/StressTestMenu.cs`.
- Headset levels/FFR: `Assets/Scripts/Core/PerformanceLevelInstaller.cs`, `PerformanceLevelPolicy.cs`, `FoveatedRenderingInstaller.cs`, `GameSettings.cs`.
- Quality/URP: `ProjectSettings/QualitySettings.asset`, `ProjectSettings/GraphicsSettings.asset`, `Assets/URPDefaultResources/Quest.asset`.
- Neighbours: `Assets/Scripts/Player/Avatars/RemoteAvatarRenderOptimizer.cs`, `RemoteAvatarIKThrottle.cs` (avatar-ik);
  `Assets/Editor/VR_Battlegrounds/Gameplay/OcclusionBakeTool.cs` (level-design);
  `Assets/Scripts/Debug/E2E/Scenarios/ShotPipelineBudgetScenario.cs` (weapon-system T-24).

## Details (grep the heading)
- `Docs/perf-stress-test.md` «Фазы», «Как читать», «Метрики», «Прогон на Quest 3 (хост, 2026-09-28) и найденное», «Дальше».
- `Docs/combat-networking.md` «Что измерить на Quest — и как».
- `Docs/scene-hierarchy.md` «Изменения и проверка»; `.agents/rules/project-workflows.md` «Asset rules».
- `Docs/troubleshooting.md` «Выделенный сервер»; `Docs/UltimateXR/known-issues.md` «Issue 20».

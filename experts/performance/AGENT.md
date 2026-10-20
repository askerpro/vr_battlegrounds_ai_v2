# Expert `performance`

Quest 2/3 performance: frame/GC/draw-call/memory budgets, measurement methods, stress test,
Quality/URP, occlusion budgets and checks, perf review of other zones' stages. You set budgets and
review; optimisation in a foreign zone is done by its owner from your spec/review.

## Owns
- Stress test and metrics (`perf.log`, `summary.json`, `history.log`); `Docs/perf-stress-test.md`.
- Headset CPU/GPU levels and FFR: installers, perf fields of `GameSettings`.
- Quality/URP: `QualitySettings`, `GraphicsSettings`, `URPDefaultResources`, `Settings/Graphics`.
- Scene and occlusion budgets plus the occlusion check criterion (level-design meets them in scenes).
- `Docs/quest-performance-manifest.md` — Quest GPU rules (QPM ids); scene-content rules also go through level-design review.
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
- Budgets are judged on the Test (Development) build: if it fits, Prod fits with margin (user, 2026-10-13).
- The stress test only records FFR/levels, never sets them. `StressTestServer.Run` phases and `StressTestPlan` change together.
- Post-processing, HDR, render scale != 1, opaque/depth texture, renderer features push rendering past
  FFR: re-measure before changing them.
- A measuring scenario never assigns its own threshold. Unknown budget: measure and agree, never invent.
- Replace before optimise (user, 2026-10-20): for every heavy object first weigh its measured GPU share against the
  effort (agent time, compute, risk) of optimising it. If a near-free substitute exists (cheap shader, baked texture,
  impostor, solid colour, removal), propose replacing it instead of tuning it. Check the standard tricks below before
  inventing a custom optimisation.

## Standard Quest tricks (check these first)
- Large screen areas (floor, ground, walls, sky) get the cheapest material: Baked Lit/Unlit + lightmap, 1–2 texture
  samples, no triplanar/world-aligned blending, no per-pixel normal far away; detail baked into one texture or vertex colour.
- Sky: solid colour, gradient or static cubemap (Unlit), never Skybox/Procedural; distant scenery as baked cards.
- Far objects: impostors/billboards, aggressive LOD (last LOD a few hundred tris), HLOD/combined meshes; cull by distance.
- Mobile shaders: half precision, few samples, no realtime lights/shadows (bake), no alpha test or blending on big areas.
- Batching: atlases and shared materials, static batching, GPU instancing for repeated props.
- Extra passes (mirrors, cameras, post) only where they matter: lower resolution, layer/distance limits, render on visibility.
- Global levers last-but-known: render scale, MSAA, FFR — they hit the whole game, decide with the user.
- Source of truth for "what is heavy": per-draw counters on the headset (`Tools/Perf/gpu_diagnostics/capture_adapters/meta_renderdoc_quest.py`
  via `Run-QuestE2E.ps1 -MetaRenderDocCmd`), not Windows timing; gain is proven only by a normal A/B on the headset.

## Budgets today (sources only)
- Frame = 1000 / XR display rate; no display -> 72 Hz -> 13.9 ms (`PerfStats.BudgetMs`). The game does
  not set the rate; 72 vs 90 Hz undecided.
- Spike: frame > budget x1.5 and > level x2. Log line: change >= 1 ms and >= 15 %.
- Headset request: CPU 4 / GPU 2 (repeat <= once per 10 s), FFR 0.75. Prod load: player + 9 remote, 40 items.
- Scene Lobby (user, 2026-10-13): phase «база» (no puppets), worst view, Test build, Quest 3, FFR 0.75, GPU 2 —
  `ovr_gpu` p50 <= 7 ms. Guides (not yet budgets): setpass <= ~40, visible tris <= ~150k.
- No budget yet for GC, draw calls/SetPass/triangles, memory, network, server frame (backlog `budget-baseline`).
- Last measurement (journal 2026-10-14 lobby-view-tour): Lobby base worst view 10.8 ms GPU > 7; puppets in view
  CPU-bound up to 18.4 ms > 13.9. Compare scenes by fixed views (`views=<file>`), never by a lying headset.

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
- View tour: `Assets/Scripts/Debug/E2E/Scenarios/PerfStressScenario.cs` uses launch-infra `E2EViews` and a shared views file;
  server owns the phase plan, client records the measured phase's `PerfViewReport`. `view.ok` currently checks execution/timing;
  compare recorded XZ/yaw/floor deviations separately before accepting a measurement. Puppets use fixed arena roots.
- Quest GPU capture: `Tools/Perf/quest_gpu_breakdown.py`; realtime timings are budget evidence, detailed driver captures only show proportions.
- Metrics (windows, spikes, ovr_*): `Assets/Scripts/Debug/StressTest/PerfFrameRecorder.cs`, `PerfStats.cs`, `OculusPerfStats.cs`, `PerfEvents.cs`.
- Entry: `Assets/Scripts/Debug/StressTest/StressTestLauncher.cs`; `Assets/Editor/VR_Battlegrounds/Debug/StressTestMenu.cs`.
- Headset levels/FFR: `Assets/Scripts/Core/PerformanceLevelInstaller.cs`, `PerformanceLevelPolicy.cs`, `FoveatedRenderingInstaller.cs`, `GameSettings.cs`.
- Quality/URP: `ProjectSettings/QualitySettings.asset`, `ProjectSettings/GraphicsSettings.asset`, `Assets/URPDefaultResources/Quest.asset`.
- Neighbours: `Assets/Scripts/Player/Avatars/RemoteAvatarRenderOptimizer.cs`, `RemoteAvatarIKThrottle.cs` (avatar-ik);
  `Assets/Editor/VR_Battlegrounds/Gameplay/OcclusionBakeTool.cs` (level-design);
  `Assets/Scripts/Debug/E2E/Scenarios/ShotPipelineBudgetScenario.cs` (weapon-system T-24).

## Details (grep the heading)
- `Docs/quest-performance-manifest.md` — canonical Quest GPU rules (QPM ids, prices, pack checklist, measurement
  method, asset-audit mapping). This zone owns it: a new proven finding updates the manifest, the asset-audit rule
  and its test in one change.
- `Docs/perf-stress-test.md` «Фазы», «Как читать», «Метрики», «Прогон на Quest 3 (хост, 2026-09-28) и найденное», «Дальше».
- `Docs/combat-networking.md` «Что измерить на Quest — и как».
- `Docs/scene-hierarchy.md` «Изменения и проверка»; `.agents/rules/project-workflows.md` «Asset rules».
- `Docs/troubleshooting.md` «Выделенный сервер»; `Docs/UltimateXR/known-issues.md` «Issue 20».

## Как использовать этот профиль
- Агент задачи: перед правкой путей из `owns` (`expert.json`) прочитать этот файл и нужный раздел Details; оптимизацию в чужой зоне делает агент задачи по ТЗ/ревью этой зоны.
- Ревьюер (чеклист diff):
  - пункты «Perf review of a foreign stage» выше;
  - у каждого числа есть условия (сборка, устройство, роль, режим, сцена, SHA); бюджет — по Test build на шлеме, Editor — только относительный рост;
  - стресс-тест только фиксирует FFR/уровни; фазы `StressTestServer.Run` и `StressTestPlan` изменены вместе;
  - пост-обработка, HDR, render scale != 1, opaque/depth texture, renderer features — с повторным замером;
  - сценарий замера не задаёт свой порог; неизвестный бюджет не выдуман.

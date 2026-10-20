# scene-detail: Lobby на Quest 3 10,81 → 4,26 мс GPU, per-draw на шлеме и A/B двух сборок, 2026-10-20

Кадр Lobby ограничен фрагментной работой (stage-трасса: Render 8,4 мс из ~11). Per-draw на шлеме — Meta RenderDoc
(`Tools/Perf/gpu_diagnostics/capture_adapters/meta_renderdoc_quest.py`, `Run-QuestE2E.ps1 -MetaRenderDocCmd`):
доли тактов replay — подсказка, не цена (Procedural-скайбокс 8% replay = −0,05 мс в A/B). Цена — только normal A/B:
рантайм-подмены `render_diag` (`PerfRenderDiagnosticControl`) внутри одной сборки или две сборки блоками A-B-A
(`tasks/scene-detail/tools/run_ab_two_builds.py`: runner принимает сервер/APK только из штатных путей build root,
подмена переименованием `Build/e2e-<sha>`, `Build/Quest-<sha>`). Итог правок (Baked Lit на окружении, box-UV вместо
трипланара, LOD вышек, зеркало, свет) — −6,55 мс, вклад по отдельности не мерился. Цены сняты на Quest 3 в режиме
совместимости Quest 2 (TargetQuest3: 0). Правила и цены — `Docs/quest-performance-manifest.md`.
Грабли: сборка с изменённой Lobby пересчитывает ContentFingerprint в `MapRuntimeCatalog.asset` build root —
это не generated-файл Build-Game; отпечаток коммитится в ветку задачи.

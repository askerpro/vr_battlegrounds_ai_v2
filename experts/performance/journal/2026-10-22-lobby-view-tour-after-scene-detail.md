# Lobby по 8 видам после scene-detail: бюджет выполнен — худший вид 5,45 мс GPU при 7

2026-10-22, задача lobby-gpu-tour (повтор тура 2026-10-14 по требованию ревью performance к scene-detail).

Условия: Quest 3 (совместимость Quest 2), Test, 72 Гц, FFR 0,75, CPU 4 / GPU 2; origin/dev c7b8d6ab3 (содержит
scene-detail), APK и Windows e2e-сервер одной ревизии (`Build-Game -GpuSceneIdentity`, dirty=false); perf-stress
map-only без кукол, `scene_detail=lod-a`, те же 8 видов `tasks/quest-perf-pipeline/tools/lobby-views.json`, фазы 20 с,
глаза 1,6 м; 3 прогона × 8 видов, 3/3 GREEN, отклонения поз 0.

- База (ovr_gpu = AppGPUTime p50, медиана 3 прогонов): west-station 5,45, center-east 4,95, center-south 4,54,
  south-edge 4,31, center-north 4,29, north-edge 3,65, east-edge-ne 3,32, east-edge-se 2,82. Все 8 видов ≤ 7 мс;
  максимум одного прогона 5,96 (west-station). 2026-10-14: 10,83 … 4,59, выше 7 — 6 из 8.
- Кадр 72 Гц держит: dt p50 13,9, p95 ≤ 15,9; GPU util p50 28–46 % (было 40–85 %).
- Ориентиры не достигнуты: setpass 37–89 (center-north 89), tris до 243k (west-station). Бюджета на них нет
  (backlog `budget-baseline`).
- Куклы не перемерялись: прежний вывод (CPU-bound до 18,4 мс с куклами в кадре) остаётся открытым.
- Попутно: `prepare-quest-run` не читал новый формат dirty-записей Build-Game — исправлено в
  `Tools/Perf/gpu_diagnostics/run_spec.py`.
- Журнал задачи: `tasks/lobby-gpu-tour/changelog/2026-10-22-view-tour.md`; сводка
  `tasks/lobby-gpu-tour/reports/tour-20261022/summary/summary.json` (вне Git).

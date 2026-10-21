# Тур GPU Lobby по 8 видам на Quest 3 после scene-detail

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/lobby-gpu-tour` (схема «задача = агент»).

## Цель

Подтвердить бюджет GPU Lobby (худший вид ≤ 7 мс ovr_gpu p50, Quest 3, Test, FFR 0,75, GPU 2) по всем 8 видам после правок scene-detail; обновить AGENT.md performance и манифест Quest.

## Итог замера (2026-10-22)

Худший вид — west-station **5,45 мс ≤ 7 мс: бюджет выполнен**; остальные 2,82–4,95 мс (тур 2026-10-14: худший 10,83 мс).
Origin/dev c7b8d6ab3, 3 прогона × 8 видов, 3/3 GREEN. Таблица, условия и ограничения —
[changelog/2026-10-22-view-tour.md](changelog/2026-10-22-view-tour.md).

## Изменения

- `experts/performance/AGENT.md` «Budgets today», `Docs/quest-performance-manifest.md` «Бюджеты» и цена QPM-SHADER-001;
  журнал `experts/performance/journal/2026-10-22-lobby-view-tour-after-scene-detail.md`.
- `Tools/Perf/gpu_diagnostics/run_spec.py`: `prepare-quest-run` читает dirty-записи нового формата Build-Game.
- `tools/summarize_view_tour.py` — сводка тура по видам из progress серии.

## Статус

Серия выполнена, документы обновлены. Плафоны шатра в шлеме приняты пользователем 2026-10-22. Ревью performance
раунда 1 — approve (до правки dt p95). Осталось: повторное ревью, приёмка правки run_spec.py, merge.
Побочная находка: на Windows e2e-сервере `ShootingRangePhotoCapture` даёт `RenderTexture.Create failed`.

## Этапы

Этапы и области записи — в [plan.json](plan.json).

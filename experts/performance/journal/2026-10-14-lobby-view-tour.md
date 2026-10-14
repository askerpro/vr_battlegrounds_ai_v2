# Lobby по 8 фиксированным точкам: база не в бюджете — худшая точка 10,8 мс GPU при 7

2026-10-14, этап perf-stress-scenario/view-tour (принят пользователем по прогону).

Условия: Quest 3 (совместимость Quest 2), Test, 72 Гц, FFR 0.75, CPU 4 / GPU 2; пара worker (тикет 605, вход
455e9425472f, отметка `-dirty`) поверх origin/dev a84b9f2ce (декор eea14fe3a, окклюзия e78dc38ad); perf-stress
map-only, арена 9 кукол, 8 точек `tasks/quest-perf-pipeline/tools/lobby-views.json`, фазы 20 с, глаза 1,6 м.

- База (ovr_gpu p50): center-north 10,83, west-station 10,53, center-east 10,35, center-south 10,11, south-edge 8,36,
  north-edge 7,73, east-edge-ne 5,88, east-edge-se 4,59. Бюджет 7 мс превышен в 6 из 8 точек; setpass до 91,
  видимые треугольники до 256k (ориентиры ~40 / ~150k). Прежние 7,64 мс — один случайный ракурс.
- Куклы в кадре — упор в CPU: dt p50 до 18,4 мс (scripts+late ~7, uxr 3,6, render 7); вне кадра — в бюджете.
- Сервер ПК: cpu_main 3–8 мс. Клиент: mem_mb 1632→1689, tex_mb 1770.
- Отправлено level-design (6467): следующий их этап — инструмент детализации, приёмка по этим 8 точкам.
- Сводка: `tasks/perf-stress-scenario/reports/view-tour-20261014.md`; сырые — `Tools/e2e/results/20261014-022853-quest-perf-stress`.

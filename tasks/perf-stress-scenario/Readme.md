# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-14. Владелец: эксперт `performance`. Worktree: `F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`,
ветка `claude/performance`. Начата по решению пользователя («начинай свою часть»).

## Цель и границы

Перф-прогон на шлеме одной командой `Tools/e2e/Run-QuestE2E.ps1 -Scenario perf-stress` — без человека в шлеме,
шлем-клиент и выделенный сервер на ПК (контракт `quest-device-run` rev3, задача `quest-perf-pipeline`, launch-infra).
Первый заказчик после performance — `lobby-decoration` (level-design): замер GPU лобби с 9 аватарами (события
4449/4453). Проект и решения — [Details.md](Details.md).

Сценарий только наблюдает и запускает стресс-тест; FFR, уровни CPU/GPU и настройки шлема не трогает; бюджетных
порогов не ставит (вердикт — «прогон состоялся», оценка чисел — performance).

## Этапы

| Этап | Суть | Состояние |
|---|---|---|
| `setup` | документы задачи, задача в `expert.json` (вливается сразу) | merged `ec711f2df` |
| `scenario` | сценарий `perf-stress` без рук: обе роли, параметры `-e2eArg`, артефакты | merged `fa83395aa` |
| `controller-free-launch` | Test-сборки Quest стартуют без пробуждения контроллеров: рук в манифесте (Test only) | merged `3cabcc1ab` |
| `arena-layout` | раскладка кукол «внутри арены», корни кукол не следуют за игроком | merged `2a5ed8f07` |
| `view-tour` | обход точек обзора (`views=<файл>`), метрики по точкам | шлем GREEN, принят пользователем 2026-10-14; тесты EditMode |
| `docs` | раздел в `Docs/perf-stress-test.md`; порядок с legs-ik/`quest-perf` (тот же файл) — согласовать | planned |

## Зависимости

- Контракт `quest-device-run` rev3 (ACK performance-onboarding). Регистрация сценария — строка в
  `E2ERunner.cs` (зона launch-infra, zone-ack).
- `view-tour`: API хелпера точек обзора (`E2EViews`, launch-infra) — согласовать до этапа.
- Приёмка: прогон на шлеме пользователем; тесты EditMode — после подтверждения.

## Сделано

- `view-tour`: 8 точек Lobby × (база, куклы), 33/33 фаз, 16/16 переносов, GREEN (`Tools/e2e/results/20261014-022853-quest-perf-stress`);
  база Lobby не в бюджете 7 мс — худшая точка 10,83 мс (таблица `reports/view-tour-20261014.md`, level-design 6467).
- `arena-layout`: куклы по полу арены Lobby (9/9, 7 запретных зон, отступ 1 м), корни закреплены;
  `Tools/e2e/results/20261013-185319-quest-perf-stress`; визуально в шлеме не проверялось — принято по прогону и логу.
  Инструмент разбивки GPU — `Tools/Perf/quest_gpu_breakdown.py`.

- `controller-free-launch`: Test-APK (ревизия 5d614df4de60) — `uses-feature handtracking required=false` + разрешение
  (aapt); Prod-APK — без записей рук; perf-stress GREEN без пробуждения контроллеров
  (`Tools/e2e/results/20261013-011525-quest-perf-stress`), система: «Is hands or controller». Prod APK 355 с против Test 150 с.
- `scenario`: прогон Quest 3 (клиент) + ПК-сервер, Lobby/разминка, ревизия 20fd70b0c771, поза головы 1,6 м —
  GREEN 14/14 (`Tools/e2e/results/20261012-204203-quest-perf-stress`, вне Git). Числа — журнал эксперта.

## Дальше

- `docs` (следующий); `Tools/Perf/quest_gpu_breakdown.py --phases` для weapon-system — отдельным этапом.
- Отдельные checkout сборок (Prod, Quest-Test) — `quest-perf-pipeline/build-checkouts` launch-infra (6455), от нас замер времени.
- `prod-vs-test` снят: бюджеты — по Test-сборке (решение пользователя 2026-10-13).

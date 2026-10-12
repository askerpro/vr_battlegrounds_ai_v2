# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-12 (вечер). Владелец: эксперт `performance`. Worktree: `F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`,
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
| `controller-free-launch` | Test-сборки Quest стартуют без пробуждения контроллеров: рук в манифесте (Test only) | шлем GREEN без контроллеров, принят пользователем; EditMode 45/45 |
| `prod-vs-test` | perf-stress на Prod-сборке против Test (решение пользователя 2026-10-13); нужен маршрут Prod у launch-infra | planned |
| `arena-layout` | раскладка кукол «внутри арены», корни кукол не следуют за игроком | planned |
| `view-tour` | обход точек обзора, метрики по точкам; после этапа `e2e-views` launch-infra | planned |
| `docs` | раздел в `Docs/perf-stress-test.md`; порядок с legs-ik/`quest-perf` (тот же файл) — согласовать | planned |

## Зависимости

- Контракт `quest-device-run` rev3 (ACK performance-onboarding). Регистрация сценария — строка в
  `E2ERunner.cs` (зона launch-infra, zone-ack).
- `view-tour`: API хелпера точек обзора (`E2EViews`, launch-infra) — согласовать до этапа.
- Приёмка: прогон на шлеме пользователем; тесты EditMode — после подтверждения.

## Сделано

- `controller-free-launch`: Test-APK (ревизия 5d614df4de60) — `uses-feature handtracking required=false` + разрешение
  (aapt); Prod-APK — без записей рук; perf-stress GREEN без пробуждения контроллеров
  (`Tools/e2e/results/20261013-011525-quest-perf-stress`), система: «Is hands or controller». Prod APK 355 с против Test 150 с.
- `scenario`: прогон Quest 3 (клиент) + ПК-сервер, Lobby/разминка, ревизия 20fd70b0c771, поза головы 1,6 м —
  GREEN 14/14 (`Tools/e2e/results/20261012-204203-quest-perf-stress`, вне Git). Числа — журнал эксперта.

## Дальше

- `controller-free-launch` (решение пользователя 2026-10-12): проект — Details.md «Этап `controller-free-launch`».
- `arena-layout`, `view-tour`, `docs`.

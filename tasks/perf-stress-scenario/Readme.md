# Стресс-тест на шлеме без рук: сценарий perf-stress, арена, обход точек обзора

Обновлено: 2026-10-12. Владелец: эксперт `performance`. Worktree: `F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`,
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
| `setup` | документы задачи, задача в `expert.json` (вливается сразу) | planned |
| `scenario` | сценарий `perf-stress` без рук: обе роли, параметры `-e2eArg`, артефакты | planned |
| `arena-layout` | раскладка кукол «внутри арены», корни кукол не следуют за игроком | planned |
| `view-tour` | обход точек обзора, метрики по точкам; после этапа `e2e-views` launch-infra | planned |

## Зависимости

- Контракт `quest-device-run` rev3 (ACK performance-onboarding). Регистрация сценария — строка в
  `E2ERunner.cs` (зона launch-infra, zone-ack).
- `view-tour`: API хелпера точек обзора (`E2EViews`, launch-infra) — согласовать до этапа.
- Приёмка: прогон на шлеме пользователем; тесты EditMode — после подтверждения.

## Дальше

- `scenario`: ТЗ субагенту, реализация, компиляция под арендой worker, прогон на шлеме.

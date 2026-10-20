# E2E этап 6: HeavyWindows — регрессия на собранных Windows-плеерах

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/e2e-heavy-windows` (схема «задача = агент»).

## Цель

Этап 6 эпика e2e-regression: профиль heavy --profile windows — сборка Windows-сервера (headless) и Windows-клиента одной ревизией (build-test-win64, привязка к SHA, а не к mtime), прогон набора сценариев через Tools/e2e/Run-E2E.ps1 и тот же сводный вердикт verdict.json + код 0/1/2, что у HeavyEditor.

## Решения пользователя (2026-10-20)

- Плеер собирает новая цель `e2e` в `Tools/release/Build-Game.ps1` (`-Targets e2e -Config Test -Ref <SHA>`):
  один владелец состояния сборочного checkout (preflight, переключение, generated-файлы, pair manifest).
- Итерация 1 — два сценария, зелёных в плеерах: e2e-kit-selftest, map-run-startup-route (оба S+2, TestMap1).
  scripted-* в плеерах — итерация 2, после этапа 5 (`-e2eInput scripted`).
- Занятый build-test-win64 heavy ждёт сам: каждые 5 мин, не дольше часа, затем код 2; чужие процессы не трогает.

## План

- [x] Исследование Run-E2E, E2EPlayerBuilder, Build-Game, состояния build-checkout
- [x] Цель e2e в Build-Game (только Test, отдельно, без UXR-сверки), `-ResultsDir` в Run-E2E
- [x] Профиль windows в heavy: закоммиченный HEAD, ожидание занятого checkout, сборка или переиспользование,
      Run-E2E по сценариям, сверка appVersion, общий вердикт
- [x] Прогон: код 0 (плеер 1f3454f7e за 448 с, kit-selftest 19/19, startup-route 14/14); приёмка пользователем
- [x] Тесты: heavy 59/59, release 22/22
- [ ] Ревью launch-infra, вливание, сообщение координатору e2e-regression
- [ ] Итерация 2: scripted-* в плеерах (после этапа 5)

## Этапы

Этапы и области записи — в [plan.json](plan.json).

## Статус

2026-10-20: итерация 1 принята пользователем. Найдено: в ветке scene-detail (`codex/scene-detail-perf-20261018`) есть почти такая же
e2e-цель Build-Game; задача уведомлена через хаб (10749), правило «e2e отдельно» взято у неё; кто вливает позже — сводит конфликт.

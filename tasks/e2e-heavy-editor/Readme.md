# E2E этап 4: HeavyEditor — одна команда регрессии

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/e2e-heavy-editor` (схема «задача = агент»).

## Цель

Этап 4 эпика e2e-regression: одна команда для агентов прогоняет набор e2e-сценариев на Editor-стенде (MPPM) и выдаёт один вердикт можно вливать / нельзя (JSON + код возврата), плюс сравнение красных EditMode с базой; правило в AGENTS.md.

## Дизайн (решение пользователя 2026-10-20)

- `python Tools/e2e/heavy.py --profile editor --suite default` **не знает о брокере**: агент сам берёт аренду worker
  (checkpoint → request → watch-ticket → claim → begin → guard), запускает heavy, затем finish/ack.
- Unity — через штатный MCP-прокси своего worktree (`uv run Tools/agents/unity_mcp_proxy.py`, stdio, MCP SDK через uv),
  как пробы `tasks/vr-test-stand/tools/`. Прокси сам направляет вызовы в worker своей аренды и отказывает без неё.
  Вызывается существующий фасад `PlayLaunch.Plan/Play/Status/Cancel`, `PlayModeTestStand.Send/Operation`;
  код Editor и контракт play-launch-control не меняются. Повторяются только наблюдения Status/Operation.
- Набор `Tools/e2e/suites/<имя>.json`: сценарии, топология (S+N), карта, режим, таймауты, профили.
- Вердикт: `Tools/e2e/results/heavy/<время>-<профиль>-<набор>/verdict.json` (вне Git) + текст + код
  0 — можно вливать, 1 — регрессия, 2 — вердикт не получен (тоже нельзя).
- Проверки: ветка поверх свежей origin/dev; код worker содержит origin/dev; Editor свободен; по сценарию —
  результаты всех участников (passed, status, все checks), logIssues по known-noise (порт E2ELogVerdict.psm1),
  архивы логов участников (UxrComponentNotFound/NRE роняют, Exception считается), нативные падения, уборка стенда.
- EditMode (следующая итерация): файл известных красных в Tools/e2e, новый красный — «нельзя».

## План

- [x] Исследование стенда, проб и брокера; дизайн согласован с пользователем
- [x] Итерация 1: heavy.py, набор default (scripted-input-smoke S+1, scripted-wall-purchase S+2), вердикт
- [x] Прогон итерации 1 на worker (зелёный, негатив, повтор старта)
- [ ] Приёмка итерации 1 пользователем
- [ ] Итерация 2: EditMode + известные красные
- [x] Раздел в AGENTS.md (черновик) и Tools/e2e/heavy/README.md
- [ ] Тесты разбора набора и вердикта (после приёмки)
- [ ] Ревью launch-infra, вливание, сообщение координатору e2e-regression

## Этапы

Этапы и области записи — в [plan.json](plan.json).

## Статус

2026-10-20: итерация 1 прогнана на worker (аренды 863, 868, 871, 874, 880; все finish/receive без изменений).
- default (smoke S+1 + wall-purchase S+2): код 0 трижды, ~4–5 мин на набор, Ready 76–87 с.
- Негатив: таймаут сценария 10 с — код 1, FAIL с причинами поимённо; Ready-таймаут 30 с — повтор старта и код 2.
- Находки по ходу: счётчик Exception ловил стековые кадры (исправлено); залипший Failed после своего таймаута
  PlayLaunch — остановленный стенд; устаревшие heartbeat клонов; клон MPPM не подключился к MPE (infra_issue 10300).
- Решения пользователя: свежесть — только игровые пути dev; один повтор старта стенда.
- Ждёт приёмки пользователем; затем тесты, ревью launch-infra, вливание. Отчёты: Tools/e2e/results/heavy/ (вне Git).

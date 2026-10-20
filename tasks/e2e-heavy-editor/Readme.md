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
- [x] Итерация 1: heavy.py, набор default, вердикт; прогоны на worker; приёмка; тесты 37/37; ревью; влито 911f93b37
- [x] Бой scripted-combat-hit в default (этап 2 влит раньше)
- [x] Итерация 2: EditMode + известные красные, замечания ревью launch-infra; прогон на worker
- [ ] Приёмка итерации 2 пользователем
- [ ] Тесты итерации 2, ревью launch-infra, вливание, журнал зоны

## Этапы

Этапы и области записи — в [plan.json](plan.json): `work` (итерация 1, влит), `it2`.

## Статус

2026-10-20, итерация 1 влита (origin/dev 911f93b37): default из трёх сценариев — код 0 за 6,5 мин (аренда 886);
негатив — код 1 (871/874), повтор старта — код 2 (880). Решения пользователя: свежесть по игровым путям,
один повтор старта стенда. Клон MPPM изредка не подключается к MPE — infra_issue 10300.

Итерация 2 (аренда 894): `--editmode also|only` — полный EditMode через MCP run_tests/get_test_job, поимённо
против Tools/e2e/editmode-known-red.json (35 красных, база совпала с этапом 1); набор + EditMode — код 0 за 14 мин.
Замечания ревью: код 2 при любом сбое heavy; сверка кода worker с текущими файлами ветки (игровые пути,
снимок через копию индекса); код 2 и Ready — уточнён текст AGENTS.md; r5 в docstring.
Находки: распаковка ответа get_test_job уходила в поле result job; get_test_job во время тестов отвечает до 90 с.

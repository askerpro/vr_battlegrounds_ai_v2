# lobby-decor-builders: предупреждения EditMode в heavy, 2026-10-24

Для зоны:
- В UTF 1.6 (NUnit 3.5) нет `Assert.Warn`. Предупреждение теста — `Assert.Inconclusive("[Warn:<область>] …")`, один вызов
  в конце теста. `Tools/e2e/heavy/editmode.py` собирает такие результаты в `warnings` записи EditMode и в заметки
  `verdict.json` («предупреждение: <тест> — <первая строка>»), вердикт не роняет; Inconclusive без маркера
  (`[TestEnvironment]` и т.п.) — отдельная заметка. `cli.py` пишет число предупреждений в сводку EditMode.
- Первый потребитель: `MapLightingAuditTests` (`[Warn:lighting]`, правила света на картах Build Settings).
- summary MCP (`TestRunnerService`) по-прежнему не считает Inconclusive (total = pass + fail + skip) — это vendor-код,
  меняет только сопровождающий; heavy читает строки results, поэтому предупреждения видны.
- Тест: `tests/test_editmode.py::test_warn_inconclusive_is_warning_note_and_keeps_pass`.

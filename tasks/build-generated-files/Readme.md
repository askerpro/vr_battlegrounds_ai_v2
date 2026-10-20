# Build-Game: чистые сборочные checkout после сборки

2026-10-20. Задача создана `task-new`; ветка `task/build-generated-files`, worktree `F:/CodexWorktrees/build-generated-files`.

## Цель

После `Tools/release/Build-Game.ps1` выделенные сборочные checkout `build-<config>-*` не остаются с изменёнными
отслеживаемыми файлами (packages-lock.json, PC.asset), и следующий `Build-Game -Ref` не отказывает; на неожиданных
правках отказ сохраняется.

## Решение

Разбор природы файлов — [Details.md](Details.md). Этапы и области записи — [plan.json](plan.json).

- (b) `Assets/URPDefaultResources/PC.asset`: в dev фиксируются значения `m_Prefilter*`, которые детерминированно
  пишет свежая Win64-сборка (`3/1/0`); Android PC.asset не трогает.
- (c) `Tools/release/BuildCheckoutGenerated.ps1`: точный список generated-файлов и проверка природы diff.
  `Build-Game.ps1` возвращает их к индексу до `-Ref` и после сборки (diff — в `Logs/`), `Run-QuestE2E.ps1 -Build` —
  перед сборкой. Неожиданные правки по-прежнему отменяют запуск.
- (a) lock MCP не унифицируется: embedded-копия — окружение worker/агентов (зона agent-infra).

## Статус

- [x] Исследование, реализация, синтаксис PS 5.1, EOL.
- [x] Классификация на реальных build-test checkout (read-only): lock и PC.asset распознаны, транзиентные файлы сборки — нет.
- [ ] Прогон `Build-Game -Ref` на паре build-test — после согласования окна со scene-detail.
- [ ] Ревью зон launch-infra и performance; приёмка пользователем; вливание.

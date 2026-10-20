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
- [x] `-DryRun -Ref da78253b6` на паре build-test (PS 5.1 и pwsh 7): вместо отказа — «будут возвращены», ревизия без `-dirty`.
- [x] Вариант (b), согласован со scene-detail (hub 10204/10214/10222): `-Ref` = текущий HEAD, подставной `-Unity`;
  lock и PC.asset возвращены, diff байт-в-байт равен сохранённым патчам scene-detail, checkout чисты, `Build/` не тронут.
- [x] Отрицательный путь на временном sparse-worktree: embedded-копия при git-форме, лишняя правка lock, staged,
  `m_Name` в PC.asset, другой ассет — отказ с причиной; generated-only — проходит.
- [x] `Run-QuestE2E -Build -DryRunBuild` (pwsh 7) с модулем на чистой паре.
- [x] Полная сборка `quest,server` `-Ref da78253b6` на build-test (06:18, exit 0, uxr-ids совпали): после неё
  Build-Game вернул lock (обе) и PC.asset (win64), оба checkout чисты; `-DryRun -Ref` на другой SHA не отказывает.
- [x] Ревью launch-infra и performance — approve (раунд 1, 0df79a52a).
- [x] Приёмка пользователем 2026-10-20. Тесты: `Tools/release/tests/test_build_checkout_generated.py` (10/10).
- [ ] Повторное ревью на финальном SHA и вливание.

Логи проверок — `reports/` (ignored).

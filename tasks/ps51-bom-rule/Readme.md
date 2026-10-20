# PowerShell 5.1: BOM-правило с автопроверкой

2026-10-20. Задача создана `task-new`; ветка `task/ps51-bom-rule`, worktree `F:/CodexWorktrees/ps51-bom-rule`.
Найдено в [build-generated-followups](../build-generated-followups/Readme.md).

## Цель

Решение пользователя 2026-10-20: единственная целевая оболочка — встроенный Windows PowerShell 5.1 (вызывается как `powershell`),
pwsh 7 не используется; нужна схема с минимальной дальнейшей поддержкой. 5.1 читает `.ps1`/`.psm1` без BOM как ANSI,
поэтому кириллица ломает разбор: `Run-QuestE2E.ps1` и `Run-E2E.ps1` под 5.1 падали при импорте
`Tools/e2e/E2EDiagnosticInputs.psm1`, сохранённого в UTF-8 без BOM. Корень регрессии — `.editorconfig` `[*] charset = utf-8`:
VS Code/Rider при сохранении снимают BOM.

## Решение

- `Tools/e2e/E2EDiagnosticInputs.psm1`, `Tools/Probes/arsenal-thread-wait-chain.ps1`, `Tools/Probes/mp5k-lobby-copy-diff.ps1` —
  добавлен UTF-8 BOM (содержимое не менялось). Теперь все 17 `Tools/**/*.ps1|psm1` с не-ASCII сохранены с BOM.
- `Docs/release.md`: команды Build-Game через `powershell` вместо `pwsh`, плюс правило про 5.1 и BOM.
- Правило действует на `Tools/**`; `tasks/**` — артефакты задач, правило их не касается (решение пользователя).
- Автопроверка — у сопровождающего (решение пользователя): запрос в hub, событие 10478, адрес agent-coordination-protocol:
  `.editorconfig` `charset = utf-8-bom` для `Tools/**.{ps1,psm1}` и отказ pre-push для таких файлов без BOM; попутно —
  `*.psm1` нет в PhaseA `eol=lf`. Product-тест не добавляется.

Области записи — [plan.json](plan.json).

## Статус

- [x] BOM в трёх файлах, `Docs/release.md`; разбор 5.1 без ошибок.
- [x] Диагностика: `Import-Module E2EDiagnosticInputs.psm1` под 5.1 — 17 команд экспортированы; сухой прогон
  `powershell -File Tools/e2e/Run-QuestE2E.ps1 -Build -DryRunBuild` (5.1.22621) на временном sparse-worktree с копией
  `Tools/e2e`/`Tools/release` ветки — доходит до печати команд Unity, exit 0, без правок других файлов.
- [x] Запрос сопровождающему отправлен (10478); штатное уведомление — manual (route-resolve: client_mismatch).
- [ ] Ревью launch-infra.
- [ ] Приёмка пользователем и вливание.

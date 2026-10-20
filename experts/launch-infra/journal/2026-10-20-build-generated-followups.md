# build-generated-followups: доработки generated-файлов сборочных checkout, 2026-10-20

Закрыты открытые пункты [build-generated-files](2026-10-20-build-generated-files.md).
- `Run-QuestE2E.ps1 -Build`: текущий проект (`$RepoRoot`), переданный явным `-Win64BuildWorktree`/`-AndroidBuildWorktree`,
  не выделенный checkout — generated-файлы в нём не возвращаются, любое изменение отменяет парную сборку (как `Build-Game.ps1`).
- `Test-GeneratedPrefilterChange`: заголовок diff пропускается только до первого `@@`; строки hunk `--- `/`+++ ` — отказ.
- `BuildProvenance.cs`: `Tools/release/BuildCheckoutGenerated.ps1` входит в отпечаток `tools`.
- Pair manifest `Build-Game.ps1`: после сборки checkout классифицируется модулем, sha256 generated-файлов берётся до возврата;
  запись `dirty.entries` builder manifest со статусом ` M` и тем же путём и sha256 получает `generated=true` и не учитывается
  в `dirty.value` (поле `dirty.generatedRule` описывает правило).
Инварианты: правило классификации — только в `BuildCheckoutGenerated.ps1`; `BuildProvenance` (build.json) остаётся
наблюдателем сырых фактов и generated не помечает (решение пользователя 2026-10-20). Без классификации, при другом sha256,
staged и в повторной агрегации (legacy recovery) запись остаётся dirty.
Проверено: тесты `Tools/release/tests/test_build_checkout_generated.py` 18/18; компиляция на worker (тикет 879) без ошибок;
п.1 — сухой прогон `-DryRunBuild` на временном sparse-worktree (unit-теста нет: логика внутри сценария Run-QuestE2E).
Замечено: Windows PowerShell 5.1 не разбирает `Tools/e2e/E2EDiagnosticInputs.psm1` (UTF-8 без BOM), поэтому
`Run-QuestE2E.ps1` под 5.1 падает на импорте модуля; pwsh 7 на машине не установлен.

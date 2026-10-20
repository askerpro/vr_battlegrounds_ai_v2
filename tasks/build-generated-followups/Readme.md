# Доработки generated-файлов сборочных checkout

2026-10-20. Задача создана `task-new`; ветка `task/build-generated-followups`, worktree
`F:/CodexWorktrees/build-generated-followups`. Продолжение влитой задачи [build-generated-files](../build-generated-files/Readme.md)
(origin/dev d4cb9c519): необязательные пункты ревью launch-infra.

## Цель

1. `Tools/e2e/Run-QuestE2E.ps1 -Build`: текущий проект (`$RepoRoot`), указанный явным `-Win64BuildWorktree`/`-AndroidBuildWorktree`,
   не выделенный сборочный checkout — generated-файлы в нём не возвращаются к индексу, любое изменение отменяет
   парную сборку (как в `Build-Game.ps1`).
2. `Tools/release/BuildCheckoutGenerated.ps1`, `Test-GeneratedPrefilterChange`: заголовок diff пропускается только до
   первого `@@`; строки hunk `--- …`/`+++ …` — изменения файла.
3. `BuildProvenance.cs`: `Tools/release/BuildCheckoutGenerated.ps1` в отпечатке `tools`.
4. Переписанный UPM lock в сборочном checkout не делает pair manifest dirty. Решение пользователя 2026-10-20:
   `BuildProvenance` остаётся наблюдателем сырых фактов; `Build-Game.ps1` после сборки классифицирует checkout тем же
   модулем, сохраняет sha256 generated-файлов до возврата и помечает совпавшие записи dirty builder manifest
   `generated=true`; `dirty.value` пары их не учитывает. Повторная агрегация (legacy recovery) пометок не ставит.

Области записи — [plan.json](plan.json).

## Статус

- [x] Правки 1–4; синтаксис PS 5.1 (0 ошибок разбора), EOL LF.
- [x] Существующие тесты `test_build_checkout_generated.py` без изменений: 10/10.
- [x] Диагностика (без тестовых ожиданий, логи — scratchpad сессии): п.1 на временном sparse-worktree с
  `-DryRunBuild` — отказ с причиной для `$RepoRoot`, старая версия печатала «Будут возвращены»; п.2 — строки
  `--- `/`+++ ` в hunk теперь отказ, старая версия принимала; п.4 — `Write-BuildPairManifest` через AST на
  подставных manifest: совпавший sha256 → `dirty.value=false`, иной sha/чужая правка/нет классификации → `true`.
- [x] Компиляция `BuildProvenance.cs` на worker: тикет 879, вход 2c024bb2, 0 ошибок (отчёт `reports/compile-2026-10-20.txt`).
- [x] Ревью launch-infra раунд 1 — approve (622cb8604).
- [x] Приёмка пользователем 2026-10-20. Тесты `test_build_checkout_generated.py` 18/18: +2 на строки hunk,
  похожие на заголовок (п.2), +6 на пометку pair manifest (п.4, `Write-BuildPairManifest` через AST).
  П.1 unit-тестом не покрыт (логика внутри сценария Run-QuestE2E), доказательство — сухой прогон выше.
  Журнал — `experts/launch-infra/journal/2026-10-20-build-generated-followups.md`.
- [ ] Ревью финального SHA и вливание.

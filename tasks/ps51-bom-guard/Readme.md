# BOM-правило PowerShell 5.1 для Tools/**/*.ps1|psm1

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/ps51-bom-guard` (схема «задача = агент»).

## Цель

Редакторы и pre-push сохраняют UTF-8 BOM в Tools/**/*.ps1 и *.psm1 с не-ASCII, чтобы Windows PowerShell 5.1 не читал их как ANSI; *.psm1 в LF-области наравне с *.ps1.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/ps51-bom-guard/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

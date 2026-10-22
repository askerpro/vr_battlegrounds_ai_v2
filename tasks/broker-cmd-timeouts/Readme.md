# Длинный таймаут для begin/finish/recover/publish-base

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/broker-cmd-timeouts` (схема «задача = агент»).

## Цель

Агенты запускают begin, finish, recover и publish-base с таймаутом инструмента не меньше 10 минут или фоном и дожидаются завершения.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/broker-cmd-timeouts/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

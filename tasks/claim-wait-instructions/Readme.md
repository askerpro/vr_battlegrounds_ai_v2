# Инструкции агентов: claim --wait --out вместо watch-ticket → claim

2026-10-21. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/claim-wait-instructions` (схема «задача = агент»).

## Цель

Продуктовые инструкции агентов описывают ожидание оффера и захват одной фоновой командой editor-broker.py claim --wait --out (runtime 6c896a4), вместо пары watch-ticket → claim, из-за которой агенты теряли 60-секундный оффер.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/claim-wait-instructions/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

# Телепорт только в режиме отладки (prod Quest+Server)

2026-10-24. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/teleport-debug-gate` (схема «задача = агент»).

## Цель

Телепорт (дуга со стика и кнопки экрана Отладка) на шлеме доступен только при включённом режиме отладки, даже у админа; prod-сборки Quest и Server.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/teleport-debug-gate/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

## Итог (2026-10-24)

- `DebugTeleportLocomotion` — единственный владелец `enabled` у `UxrTeleportLocomotionBase` локального аватара:
  дуга со стика только в режиме отладки устройства и без приглушений; `MenuStickScroll` приглушает через него.
- Сервер (`DebugModeNetwork`): телепорт из меню — админу с включённым режимом отладки на устройстве.
- Решение пользователя: `DebugAdminPolicy.ServerAllows` — любой сервер, prod тоже (жест даёт права админа; боты тоже).
- Prod quest+tablet+server собраны с `a8d3d1869` на build-prod-* (uxr `cafa6426daccb1bb`); пользователь принял
  и велел вливать сразу — ревью зон и heavy пропущены по его указанию, EditMode не запускался.
- Хвост: в dev устарел `ContentFingerprint` Lobby в `MapRuntimeCatalog.asset` — каждая сборка оставляет diff
  в build-checkout (отдельная задача).

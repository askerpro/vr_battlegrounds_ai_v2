# Каталог карт: отпечаток Lobby

2026-10-25. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/map-catalog-lobby-fingerprint` (схема «задача = агент»).

## Цель

MapRuntimeCatalog.asset в dev совпадает с тем, что пересчитывает сборка; сборки не оставляют diff в build-checkout.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/map-catalog-lobby-fingerprint/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

## Итог (2026-10-25)

`ContentFingerprint` Lobby в `Assets/Data/Maps/MapRuntimeCatalog.asset` устарел после `332a65214`
(lobby-decor-builders: сцена Lobby изменена без пересборки каталога). Prod-сборки 25d7af379, a8d3d1869
пересчитывали его одинаково (`MapRunPreflight.ContentFingerprint`) и оставляли diff в build-prod-*, из-за чего
следующий `Build-Game.ps1 -Ref` отказывал. Значение взято из diff сборки (совпал побайтно у трёх сборок
на обеих платформах). Проверка: следующая сборка не оставляет изменений в `MapRuntimeCatalog.asset`.

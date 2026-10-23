# Документация пересборки оружия и хвосты ревью

2026-10-23. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/weapon-rebuild-docs-followups` (схема «задача = агент»).

## Цель

Закрыть хвосты влитой задачи weapon-rebuild-sight-guarantee: решение «без пересвести позже» и маршрут нового/сменённого прицела в Docs/weapons, актуальные разделы sight-calibration, gameplay, grip-extraction и SKILL add-weapon, шапка known-red, мелкие места кода из ревью.

## Этапы

Этап `work` ([plan.json](plan.json)):
- документация: решения 2026-10-22/23 в `Docs/weapons/decisions.md`, схема «всё или ничего» и маршрут нового/сменённого
  прицела в `Docs/weapons/sight-calibration.md`, шарнир отдачи в `Docs/gameplay.md` и SKILL add-weapon, счётчики и
  запись snap-точек в `Docs/grip-extraction.md`, шапка и reason в `Tools/e2e/editmode-known-red.json`;
- код (решения пользователя 2026-10-23):
  - автосведение родного прицела без записи при пересборке, кнопка стенда «Сбросить запись», таблица в транзакции;
  - `_assetId` после разметки тегов в `WeaponRebuildFinish.Complete`;
  - комментарий шарнира `RecoilAccumulator`.

Не делается (решение пользователя): ограничение удалённых узлов переноса хватов префабом магазина.

## Статус

2026-10-23: код и документы в worktree, не закоммичены. Дальше — проверка на worker, приёмка пользователем.

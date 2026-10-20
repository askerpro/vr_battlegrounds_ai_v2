# Лаги инспектора Play Launch

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/play-launch-inspector-lag` (схема «задача = агент»).

## Цель

Устранить лаги окна/инспектора Play Launch (PlayLaunchWindow): найти и убрать лишние перерисовки и тяжёлую работу в OnGUI/Update.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Этап `work` пишет `PlayLaunchWindow.cs`, `PlayLaunch.cs` и материалы задачи.

## Статус

- Причина найдена и подтверждена замером на worker: `PlayLaunch.Plan` (Lobby) ~1000 мс, почти всё — рекурсивный
  `AssetDatabase.GetDependencies`; окно звало его в каждом OnGUI при безусловном Repaint 10 Гц. Подробности — [Details.md](Details.md).
- Исправление: кэш плана/статуса в окне, перерисовка по изменению; кэш разметки сцены в `PlayLaunch.Resolve` со сбросом по `projectChanged`.
- Проверки: компиляция и замеры в аренде брокера; EditMode `DebugBootstrapEditorOnlyTests`. Ждёт ручной приёмки пользователем.

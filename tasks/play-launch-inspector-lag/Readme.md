# Лаги инспектора Play Launch

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/play-launch-inspector-lag` (схема «задача = агент»).

## Цель

Устранить лаги окна/инспектора Play Launch (PlayLaunchWindow): найти и убрать лишние перерисовки и тяжёлую работу в OnGUI/Update.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Этап `work` пишет `PlayLaunchWindow.cs`, `PlayLaunch.cs` и материалы задачи.

## Статус

- 2026-10-20: этап `work` влит в origin/dev — 24f806907 (вливание подтверждено пользователем). Этап `journal` —
  запись в `experts/launch-infra/journal/`.

- Причина найдена и подтверждена замером на worker: `PlayLaunch.Plan` (Lobby) ~1000 мс, почти всё — рекурсивный
  `AssetDatabase.GetDependencies`; окно звало его в каждом OnGUI при безусловном Repaint 10 Гц. Подробности — [Details.md](Details.md).
- Исправление: кэш плана/статуса в окне, перерисовка по изменению; кэш разметки сцены в `PlayLaunch.Resolve` со сбросом по `projectChanged`.
- Проверки: компиляция и замеры в аренде брокера; EditMode `DebugBootstrapEditorOnlyTests`; запуск Play Launch на Lobby
  и неразмеченной сцене. Ревью launch-infra round 1 — approve (замечание про dependency hash учтено, нужен round 2).
- Аренда 941 (вход e76a6cb91 = код 3fc53386f): компиляция чистая; кэш с hash: первый Plan(lobby) 2985 мс после
  domain reload, повторный 3 мс, вывод идентичен; PlayLaunch Lobby (host) → Ready → Cancel → Idle, cleanup=true;
  неразмеченная `UI_SETUP/TeamSelection_Screen.unity` (без AllowUnmarked — UnmarkedScene) → Ready → Cancel → Idle,
  cleanup=true, открытое окно показывало Ready/владельца и вернулось в Idle; консоль 0 ошибок; EditMode
  DebugBootstrapEditorOnlyTests 4/4.
- Heavy e2e пропущен по решению пользователя 2026-10-20 (передано сессией-инициатором): правки только Editor-кода.
- Ручная приёмка отзывчивости окна: пользователь 2026-10-20 (передано сессией-инициатором): «я уже проверил окно на
  прошлом прогоне - все ок». По записям задачи окно с новым кодом открывалось только на worker в аренде 936
  (вход 3a2da9068 = код коммита c37b9ac2f), то есть до 3fc53386f (dependency hash в кэше разметки). Какой именно
  прогон смотрел пользователь, агент не наблюдал. Коммит 3fc53386f покрывают только автоматические проверки по заявке 941.

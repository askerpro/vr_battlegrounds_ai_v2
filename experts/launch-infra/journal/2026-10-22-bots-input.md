# bots-input: стенд ботов — демо Input Smoke, тестовый ввод в продукте, 2026-10-22

Влито в dev c7b8d6ab3 (задача bots-input).

Для зоны:
- `BotCombatStandEditor` удалён вместе с кейсами T01–T10 (решение пользователя 2026-10-21); вместо него
  `BotStandInputSmokeEditor` — Plan → Play → Status → Cancel через PlayLaunch, без второго запускателя и записи портов.
  `expert.json` обновлён.
- Тестовый XR-ввод перенесён в `Assets/Scripts/Player/Input/` (GUID сохранены): единственный писатель камеры,
  трекинга и кнопок — `AvatarInputDevice`; `E2EScriptedInput` — фасад. В VRBG_NO_E2E Local-аватару ввод не подаётся.
- Клоны MPPM, запущенные чужим S+2-прогоном, остаются открытыми и после выхода из Play; host-only запуск
  (`ClientCount=0`) их не использует — сценарий стенда без экземпляров редактора (наблюдение 2026-10-22).
- Открыто для сведения: namespace `DevTools.E2E.Input` в продуктовом коде, путь отчёта демо `tasks/bots-input/reports`.

Материалы: tasks/bots-input/Readme.md.

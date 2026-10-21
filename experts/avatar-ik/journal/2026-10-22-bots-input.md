# bots-input: внешний аватар с вводом на этой машине (патч 89), 2026-10-22

UltimateXR patch 89 — `UxrAvatar.InputDriven` и `HasInputHere`: внешний аватар бота на сервере проходит стадию
Update контроллера, получает свой `ControllerInput` и Local-гейты хвата и курка. Без флага `HasInputHere == Local`,
поведение SDK прежнее. Влито в dev c7b8d6ab3 (задача bots-input).

Для зоны:
- Писатели не менялись: запястья — `UxrControllerTracking.UpdateAvatar`; local pose камеры и сенсоры пишет только
  `AvatarInputDevice` в StageUpdating; корень, кости, BodyIK и калибровку бот не пишет.
- `UxrControllerTracking`: `GlobalControllerConnected` фильтруется по аватару; у бота корутина камеры не стартует.
- Исключения: патч 84 (`UxrStandardAvatarController`) и патч 24 (троттлинг IK удалённых) для InputDriven не действуют —
  боты считают полный IK каждый кадр.
- Открыто: стоимость IK ботов и аллокация геттера `ControllerInput` при InputDriven на Quest-хосте (перф-этап);
  на listen-хосте с HMD проверить, что TrackedPoseDriver камеры бота выключен.

Материалы: tasks/bots-input/changelog/2026-10-20-sdk-89.md, contracts/bot-input-design.md.

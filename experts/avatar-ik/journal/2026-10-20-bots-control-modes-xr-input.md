# bots-control-modes: скриптовое UXR-устройство в телах игрока, 2026-10-20

Влито `43332888b`. В PlayerBase и GhostAvatar добавлен выключенный объект `ScriptedInput` (под `Components`):
`ScriptedControllerInput` (Dual) и `ScriptedControllerTracking` с сенсорами трекинга Quest 3, сглаживание 0;
варианты наследуют, UXR дописал служебные `__prefabGuid/__isInPrefab`. Писатели: камера — `E2EScriptedInput` в
`StageUpdating(Update)` (как E2EHeadPose), запястья — штатный UxrControllerTracking (слот 1), сенсоры в пространстве
корня минус HeightOffset. Кости, корень, пивот, BodyIK, калибровка не пишутся. Замер (S+2): запястья ≤ 0,017 мм,
угол запястья до 7,6° после PostProcess (штатные поздние писатели — clamp ArmIK/HitReaction). Шаблоны путей
`Assets/Prefabs/Player/*` в expert.json устарели после player-prefab-layout — обновить владельцу зоны.

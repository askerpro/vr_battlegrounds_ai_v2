# bots-input: хват бота — штатный контроллер по фронту Grip, 2026-10-22

Бот берёт предметы как человек: фронт Grip в кадре устройства, штатный `UxrStandardAvatarController` и правила
PlayerGrabManager/GrabRules; `GrabObject`/`ReleaseObject` в коде ботов не вызываются (сторож `BotAuthorityTests`).
Влито в dev c7b8d6ab3 (задача bots-input).

Для зоны:
- Патч 89 в `UxrGrabManager.Manipulation`: AlignToController и дальнее отпускание учитывают `HasInputHere`
  (бот с InputDriven); без флага — как раньше. Вибрация бота отсекается `SdkHapticRouter` (нелокальный источник).
- `InteractionFeedback`: ранний выход при неизменном состоянии без готовности — нет покадрового Verbose.
- При смерти бот не разжимает руку: снаряжение отпускает только `AvatarTeardown` (один путь сброса).

Материалы: tasks/bots-input/Readme.md, changelog/2026-10-20-sdk-89.md.

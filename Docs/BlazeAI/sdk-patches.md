# Патчи импортированного Blaze AI

Игра использует штатные AlertStateBehaviour, CoverShooterBehaviour и GoingToCoverBehaviour.
Готовые боевые решения пакета не заменяются своими. Патчи не зависят от игровой сборки.

- `Scripts/BlazeAI.Runtime.asmdef` и три Editor asmdef: отделение runtime от инспекторов,
  ссылка игровой сборки на runtime. Инспекторы поведения/добавочных компонентов используют BlazeAI.Editor.
- `BlazeAI.TargetFilter`: фильтр цели по компонентам (команда, роль, живость) вместо ручных тегов.
  Если адаптер не установлен, остаётся штатная проверка hostileTags.
- `BlazeAI.CoverFilter`: отбор защитной геометрии перед выбором укрытия в GoingToCoverBehaviour.
  Без адаптера отбор пакета не меняется. Статическая геометрия без CoverSurface — Hard по контракту игры.
- `BlazeAI.ColliderIgnoreFilter` / `IsSelfCollider`: исключение видимого тела владельца из
  проверок видимости отдельного рига. Запросы не создают собственную модель урона.
- `BlazeAI.RequireCompletePaths`: в MoveTo прекращает движение и очищает старую точку при отказе
  полного пути. Выключено по умолчанию, включается BotCombatDriver.

При обновлении пакета перенести эти точки интеграции и прогнать
[пилот](../tasks/bots-blaze-integration.md). UltimateXR/Mirror этой задачей не патчатся.

# Зона level-design утверждена картой экспертов

Пользователь 2026-10-11 согласовал карту экспертов: level-design владеет содержимым карт и лобби (иерархия,
геометрия, свет и запекание, окклюзия в сценах, navmesh как контент, расстановка спавнов/станций/укрытий,
декор и эмбиент, валидация карт); SDK — ParticlePack. Соседи: gameplay (загрузка, каталог, правила спавна и
матча), performance (бюджеты, ревью окклюзии), bots (использование navmesh), launch-infra (сцены в сборке).

Инвентаризация (read-only, 2026-10-11, origin/dev `71674333`): navmesh ботов строится в памяти сервера
(`BotNavMesh`), в сценах его нет; валидаторы и Bake Occlusion лежат в `Editor/.../Gameplay/`;
`LobbyRangeLayoutBuilder` — в `Editor/.../Weapons/Calibration/` (владелец не определён картой).

Источники: `tasks/expert-workspace-infra/expert-map.md`; `Assets/Scripts/Bots/BotNavMesh.cs` (summary).

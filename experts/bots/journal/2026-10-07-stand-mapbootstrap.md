# BotCombatStand переведён на MapBootstrap (изменение gameplay)

Коммит `24ea57538cf0694b4d8f455a4f7fe78c6ddbf2ce` (2026-10-07, map-runtime-bootstrap): у MapReferee
удалён неуправляемый путь; BotCombatStand — единственная сцена со сценовым судьёй — переведена на
MapBootstrap, каталог получил список отладочных стендов `MapRuntimeCatalog.DebugMaps`. В нашей зоне
изменён `Assets/Editor/VR_Battlegrounds/Bots/BotCombatStandBuilder.cs`. Связанные коммиты
`4c80856bb`, `ed66bbf33` (допуск карты для сетевых предметов) правили `BotGunner.cs` (3 и 1 строка).

Проверки этого изменения для бот-стенда в источниках не указаны — unknown.

Источники: `git show 24ea57538`; `Docs/game-manager.md` (поиск `DebugMaps`).

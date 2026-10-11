# PhysicalArenaLayout отделён от геометрии шести сцен

Коммит `7d3c0a63d9b977a9a18fa04f6195f24935df2150` (2026-10-04). Lobby и пять карт: общий чертёж без
Collider, Geometry скрыта, CalibrationAnchors активны; собственный Environment (Geometry/MapShell),
спавны и станции — в Gameplay. Валидаторы: `MapGameplayHierarchy.ValidateAll`,
`PhysicalArenaLayoutMigration.ValidateAll`.

Проверено (по документу): шесть сцен PASS, AndroidCompileGate PASS, 22/22 существующих EditMode PASS,
occlusion запечён для шести сцен; сверка с копиями — 6989 компонентов сохранили поля и ссылки, сетевые ID и
меши/материалы совпали; калибровка Lobby работает при нуле Collider в разметке. Окружение прогона — unknown.

Не выполнено: проверка пользователем в шлеме; разделение LobbyRange (направление описано, пути не изменены).

Источники: `Docs/scene-hierarchy.md` «Физическая разметка», «LobbyRange: частичное разделение»,
«Изменения и проверка»; `Docs/README.md` строка «Единая принадлежность и иерархия объектов карт».

# 2026-10-24 — maps-rename: имена карт и структура сцен

- Карты названы по темам: TestMap1 → `Maps/Game/MilitaryBase` («Военная база»), TestMap2 → `Maps/Game/Warehouse`
  («Склад»), ServiceYard → `Maps/Game/`; TestMap3 → `Maps/Dev/BlockStand`, ReferenceMap04 → `Maps/Dev/`.
  Игровые карты и карты разработки разделены подпапками внутри `Maps/`: инструменты (окклюзия, миграции арсенала,
  тесты) ищут по префиксу `Assets/Scenes/Maps/` рекурсивно.
- Стенды — единый корень `Assets/Scenes/Stands/<область>/` (Avatar, Arsenal, Bots; LevelDesign для генерируемых
  ContactCatalogLab/IndustrialCandidateReview). Самостарт мимо Offline — только `Stands/Avatar/`.
- Ключи станций в сценах (`TestMap1.SpawnZone.ArsenalWall-…`) сохранены: стабильные идентификаторы, не имена сцен.
- Открыто: `MapGameplayHierarchy.ValidateAll` падает на Lobby («1/1» вместо ожидаемых 4 станций) после
  lobby-arena-cleanup; известный красный `testmap1-beam-overlap` теперь на MilitaryBase. Следом — задача
  `map-military-base`: висящие блоки и упавший цилиндр в блокауте, паспорт, арт-пасс.

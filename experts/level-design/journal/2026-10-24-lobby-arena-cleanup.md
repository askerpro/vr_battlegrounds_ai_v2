# lobby-arena-cleanup: чистка арены Lobby, 2026-10-24

Для зоны:
- Граница стрельбища — торцевые стойки `Table_North/South` (0,96 м, полка для оружия). Их проём (x −4,1…3,6) — остаток
  старой раскладки (`ArsenalWallOpenings` резал стойку по объёму, который потом переехал к западной стене; повторный
  запуск инструмента вернёт стойку целиком). Проём закрыт мешками `SM_SandBarrier_01` в два ряда (z 7,25/7,7,
  1,26 м) в `NorthLowShootingBoundary`/`SouthLowCampBoundary`, юг — зеркало севера; сдвиг ±4 см, поворот 1–3°.
- Мешки — Lightmap Static без проб: дубль без запекания чёрный (lightmapIndex −1). После правок — «Ночь: подготовить
  запекание» → Generate Lighting.
- `ShootingPositionNorth/South` — пустые точки акцента перед стойкой (z ±6,7) под `TentAccent_Range*` (z ±6,9).
- Legacy `TextMesh` (таблички «КАЗАРМА»/«КПП», `GUI/Text Shader`) рисовался сквозь геометрию — заменён на TextMeshPro;
  номера дорожек — TMP «Номер N» вместо семисегментных кубов; `RangeDistanceLabel` и мониторы дорожек удалены.
- Удалён выключенный корень `Scene` (остаток сцены-примера); при удалении объектов чистить пустые записи
  `SceneDetailApplier._renderers` (тест `LobbyArenaCleanupTests`).
- Мишень `ShootingTarget.prefab`: разметка зоны головы `Pivot/HeadZone` (`ShootingTargetZone`).
- Окклюзия Lobby не перезапекалась (новые мешки — не окклюдеры).

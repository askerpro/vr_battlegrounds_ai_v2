# Полные паки окружения вынесены в EnvironmentPackCatalog

Коммит `2d0789bb0d140c1f5545542af5488bdb5ccfbf9b` (2026-10-05). Паки и авторские демо — в
`F:/UnityProjects/EnvironmentPackCatalog`; в игре остались 16 ассетов (около 13,2 МиБ), из игры убрано
около 10,96 ГиБ. По документу: GUID/зависимости и Android PASS; каталог — 7933 GUID, 24 SceneAsset,
Industrial preview PASS. Тем же днём `92488a18c1973c41cb37e33f06f6372bda6323ed` — контакты блокаута и
профиль тела карты.

Ограничение: Unity-сканер, галерея, разметка и сборщики к внешнему каталогу не адаптированы.
Инструменты переноса/проверки: `Tools/AssetCatalog/` (`CatalogImportValidation.cs`). Манифесты — в
локальном отчёте вне Git; их наличие в этом checkout — unknown.

Источники: `Docs/README.md` (абзац «Выполнено отделение полного каталога окружения»),
`Docs/level-design/environment-pack-policy.md`, `Docs/plans/2026-10-05-environment-pack-separation.md`.

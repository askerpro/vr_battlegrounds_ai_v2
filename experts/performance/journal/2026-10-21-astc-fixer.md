# astc-fixer: первый фиксер аудитора — Android ASTC 6x6, 2026-10-21

Аудитор только читает; исправляют фиксеры `AssetAudit/Fixers/` — наследники `AuditFixer` (id правила,
`Plan` = dry-run, `Apply`), находятся через TypeCache (`AuditFixerCatalog`). Запуск: кнопка «Исправить»
в окне аудитора, меню `Asset Audit/Fix Android ASTC in Selected Folders`, MCP
`AuditFixerCatalog.PlanForFolders(ruleId, folders)` → `ForRule(ruleId).Apply(plan)`.

`AndroidAstcFixer` (`texture.android-format`): override Android ASTC 6x6 — конвенция проекта (318 текстур),
max size прежний, Default/Standalone не трогаются, Sprite/GUI/Cursor пропускаются. Применён к 26 текстурам
паков Lobby (19 MilitaryCamp ORM/ORMH/Mask, 4 Catalog/RuralTown, 3 стул Interior): было RGB24/RGBA32 без
override (~4 МБ на 1024 с мипами), стало ASTC 6x6 (~0,5 МБ). PSNR исходник ↔ ASTC 31–56 дБ по каналам,
кадры до/после префабов приняты пользователем. Прирост FPS на шлеме не мерился — это память и трафик.

Грабли: worker в Standalone — Android override в Editor не виден; сравнение делалось копией через
`EditorUtility.CompressTexture(ASTC_6x6)` в PreviewRenderUtility без записи ассетов. Нативный крэш worker
на загрузке Lobby.unity в restore_original (ticket 952) снят сопровождающим, результат сохранён.

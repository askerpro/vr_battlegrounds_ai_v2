# astc-fixer — фиксер ASTC для несжатых текстур паков

Обновлено: 2026-10-21. Owner: astc-fixer (Claude). Ветка `task/astc-fixer`.

## Цель и мотивация
Первый «фиксер» к аудитору ассетов (asset-audit): исправляет находки `texture.android-format` — ставит
Android override ASTC. Причина: 19 ORM-текстур MilitaryCamp имеют Default `textureCompression: None` и без
Android override собираются на Quest несжатыми (RGB24/RGBA32, 1024 px).

## Решения пользователя (2026-10-20)
- Формат — ASTC 6x6 всем: конвенция проекта (318 текстур с override 6x6, других почти нет).
- Охват — паки Lobby (`Assets/Art/Lobby`): 19 MilitaryCamp + 7 Catalog/Interior. Гизмо, превью блоков и
  демо SDK (в сборку Quest не идут) не трогать.

## Архитектура
- `AssetAudit/Core/AuditFixer.cs` — базовый фиксер (id правила, Plan = dry-run, Apply) и `AuditFixerCatalog`
  (TypeCache, `PlanForFolders(ruleId, folders)` — аудит только этого правила → план).
- `AssetAudit/Fixers/AndroidAstcFixer.cs` — override Android ASTC 6x6, max size прежний; UI-текстуры пропускает.
- Окно аудитора: кнопка «Исправить» у правила с фиксером (dry-run → диалог со списком → применение → повторный аудит);
  меню `Asset Audit/Fix Android ASTC in Selected Folders`.

## План
- [x] Опрос форматов проекта (`tools/survey_android_formats.py`), решения пользователя
- [x] Код фиксера, компиляция офлайн и в worker (ticket 938, .meta получены)
- [x] Dry-run в worker (ticket 944): 26 текстур — 19 MilitaryCamp, 4 Catalog/RuralTown, 3 Interior; пользователь
  согласовал все 26
- [x] Применение в worker (ticket 952): 26/26, повторный аудит Lobby — 0 находок `texture.android-format`,
  повторный план — 0; снимок результата 66d36b2e (26 `.meta`)
- [x] Проверка пользователем 2026-10-21: кадры до/после 10 префабов паков (исходник против ASTC 6x6 тем же
  кодеком, ticket 967, ассеты не менялись) и PSNR 26 текстур 31–56 дБ по каналам. Ответ пользователя:
  «Принимаю» (результат фиксера принят). Кадры — `reports/shots/` (вне Git).
- [x] EditMode-тест `Assets/Tests/EditMode/AssetAudit/AuditFixerTests.cs` (каталог, dry-run, Apply): сборка
  AssetAudit 15/15 в worker (ticket 974)
- [x] Ревью performance — approve (раунд 1, event 11205); замечания не блокирующие: публичные MarkApplied/MarkFailed,
  меню не показывает неприменённые действия, 6x6 и для нормалей при расширении охвата (FixHint советует 4x4)
- [x] Heavy editor/default + EditMode (ticket 978) — код 1, падения не из этой задачи: scripted-combat-hit
  («нёс оружие не ровно 0.516»), красные GameTagsTests (теги AK105/Mk14/MP5K) и HandsPackWeaponTests(Scar) —
  те же сообщения в heavy e2e-combat 2026-10-21 02:09 без изменений этой задачи. Остальное зелёное
  (input-smoke, wall-purchase, EditMode 2877 без иных новых красных). Пользователь 2026-10-21: «Вливать».
- [ ] merge, request-base, task-close

## Статус
Готово к вливанию: принято пользователем, тест, ревью, heavy (с обоснованием) пройдены. Списки и отчёты
(dry-run, кадры, PSNR, heavy-лог) — `reports/` (вне Git); verdict heavy —
`Tools/e2e/results/heavy/20261021-032358-editor-default/verdict.json`.

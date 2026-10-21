# astc-fixer — фиксер ASTC для несжатых текстур паков

Обновлено: 2026-10-20. Owner: astc-fixer (Claude). Ветка `task/astc-fixer`.

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
- [ ] EditMode-тест фиксера (`Assets/Tests/EditMode/AssetAudit/AuditFixerTests.cs`), прогон в worker
- [ ] Ревью performance, heavy, merge

## Статус
Применено и принято пользователем 2026-10-21; 26 `.meta` в worktree (finish 952 упал на нативном крэше worker
при restore_original; сопровождающий поднял worker, штатный recover → DONE 66d36b2e, receive). Идёт тест.
Ничего не закоммичено. Список dry-run — `reports/2026-10-20-dry-run-lobby.txt` (вне Git).

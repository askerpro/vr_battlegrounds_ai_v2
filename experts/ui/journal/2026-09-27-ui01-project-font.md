# Кириллица в UI: шрифт проекта — статический Roboto Condensed (UI-01)

Коммит `640ca216311afea36d5d0a80435b5bf0b012d3ab` (2026-09-27), документация `ed66fb76e`.
Причина: из git убрали динамический `LiberationSans SDF - Fallback` — единственный источник
кириллицы; оставшиеся шрифты меню (LiberationSans, RUBIK, LATO из SlimUI) статические, только латиница.
Решение: Roboto Condensed Regular/Bold (OFL), статические атласы; `ProjectFontTool` пересобирает атласы
с сохранением GUID и применяет шрифт к UI-префабам; шрифт — дефолт TMP и глобальный fallback.
Динамический шрифт в проект не возвращать.

Проверено (по сообщению коммита): `UiFontCoverageTests` красный до правки на всех 16 надписях меню;
итоговый прогон и окружение в источниках не указаны — unknown. Ограничение: тест не видит текст,
выставляемый из кода в рантайме. Риск: обновление TMP Essential Resources затирает `TMP Settings.asset`
(случилось 2026-09-28) — ловит `DefaultTmpFont_CoversCyrillic`.

Источники: `Docs/ui-fonts.md` (вводная, «Почему не fallback», «Проверка»); `Docs/troubleshooting.md`
«Кракозябры / квадраты вместо букв в меню»; `git show 640ca2163`.

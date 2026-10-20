# Подготовленный пакет Android-компиляции

2026-10-18. Без изменения runtime и без новых тестовых ожиданий.

1. Fresh fetch origin/dev; HEAD и broker baseline должны включать принятую базу.
2. Технический checkpoint, request owner/task bots-validation, output только
   `tasks/bots-validation/reports`, затем watch-ticket/claim/begin/guard.
3. Единственное действие существующего исполнителя:
   `uv run --with "mcp>=1.20,<2" Tools/TestStand/worker_probe.py --compile-only --instance <guard.unity_instance> --editor-root <guard.project_root> --output tasks/bots-validation/reports/android-compile/result.json`.
   Запуск из корня bots-validation. Исполнитель сам использует штатный proxy
   этого checkout. Native MCP старого root не используется.
4. Дождаться известного результата. В compile-only нет direct Play/MPPM,
   смены EditorSettings, новых gameplay checks или пользовательского ожидания.
5. Сразу штатный finish/receive. Полный JSON/transport log сохраняется локально
   в ignored reports. Gate создаёт только Temp/AndroidCompileGate в worker.

При неизвестном исходе MCP/refresh не повторять действие и не выполнять finish
активной операции; сохранить её IDs/состояние и направить infra_issue сопровождающему.
Статус delayCall stalled — инфраструктурная диагностика, не причина игрового808.
Агент не исправляет делегаты/фокус/настройки или занятость worker.

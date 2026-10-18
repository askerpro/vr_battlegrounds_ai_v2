# Даты provenance и повторная агрегация, 2026-10-19

Build-Game.ps1 обрабатывает DateTimeOffset/DateTime без локализованного string
round-trip; строковые timestamps требуют ISO timezone/InvariantCulture.
Новый режим -ReaggregateInput/-ReaggregateOutputDirectory использует общий
validator до Unity lookup/build: attempt intervals, pinned SHA/helper/console,
полный inventory/размеры/SHA256, safe paths/reparse guards и UXR сверяются.
Original failed manifest не перезаписывается; UNKNOWN/dirty сохраняются,
выведенный из старого control flow исход отмечен derived, exitCode остаётся null.

Реальные PS5.1/PS7/principal вызовы exit0, Quest3/Server237 файлов, pair success=true,
UXR ea5574049cff8620. Parser errors0; исходные pair/raw hashes не изменились.
Результаты и ограничения — локальный reports/helper-fix/diagnosis.md и final reports.
Новая Unity сборка для агрегации не запускалась. Launch-infra исходного diff approve.
2026-10-19 пользователь прямо поручил влить готовое и продолжить исследование
клиентов с субагентом. Публикация выполняется по этому поручению; отдельная
пользовательская QA helper не заявляется выполненной, новые тестовые ожидания
до неё не добавлялись. Это не приёмка APK/Quest или сетевого startup.

После автоматического отказа коммита пользователь явно разрешил исключение:
«Разрешаю выпуск пола и helper без QA». Это разрешение публикации по имеющимся
техническим данным; фактическая QA не подменяется и остаётся открытой.

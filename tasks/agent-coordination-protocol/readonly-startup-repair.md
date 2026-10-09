# Readonly startup: status и диагностика hook

Закреплённый source release: 7ca4dbdeee2e0f96bfe14f79f28fbfb0b4926707,
включающий f51582b558bb6d2226f5bcf5079780475cfabcc2.
Репозиторий реализации — отдельный agent-infra, isolated source checkout проверен.
Этот release record публикуется через штатный merge origin/dev до runtime deployment.

`coordination status` открывает существующий SQLite в mode=ro, не создаёт каталог,
схему или binding и не пишет общий reports/status.json. Полный публичный снимок
возвращается в result.snapshot; report_path=null. Токены удаляются из ответа.
Остальные изменяющие команды сохраняют штатный протокол.

Узкий совместимый случай implicit Windows Get-Content -LiteralPath разрешён для
одной команды с literal абсолютным Windows path. Цепочки, подстановки, перенаправления,
дополнительные аргументы, explicit Bash и произвольный Python не получают новый допуск.

Исходный hook false denial1971/2023 этим выпуском не объявляется исправленным:
чистый reported full JSON и настоящий Windows launcher на TEMP repo уже дают ALLOW,
а реальный вызывающий hook продолжал DENY. Raw stdin не был сохранён.
Для ограниченного owner probe добавлена диагностика формы input: target session,
finite expiry, opt-in; stderr содержит только whitelist metadata, presence booleans
и решение. Команды, произвольные identifiers и capability не публикуются.
После probe root снимает только diagnostic key, сохраняя остальную конфигурацию.

Проверки: meaningful RED, 137 coordination tests PASS для f515; независимый subset7/7;
diagnostic6/6 PASS и повторное независимое ревью после whitelist/expiry/stderr fixes.
Полная Unity/игровая приёмка не выполнялась. Runtime deployment и настоящий owner
readonly probe следуют отдельно после merge, в безопасном maintenance окне.

Чужие worktree напрямую не изменяются. Владельцы обновляют tracked базу обычным
rebase с сохранением локальных изменений. Общая runtime копия обновляется root из
проверенного source commit с backup, без отмены FIFO, контрактов и ACK.

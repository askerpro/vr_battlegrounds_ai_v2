# Продолжение SceneDetail в Codex

Основной checkout, ветка `scene-detail`, WIP `cc485d9bd`. Fetch подтвердил origin/dev `d3c62ca5e`
в HEAD; rebase не нужен. Чужой `.claude/worktrees/` сохранён. Coordination mode off.

Исправления:

- Уникальный временный JSON, проверка кода adb, параллельное чтение stdout/stderr и таймаут 60 с.
- Проверка обязательных полей до изменения профиля; перенос только правил с ограничениями.
  Сохранение только своего ассета через SaveAssetIfDirty; редакторские списки сохранены.
- Возврат исходных mipmap-настроек вместо сброса в ноль; второй активный применитель запрещён.
- JSON сохраняется по сцене применителя, границы арены проверяются до создания профиля.
- Исключённый renderer защищает всю LODGroup; сбор от нуля поддерживает любые границы профиля.
- Предупреждение об отсутствующем исключении переведено на GameLog.Perf.Warning.

Ревью core_review обнаружило дочерние исключения, порог 5 м, неполный JSON и риск конкуренции
DetailBand; исправлены в этом продолжении. Это статический обзор, не runtime-приёмка.

Исходный WIP компилировался в открытом основном Editor. Промежуточная правка обнаружила CS0104
на Debug.LogWarning после удаления alias; вызов исправлен на GameLog. Итоговые Editor/Android
проверки прошли, Android ошибок 0; console ошибок 0. Лог — reports/main-editor.log вне Git.
Lobby: создан профиль с исключениями Gameplay и Environment/ArmoryRangeZone; собрано 690 LODGroup,
361 мелкий рендерер, 252 текстуры (112 исключены). Переведены 140 texture-meta, снят прежний tuner,
удалены 9 LobbyBand, сцена сохранена. Записи и mipmap defaults двух Quality-уровней — PASS;
globalTextureMipmapLimit остался 0/0. Геометрия не менялась. Play/Quest/UI ещё требуют проверки.
Новые тесты до пользовательской приёмки не писались, принятых коммитов нет.

Auto-review отклонил временный InitializeOnLoad-bootstrap MCP; файл не создан.
Основной Editor позднее подключился штатно. Resource API возвращает Session terminated, но прямые
инструменты MCP работают; операции проверены по projectRoot основного Editor. Worker не менялся.
Отчёты — reports/android-compile.json, lobby-authoring-check.json, lobby-collect.json, lobby-migration.json.

Финальный Android-вызов вернул MCP timeout, но действие завершилось позже: android-compile.json
со scope final OnEnable ownership snapshot содержит passed=true, errors=[]; лог также подтверждает PASS.
Не повторять завершённую операцию ради её вывода. Registry register и inbox вернули database is locked;
обновление статуса и отправку UI/performance через хаб требуется завершить после восстановления.

После сообщения сопровождающего worker_ready_resume 6805 свежие inbox/status работают.
Отправлены canonical inbox UI 6807, performance 6813, infra 6817. Ошибка register SCOPE на **/*.meta
устранена перечислением ровно 140 изменённых путей; область правок не расширена.

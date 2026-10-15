# Рефактор экрана «Отладка»

Обновлено: 2026-10-15. Задача `debug-menu-refactor`, владелец ui.
Checkout: `F:/CodexWorktrees/ui/Vr_Battlegrounds_ai`.

## Результат

Четыре вкладки: «Оверлеи», «Касание», «Телепорт», «Перф-анализ».
Время кадра, FPS и худшее значение постоянно видны строкой в перф-анализе;
существующий DebugPerfReadout обновляет её каждые0,5с без пересборки кнопок.
GPU: тест сцены и единственный SceneDetail Build/Snapshot. CPU: нагрузка аватаров.
Переход выбирает режим, не запускает тест и сохраняет активный прогон.
Боты — «Админ → Игроки и команды». Дублирующий админ-переход из отладки удалён.
Вкладки MenuKit имеют равную ширину.

Пользователь просмотрел снимки714, потребовал постоянное время кадра и «вливаем».
Именно этот вариант подготовлен к разрешённому FF-релизу.
Accepted SceneDetail74bd42f57 опубликован владельцем и доставлен в worker719DONE;
итоговая база UI — latest accepted1e945a996, без чужого WIP.
Принятый SHA и результат публикации worker фиксируются в generated
`reports/release-publication.json`, вне Git.

## Проверки

Unity725: inputeb6ff835b350ff49707c80dc1c5ca1438cda0fc5, parent1e945a996,
delta20own/foreign0. Явный refresh/compile/reload, native root/input/PID41776 подтверждены.
AndroidCompileGate: Passed=true, Errors=[] в уникальном durable report.
Ответ MCP на этот вызов потерян; повтор не выполнялся, исход установлен по файлу
и idle native guard. Сопровождающий уведомлён8142/8144.
Discovery6fixtures/50Runnable; job71b17c0092d64d8ea2cb8a2facc3b9d7:
50 PASS,0failed/0skipped, все50details сохранены доfinish.
7PNG сохранены; новый перф-снимок просмотрен, остальные6 побайтно совпадают с714.
Cleanup idle/leftover0; finish/receive R1213056bcbafe8d855529bf132d9832559320a7b,
paths/unexpected=[]; worker освобождён. Полные отчёты: `reports/725/`.

## Границы

Это EditMode UI и Android-компиляция. Quest, реальные касания/бот-команды,
живое обновление чисел и SceneDetail controls в Play этим прогоном не подтверждены.
В preview нет SceneDetail.Current, поэтому controls штатно скрыты.
Новые tests не записаны: auto-review отклонил расширение до headset validation;
существующие tests не менялись. Шаги дальнейшей приёмки и история — `Details.md`.

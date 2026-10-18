# bots-fix: приёмка локального поведения

2026-10-18. Зона `gameplay`: player-flow/PlayersManager, смысл команд PlayerSession,
выбор команды и режимы Respawn/Elimination; маршрутизация по `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял текущую стрельбу и удержание оружия в обычном Editor Play,
разрешил коммит и вливание. Read-only worker808: TestMap1, Server/Client=true,
3 бота. Эта оценка не подтверждает все раунды, режимы и сетевые роли.
Исходный N8 незавершён: H800 не создал огневую нагрузку, C801 достиг watch cap.

[Решение и evidence](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md),
[следующая итерация](../../../tasks/bots-fix/next-iteration.md).
Журнал фиксирует принятую область; выполнение публикации и zone ACK учитывает ведущий.

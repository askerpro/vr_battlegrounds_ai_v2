# bots-fix: ручной Editor Play и незавершённый внешний запуск

2026-10-18. Зона `launch-infra`: PlayModeStandParticipant, StandProtocol и
роль запуска DebugOrchestrator; маршрутизация по `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

После ручной проверки пользователь принял текущую стрельбу/удержание оружия
и разрешил коммит/вливание. Worker808: обычный Editor Play, TestMap1,
Server/Client=true, 3 бота. Это принятый локальный сценарий.

Ручной внешний server + 2 Spectator не достиг Ready (cold startup/KCP timeout),
повтор с 127.0.0.1 также не готов. Infra9410/9451 переданы сопровождающему;
эта запись не заявляет устранение причины или прохождение внешней приёмки.
Quest/APK и несовместимость cc3 B0/current probe остаются дальнейшей работой.
[Решение и evidence](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md),
[следующая итерация](../../../tasks/bots-fix/next-iteration.md).
Журнал не заменяет zone ACK и не подтверждает публикацию.

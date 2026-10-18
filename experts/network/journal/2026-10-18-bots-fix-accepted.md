# bots-fix: приёмка Editor и границы сетевой проверки

2026-10-18. Зона `network`: GameNetworkManager, HeadlessPrecacheGuard,
PlayerSession, Mirror и SDK registry/UniqueId/receive; маршрутизация по `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь разрешил коммит/вливание после оценки стрельбы и удержания оружия
в локальном Editor. Worker808: TestMap1, Server/Client=true, 3 бота.
Host-проверка не подтверждает удалённых Player/Spectator или физический Quest.
Внешний PlayLaunch server + 2 Spectator не достиг Ready после KCP timeout;
повтор с loopback тоже не готов. Infra9410/9451 переданы сопровождающему.

Unity802: unchanged weapon240/240 + observer Initialize-before-Start1/1 passed;
это ограниченная проверка, не общий сетевой PASS.
[Решение](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md),
[сохранённые ограничения](../../../tasks/bots-fix/next-iteration.md).
Журнал не заменяет исходное ревью/ACK и не удостоверяет merge.

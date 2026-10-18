# bots-fix: приёмка текущего Editor поведения

2026-10-18. Зона `avatar-ik`: UxrManager и SDK avatar/controller/HandState,
порядок штатных писателей; маршрутизация по `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял текущие стрельбу и удержание оружия после обычного Editor Play
и разрешил коммит/вливание. Worker808: TestMap1, Server/Client=true, 3 бота.
Это локальная визуальная оценка; механика тела на физическом Quest и полное
сетевое/модельное покрытие данной приёмкой не подтверждены.

G805 сохранил runtime identity и позы AK105 до/после respawn, но HandFit остался
`analysis_incomplete`. Контакт, Quest и сравнительная производительность перенесены
в [следующую итерацию](../../../tasks/bots-fix/next-iteration.md).
[Решение пользователя](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md).
Запись не заменяет zone ACK и не является доказательством публикации.

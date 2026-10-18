# bots-fix: ручная приёмка удержания оружия

2026-10-18. Зона `avatar-grip`: UxrAvatar.HandState и UxrGrabPointInfo;
пересечение зон сверено с `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял наблюдавшееся удержание оружия и стрельбу в локальном Editor,
разрешил коммит/вливание. Worker808: TestMap1, Host, 3 бота.
G805: AK105, runtime пары 2401/3426 до/после respawn, Right0/Left1;
все четыре HandFit `analysis_incomplete`, `ownerDecision=UNKNOWN`.
Ручная оценка не превращает Fit в PASS; скрытый контакт со спуском не доказан.

Физический Quest, геометрический контакт и остальные пары моделей/оружия
сохранены в [следующей итерации](../../../tasks/bots-fix/next-iteration.md).
[Решение и evidence](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md).
Журнал не заменяет исходный zone review/ACK и не подтверждает merge.

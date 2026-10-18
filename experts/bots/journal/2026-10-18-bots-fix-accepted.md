# bots-fix: пользовательская приёмка текущей итерации

2026-10-18. Зона `bots`: player-flow, BotKit/намерения, штатный хват и оружейный путь;
карта владения — `experts/bots/expert.json`. Проверенный production input
`5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял стрельбу и удержание оружия в локальном Editor и разрешил
коммит/вливание. Снимок worker808: TestMap1, Host (`Server=true`, `Client=true`),
3 бота. G805 того же input: 48 выстрелов, 34 события урона, ghost shots/grabs=0,
cleanup завершён. Это не полный N8, внешний сетевой или физический Quest PASS.

Исходный N8, прочие пары модель/оружие, контакт/Quest/perf и будущие
`pilot-split`/`script-brain`/`pf-quest` перенесены дальше; H′804 остаётся диагностикой.
[Решение пользователя](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md),
[отложенная работа](../../../tasks/bots-fix/next-iteration.md).
Журнал не заменяет zone review/ACK и не подтверждает публикацию.

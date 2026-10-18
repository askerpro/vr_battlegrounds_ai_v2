# bots-fix: приёмка стрельбы в Editor

2026-10-18. Зона `weapon-system`: readiness/WeaponSystem, SDK firearm/ledger,
WeaponSystemAuthoring, MagazineOnly profile и R08/Revolver; карта — `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял текущую стрельбу и удержание оружия в Editor и разрешил
коммит/вливание. G805 того же input: AK105, 48 выстрелов и 34 события урона,
ghost shots/grabs=0, cleanup завершён. Unity802: unchanged weapon240/240
и observer1/1 passed; исключённое obsolete cold-ledger ожидание не переписывалось.

Реальный F5 R08/Revolver, прочие пары и Quest не объявлены принятыми этими checks.
H′804 — отдельная диагностическая замена, не принятый baseline.
[Решение пользователя](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md),
[отложенные проверки](../../../tasks/bots-fix/next-iteration.md).
Запись не заменяет review/ACK и не подтверждает итоговый accepted SHA.

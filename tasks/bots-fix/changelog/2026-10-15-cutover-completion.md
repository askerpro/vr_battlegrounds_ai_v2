# Полностью завершить clip-cutover: продолжение cc-1…cc-5

2026-10-15. Пользователь после уточнения оставшихся работ поручил: «тогда завершай clip-cutover полностью».
Прежняя подготовка публикации остановлена; технический кандидат a2576cab не публикуется и не принимается.
Исходная ветка bots-fix/HEAD e812a2dfa и локальные изменения сохранены. Отклонённый auto-review stash
не повторялся. Нормализация LF восьми собственных текстовых путей имеет byte backups в ignored reports.

## Согласованный ход

1. cc-1: readiness до активного измерения, owner7805 stop-new-fire + conditional tail observation,
   точный public virtual trace Blaze; новый опыт сохраняет все Fired и старые FAIL.
2. cc-2: BotAddOptions.AutoEquip, один OfferPurchase на (тело, закупка), Arm штатный;
   DirectorEquip проверяет собственный путь директора. Стенд явно отключает автоматическую закупку.
3. cc-3: свежие targeted T1/compile и стенд; затем M3/M8/M10/S8 с условиями остановки, SDK grant/base,
   сетевое/производительное ревью и пользовательское демо. Каждая аренда заканчивается после пакета.
4. cc-4: актуальные product Docs в области bots и task material; контракт binding r3 через владельца.
5. cc-5: только после пользовательской проверки механики — новые/обновлённые tests, затем scoped интеграция.

## Резервация параллельных writes

- cc2_purchase: BotDirector.cs, BotMind.cs, новый BotAddOptions.cs; BotShopper.cs только при необходимости.
- cc1_fixture_tail: BotCombatStand.cs, BotStandSession.cs, BotStandRunContracts.cs, новый BotStandShotTailObservation.cs.
- cc1_proxy_cause: BotCombatDriver.cs и собственные новые BotBlaze diagnostic source.
- Principal: BotGunner.cs, tools/cc1-*, task Readme/plan/backlog; Director stop intent только после передачи cc2.

## Решения и пределы

Ruling: owner7805 применяется как узкая диагностика, не изменение gameplay PASS100% — сохраняются active-end
и final counts, shot identity UNKNOWN; ошибочное предположение о manager updates даёт incomplete.
Ruling: текущий подтверждённый clip-cutover-plan остаётся основой реализации; новые детали уточняются через
единственных владельцев, без нового подсистемного writer и без SDK/private reflection.
Ruling: AGENTS запрет на новые gameplay tests до human acceptance имеет приоритет над общим TDD workflow;
до приёмки используем компиляцию, существующие тесты и реальные runtime evidence.

Исторические679/687 не переоцениваются; новый код ещё не является runtime PASS. SDK81 не принят,
M8/late-client/Quest не закрыты. Player-only admission стенда принадлежит launch-infra (7663/7665).

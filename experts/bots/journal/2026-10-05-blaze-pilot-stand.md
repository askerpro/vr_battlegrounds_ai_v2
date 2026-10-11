# Пилот Blaze Cover Shooter и первый BotCombatStand влиты как checkpoint; качество не принято

Blaze AI импортирован `3af10777f` (2026-10-04). Коммит `b00e63ac257489a0ac1cea568cbec072576c04b9`
(2026-10-05): Combat ведёт штатный Blaze через серверный BotCombatDriver; BotBody/BotGunner переносят
клиповую позу и исполняют настоящие выстрелы с проверкой authority; патчи Blaze — TargetFilter,
CoverFilter, ColliderIgnoreFilter, RequireCompletePaths, отдельные asmdef. BotCombatStand сохраняет
ракурсы, траектории и покадровое движение рук.

Проверено: Android gate PASS и существующие bot tests 30/30 PASS в общей рабочей копии; серверный
пилот TestMap1 до четвёртого раунда; прогон стенда `20261005-113054-cbc662`: 11 NeedsReview,
2 Unsupported, реальных выстрелов 0. Не принято: качество хвата и укрытий, V01/L01, сеть, Quest.
Окружение прогонов (версия Unity, машина) — unknown.

Источники: `git show b00e63ac2`; `Docs/tasks/bots-blaze-integration.md` «Подтверждено», «Приёмка»;
`Docs/tasks/bot-combat-stand.md` «Наблюдения», «Проверки текущего среза»; `Docs/BlazeAI/sdk-patches.md`.

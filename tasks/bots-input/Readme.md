# Боты: ввод устройства в аватар бота (этап 2 bots-epic)

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/bots-input`
(схема «задача = агент»). Эпик — [bots-epic](../bots-epic/Readme.md).

## Цель

Бот на сервере управляется вводом устройства так же, как аватар обычного сетевого игрока: кадр шлема и контроллеров
подаётся в аватар бота, и всё ниже ввода (локомоция головой, хват, огонь) — код человека. Источники ввода: Blaze
(`BlazeInputSource`) и сценарий (`ScenarioInputSource`, тот же JSON, что у скриптового клиента).

## Дизайн

[contracts/bot-input-design.md](contracts/bot-input-design.md) — согласован пользователем 2026-10-20: SDK-флаг
`InputDriven` (патч UltimateXR 89, ~12 точек), устройство на аватар и общий пилот в `Assets/Scripts/Player/Input/`,
корень стоит — движется голова, один писатель на степень свободы; сначала сценарий, потом Blaze; прежний путь ботов
удаляется после приёмки Blaze.

## Итерации

| # | Итерация | Состояние |
|---|---|---|
| 0 | Дизайн и согласование | сделано 2026-10-20 |
| 1 | SDK-патчи 89 (InputDriven) и 90 (досылание при Initialize), `AvatarInputDevice`, `AvatarPilot`, `BotInputKit`, демо Input Smoke | демо PASS (прогон 6): ходьба головой, хват по Grip, выстрел и попадание первым нажатием; лог учёта оружия и без спама InteractionFeedback; heavy-2 (ticket 945) — код 0, 3/3 |
| 2 | `BlazeInputSource`, все боты на вводе, прежний путь удалён | Blaze через ввод **принят пользователем 2026-10-21** (Respawn, 4 бота: ходьба головой, хват двумя руками, выстрелы и убийства, без дрожания); прежний путь удалён 2026-10-21 (решение пользователя; кейсы стенда прежнего пути удалены) |
| 3 | Тесты после приёмки, heavy, ревью зон, вливание FF | тесты: EditMode 71/71 (ticket 986); патч 90 — `InitializePreparedTests` 2/2; EditMode Weapons+Bots 250/251 (ticket 989; красный только `LedgerSpawnReadyTests.Отказ_replay…` — устаревший контроль из origin/dev после 044888e1b, не связан с задачей); heavy-5 (ticket 989, f5d8819ea) — код 0, 3/3; ревью зон раунд 2 — approve все 5; **влито FF в origin/dev c7b8d6ab3 2026-10-21** (merge-request 21392094…, публикация базы — ticket 990) |
| 4 | Сведение пилота/стрелка клиента и бота в продуктовый, `ScenarioInputSource` (тот же JSON) | после вливания series (e2e-combat), отдельным этапом |

Этапы и области записи — [plan.json](plan.json).

## Координация

- e2e-combat правит `ScriptedPilot`/`ScriptedGunner`/`ScriptedClientRunner`/`InputScript`: пилот не трогаем до вливания
  их этапа `hit` (хаб, событие 10665).

## Статус

2026-10-21: этап `input-scenario` (итерации 1–2: сценарий и Blaze через ввод, прежний путь удалён) **влит FF в origin/dev c7b8d6ab3**. Ревью зон (bots, avatar-ik, manipulation, weapon-system, launch-infra) — approve; heavy-5 код 0. Этап `input-blaze` пуст (работа вошла в `input-scenario`). Дальше: записи в journal зон, этап сведения пилота/стрелка и `ScenarioInputSource` после вливания series у e2e-combat; перф (кеш `ControllerInput`, `WeaponCategory`), тест «Action не в покое → не дослан», тест InputDriven-курка.

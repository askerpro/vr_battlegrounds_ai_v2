# KinemationMaterials: принятый тон альбедо и _Color в конвертере

2026-10-23. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/kinemation-albedo-tint` (схема «задача = агент»).

## Цель

Конвертер KinemationMaterials сам воспроизводит принятый пользователем тон альбедо M_* (цель средней линейной яркости 0,055, множитель 1..1,6) и пишет _Color = _BaseColor; IronKitMaterials тоже пишет _Color = _BaseColor. Существующие материалы приводятся к правилу (решение пользователя 2026-10-23).

## Итоги (2026-10-23)

- `Convert`: цвет = `_TintColor` × `AlbedoBoost(MeanLinear(_BC))` (цель 0,055, 1..1,6, округление 0,01), одинаково в
  `_BaseColor` и `_Color`; `IronKitMaterials` пишет `_Color` = `_BaseColor`. Компиляция — worker 1121.
- Расчёт по полной карте расходится с принятыми множителями (их считали по копии 256×256): решение пользователя —
  «привести материалы к правилу». Worker 1123 ([tools/apply_rule.cs](tools/apply_rule.cs)): 8 материалов,
  AK105 Body 1,31→1,26, Rail 1,41→1,40, Herrington 1,06→1,05, R08 1,17→1,16, SRM-12 Body 1,57→1,53, Mag 1,38→1,36,
  Viper Body 1,34→1,32, Magazine 1,53→1,57; [tools/boost_check.cs](tools/boost_check.cs) mismatch=0;
  Offline → Lobby + 10 с — 0 dirty-материалов; `KinemationMaterialsTests` 4/4 (тест формулы переименован в `AlbedoBoost_FollowsAcceptedRule` после ревью).

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/kinemation-albedo-tint/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

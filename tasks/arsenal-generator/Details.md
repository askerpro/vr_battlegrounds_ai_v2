# Подробности задачи

## Факты и границы

- Дизайн — [design.md](design.md), исходный план — [plan.md](plan.md), миграция на новый флоу — [new-flow.md](new-flow.md).
  Действующая архитектура — [Arsenal_Code_Architecture_RU.md](../../Docs/Arsenal/Arsenal_Code_Architecture_RU.md),
  основа генератора — [generator-foundation.md](../../Docs/Arsenal/generator-foundation.md),
  стенд раскладки — [layout-authoring-stand.md](../../Docs/Arsenal/layout-authoring-stand.md).
- Решение пользователя 2026-10-07: позы настраивает человек, генератор их применяет и не знает размеров оружия.
- Решения 2026-10-08/09: обратной совместимости нет; авторский формат станций удалён со всех карт; позы ушли из
  стиля в раскладку слота (`ArsenalSlotLayout`): умолчание у префаба слота, своя у оружия; коробка приёма и
  карточка — часть раскладки.

## Архитектурный анализ

- Один писатель представления слота: `ArsenalSupportProjection` (рантайм), его вызывает только сборщик слота.
- Предложение магазина ставит сборщик слота; редакторского инсталлятора и подгонки к поверхности нет.
- Коробка приёма — валидатор якоря UltimateXR плюс расширенная сфера (`MaxPlaceDistance` до дальнего угла);
  правка SDK не нужна.
- Сборщик станции работает только в Play (`ArsenalComposer.NotPlaying`): ID ролей назначаются непроснувшимся
  объектам. EditMode-тесты собирают станцию через публичные входы (`ArsenalTestStation`).

## Координация

- Чужие файлы правились с согласия владельцев (сообщения 2026-10-08, id 263–269, 292, 389/294, 791):
  map-runtime-bootstrap (MapRoot/MapBootstrap/адаптер), weapon-system (`WeaponInfo.cs`, SightGameplayReviewBuilder,
  WeaponNamingMigration), bots-fix (BotCombatStandBuilder), vr-test-stand (`CommonArsenalReview`).
- План ревизии 13 добавил `Assets/Tests/EditMode/Economy/MatchEconomyServerTests.cs`: стена в тесте собирается как
  станция.

## Проверки и расследования

- Офлайн Roslyn (локальные `reports/rebase-20261006/`): сборка `VrBattlegrounds.Weapons.Core` берётся из `Library`
  основного checkout и может отставать — такие ошибки офлайн-компиляции не настоящие; решает Unity в аренде.
- Пробы (`tools/station-probe.cs.txt`, `tools/map-status.cs.txt`) и пересохранение данных
  (`tools/reserialize-new-flow.cs.txt`) — отчёт аренды 266 в `reports/new-flow/lease266-report.md`.
- `ArsenalMapGeometryTests` TestMap1: `TrayFrontLip` пересекает `LD_Beam_Low` (1,3–1,5 мм) — было до задачи.
- Консоль Play: каскад «BeginSync/EndSync mismatch» от `UxrAvatar._avatarRenderers` (14 рендереров `Cyborg/Ghost`,
  внесены в список коммитом 357a83f57) — не арсенал, передано агенту калибровки.

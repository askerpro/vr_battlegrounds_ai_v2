# Лобби на генераторе арсенала: подготовка

Дата: 2026-10-08. Задача: arsenal-generator. Тип: feature.
Статус: реализовано офлайн (компиляция Roslyn рантайма и Editor), в Unity не проверено.

## Результат

- Сборщик станции (`ArsenalStationComposer`) проецирует на каждый сгенерированный слот представление стиля:
  якоря оружия и магазина, карточку, опоры и подсказки возврата SDK, и ставит `ArsenalMagazineOffer`.
  Без предложения склад не выдал бы магазины сгенерированных слотов.
- Проекция вынесена из редакторского `ArsenalSupportModuleBuilder` в рантайм `ArsenalSupportProjection`:
  один код для префабов авторских станций и для сборки на машине. Редакторский сборщик добавляет
  только stale-проверку результата.
- Компилятор каталога (`ArsenalCompositionMetadataCompiler`) сохраняет центр ряда в позе ряда: в эталоне
  `LobbyDemoArsenalStation` ряды сдвинуты на 8,5 см, без этого сгенерированные слоты съезжали бы
  относительно корпуса.
- `ArsenalGeneratedLobbyMigration` (Editor, `Tools/VR Battlegrounds/Arsenal/`): «Пересобрать каталог
  генератора» — шаблоны слотов из эталона на месте ассета; «Перевести лобби на генератор» — стиль
  `FullDemoArsenal`, каталог в `MapRuntimeCatalog`, станции Lobby в Generated без авторских рядов слотов
  (корпус прежний). Перевод сцены отказывает, пока адаптер генератора в MapBootstrap не влит.

## Проверка

Офлайн-компиляция Roslyn: UltimateXR, VrBattlegrounds, Assembly-CSharp-Editor — 0 ошибок. Проба
`lobby-parity` (локально `reports/lobby-generated/`) компилируется, в Unity ещё не запускалась.

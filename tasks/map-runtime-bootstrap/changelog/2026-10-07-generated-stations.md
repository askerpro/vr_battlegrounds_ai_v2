# Генерируемые станции арсенала в запуске карты

Дата: 2026-10-07. Задача: map-runtime-bootstrap. Тип: feature.
Статус: проверено агентом на worker, влито по поручению пользователя (SHA — в истории Git).

## Результат

`MapArsenalCompositionAdapter`: станции в режиме Generated `MapBootstrap` описывает генератором до публикации config
(оформление, fallback, layout hash, версия схемы ID — в `MapStationConfig`), собирает через `ArsenalStationComposer` на
сервере и на каждом клиенте с одним `MapRunKey`; клиент сверяет своё описание с config. Готовность для барьера Relay
(`IsLocallyReady`) и server Ready требуют Passed всех станций; Pending — ждать в опросе `Update` (стадия
`ComposingStations` на сервере), Failed — именованный отказ `Arsenal.Generated.*` без отката на Authored. Разборка —
scope запуска. `MapRoot` больше не отвергает станции Generated; каталог ресурсов генератора —
`MapRuntimeCatalog.ArsenalComposition`. Оформление — `ArsenalStationCompositionBinding.VisualRequest`.

## Проверка

На базе 2c845870: компиляция 0 ошибок, AndroidCompileGate PASS, Play Mode-проба 32/32 на тестовой станции (настоящий
сборщик, копия FullDemoArsenal в памяти со стилем), EditMode без новых падений против dev. В игре станции пока
Authored: стильного пресета нет. На базе ec92098a (аренда 212): компиляция 0 ошибок, AndroidCompileGate PASS, пробы 32/32, EditMode затронутых групп без падений
в коде этапа, хост Lobby → TestMap1 → перезагрузка → Lobby без ошибок.

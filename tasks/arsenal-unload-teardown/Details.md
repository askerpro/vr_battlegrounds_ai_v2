# Детали: разборка станций арсенала при выгрузке карты

2026-10-13, этап `top-level-detach`.

## Кто вызывает `ArsenalCompositionHandle.Dispose`

Handle регистрируется в scope запуска (`ArsenalStationComposer.PrepareComposition` → `scope.Own(handle)`), поэтому
`Dispose` зовёт только `MapRunScope.Dispose` (в обратном порядке регистрации). Откат неудачной сборки уничтожает
слоты напрямую (`DestroyImmediate` в `catch`), без handle. Пути до `MapRunScope.Dispose`:

| Путь | Где | Сцена карты в этот момент |
|---|---|---|
| `MapBootstrap.OnDestroy` → `RetireServerRun` → `MapRunAuthority.Retire` → `CommitTerminal` → `DisposeScope` | сервер / host | **уничтожается** (смена сцены Mirror `ServerChangeScene` из `MapLoader.LoadMap`, выход) |
| `MapBootstrap.OnDestroy` → `DisposeLocalScope` | клиент | **уничтожается** (клиент грузит сцену по `SceneMessage`, выход) |
| `MapBootstrap.FailRun` → `MapRunAuthority.Fail` → `CommitTerminal` → `DisposeScope` | сервер | живёт (отказ запуска) |
| `MapBootstrap.FailLocalRun` → `DisposeLocalScope` | клиент | живёт (отказ запуска на клиенте) |
| `MapRunAuthority.OnStopServer` → `CommitTerminal(Retiring)` / `DisposeScope` | сервер | живёт: Mirror `StopServer`/`StopHost` (и из `OnApplicationQuit`) зовёт его до смены на offline-сцену |
| `MapRunAuthority.OnDestroy` → `DisposeScope` | сервер | обычно scope уже снят в `OnStopServer`; иначе — уничтожение `SessionContext` |
| `MapRunAuthority.BeginRun` → `DisposeScope` | сервер | живёт; после `CommitTerminal` scope уже `null` — на практике пусто |

Смена запуска на той же сцене (сервер перезапущен в сцене карты, `ResetServerRun` → новый `BeginRun`) разбирает
прежний scope в `OnStopServer`/`OnDestroy` старого authority — при живой сцене.

## Признак выгрузки

Явный, от владельца сцены: `MapBootstrap.OnDestroy` до разборки scope вызывает
`ArsenalStationComposer.ReleaseWithScene(gameObject.scene)`. Сборщик (владелец реестра живых сборок `Live`)
помечает сборки станций этой сцены (`ArsenalCompositionHandle.ReleasesWithScene`; сцена запомнена при сборке —
находится, даже если объект станции уже уничтожен). Затем scope разбирается как раньше, и `Dispose` помеченной
сборки предметы не отцепляет.

Почему так:
- `OnDestroy` компонента `MapBootstrap` на корне карты — ровно момент «сцена карты (или сам корень карты)
  уничтожается»; других игровых вызовов `Destroy` для `MapRoot` нет. Все пути с живой сценой идут не через него.
- Причину нельзя передать через `MapRunAuthority.DisposeScope`: scope (`MapRunScope`) хранит только `Action`
  без причины, а разборка станции — не дело издателя состояния карты. `MapRunAuthority` не менялся и остаётся
  единственным публикатором; `MapRunConfig`, ключ и порядок сборки не тронуты.
- Эвристика по Unity (`gameObject.scene.isLoaded == false` в `OnDestroy` при выгрузке) широко применяется, но
  в документации Unity порядок состояния сцены во время выгрузки не описан, а при выходе из приложения сцена
  остаётся «загруженной». Явный признак от владельца не зависит от этого и от порядка `OnDestroy` в сцене.
- Шов `IArsenalStationComposer` не расширен (файл адаптера вне writes этапа): сборки тестовых сборщиков в `Live`
  не попадают, для них поведение прежнее.

## Что происходит с предметами

«Чужой» — хватаемый предмет (`UxrGrabbableObject`) под слотом, которого не было в слоте при сборке: оружие стены
(`ArsenalSlotController.AssignNetworkItem`), магазин склада (`ArsenalMagazineOffer.Assign`), вложенные в них
части. Свои объекты слота (якоря, опоры, карточка) хватаемых предметов не содержат.

| Случай | Верхний уровень (нет чужого хватаемого предка до слота) | Вложенный (стартовый магазин в оружии, затвор) |
|---|---|---|
| Сцена живёт | отцепляется в корень своей сцены (`SetParent(null, true)`), остаётся жить | остаётся в родителе и уезжает с ним |
| Сцена уходит | не переподвешивается, уничтожается со сценой | уничтожается с родителем |

Сетевой / нет:
- Под слотом объект всегда в сцене карты (родитель в сцене карты; предмет из DontDestroyOnLoad, положенный в слот,
  тоже переходит в её сцену). При выгрузке сетевой предмет уничтожается со сценой, и Mirror сам снимает его:
  `NetworkIdentity.OnDestroy` на сервере → `NetworkServer.Destroy` → запись уходит из `NetworkServer.spawned`.
  Второго писателя нет: разборка ничего не спавнит и не уничтожает сверх слотов.
- При живой сцене отцепление локально на каждой машине (каждая собирает и разбирает свою станцию по одному ключу);
  это не синхронизированное действие, авторитет не требуется. Несетевые предметы ведут себя так же.
- Раньше отцепление во время выгрузки рождало корни, переживавшие выгрузку вне сцены (`!scene.IsValid()`), с живым
  `NetworkIdentity` в `spawned` и `AnchoredItemCollisionIgnore`, трогающим коллайдеры вне сцены.

Правило вложенности уточнено: предком-«перевозчиком» считается только чужой хватаемый предмет. Свой хватаемый
предок уничтожался бы со слотом и вложенное не спас бы; в текущем префабе слота своих хватаемых нет, поэтому на
игру это не влияет.

Попутно: `ArsenalStationComposer.Forget` снимал запись `Live` только при живой станции (`Station != null` —
Unity-null). При выгрузке станция бывает уничтожена раньше разборки, и запись разобранной сборки оставалась в
реестре. Теперь проверка по ссылке (`ReferenceEquals`), запись снимается всегда.

## Проверки

- Офлайн-компиляция `VrBattlegrounds` (Assets/Scripts, Roslyn Unity 6000.4.1f1, ссылки из csproj основного
  checkout) — 0 ошибок; `VrBattlegrounds.Tests.EditMode` против новой сборки — 0 ошибок. Тесты не запускались.
- Приёмка в Unity: перезагрузка той же карты через `MapLoader.LoadMap` на выделенном сервере и host — 0
  `UxrGrabbableObject` с `!scene.IsValid()`, нет «not cleaned up», 0 «Tried to ignore collisions»; прогон
  launch-infra `map-run-startup-route`; отказ запуска / перезапуск сервера в сцене — предметы в слотах не
  уничтожены, вложенные магазины остаются в оружии.

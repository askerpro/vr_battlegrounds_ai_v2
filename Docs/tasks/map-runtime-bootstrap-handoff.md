# Продолжение map-runtime-bootstrap

| Цель | Мы здесь | Осталось выполнить | Технический документ |
|---|---|---|---|
| Перевести карты на единый immutable run и управляемую runtime composition | Runtime-интеграция и миграция шести карт реестра влиты в `dev` (6f54dac0, 01c28d22, a8d521b0). Доводка задач 5 и 8 (барьер Relay, транзакция загрузки, удаление неуправляемого пути MapReferee, постоянные тесты) проверена пользователем и влита в `dev` — см. [прогресс](map-runtime-bootstrap-progress.md) | Старт карты мимо лобби в отладке, мелкие правки; генерируемые станции (задача 7 ждёт API сборщика генератора) | [Дизайн](map-runtime-bootstrap-design.md), [план](map-runtime-bootstrap-plan.md), [прогресс](map-runtime-bootstrap-progress.md), [runtime-интеграция](map-runtime-bootstrap-runtime-integration.md) |

## Действующий срез

Рабочая копия: linked worktree `F:\CodexWorktrees\map-runtime-bootstrap\Vr_Battlegrounds_ai`, ветка
`claude/map-runtime-bootstrap`. Всё описанное в этом разделе и доводка задач 5 и 8 уже в `dev`; ход и проверки
доводки — в [прогресс-документе](map-runtime-bootstrap-progress.md). Unity-изменения
идут только через `Tools/agents/editor-broker.py`.

В `dev`:

- `MapBootstrap` (на `MapRoot`) — единственный серверный запуск карты: ValidateBindings → Resolve (режим из
  `Series.CapturedModeId`, иначе выбор админа, лобби — NoMatch) → BeginRun → Prepare пресета станций → спавн
  `MapReferee` и `ArsenalEquipmentCoordinator` из каталога в сцену карты → CommitPrepared (CompositionReady) →
  `MapReferee.ServerStartRun` → server Ready → отложенные аватары. Отказ — `GameLog.Error` с кодом. Клиент
  связывает координатор со станциями по netId из descriptor и сверяет отпечаток содержимого карты.
- `MapRunAdmission` — производный допуск без собственного состояния: gameplay сцены без `MapBootstrap`
  открыт (стенды), очередь аватаров до Ready — один запрос на сессию.
- Владельцы: `MapReferee` (InitializeRun/ServerStartRun, публикация только в `ServerSwitchTo`, режим — отдельный
  сетевой корень, клиент принимает режим только с netId из descriptor), `ArsenalWallController` (первичное
  пополнение после допуска), `AvatarManager` (спавн через допуск), `Series` (CapturedModeId, снаряжение
  снимается после `MapLoader.CanAcceptLoad`), `MapLoader` (LoadMap → bool, IsLoading до конца загрузки Mirror),
  `GameNetworkManager` (ссылка на каталог).
- Closing: `MapLoader.MapLoadStarted` → `MapRunAuthority.Close` (статус `Closing`, `MapRunScope.Close()` отменяет
  токен без teardown). Допуск, коммит режима и все выдачи предметов (`ReplenishSlotsWhere`, `ArsenalMagazineSupply.SpawnStock`,
  `PlayerLoadoutManager.ServerEnsureMagazines`/`ServerGiveWeapon`, покупка бота) закрыты
  до выгрузки; teardown — по-прежнему `Retire` на выгрузке. Это пункт 3 стыка с генератором, сделан заранее.
- Editor: `MapBootstrapMigration` (DryRun/Apply/RebakeCatalog, меню `Tools/VR Battlegrounds/Maps/Map Bootstrap/`)
  и `MapCatalogBuildStep` (запечка и preflight перед сборкой, отказ ломает сборку).

Неуправляемый путь `MapReferee` в ветке удалён (см. [прогресс](map-runtime-bootstrap-progress.md)): стенд
`BotCombatStand` запускается MapBootstrap как отладочная карта каталога, EditMode-тесты — через `TestMapRun`.

Решение без отдельного согласования: транзакцию смены режима со «спящим» кандидатом не делали. Каталог
проверяет все префабы режимов до старта карты, поэтому между уничтожением старого режима и коммитом нового
отказов не остаётся.

## Проверки и пределы

База — `dev` 45ef9f6c (с ручным заряжанием дробовиков). Последние прогоны на worker:

- AndroidCompileGate PASS. Миграция применена: 6/6 карт, preflight каталога 6/6, иерархия и
  площадка PASS, окклюзия перезапечена. Сегменты границы зон восстановлены генератором: 16 на каждой
  боевой карте, мировые позы совпадают с исходными до 0.
- Play Mode, хост: Lobby (ключ /1) → TestMap1 (/2) → GoLive → Pause → Resume → Lobby (/3). На каждом
  шаге `MapBootstrap` Ready, аватар 1, оружие и магазины на стенах (50/50 в лобби, 80/80 на TestMap1),
  эпоха режима 1→2→3→4. TestMap2, TestMap3, ServiceYard, ReferenceMap04 доходят до Ready.
- EditMode по группам (лимит списка падений MCP — 25, поэтому прогон по пространствам имён) против
  чистого dev: 49 падений в dev, на ветке добавилось одно — `GameModeWiringTests.В_сцене_лобби_есть_оркестратор_режима`
  (ждёт сценовый MapReferee, теперь его спавнит MapBootstrap) — устаревшее ожидание, править после приёмки.
  Группа Prefabs в dev падает >50 раз (позы рук, обратная связь оружия); затрагиваемые классы
  (PrefabComposition, NetworkAssetIdOnDisk, UxrUniqueIdOnDisk/Stability, GameTags, OutOfWorldGuard,
  WeaponScale) сравнены точечно.
- Сканы всей сцены (сетевые sceneId, UltimateXR id) в рантайме отключены: UxrManager навешивает
  `UxrCanvas` с пустым id на world-space канвасы (known-issues, Issue 36). Ограничение: проверка станций
  по-прежнему требует непустые id всех UXR-компонентов внутри станции — world-space канвас на станции
  дал бы такой же ложный отказ; сейчас его нет.
- Не проверено: два клиента (поздний вход, descriptor/режим по netId), выделенный сервер, шлем.
  Барьер начального состояния `NetworkStateRelay` не реализован (задачи 5/7).

## Следующее действие

Барьер Relay, транзакция загрузки, direct Play, постоянные тесты, класс «выдача предмета мимо допуска» и удаление
неуправляемого пути `MapReferee` сделаны в ветке; ход, проверки и пределы — в
[прогресс-документе](map-runtime-bootstrap-progress.md).

1. Проверка пользователем в шлеме (чек-лист — в прогресс-документе), затем коммит ветки.
2. Генерируемые станции (задача 7) — после handoff API `ArsenalStationComposer` от arsenal-generator.

## Как продолжить проверку

```text
checkpoint → request → watch-ticket → claim → begin → guard
execute_code: return VrBattlegrounds.EditorTools.MapBootstrapMigration.DryRun();
execute_code: return VrBattlegrounds.EditorTools.MapBootstrapMigration.Apply();
execute_code: return VrBattlegrounds.EditorTools.AndroidCompileGate.Run();
finish → receive
```

Отчёты миграции пишутся в `Docs/tasks/report/map-runtime-bootstrap/` (игнорируется Git). Probes прежних срезов
заменены постоянными тестами (`Assets/Tests/EditMode/Maps/MapRunContractTests.cs`, `MapCatalogIntegrityTests.cs`);
в `Tools/Probes/MapRuntimeBootstrap/` остались инструменты инвентаризации и probe direct Play/Relay. Чтение результата
MCP — через `Tools/UnityMcp/compact-result.js`.

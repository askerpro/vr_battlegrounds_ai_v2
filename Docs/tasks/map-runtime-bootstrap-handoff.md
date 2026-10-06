# Продолжение map-runtime-bootstrap

| Цель | Мы здесь | Осталось выполнить | Технический документ |
|---|---|---|---|
| Перевести карты на единый immutable run и управляемую runtime composition | Runtime-интеграция (MapBootstrap, допуск, владельцы) написана и компилируется под Android; инструмент миграции прошёл dry run 5/6 карт, исправлен под шестую | Применить миграцию сцен через аренду, Play Mode на worker, прогон EditMode, проверка пользователем в шлеме, затем Relay-барьер и генерируемые станции | [Дизайн](map-runtime-bootstrap-design.md), [план](map-runtime-bootstrap-plan.md), [runtime-интеграция](map-runtime-bootstrap-runtime-integration.md) |

## Действующий срез

Рабочая копия: linked worktree `F:\CodexWorktrees\map-runtime-bootstrap\Vr_Battlegrounds_ai`, ветка
`claude/map-runtime-bootstrap` поверх текущего `dev`. Правки не закоммичены; Unity-изменения идут только
через `Tools/agents/editor-broker.py`. Пакет runtime-интеграции, ранее отклонённый автоматической проверкой,
пользователь разрешил явно: «продолжай до полного завершения интеграции и миграции».

Контрактный и authoring срезы (`MapRunConfig`/`Resolver`/`Scope`/`Snapshot`, `MapRunAuthority`, `MapRoot`,
`MapRuntimeCatalog`, `MapRunPreflight`, kind на `MapData`) уже в `dev`. Поверх них в ветке:

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

Неуправляемый путь `MapReferee` (сцена без `MapBootstrap`) сохранён для стендов и тестов; для карт реестра он
пишет Warning. Удалить после приёмки вместе с правкой тестов.

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

1. Проверка пользователем в шлеме (список — в итоговом сообщении задачи), затем коммит.
2. После приёмки: поправить `GameModeWiringTests` (MapReferee спавнится, а не лежит в лобби), закрепить
   тестами MapBootstrap/MapRunAdmission/Closing и класс «выдача предмета мимо допуска».
3. Удалить неуправляемый путь MapReferee вместе с тестами, которые на него опираются.

После приёмки закрепить тестом класс «выдача предмета мимо допуска»: каждый вызов
`NetworkUxrIdentity.CreateInstance` в игровом коде (кроме Debug-стендов) стоит за проверкой `MapRunAdmission`.

Отложено: Relay-барьер (задача 5), генерируемые станции и их admission (задача 7, стык с генератором
арсенала), удаление неуправляемого пути `MapReferee`.

## Как продолжить проверку

```text
checkpoint → request → watch-ticket → claim → begin → guard
execute_code: return VrBattlegrounds.EditorTools.MapBootstrapMigration.DryRun();
execute_code: return VrBattlegrounds.EditorTools.MapBootstrapMigration.Apply();
execute_code: return VrBattlegrounds.EditorTools.AndroidCompileGate.Run();
finish → receive
```

Отчёты миграции пишутся в `Docs/tasks/report/map-runtime-bootstrap/` (игнорируется Git). Временные probes
прежних срезов — `Tools/Probes/MapRuntimeBootstrap/`. Чтение результата MCP — через
`Tools/UnityMcp/compact-result.js`.

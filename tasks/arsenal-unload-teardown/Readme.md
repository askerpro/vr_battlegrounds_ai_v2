# Разборка станций арсенала при выгрузке карты не оставляет предметы вне сцены

2026-10-13. Владелец — эксперт `gameplay`. Находка manipulation 6071/6073 (копия launch-infra 6032/6077); приоритет
пользователя — то, что мешает игровой логике и чего ждут другие.

## Проблема

На выделенном сервере после перезагрузки карты — 88× Error «Tried to ignore collisions … not part of a UnityScene»,
прогон launch-infra `map-run-startup-route` красный. Цепочка: `MapBootstrap.OnDestroy` (:146) → `RetireServerRun` →
`MapRunAuthority.DisposeScope` (:199) → `ArsenalCompositionHandle.Dispose` (`ArsenalStationComposer.cs:44-56`):
обходит `slot.GetComponentsInChildren<UxrGrabbableObject>(true)` и делает `SetParent(null, true)` каждому чужому
предмету, включая вложенные в оружие (стартовые магазины, Slide). Это идёт во время выгрузки сцены — новые корни
переживают выгрузку вне сцены; `AnchoredItemCollisionIgnore` (manipulation) у оставшегося оружия трогает коллайдеры вне
сцены. Оставшееся серверное оружие держит `NetworkIdentity` в `NetworkServer.spawned`.

## Решение (направление)

1. Отцеплять только предметы верхнего уровня (без хватаемого предка ниже слота) — вложенные едут с родителем.
2. При разборке во время выгрузки сцены не переподвешивать в корень: предметы уходят вместе со сценой (сетевые — без
   висящих записей в `spawned`). Отцепление в корень остаётся только для разборки при живой сцене.

## Этапы

1. `top-level-detach` — правка разборки; проверки: после `MapLoader.LoadMap` той же карты 0 `UxrGrabbableObject` с
   невалидной сценой, нет «not cleaned up», 0 «Tried to ignore collisions»; прогон launch-infra `map-run-startup-route`.
   Ревью: manipulation (предметы/якоря), network (spawned).
2. `accepted-tests` — после приёмки: EditMode-тест разборки (вложенные предметы, выгрузка).

## Состояние

- 2026-10-13: задача заведена.
- 2026-10-14: `top-level-detach` — `ArsenalCompositionHandle.Dispose` отцепляет только чужие предметы верхнего уровня;
  `MapBootstrap.OnDestroy` → `ArsenalStationComposer.ReleaseWithScene` (явный признак выгрузки от владельца сцены) —
  при выгрузке предметы уходят со сценой, без переподвешивания в корень; `Forget` по ссылке. Диагностика: через кадр
  после загрузки карты строка `[MapBootstrap] <карта>: после загрузки — grabbable вне сцены=M, spawned вне карты=N`
  (Warning при ненулевых). Ревью: network ACK 6107, manipulation 6201. Проверка: launch-infra Run-E2E
  map-run-startup-route (TestMap1, выделенный сервер + 2 клиента, перезагрузка карты, плеер кандидата ad72274) —
  GREEN (6334): 0 «Tried to ignore collisions» (до исправления 88), 0 «not cleaned up», 0 UxrComponentNotFound,
  0 ошибок Relay, счётчики 0/0 в обе загрузки. Ограничение: плеер до вердикта по логам (0cd489fd) — полный вердикт в
  прогоне после вливания. Офлайн-компиляция VrBattlegrounds — 0 ошибок. Принят пользователем 2026-10-14; вливание.
- Дальше: `accepted-tests` (EditMode-тест разборки: вложенные предметы, выгрузка).

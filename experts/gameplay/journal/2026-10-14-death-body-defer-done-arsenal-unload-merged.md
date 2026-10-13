# Задача death-body-defer закрыта; влита разборка станций при выгрузке карты (arsenal-unload-teardown)

Дата: 2026-10-14. Оба этапа приняты пользователем.

## death-body-defer — закрыта (все этапы влиты)

- `deferred-body` 160223c7b, `death-equipment-drop` 01181ba22, `accepted-tests` edc0e1655.
- Тесты: 11 тестов отложенной смены тела (`Managers/AvatarManagerDeferredBodyTests`), смерть без смены тела и
  серверный сброс из кобуры (`Player/AvatarTeardownTests`), удалён пустой `PlayerController.RpcOnDied`
  (`RpcCarriesNoStateTests`, zone-ack network 5806). Unity: 846 EditMode, 845 passed, 0 failed, 1 skipped (вне этапа).
- Уроки тестов: тестовые тела нужно делать «как клон префаба» (`sceneId = 0`), иначе `NetworkServer.Destroy` в EditMode их
  не уничтожает; ленивый `UxrNetworkManager.Instance` (из `UxrActor.OnReceiveDamage`) создаёт менеджер без реализации,
  который протекает в соседние тесты — чистить в TearDown. Manipulation добавит null-проверку в патч 75 (6204).

## arsenal-unload-teardown / top-level-detach — ec0887cf5

- Находка manipulation 6071 / launch-infra 6032: 88× «Tried to ignore collisions» на выделенном сервере после перезагрузки
  карты. Исправление: `ArsenalCompositionHandle.Dispose` отцепляет только чужие предметы верхнего уровня; при выгрузке
  (`MapBootstrap.OnDestroy` → `ReleaseWithScene`) ничего не переподвешивает. Постоянная диагностика остатков прежней
  карты в `MapBootstrap` (Warning при ненулевых).
- Проверка: launch-infra map-run-startup-route (сервер + 2 клиента, перезагрузка TestMap1) GREEN — 0 вместо 88.
  Полный вердикт по логам — их прогон после вливания (6367).
- Дальше: `accepted-tests`.

## Открыто

- `catalog-fingerprint/preview-loaded-scene` (462948518) — ждёт прогона в Unity; затем `accepted-tests` обеих задач.
- `series-smoke` — ждёт каркас launch-infra `e2e-smoke-kit`.

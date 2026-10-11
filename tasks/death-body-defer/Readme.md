# Смерть доходит до клиентов: смена тела на верхнем уровне

2026-10-12. Владелец — эксперт `gameplay`. Задача выбрана пользователем 2026-10-12.

## Проблема

На сервере `UxrActor.DieInternal` обёрнут в `BeginSync()` … `EndSyncMethod()` (`UxrActor.cs:257-278`).
Внутри `Died` → `PlayerController.OnActorDied` → `Die()` → последним `ReconcileBody("смерть")` →
`AvatarManager.ServerReconcileBody` → `ReplaceBody` → `NetworkServer.Destroy(тело)`. К `EndSync` тело уже
снято, Relay (`DespawnedObjectEventFilter`) отбрасывает событие смерти. Клиенты не исполняют `DieInternal`:
молчат подписчики `PlayerDied` (`TeamSpawnZone`, `GhostViewEffect`, `PlayerGrabManager`).
Источник: network 4305, manipulation 4296/4309 (вариант C network 4303).

## Решение (дизайн согласован: network ACK 4313, manipulation 4309)

- `AvatarManager` — единственный исполнитель смены тела. `ServerReconcileBody`, вызванный внутри
  синхронизации (`SyncCallDepth > 0` или `IsInsideStateSync`), ставит отложенный запрос по сессии
  (без дублей, последний reason); исполнение — в `LateUpdate` того же кадра. Вне синхронизации — сразу, как сейчас.
- При исполнении: явная проверка `SyncCallDepth == 0 && !IsInsideStateSync` (иначе ждать следующего
  `LateUpdate`) и выбор желаемого тела по текущему состоянию сессии.
- Внутри обработчиков смерти — без Spawn/Destroy и без событий SDK. `ServerBecomeCorpse` (Rpc) и
  `MapReferee.OnPlayerDied` остаются в `Die()`.
- Часть manipulation (их zone-ack): `PlayerGrabManager` снимает подписку `PlayerDied → OnPlayerDied`;
  `PlayerController.RpcOnDied` не зовёт `ReleaseAllGrabbedObjects()`. Единственный отпускающий при смерти —
  сервер в `AvatarTeardown.ReleaseBeforeDestroy` на верхнем уровне.

## Этапы

1. `deferred-body` — реализация (writes в [plan.json](plan.json)). Ревью: network (сетевая часть до checkpoint),
   manipulation (хват). Проверки: `PlayerDeathSignalScenario`, `RoundReadinessMatchScenario`, стенд
   manipulation 453 (ServerOnly + 2 клиента: одно ReleaseObject на руку от сервера, одна поза предмета у всех,
   0 `UxrComponentNotFound`), EditMode Player/Avatars. Затем проверка пользователем в Unity.
2. `accepted-tests` — после приёмки пользователем: обновить тесты под новые ожидания.

## Состояние

- 2026-10-12: задача заведена, дизайн согласован; реализация не начата.
- Ждут этот этап: bots-fix `pf-equipment` (переставят `after` с grab-authority на `deferred-body`).

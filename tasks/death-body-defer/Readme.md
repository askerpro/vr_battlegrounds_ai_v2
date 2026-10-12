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
2. `accepted-tests` — после приёмки пользователем: обновить тесты под новые ожидания; удалить пустой
   `PlayerController.RpcOnDied` вместе с правкой `RpcCarriesNoStateTests`.
3. `death-equipment-drop` — после вливания `deferred-body` (согласие network 4396):
   `PlayerLoadoutManager.ServerHandlePlayerDied` сбрасывает снаряжение внутри `DieInternal`, вложенное событие
   до клиентов не доходит (предмет остаётся в кобуре мёртвого тела у клиентов). Снять подписку `PlayerDied`,
   сбрасывать только в отложенном шаге верхнего уровня (`ReleaseBeforeDestroy` / ветка без замены), один писатель.

## Состояние

- 2026-10-12: задача заведена, дизайн согласован.
- 2026-10-12: `deferred-body` реализован (3 коммита fix, после rebase на origin/dev — e625cf5d8, 920428536,
  28d12b7e4). Ревью: network ACK (4388, 4396), manipulation — хват одобрен по коду (4386). Проверено:
  стенд manipulation по e11f915f9 (до rebase) — PASS (4537: ServerOnly + 2 клиента, TestMap2, смерть у обоих
  клиентов, одно серверное ReleaseObject на руку, одна поза, kin=False; смерть без призрака не покрыта);
  офлайн-компиляция VrBattlegrounds (Roslyn, без weaver Mirror; зависимые сборки не собирались) — 0 ошибок.
  НЕ выполнены: компиляция в Unity с weaver, PlayerDeathSignalScenario, RoundReadinessMatchScenario,
  AvatarSwapDeathReplicationScenario, ярус C network (хост, поздний вход).
- 2026-10-12: принят пользователем (сессия эксперта gameplay); вливание в origin/dev. Детали — [Details.md](Details.md).
- Дальше: `accepted-tests` (тесты под новые ожидания, удаление пустого RpcOnDied), затем `death-equipment-drop`.
- Ждут этот этап: bots-fix `pf-equipment` (переставят `after` с grab-authority на `deferred-body`).

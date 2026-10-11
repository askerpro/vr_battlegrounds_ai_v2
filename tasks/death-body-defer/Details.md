# death-body-defer — детали этапа deferred-body

2026-10-12. Находки реализации; решения — в [Readme.md](Readme.md).

## API синхронизации UltimateXR

- `UxrStateSyncImplementer.SyncCallDepth` (static, `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/StateSync/UxrStateSyncImplementer.cs:55`):
  растёт в `BeginSync` (`UxrStateSyncImplementer_1.cs:168`), падает в `EndSyncState`/`CancelSync`
  (`UxrStateSyncImplementer_1.cs:190-192`, `253-259`). Событие уходит в сеть только с глубины 1
  (`UxrManager.cs:1819`, `UseTopLevelStateChangesOnly`).
- `UxrManager.Instance.IsInsideStateSync` (`UxrManager.cs:186`) — исполнение принятого события
  (`ExecuteStateSyncEvent`, `UxrManager.cs:657-667`).
- Условие «верхний уровень» — как `WeaponSystem.CanCommandNow` (`Assets/Scripts/Weapons/WeaponSystem/WeaponSystem.cs:443-444`):
  `SyncCallDepth == 0 && !(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync)`.
  В `AvatarManager` — `CanReplaceBodyNow()`.

## Вызывающие смены тела

| Вызов | Откуда | Внутри синхронизации? |
|---|---|---|
| `ServerReconcileBody("смерть")` | `PlayerController.Die` ← `OnActorDied` ← `UxrActor.DieInternal` | да (сервер, `BeginSync` смерти) → отложенно |
| `ServerReconcileBody(reason)` | `ServerEliminateSilently`: `EliminationMode` «бой кончился» (может прийти из цепочки смерти) / «новый аватар в матче» (из `Admit` внутри `ReplaceBody`) | по обстоятельствам — механизм решает сам |
| `ServerReconcileBody("возрождение")` | `PlayerController.Respawn` ← фазы раунда | обычно нет → сразу |
| `ChangeAvatar` | `SessionTeamAssigner.Apply` (выбор игрока, админ, автобаланс режима, боты), `PlayerSession` Cmd, `GameNetworkManager.OnServerReady`, `BotStandSession`, E2E | обычно нет; если да → отложенно с изъятием |

`ReplaceBody` остаётся единственным исполнителем; вызывают его только `ChangeAvatar`, `ReconcileBodyNow`
и исполнение очереди.

## Очередь и семантика слияния

- Одна запись на сессию (`PendingBody`: сессия, тело на момент постановки, последний reason, флаг `Change`).
- `Change` липкий (ИЛИ): `ChangeAvatar` в кадре — замена безусловная, снаряжение изымается
  (`ConfiscateBeforeDestroy`). Команда/скин уже записаны в сессию синхронно, поэтому параметры
  `ChangeAvatar` отдельно не хранятся; префаб выбирается при исполнении по сессии (выбывший — призрак).
- Желаемое тело выбирается при исполнении: смерть + возрождение одного кадра → ноль замен.
- Запись о другом теле (прямой вызов с верхнего уровня уже сменил тело) при новой постановке заменяется
  целиком; при исполнении — снимается.
- Исполнение: `AvatarManager.LateUpdate`. Сначала: сервер не активен → очередь снимается (Info);
  не верхний уровень → ждать следующего `LateUpdate`; `NetworkServer.isLoadingScene` → ждать
  (после загрузки тела нет → запись снимется; отменённая смена — исполнится).
- Устаревшая запись: сессия уничтожена/не на сервере; тело сменилось или пропало (смена карты,
  отключение, прямой вызов). Снимается молча с `GameLog.Player.Info`. Исключение одной записи
  ловится (`GameLog.Error`) и не мешает остальным.

## Отпускание при смерти

- Снята подписка `PlayerGrabManager` на `PlayerDied` и вызов `ReleaseAllGrabbedObjects` из
  `PlayerController.RpcOnDied` (RPC оставлен как лог-маркер: удаление требует правки
  `Assets/Tests/EditMode/Network/RpcCarriesNoStateTests.cs`, вне writes этапа accepted-tests).
- Единственный отпускающий при смерти — сервер, `AvatarTeardown.ReleaseBeforeDestroy` в `ReplaceBody`,
  теперь на верхнем уровне (`LateUpdate`). `AvatarTeardown.cs` не менялся.

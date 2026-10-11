# Смерть не доходит до клиентов: смена тела внутри DieInternal; решение — отложить на верхний уровень

Запрос manipulation 4296 (grab-authority/death-release-authority, прогон 453: ServerOnly + 2 клиента). Ответ 4303,
копия gameplay 4305. Факты собраны субагентом и перепроверены по коду `origin/dev`.

Корень: `UxrActor.DieInternal` → `Died` → `PlayerController.Die` → `ReconcileBody` → `AvatarManager.ReplaceBody`
→ `NetworkServer.Destroy(тело)` внутри `BeginSync`. К `EndSync` netId тела снят, и `DespawnedObjectEventFilter`
отбрасывает событие смерти на сервере. Клиенты смерть не исполняют, вложенное отпускание теряется вместе с ней.
Путь владельца `RpcOnDied → Cmd ReleaseObject` падает на уничтоженной руке. У наблюдателя предмет остаётся
кинематическим в позе мёртвой руки: сирот SDK чистит без событий, у оружия нет NT.

Решение network — вариант C:
- внутри обработчиков смерти — только локальные реакции, без событий SDK и Spawn/Destroy;
- смена тела откладывается на верхний уровень (`SyncCallDepth == 0 && !IsInsideStateSync`), владелец — gameplay;
- отпускание делает только сервер, на верхнем уровне, до Destroy (`AvatarTeardown.ReleaseBeforeDestroy`);
  авторство уже разрешено (`StateEventAuthority`: `UxrGrabManager`/`UxrActor`);
- вложенное отпускание в `PlayerGrabManager.OnPlayerDied` и Cmd владельца убрать (manipulation).

Отклонены: A (отпускать до Die) — много точек смерти, сама смерть всё равно не доходит; B (обход
`UseTopLevelStateChangesOnly`) — двойной повтор у клиентов, второй писатель.
Проверка — ярус C после правки и приёмки. Попутно: backlog `mirror-rigidbody-enable-arg`.

## Реализация gameplay: death-body-defer, diff e11f915f9 — ACK с условием (4382 → 4388)

`AvatarManager.CanReplaceBodyNow` проверяет условие при постановке и при исполнении. Очередь ведёт только
сервер, одна запись на сессию, тело выбирается при исполнении. `PlayerGrabManager` больше не отпускает на
смерти, `RpcOnDied` оставлен только с логом. held-motion manipulation (4363 → 4375): ACK, выстрел несёт позу
дула автора (`UxrProjectileSource.cs:152`).
Условие до вливания: при смерти без смены тела (нет призрака) отпускание не происходит. Нужна серверная
верхнеуровневая страховка в отложенном шаге.
Отдельная работа: серверные записи SDK внутри `DieInternal` (`PlayerLoadoutManager.ServerHandlePlayerDied →
RemoveObjectFromAnchor`, `ServerEliminateSilently` с Life=0 изнутри синхронизации) клиентам не уходят.
Их нужно перенести в отложенный шаг.

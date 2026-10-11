# События аватара до выравнивания UniqueId придерживаются, события снятого объекта отсекаются

NET-26 (`515feaa2b5780b091c5d0950290c73197d0ec292`): `AvatarStateEventGate` держит события невыровненного
сетевого аватара и отдаёт их по `UxrMirrorAvatar.AvatarSpawned`; очередь сбрасывается по `AvatarDespawned` или
через 10 с. Попутно исправлен досыл M16 (аргументы `EndSyncMethod`). Проверено автором: EditMode 176/176,
AndroidCompileGate, хост + клиент MPPM со шлемом на клиенте — ошибок нет.

Патч SDK 9 (`4068663ef51507192a85760af8cf17f65b8378ce`): `UxrMirrorAvatar.OnDestroy` снимает аватар с учёта
локально; `DespawnedObjectEventFilter` в Relay отсекает события объектов, netId которых уже нет в `spawned`.
Проверено автором: `DespawnedObjectEventFilterTests` (4), сетевой набор 100/100. Ограничение: в живой сессии
хост + клиент после последней правки фильтра не проверено (backlog `despawn-filter-live`).

Источники: коммиты выше; `Docs/UltimateXR/known-issues.md` Issue 12; `Docs/UltimateXR/sdk-patches.md` «Патч 9».

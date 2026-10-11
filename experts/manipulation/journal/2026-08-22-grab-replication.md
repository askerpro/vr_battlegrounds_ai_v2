# Манипуляции UltimateXR реплицируются: общий UniqueId рантайм-объектов (NET-16/17/23)

Коммит `c41f1e1c34edd5fbd53ac10592d2388fc6ba0659` (2026-08-22, в origin/dev). До правки по сети не
реплицировалось ничего из взаимодействия с предметами, кроме позиции: разный `UniqueId` рантайм-объектов.
Владелец идентичности — `NetworkUxrIdentity` (аватары исключены, их ведёт SDK); SDK-патч 6
(`AutoCreateStartAnchor`, `SetNetworkAnchor`); выдача ждёт `activeInHierarchy`; `RpcAssignItemToSlot`
заменён `SyncDictionary`.

Проверено (по тексту коммита): ворота PASS; EditMode 106/106; arsenal-item-grab 0,0,0; ярус C — 9 прогонов
подряд без незелёных; `UxrComponentNotFoundException` 0. Проверка в шлеме — unknown. Открыта NET-24
(режим отрисовки аватара не доезжает) — вне зоны хвата.

Источники: `git show c41f1e1c3`; `Docs/UltimateXR/known-issues.md` Issue 7.

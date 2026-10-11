# Манипуляции UXR реплицируются: единый владелец UniqueId рантайм-объектов — NetworkUxrIdentity

`c41f1e1c34edd5fbd53ac10592d2388fc6ba0659` (NET-16, NET-17, NET-23). Захват/выстрел/магазин отвергались у
получателя из-за разного UniqueId рантайм-объектов. `NetworkUxrIdentity` выравнивает id на обеих сторонах после
появления netId (сервер — `SpawnServerObject`, клиент — `RegisterPrefab` со спавн-обработчиком из
`GameNetworkManager.OnStartClient`); аватары исключены (их ведёт SDK, комбинация не идемпотентна).
Привязка «предмет → слот» — `SyncDictionary` на стене вместо разового `ClientRpc`.

Проверено автором: ворота PASS, EditMode 106/106, `arsenal-item-grab` 0,0,0 (был 1,1,1), девять прогонов
яруса C подряд без незелёных; UniqueId предмета netId=77 совпал на обеих машинах.

Источники: коммит выше; `Docs/audit/network-audit-2026-08.md` NET-16, NET-17, NET-23.

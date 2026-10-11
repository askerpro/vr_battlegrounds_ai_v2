# Барьер начального снимка Relay по MapRunKey и выдача UniqueId сгенерированным компонентам

`4f874aff04929f8672e008f688411fde3f3a92c1` (работа map-runtime-bootstrap в путях network): удалённый клиент
просит снимок UXR только для локально готового запуска карты; ответ несёт `MapRunKey(SessionEpoch, LoadSequence)`
и номер запроса — ответ старого запуска не применяется (`InitialStateBarrier`); незарегистрированный адресат
снимка — именованная ошибка (`InitialStateInventory`). Хост второй запрос не делает, выделенный сервер не ждёт.
Тесты и e2e — `5840ad1f0b42e3a945f7bb8ecf984e1e1eb1003e`: EditMode без новых падений против dev,
e2e `map-run-relay-barrier` (выделенный сервер + 2 клиента, поздний вход и перезагрузка карты) GREEN.

`8507d7d6d3b3ebfd3505a9f2a4c9d45ecea8d2cc`: `NetworkUxrIdentity.PrepareGeneratedIdentities` выдаёт заранее
вычисленные UniqueId неактивным компонентам UXR (через SDK патч 51), атомарно с отказом. Проверено в worker
(аренда 98): Android, Play Mode на шаблонах Pegboard/Shelf 29/29.

Окружение прогонов (ОС/Unity) — unknown. Источники: коммиты выше; `Docs/tasks/map-runtime-bootstrap-progress.md`.

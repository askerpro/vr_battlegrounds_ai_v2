# Этап generated-stations влит: адаптер генерируемых станций в запуске карты

Этап `map-runtime-bootstrap/generated-stations`: merged `c427527df9ab7b29d4f578220c6737e526724b37`
(код `7c400b1e4083243ec80bf7f24e6e34d8cce8c149`), проверен на worker и влит по поручению пользователя.

Проверено (база 2c845870): компиляция без ошибок, AndroidCompileGate PASS; Play-проба 32/32 без сети
(описание → config, Pending/Passed, клиентская половина, Closing, выгрузка, отказ старого ключа, `ConfigMismatch`);
EditMode Arsenal/ArsenalWall/Managers/Maps/Modes/Network/UI — 741 тест на ветке против 730 и 21 падения на
чистом dev, новых падений нет, `MapArsenalCompositionAdapterTests` 11/11.

Не проверено на момент слияния: серверная половина `TryBeginRun` с Generated-станцией и E2E (не было карты
со станцией) — закрыто записью 2026-10-09; сетевые предметы в пробах перестановок; шлем.

Источники: `tasks/map-runtime-bootstrap/Readme.md` «Статус»; `Details.md` «Проверки и пределы», «Критерии приёмки `generated-stations`»; хаб.

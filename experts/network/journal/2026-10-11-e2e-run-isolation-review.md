# Изоляция портов E2E-плееров: согласие network с шестью условиями (vr-test-stand/e2e-run-isolation)

Запрос: zone_ack_requested, событие хаба 3489, писатель launch-infra. Пути зоны network:
`Assets/Scripts/Network/GameNetworkDiscovery.cs`, `Assets/ThirdParty/Mirror/Components/Discovery/NetworkDiscoveryBase.cs`.
Дизайн: `tasks/vr-test-stand/Details.md` §15 в worktree launch-infra (коммит `4cc338552`). Run-E2E.ps1 передаёт
пару UDP в CLI, в плеере её применяет `E2EPlayerPorts` через `DebugBootstrapGate.PlayerBeforeNetworkStart`, в Editor
остаётся `PlayLaunchNetworkPorts`. Один писатель портов на процесс. Mirror patch 2 добавляет только аксессор
`ServerBroadcastListenPort`.

Решение: согласие с условиями. Порты — зона launch-infra, механизм старта сети — network. Новый писатель
сетевого состояния не появляется.

1. Хук выбирается в одном приватном методе для обеих точек старта: `ApplyRole` и `RequestClientConnect` (reconnect).
2. Порты применяются при каждом вызове, без кэша: у дубликата MANAGERS в Offline свой NetworkDiscovery на 47777.
   Проверки те же, что в Editor: диапазон, network≠discovery, PortTransport, сеть не активна.
3. Отказ портов валит прогон без отката на 7778: fallback-ветки сценариев `StartClient()` иначе подключат к чужому серверу.
4. Клиент применяет оба порта; проверка свободного порта нужна только Server/Host.
5. Патч Mirror содержит только аксессор: номер через `patch-reserve`, маркер `VR Battlegrounds patch`, запись в
   changelog задачи. Diff сетевых файлов показать network до checkpoint.
6. Добавлено 2026-10-11 при сверке с кодом на `17293aa3c`: поле `serverBroadcastListenPort` не переименовывать и
   не снимать `[SerializeField]`. Аксессор работает поверх поля. Поле по имени читают
   `PlayLaunchNetworkPorts.cs:182,248` (SerializedObject) и `SocketInheritanceTests.cs:83` (рефлексия), значение
   сохранено в сценах.

Остаточные риски: TOCTOU между выбором пары и bind; необработанный SocketException из `new UdpClient`
(`NetworkDiscoveryBase.cs:167`).

Формальный zone-ack: у network нет задачи в хабе, `ack_command` в 3489 пуст. Пробел инфраструктуры
принят сопровождающим (agent-infra-b1). `zone-override` он не оформил: при политике зон off он не нужен,
технически заблокирован регистрацией lobby-decoration в основном checkout, а ACK от имени пользователя
выдаётся только по прямому решению пользователя. Предложена задача-«дом» для network/ui/performance;
после её регистрации network сам выдаёт `zone-ack` по scope `07900133…7cc`.

## Ревью diff до checkpoint — ACK

`tasks/vr-test-stand/reports/network-zone.diff` (worktree launch-infra), замечаний нет. Аксессор Mirror поверх
поля; `PrepareNetworkStart` покрывает `ApplyRole` и `RequestClientConnect`; в плеере шов `PlayerBeforeNetworkStart`.
Вопрос launch-infra о ложном `NetworkAlreadyActive` у дубликата MANAGERS после disconnect: невозможно.
`OnClientDisconnectInternal` синхронно выполняет `OnStopClient` → `NetworkClient.Shutdown` (`connectState=None`),
и только потом `Invoke(ClientChangeOfflineScene)`, то есть Offline грузится не раньше следующего кадра. Если такой
отказ всё же придёт, это реальный второй старт сети и он валит прогон.

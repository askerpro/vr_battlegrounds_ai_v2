# bots-validation: старт сервера и первое подключение клиента, 2026-10-20

`GameNetworkManager` — единственный владелец доступности сервера: Server/Host стартуют с
`NetworkServer.listen = false` (Mirror поднимает граф без `Transport.ServerStart`), порт открывается
один раз после конца старта (`DecidePublication`: смена сцены и подписчики отработали,
`MapBootstrap.IsServerReady`, ещё кадр), затем событие `ServerPublished` → объявление в discovery.
Занятый порт — `Failed` и остановка; отказ карты — `Failed` без публикации. `Shutdown` возвращает
`listen = true`, поэтому флаг ставится перед каждым запуском.
`GameNetworkDiscovery` — первое подключение по явному адресу: до первого handshake обрыв даёт новую
попытку с паузой 1→2→4→5 с в общем сроке 180 с, затем `initial-connect-timeout`; после первого
подключения и после отмены повторов нет. KCP/Mirror не менялись.
Причина: trace829 — сокет раньше карты (тишина KCP) и клиент раньше сокета (SocketException без повтора).
Проверено пользователем (аренды 844/847); тесты `StartupConnectLifecycleTests`. Дизайн:
`tasks/bots-validation/contracts/startup-lifecycle-design.md`; бюджет синхронизации (S3) пока только замер.

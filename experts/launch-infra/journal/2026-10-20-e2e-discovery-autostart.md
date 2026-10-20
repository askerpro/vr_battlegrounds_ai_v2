# e2e-discovery-autostart: порты e2e-плеера до старта Discovery, 2026-10-20

heavy `--profile windows` при открытом Editor (держит UDP 47777) краснел плавающе: headless-сервер e2e-плеера писал
`SocketException` из `NetworkDiscoveryBase.Start()` → `AdvertiseServer()` раньше строки `[E2E] Порты применены`.
Причина — автостарт рекламы Mirror в headless на порту сцены, не порядок шва `PlayerBeforeNetworkStart`. Mirror патч 8
убрал автостарт; пара по-прежнему применяется одним писателем (`E2EPlayerPorts`) до старта сети, реклама — по
`ServerPublished` на применённом порту. Проверено: heavy windows при занятом 47777 — код 0, `SocketException` 0, клиент
нашёл сервер на изолированном discovery-порту.

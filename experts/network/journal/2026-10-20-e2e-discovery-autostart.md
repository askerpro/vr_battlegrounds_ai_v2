# e2e-discovery-autostart: Mirror патч 8 — Discovery без автостарта в headless, 2026-10-20

Апстримный `NetworkDiscoveryBase.Start()` в headless (`Utils.IsHeadless()`: `UNITY_SERVER` или графика Null) сам вызывал
`AdvertiseServer()` на порту сцены 47777 — второй писатель рекламы рядом с `GameNetworkDiscovery` (реклама по
`ServerPublished`). Следствия: выделенный сервер отвечал discovery до готовности карты (против S1), e2e-плеер биндил 47777
до шва `PlayerBeforeNetworkStart` и при занятом порте писал `SocketException`. Параметром инспектора автостарт не выключается.
Патч 8 убрал вызов из `Start()`; `ServerId` и `Transport.active` там остались.
Инвариант: `AdvertiseServer()` вызывает только `GameNetworkDiscovery`. Страж: `DiscoveryAdvertiseOwnerTests`.
Запись: `tasks/e2e-discovery-autostart/changelog/2026-10-20-sdk-8.md`. При обновлении Mirror — снова убрать ветку.

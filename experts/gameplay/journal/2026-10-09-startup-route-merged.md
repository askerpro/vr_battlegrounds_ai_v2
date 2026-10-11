# ServerStartupRoute влит: сервер стартует сразу в карту, контракт map-startup-route r2 активен

Этап `map-runtime-bootstrap/startup-route`: merged `f8daff6843f35c569268fc33f0d4b9a8537dd8d0` (код
`6b4b43b32471556de89f441a7a8befd86bb9c767`). Контракт `map-startup-route` ревизия 2 active, implementation
f8daff68, ACK vr-test-stand и map-runtime-bootstrap; r1 отклонена (замена на handle/RequestId по замечаниям
vr-test-stand). Решение: единственный владелец первой смены сцены сервера — `GameNetworkManager.ServerChangeScene`
через ServerStartupRoute; onlineScene остаётся конфигом (NET-21). Явная приёмка пользователем в источниках
checkout не зафиксирована (Readme писал «ждёт приёмки») — unknown; хаб: merged.

Проверено: аренда 243 (вход 05cd7867) — компиляция 0 ошибок, AndroidCompileGate PASS, EditMode
Managers/Maps/Network/Modes 549 тестов, 5 падений только в тестах содержимого сцен, `ServerStartupRouteTests` 13/13;
Play: хост TestMap1/elimination и выделенный сервер TestMap2 без режима — без Lobby, server Ready; стенд ботов —
`SceneNotLoadable` (не в списке сборки); без запроса — Offline → Lobby. E2E `map-run-startup-route` (аренда 253) GREEN,
поздний клиент через 40 с.

Ограничения: E2E и Play в worker делят порты 7778/47777. Запрос до загрузки Offline невозможен (нет каталога).

Источники: `tasks/map-runtime-bootstrap/Details.md` «Этап `startup-route`…»; `Docs/game-manager.md` «Первая сцена сервера»; хаб (contracts).

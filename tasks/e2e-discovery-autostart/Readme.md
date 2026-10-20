# Discovery без автостарта в headless: порты E2E до старта сети

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/e2e-discovery-autostart` (схема «задача = агент»).

## Цель

Headless-сервер собранного плеера не биндит стандартный discovery-порт до применения пары портов: рекламой Discovery владеет только GameNetworkDiscovery (после ServerPublished), автостарт Mirror в NetworkDiscoveryBase.Start() убран SDK-патчем. heavy --profile windows при занятом UDP 47777 даёт код 0 без SocketException.

## Итог (2026-10-20)

- Причина: `NetworkDiscoveryBase.Start()` Mirror в headless сам вызывал `AdvertiseServer()` на 47777 до применения пары
  портов e2e-плеера. Параметра отключения нет. Исправлено SDK-патчем Mirror 8
  ([changelog/2026-10-20-sdk-8.md](changelog/2026-10-20-sdk-8.md)); рекламой владеет только `GameNetworkDiscovery`.
- heavy `--profile windows` при занятом UDP 47777 (на коммите патча): код 0, `SocketException` 0 в логах обоих сценариев.
- Принято пользователем 2026-10-20. Страж: `Assets/Tests/EditMode/Network/DiscoveryAdvertiseOwnerTests.cs`.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/e2e-discovery-autostart/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

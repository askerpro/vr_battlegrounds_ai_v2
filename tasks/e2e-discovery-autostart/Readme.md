# Discovery без автостарта в headless: порты E2E до старта сети

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/e2e-discovery-autostart` (схема «задача = агент»).

## Цель

Headless-сервер собранного плеера не биндит стандартный discovery-порт до применения пары портов: рекламой Discovery владеет только GameNetworkDiscovery (после ServerPublished), автостарт Mirror в NetworkDiscoveryBase.Start() убран SDK-патчем. heavy --profile windows при занятом UDP 47777 даёт код 0 без SocketException.

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/e2e-discovery-autostart/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

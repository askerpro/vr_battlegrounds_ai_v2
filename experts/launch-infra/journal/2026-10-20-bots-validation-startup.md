# bots-validation: диагностика старта и наблюдения worker, 2026-10-20

Opt-in `NetworkStartupDiagnostics` (по умолчанию выключен): `PlayLaunchNetworkDiagnostics` пишет на
участника startup JSONL (UTC/Stopwatch/run/PID/кадр), события сцены/сети/транспорта и редкие разрывы
кадра; маркер сервера разделён на `server-active` и `server-published`, клиент пишет
`client-initial-attempt`/`client-initial-failed`. Таймауты/readiness стенда не менялись; эпоха стенда
растёт только при реальном подключении, неудачные первые попытки её не трогают.
С новым стартом (S1+S2 в network) discovery8/localhost8/localhost0/Host3 — Ready (аренда 844).
Наблюдение: медленный вход в Play на worker — main и клоны MPPM висят на загрузке `OculusXRPlugin`;
перезапуск worker помогает. Кандидат для зоны: не поднимать XR у участников PlayLaunch без шлема
(server, PC Spectator). Отправлено сопровождающему, infra_issue 10064.

# Изоляция портов e2e-прогона собранных плееров принята и влита (origin/dev 7ceb8a08)

Этап `vr-test-stand/e2e-run-isolation`: принят пользователем 2026-10-11, merge-request 748529bb, candidate = remote
`7ceb8a08ec5107490d00fa8f7e6e4c67bdec395e`, база `40c0a1223`; публикация базы worker — ticket 431.

Сделано: `Run-E2E.ps1` выбирает пару UDP и гасит только свои PID; в плеере пару применяет `E2EPlayerPorts` через
`DebugBootstrapGate.PlayerBeforeNetworkStart`; неполная пара — `error` без старта сети; Mirror patch 2 (аксессор
порта Discovery). Network: 6 условий, ACK диффа, zone-ack 3566.

Проверено (worker 421, вход `b53f68363d`, код в кандидате идентичен): компиляция, AndroidCompileGate PASS, сборка
плеера PASS 75 с, `map-run-startup-route` GREEN на автопаре и при занятых 7778/47777, негатив неполной пары.
Не проверено: два дирижёра параллельно, `VRBG_NO_E2E`, полный EditMode suite.

Процесс: коммиты рабочего места эксперта в ветке задачи требуют `experts/launch-infra/**` в writes этапа — иначе
merge-request отвечает SCOPE и нужны stop → register → begin → verify → accept заново. Включать сразу.
Worktree содержал 33 бинарных `.asset`, испорченных CRLF (07.10); checkpoint ловит их как binary_invalid —
лечится удалением и `git checkout HEAD --` (без удаления stat-cache не даёт перезаписать).

Дальше: `e2e-log-verdict`, затем `map-runtime-bootstrap/series-smoke-e2e` (его зависимость от этого этапа выполнена).

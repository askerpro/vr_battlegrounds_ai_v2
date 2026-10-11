# Адресный стенд и интеграция Play Launch

Обновлено: 2026-10-11. Владелец: эксперт `launch-infra`. Worktree: `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`,
ветка `codex/vr-test-stand`, база `17293aa3c`. План хаба rev 30.

## Цель и границы

Запускать нужные роли в нескольких процессах (Play Mode и собранные плееры) и направлять команды конкретному
участнику, изолируя прогоны разных checkout. Окно человека и MCP используют один planner и владельца request. Сеть
и карты ведут штатные владельцы игры.

## Устройство

PlayLaunch владеет frozen config; PlayModeTestStand — MPE/IPC; GameNetworkDiscovery — сетью. Адрес включает RunId,
ParticipantId, ProcessSessionId; reconnect проверяет epoch/avatar. Ready требует свежих heartbeat и фактического
допуска карты. Порты Editor: один писатель (`PlayLaunchNetworkPorts`) применяет frozen пару до старта сети.

Полный анализ: [Details.md](Details.md) (история итераций, §14 — порты). Машинный план: [plan.json](plan.json).
Контракт [play-launch-control](contracts/play-launch-control.json) r1 active. Инструкции: [Docs/test-stand.md](../../Docs/test-stand.md).
SDK patch 58 — [changelog](changelog/2026-10-08-sdk-58.md).

## Этапы

| Этап | Состояние |
|---|---|
| `play-launch-integration` | merged `51c8c047` |
| `docs-publication` | merged `9bcef90d` |
| `checkout-network-ports` | merged `07002dde` (прогон worker383) |
| `expert-workspace-pilot-handoff` | merged `1001415b` |
| `e2e-run-isolation` | running — проверено на worker, ждёт приёмки |

## Текущий этап: `e2e-run-isolation`

Дефекты дирижёра собранных плееров [Tools/e2e/Run-E2E.ps1](../../Tools/e2e/Run-E2E.ps1):
1. Портов нет: все процессы на 7778/47777 — SocketException при Play на worker.
2. Клиент ищет сервер через Discovery на общем 47777 и может подключиться к чужому серверу.
3. Гашение по имени процесса убивает плееры других worktree.

Решение: Run-E2E.ps1 выбирает свободную пару UDP и передаёт её CLI (`-e2eNetworkPort`, `-e2eDiscoveryPort`) всем
процессам прогона; гасит только свои PID. В плеере пару применяет E2E-харнесс через launch hook
`DebugBootstrapGate.PlayerBeforeNetworkStart` (в Editor остаётся прежний писатель). Порт discovery в плеере —
публичное свойство Mirror `NetworkDiscoveryBase` (патч SDK, номер через patch-reserve).

Зона network: согласие с 6 условиями, ACK диффа, zone-ack event 3566 (Details §15).

**Состояние:** реализовано, проверено на worker (аренда 421): компиляция и AndroidCompileGate PASS, сборка плеера
PASS, `map-run-startup-route` GREEN дважды — на автопаре и при занятых 7778/47777; неполная пара даёт `error` без
старта сети. Ждёт приёмки пользователем, затем коммит и штатное вливание.

Следом: `e2e-log-verdict` (агрегация логов всех процессов, механизм известных шумов), затем
`map-runtime-bootstrap/series-smoke-e2e`.

## Ограничения

HardwareXR Off, XR input/simulation, fault injection и recovery смерти координатора — последующие этапы.
Полный suite, физический клик toolbar и аппаратная приёмка не заявлены. Отчёты — `reports/`, вне Git.

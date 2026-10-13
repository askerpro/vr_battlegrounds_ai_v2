# Адресный стенд и интеграция Play Launch

Обновлено: 2026-10-13. Владелец: эксперт `launch-infra`. Worktree: `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`,
ветка `codex/vr-test-stand`. План хаба rev 36.

## Цель и границы

Запускать нужные роли в нескольких процессах (Play Mode и собранные плееры) и направлять команды конкретному
участнику, изолируя прогоны разных checkout. Окно человека и MCP используют один planner и владельца request. Сеть
и карты ведут штатные владельцы игры.

## Устройство

PlayLaunch владеет frozen config; PlayModeTestStand — MPE/IPC; GameNetworkDiscovery — сетью. Адрес включает RunId,
ParticipantId, ProcessSessionId; reconnect проверяет epoch/avatar. Ready требует свежих heartbeat и фактического
допуска карты. Порты Editor: один писатель (`PlayLaunchNetworkPorts`) применяет frozen пару до старта сети.

Полный анализ: [Details.md](Details.md) (история итераций, §14 — порты, §16 — sceneId в мультипроцессе).
Машинный план: [plan.json](plan.json). Контракт [play-launch-control](contracts/play-launch-control.json) r1 active.
Инструкции: [Docs/test-stand.md](../../Docs/test-stand.md). SDK patch 58 — [changelog](changelog/2026-10-08-sdk-58.md).

## Этапы

| Этап | Состояние |
|---|---|
| `play-launch-integration` | merged `51c8c047` |
| `docs-publication` | merged `9bcef90d` |
| `checkout-network-ports` | merged `07002dde` (прогон worker383) |
| `expert-workspace-pilot-handoff` | merged `1001415b` |
| `e2e-run-isolation` | merged `7ceb8a08` (worker 421) |
| `agent-play-recipe` | merged `7c42ac5d` |
| `stand-preview-preflight` | принят пользователем 2026-10-13, вливается |

## Этап `stand-preview-preflight`

Ошибки «Spawn scene object not found» у клиентов мультипроцессного стенда (находка bots 5080) давали preview-сцены
префабов оружия, висевшие в долгоживущем worker-редакторе: Mirror выдавал их объектам sceneId и спавнил на сервере.
Сделано:
- отказ `PreviewSceneNetworkObjects` в PlayLaunch (Plan/Play) и в обычном Play, со списком префабов;
- архив лога каждого участника в `Logs/VRBattlegroundsTestStand/<RunId>/` (MPPM перезаписывал логи клонов);
- гонка descriptor `active.json`: чтение с общим доступом и последним значением при сбое, повтор записи/удаления,
  флаг остановки после успешной записи — раньше стенд зависал в Stopping, а клон мог выйти из прогона.

Проверено на worker (аренды 533, 539): отказ при открытом MP5K и проход после выгрузки; S2C TestMap2 Ready дважды,
sceneId-ошибок 0; штатная остановка до Idle; AndroidCompileGate PASS; ProtocolHarness 32/32. Гонка во второй аренде
не воспроизвелась — проверено отсутствие регрессии. Архив `main` на worker неполный (общий Editor.log, infra_issue
5403). Патч Mirror для исключения preview-сцен — кандидат network (5302).

Следом: `quest-perf-pipeline/e2e-log-verdict`, затем `map-runtime-bootstrap/series-smoke-e2e`.

## Ограничения

HardwareXR Off, XR input/simulation, fault injection и recovery смерти координатора — последующие этапы.
Полный suite, физический клик toolbar и аппаратная приёмка не заявлены. Отчёты — `reports/`, вне Git.

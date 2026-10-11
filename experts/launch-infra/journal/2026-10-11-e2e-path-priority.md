# Приоритет — путь к series-smoke-e2e: сначала изоляция прогона собранных плееров

Пользователь 2026-10-11 передал выбор порядка работ эксперту. Решение: критический путь —
единственный запланированный этап хаба `map-runtime-bootstrap/series-smoke-e2e`. Его критерии
(«ноль ошибок во всех процессах», «порты по механизму vr-test-stand») не выполнимы на текущем
`Tools/e2e/Run-E2E.ps1`.

Найденные дефекты дирижёра (чтение кода, база `17293aa3c`):
- портов нет: сервер и клиенты всегда идут на 7778/47777 (SocketException при Play на worker);
- e2e-клиент ищет сервер через Discovery на общем 47777 и может подключиться к чужому серверу
  (Play другого checkout) — загрязнённый прогон хуже падения;
- `Get-Process -Name` + `Stop-Process` до и после прогона гасит все плееры `VrBattlegrounds`,
  включая чужие worktree;
- логи процессов не разбираются: исключения и ошибки синхронизации вердикт не меняют.

Порядок:
1. `vr-test-stand/e2e-run-isolation` (план rev 30) — пара портов плеера из CLI, свободная пара
   в Run-E2E.ps1, гашение только своих PID. Правки в зоне network (Mirror
   `NetworkDiscoveryBase` — публичное свойство порта discovery, патч SDK; launch hook
   `GameNetworkDiscovery` для плеера) — zone-request event 3489. У network нет задачи в хабе,
   адресный message `owner:network` отклонён (`UNKNOWN`); проектное ревью — через эксперта network.
   Рефлексия по protected-полю Mirror отвергнута.
2. `e2e-log-verdict` (бывш. `live-smoke-runner`) — агрегация логов всех процессов, механизм шумов.
3. `series-smoke-e2e` — содержание сценария с gameplay.

`async-player-build` идёт параллельно сначала диагностикой: побочные эффекты сборки задевают
файлы performance (`PC.asset`) и gameplay (`MapRuntimeCatalog.asset`). `xr-startup` понижен:
e2e-плеерам XR не нужен, он нужен Editor/MMP-стендам.

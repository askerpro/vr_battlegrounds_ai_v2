# scene-detail-followups: отказ LoadMap в вердикте perf-stress, 2026-10-22

PerfStressScenario (диагностический путь): перед `MapLoader.Instance.LoadMap` — `CanAcceptLoad`; отказ сразу пишется в
`result.Summary` («MapLoader отказал в загрузке …: причина»), а не теряется до таймаута ожидания MapRun. Карта по-прежнему
грузится только через MapLoader на сервере.

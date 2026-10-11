# Стресс-тест пишет память графики: gfx_mb, tex_mb, mesh_mb

Коммит `d07c183ff75fc9d70af8d0a4551495be7a78a30b` (в origin/dev). На Quest память общая, `mem_mb` графику
не отделял. `gfx_mb` (Gfx Used Memory) доступен и в release, `tex_mb`/`mesh_mb` — только Development.
`PerfMemoryCountersTests` сверяет имена счётчиков с профайлером (по сообщению — красный до правки).
Прогон на шлеме с этими колонками в источниках не найден — unknown.

Источники: `git show d07c183ff`; `Docs/perf-stress-test.md` «Метрики».

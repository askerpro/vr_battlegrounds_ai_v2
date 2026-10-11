Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет.

1. `budget-baseline` — согласовать бюджеты: частота 72/90 Гц, кадр по подсистемам, GC/кадр, draw calls/SetPass/треугольники, память (`gfx_mb`), кадр выделенного сервера, сеть; записать в `Docs/perf-stress-test.md` (сейчас есть только кадр) — journal 2026-09-29.
2. `quest-rerun` — прогон на шлеме: `ovr_gpu` не нулевой, `ovr_cpu_lvl` = 4, в заголовке `FFR: 0.75`; `ovr_gpu` p95 против прошлого (до 20 мс) — `Docs/perf-stress-test.md` «Дальше».
3. `remote-avatar-geometry` — ТЗ avatar-ik: слить SMR тела/снаряжения с атласом (MEF 49 SMR / 27 материалов, Heavy 52 SMR), LOD чужих ~15–20k треугольников; критерий — замер «по скинам» — `Docs/perf-stress-test.md` «Дальше», `Tools/mef-avatar/README.md`.
4. `scene-budgets` — с level-design: бюджеты сцены (видимые draw calls/треугольники, свет, тени) и критерий окклюзии; `OcclusionCullingBakedTests` не ловит устаревшие данные; ссылка `Docs/README.md` на `level-design.md` «Occlusion culling» ведёт в несуществующий раздел — сообщить level-design — `Docs/README.md`.
5. `puppet-weapons` — куклы повторяют оружие (копия предмета, выстрелы, гранаты); счётчики `Shot`/`Explosion`/`Grab`/`Release` в `PerfEvents` уже есть — `Docs/perf-stress-test.md` «Дальше».
6. `projectile-cost-review` — методика и ревью замера T-24 на Quest (`UxrWeaponManager.UpdateManager`, 2/4 стрелка) у weapon-system — `Docs/combat-networking.md` «Что измерить на Quest — и как».
7. `default-pipeline` — `ProjectSettings/GraphicsSettings.asset` ссылается на сэмпловый URP-Balanced из ThirdParty; нужен ли свой default — journal 2026-09-28 quest-graphics-baseline.
8. `ffr-intermediate-texture` — у `Default_Forward_Renderer` Intermediate Texture = Always; не теряет ли FFR основные проходы — `Docs/perf-stress-test.md` «Дальше».

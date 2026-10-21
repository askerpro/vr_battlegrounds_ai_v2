# Источник: вердикты ревью scene-detail/quest-gpu-lobby (хаб, раунды 1–3)

Выгрузка read-only из `.agent-state/coordination/coordination.sqlite`, 2026-10-22. Пункты этой задачи — неблокирующие замечания отсюда.

## zone-review:scene-detail/quest-gpu-lobby/performance/c55a328e550a4a4e round 1 sha b5056aa35272d20d3ca988c1f91c229f20ac01f8
verdict approve
Зона performance: блокеров нет. Границы: все пути покрыты writes этапа. Горячие пути: наблюдатель кадра без аллокаций (struct in, предвыделенный буфер, IO только после остановки), legacy E2E без нового канала, снимок SceneDetail и поиск по сцене только до разгона, второго писателя нет (Live плюс Apply владельца, полный откат render_diag и LOD).
Методика: цены по normal A/B с A/A и чередованием, условия и SHA записаны; итог Lobby 10,81 -> 4,26 мс (Quest 3 compat Quest 2, Test, FFR 0,75, CPU4/GPU2, center-north, AppGPUTime p50, A-B-A 10/10). EditMode 21/21 на коде 2986a88c5 (отличие от кандидата только документы); heavy код 1, красные EditMode совпадают с heavy e2e-combat (19), тестов зоны среди них нет.
Не блокирует: Tools/Perf/README.md:3-4 ссылки на несуществующие gpu-implementation-plan.md и gpu-package-v1.md; README:170 и quest_gpu_breakdown.py:24 без раздела про meta_renderdoc_quest.py.
Не блокирует: бюджет Lobby задан по худшему виду, итог измерен только center-north; changelog 2026-10-20-render-diag-ab.md:109 сузить формулировку, manifest:52 указать вид, до объявления бюджета нужен тур по 8 точкам; AGENT.md:42 Last measurement устарел.
Не блокирует: manifest:62 SHA a0432b240 и 8500240ab вне истории ветки, связать с 2f9a69b80 и 2d46a778f; после замера изменились BoxUvProjector v2 и эмиссия лампы, по сути без цены, отметить в журнале.
Не блокирует: PerfPlanarFloor.shader в Resources попадает в Prod; буфер PerfDiagnosticRecorder около 8 МБ в диагностическом прогоне (mem_mb несопоставим с обычным); меню LobbyGpu без префикса Tools.

## zone-review:scene-detail/quest-gpu-lobby/launch-infra/73b9029b0f3d48b0 round 1 sha b5056aa35272d20d3ca988c1f91c229f20ac01f8
verdict approve
launch-infra approve: границы зоны в writes; ps1/psm1 c BOM, LF, CR 0; Meta RenderDoc в Run-QuestE2E opt-in, finally ждёт launch/capture, finish и restore-only, доказано на Quest (GREEN, откат verified); KeepAwake/KEYCODE_SLEEP по решению пользователя 2026-10-20; RenderDocBridge в Run-E2E только instrumented/draw/perf-stress/1 клиент, PID до receipt; -GpuSceneIdentity только с Config Test и Development; карта только через MapLoader, второго запускателя нет; legacy-путь без RunSpec прежний.
Проверки: тесты 21/21 и heavy на 9371a586/2986a88c5 по игровым путям равны кандидату; провалы heavy (scripted-combat-hit, 2 новых красных EditMode) совпадают с heavy e2e-combat на dev 83d38b5e без отличий игровых путей от базы. Формально heavy код 1: вливание требует решения пользователя или known-red владельца.
P2 E2EDiagnosticSession.cs:477-493,788,810,936-946: discoveryOnly и not-applicable ffr/cpuLevel/gpuLevel для Windows расширяют gpu-launch-inputs r2 (dto-v2:144-155 запрещает ready при неполных условиях); зона принимает как совместимое дополнение по quality-discovery-v1/runtime-adapter-v1, follow-up: rev3 контракта в tasks/quest-perf-pipeline/contracts.
P2 Run-QuestE2E.ps1:1866-1872: KEYCODE_SLEEP до logcat и pull (до 600 с), Wi-Fi adb во сне надёжен около 5 мин; перенести сон в конец finally.
P2 Build-Game.ps1:38,106,118: флаг GpuSceneIdentity не записан в build.json/pair manifest; записывать в provenance.
P3 Scenarios/Resources/PerfDiagnostics/PerfPlanarFloor.shader попадает в Prod через Resources; Run-QuestE2E.ps1:1292-1300 readlink-проба до try/finally, Docs/testing.md:549 всё ещё /sdcard; Tools/e2e/.gitattributes дублирует корневые правила; путь RenderDocBridge на реальном Windows launch/capture не прогонялся.

## zone-review:scene-detail/quest-gpu-lobby/gameplay/da52b00556604814 round 1 sha b5056aa35272d20d3ca988c1f91c229f20ac01f8
verdict approve
Границы: 307 путей diff покрыты writes этапа; зона gameplay затронута только в MapRuntimeCatalog.asset и Assets/Scripts/Debug/E2E/**.
MapRuntimeCatalog.asset:36 - меняется только ContentFingerprint Lobby; независимый пересчёт v2 по git-блобам (Lobby.unity, MapData_Lobby, GameModeRegistry) даёт a7553c0b на базе и adc567bb на кандидате - совпадает; ArsenalFingerprint и прочие карты без изменений.
Lobby.unity - изменения только рендерные (ReceiveGI, ScaleInLightmap, LOD, тени, зеркало 512 и 8 м, 4 лампы InteriorWarmLight, меш пола на LobbyTentFloor.asset); NetworkIdentity, sceneId, спавны, станции и MapBootstrap не задеты; MeshCollider пола не изменён (выключен и на базе).
PerfStressScenario.cs:743 - карта грузится только через MapLoader.Instance.LoadMap на сервере; MapReady только читает MapRunAuthority.Current; второго писателя нет; без RunSpec сценарий идёт прежней веткой.
Тесты ticket 973 21/21 на 2986a88c5 - код и ассеты совпадают с кандидатом (разница только документы). Heavy код 1 по словам автора совпадает с e2e-combat на dev; ревьюер это не перепроверял.
Не блокеры: PerfStressScenario.cs:743 игнорирует отказ LoadMap (причина теряется до таймаута); PerfStressDiagnosticAdapter.cs:118 - покадровый MapReady в диагностике (оценка за performance); E2EDiagnosticSession.cs:788 и 936 - каркас launch-infra, нужно её ревью.
Approve не равен приёмке пользователем: лобби как этап сессии после перезапекания в Play или в шлеме этим ревью не проверено.

## zone-review:scene-detail/quest-gpu-lobby/performance/b1475f8ced744ace round 2 sha dc01bc670aed72b7846af1f6e05050698b166535
verdict approve
Зона performance, раунд 2: блокеров нет. Пути этапа те же, код и ассеты этапа идентичны одобренному b5056aa35 (0642c03c2 по путям этапа без разницы); приращение только документы dc01bc670; правки dev 670741f53 (e2e-combat) путей этапа не пересекают. Выводы раунда 1 в силе: горячие пути без аллокаций, второго писателя нет, контракт gpu-scene-evidence rev 1 соблюдён, методика normal A/B с условиями и SHA.
Закрыто: manifest:52, changelog:109-110, Readme и AGENT.md:42-43 говорят вид center-north, тур по 8 видам не сделан, бюджет по худшему виду не подтверждён; changelog:111-113 отмечает изменения после замера (BoxUvProjector v2, эмиссия лампы).
Не блокирует, исправить до merge-execute: Docs/quest-performance-manifest.md:62 и changelog 2026-10-20-render-diag-ab.md:111 называют 2f9a69b80 и 2d46a778f коммитами истории ветки, но после rebase на 670741f53 они не предки dc01bc670 (сейчас это 42344e021 и c64e87b77); ссылаться на тему коммита или на финальный SHA после последнего rebase.
Не блокирует (из раунда 1, без изменений): Tools/Perf/README.md:3-4 ссылки на несуществующие план и контракт пакета; README:170 и quest_gpu_breakdown.py:24 без раздела про meta_renderdoc_quest.py; PerfPlanarFloor.shader в Resources попадает в Prod; буфер PerfDiagnosticRecorder около 8 МБ; меню LobbyGpu без префикса Tools.

## zone-review:scene-detail/quest-gpu-lobby/launch-infra/e68eec46684a47ca round 2 sha dc01bc670aed72b7846af1f6e05050698b166535
verdict approve
launch-infra approve раунд 2: diff этапа по зоне (Tools/e2e, Tools/release, Assets/Scripts/Debug/E2E) текстуально идентичен раунду 1 (b5056aa35 от 500991c0a), список путей тот же; дельта b5056aa35..dc01bc670 по Assets/Tools/Packages/ProjectSettings равна дельте dev 500991c0a..670741f53, новый коммит dc01bc670 только документы; ps1/psm1 BOM, CR 0.
Взаимодействие с dev e2e-combat: E2ERunner.cs:443 только регистрация scripted-combat-next-round; ScriptedClientRunner/ScriptedClientScenario не ссылаются на StressTest, SceneDetail, PerfStress и diagnostic-контекст; диагностический допуск E2ERunner.cs:114-130 из базы отклоняет неизвестный diagnostic scenario; общих файлов и писателей нет.
Проверки: тесты 21/21 и heavy на 2986a88c5 по игровым путям больше не равны кандидату (dev добавил E2E-правки); heavy на новой базе ещё не перепрогнан, его код и verdict.json приложить к merge-request; код 1 требует решения пользователя или known-red владельца.
Открытые follow-up раунда 1 без изменений: P2 rev3 контракта gpu-launch-inputs (discoveryOnly, not-applicable Windows), P2 Run-QuestE2E KEYCODE_SLEEP до logcat/pull, P2 GpuSceneIdentity в build provenance, P3 шейдер в Resources попадает в Prod, readlink-проба до finally, Docs/testing.md с путём /sdcard, дублирующий Tools/e2e/.gitattributes, RenderDocBridge не прогнан на Windows.

## zone-review:scene-detail/quest-gpu-lobby/gameplay/b4231b0152e9472e round 2 sha dc01bc670aed72b7846af1f6e05050698b166535
verdict approve
Раунд 2: range-diff показывает, что 24 коммита раунда 1 перенесены на 670741f53 без изменений; новый dc01bc670 только документальный (quest-performance-manifest.md, experts/performance/AGENT.md, Readme и changelog задачи).
Файлы зоны не изменились относительно b5056aa35: diff пустой по Assets/Data, Assets/Scenes, Assets/Scripts/Maps, Managers, PerfStressScenario.cs, Perf*-сценариям и E2EDiagnosticSession.cs; сдвиг dev (e2e-combat) этих путей и входов отпечатка не трогает.
MapRuntimeCatalog.asset:36 - пересчёт отпечатка v2 по git-блобам на dc01bc670 даёт adc567bb, совпадает с каталогом; ArsenalFingerprint и прочие карты без изменений.
Границы: те же 307 путей покрыты writes этапа; выводы раунда 1 по Lobby.unity (только рендер), MapLoader.LoadMap на сервере и чтению MapRunAuthority в силе.
Не блокеры раунда 1 остаются: PerfStressScenario.cs:743 игнорирует отказ LoadMap; PerfStressDiagnosticAdapter.cs:118 - покадровый MapReady (оценка за performance); E2EDiagnosticSession.cs:788 и 936 - ревью launch-infra.
Тесты 973 и heavy прогонялись на дереве старой dev (2986a88c5); объединённое дерево с e2e-combat не прогонялось, пересечения по файлам нет.
Approve не равен приёмке пользователем: лобби как этап сессии после перезапекания в Play или в шлеме не проверено.

## zone-review:scene-detail/quest-gpu-lobby/performance/b091bf033e0c4fe3 round 3 sha bae6e5352d6a822cab2da5aad2f47a9945dd91b8
verdict approve
Зона performance, раунд 3: блокеров нет. Пути этапа те же; относительно одобренного dc01bc670 изменились только BoxUvModelPostprocessor.cs, BoxUvTargets.cs, manifest:62 и changelog; правки dev b681a938f (ASTC-фиксер, AssetAudit) путей этапа не пересекают. Рантайм не менялся, выводы раундов 1-2 о горячих путях, втором писателе, контракте gpu-scene-evidence rev 1 и методике в силе.
Закрыто замечание раунда 2: manifest:62 и changelog:111-112 называют измеренные сборки по темам коммитов, не по SHA.
Box-UV v3 корректен: путь конфига от Application.dataPath, зависимость от конфига для всех моделей Assets/Art/Lobby (все 23 модели-цели внутри). Цена только в редакторе: GetVersion 3 однократно переимпортирует все около 1179 моделей проекта в каждом Library (не только 166); правка BoxUvTargets.json переимпортирует 166 моделей Lobby, приемлемо при редком Scan And Apply. Мелочь: delayCall ReimportTargets повторно импортирует 23 цели, уже переимпортированные по зависимости.
Доказательства: heavy 985 на a53029f51 (дерево Assets совпадает с кандидатом, отличие только changelog): три сценария GREEN, EditMode 2890, красных 19, тестов Lobby, BoxUv, Perf, Stress среди них нет; FAIL проверки код worker содержит dev формальный (родитель чекпоинта 670741f53, дерево с новой базой).
Не блокирует (из раунда 1, без изменений): Tools/Perf/README.md:3-4 ссылки на несуществующие план и контракт пакета; README:170 и quest_gpu_breakdown.py:24 без раздела про meta_renderdoc_quest.py; PerfPlanarFloor.shader в Resources попадает в Prod; буфер PerfDiagnosticRecorder около 8 МБ; меню LobbyGpu без префикса Tools.

## zone-review:scene-detail/quest-gpu-lobby/launch-infra/e07d2bf07936440f round 3 sha bae6e5352d6a822cab2da5aad2f47a9945dd91b8
verdict approve
launch-infra approve раунд 3: diff этапа по зоне (Tools/e2e, Tools/release, Assets/Scripts/Debug/E2E) от базы b681a938f текстуально идентичен раунду 2 (dc01bc670 от 670741f53), список путей тот же; ps1/psm1 BOM, CR 0. Свежая dev (astc-fixer: texture meta, Editor/Performance/AssetAudit) Tools, Assets/Scripts, Packages, ProjectSettings не трогает, с E2E-диагностикой не пересекается; box-UV v3 вне зоны.
Heavy ticket 985: дерево worker a53029f51 полностью равно ветке 361eca984 (git diff пуст), поэтому FAIL проверки код worker содержит dev ложный (проверка по происхождению checkpoint, не по содержимому); scripted-input-smoke, wall-purchase, combat-hit GREEN; EditMode 2 новых красных (GameTagsTests, HandsPackWeaponTests Scar) из dev, ветка оружие не трогает; формально код 1 требует решения пользователя или known-red владельца.
Открытые follow-up раунда 1 без изменений: P2 rev3 gpu-launch-inputs, P2 KEYCODE_SLEEP до logcat/pull, P2 GpuSceneIdentity в provenance, P3 прежние. Новое P3 владельцу Tools/e2e/heavy: сверять свежесть worker по содержимому игровых путей, а не по происхождению коммита.

## zone-review:scene-detail/quest-gpu-lobby/gameplay/c7782d19b42246f2 round 3 sha bae6e5352d6a822cab2da5aad2f47a9945dd91b8
verdict approve
Раунд 3: range-diff показывает, что 25 коммитов раунда 2 перенесены на b681a938f без изменений; новые d93160970 и bae6e5352 - документы, 361eca984 - box-UV v3 в Editor Performance/BoxUv (вне зоны gameplay).
Файлы зоны не изменились относительно dc01bc670: MapRuntimeCatalog.asset, Lobby.unity и E2E-сценарии идентичны; сдвиг dev (astc-fixer: png.meta, AssetAudit) не трогает Assets/Data, Assets/Scenes и Assets/Scripts.
Пересчёт по git-блобам bae6e5352 всех 7 записей каталога (TestMap1-3, Lobby, ServiceYard, ReferenceMap04, BotCombatStand): ContentFingerprint и ArsenalFingerprint совпадают у каждой, Lobby adc567bb.
Границы: те же 307 путей покрыты writes этапа.
Heavy 985 на a53029f51 (отличие от кандидата - только changelog): сценарии GREEN, MapRunPreflight и MapCatalogIntegrity не в красных - по записи автора, verdict.json ревьюер не открывал.
Не блокеры прежних раундов остаются: PerfStressScenario.cs:743 игнорирует отказ LoadMap; PerfStressDiagnosticAdapter.cs:118 - покадровый MapReady (оценка за performance); E2EDiagnosticSession.cs:788 и 936 - ревью launch-infra.
Approve не равен приёмке пользователем: лобби как этап сессии в Play или в шлеме не проверено.

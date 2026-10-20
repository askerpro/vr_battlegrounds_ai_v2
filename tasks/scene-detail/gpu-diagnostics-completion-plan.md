# План завершения детальной GPU-диагностики

> Для исполнителя: `superpowers:executing-plans`, последовательное выполнение в этом
> диалоге. Один ведущий реализует изменения во всех нужных файлах своего checkout;
> эксперты ревьюят готовый результат. Делегированное read-only исследование допустимо.

**Цель:** автор сцены быстро находит дорогую работу в Editor/Windows, сопоставляет
проходы/ресурсы/объекты и проверяет эффект конкретной правки настоящими Q3 A/B.
**Архитектура:** существующий E2E launch/provider → actual phase/view/conditions и
raw recorder → неизменяемый машиночитаемый пакет → анализ агентом; подробные GPU captures
связываются с scene/build manifest. SceneDetailApplier остаётся единственным writer.
**Стек:** Unity6 URP, существующие E2E/PlayLaunch, C#, Python stdlib, PowerShell,
доступные Meta/Unity/RenderDoc backend после проверки capabilities.
**Основание:** [восстановленный статус](status-2026-10-18.md), авторская
[LOD-пара](contracts/lod-ab-control-v1.json), spec/package/runtime notes из
`1d85f2305da7d26312e190a6ae19f02c64329ed5:tasks/perf-stress-scenario/contracts/`,
принятые `tasks/quest-perf-pipeline/contracts/gpu-launch-inputs.md` и `gpu-launch-dto-v2.md`.

Дата18.10.2026. Статус: **реализация разрешена пользователем и начата**.
Rebase и восстановление источников/доказательств уже выполнены.

Уточнение пользователя18Oct: HTML не нужен. Обязательный интерфейс — JSON/JSONL/CSV
с происхождением, условиями, связями, выборками и A/B-дельтами, которые агент читает
ограниченными порциями. Развитие HTML и браузерная приёмка исключены из плана.

## Правила выполнения и критерий результата

- Все изменения выполняю сам в71af; нет будущих ожиданий «perf/launch/UI напишет»
  или zone-ACK. Контракты — проверяемые интерфейсы. Их нужные изменения также делаю
  здесь, с версией/совместимостью и последующим экспертным ревью.
- Чужие checkout остаются только для чтения. Unity — через собственный broker lease;
  сборочные root — по штатному свободному dedicated route с immutable source.
- Сначала ранний полезный Editor/Windows отчёт, затем Q3-подтверждение. Quest2 —
  notMeasured; запас Q3 не объявлять измерением Q2. Новые GPU tools не заменять ещё
  одним launcher, renderer, LOD/mip controller или универсальным профайлером.
- Normal GPU замеры с detailedOFF, instrumented/replay captures отдельно. Источники,
  units/scope/cadence/clock/overhead/потери отражены. Unknown/null не заменять нулём.
- Timing родительских/дочерних/перекрывающихся passes не суммировать. Ресурсная связь
  не равна стоимости; mesh/texture/light/shader обычно разделяют стоимость прохода.
  Exact/group/candidate/unresolved и контролируемая workload delta остаются различимы.
- До пользовательской проверки изменённого поведения в этом плане — compilation и
  существующие применимые checks, без новых/переписанных tests и проб с исполняемыми
  ожиданиями, включая tooling. Реальные продуктовые прогоны и просмотр отчёта нужны
  для самой проверки. После подтверждения —
  закрепляющие tests, review и штатное FF-вливание. Checkpoint не является приёмкой.
- Не входят: shader-tier, per-prefab/per-object override system, автоматическая загрузка
  любого runtime JSON, новый UI-профайлер и дальнейшая разработка HTML.

Готовый результат: воспроизводимые Windows и Q3 пакеты/отчёты, переход от дорогой
или изменившейся работы к месту правки, доказанный mesh/LOD A/B, примеры geometry,
texture, pass и light, повторяемость/ошибки/coverage, понятный автору следующий шаг.
Отдельно фиксируется состояние бюджета Lobby; наличие отчёта не означает его достижение.

## 0. Рабочая база и восстановление — выполнено / административный хвост

- [x] Rebase на dev2e1b6a2fc, сохранение исходного47345cb51 и несвязанного WIP.
- [x] Сверка core/UI/provider и unmerged perf;9файлов evidence сохранены с хешами.
- [x] Восстановление взаимодействий, контрактов и реальных границ доказательств.
- [x] Штатная передача hub task к71af выполнена сопровождающим (rev24/event9672),
  текущий route опубликован epoch8. OWNER guard сохранён.

**Выход:** один действующий task/worktree/route и свежая accepted base перед lease.
План/локальный код не требуют повторных zone согласований.

## 1. Собрать исходники и показать первый полезный структурный отчёт

**Файлы из1d85f2305, переносить выборочно, не весь branch/WIP:**
`Tools/Perf/gpu_analyze.py`, `gpu_diagnostics/{__init__,model,report,scene_report,collector}.py`,
`quest_gpu_breakdown.py`, README; SceneDetailApplier accessor/DiagnosticsSnapshot;
`Assets/Editor/VR_Battlegrounds/Performance/GpuSceneManifestExporter.cs`;
`Assets/Scripts/Debug/StressTest/{PerfDiagnosticObservation,PerfDiagnosticRecorder}.cs`,
hooks в PerfFrameRecorder/StressTestClientSession и соответствующие meta.

- [ ] Сверить scoped diff с нынешним dev, отделить технические checkpoints от принятого
  кода, сохранить provenance; перенести нужные specs в материалы этой задачи.
- [x] Сохранить `TryCaptureDiagnostics(out snapshot,out reason)`, detached original/
  effective/mapping и readonly exporter; не переписывать Apply/Restore ради экспорта.
- [x] Получить JSON/JSONL-пакет через `gpu_analyze.py inspect-scene` из существующего
  `reports/status-recovery-20261018/performance/scene.json`, проверить реальный `--help`.
- [x] Выполнить ограниченные машиночитаемые выборки: поиск, ID, связи object→renderer→
  mesh/material→shader/texture и LOD/light. Сохранить исходные поля, unknown/unresolved
  и хеши; структурные кандидаты отделить от измеренных GPU bottlenecks.
- [x] На текущем checkpoint получить compilation/существующие применимые checks;
  старый r2 receipt без inputSHA не переносить как проверку новых исходников.

**Выход:** первый машиночитаемый пакет с реальными данными сцены и явным статусом
«структура, не GPU timing». Для этого не нужна новая Android-сборка или повторный
Unity export только ради наличия JSON.

## 2. Соединить actual workload, рекордер и принятый provider

**Менять:** `Assets/Scripts/Debug/E2E/Scenarios/PerfStressScenario.cs`, необходимые
StressTest callbacks; добавить узкий `PerfStressDiagnosticAdapter.cs` рядом с recorder.
Provider уже принят: E2EContext/E2EDiagnosticSession повторно не создавать.

**Потреблять:** `StressTestClientSession.ObserveRuns(IStressTestDiagnosticObserver)`
до TryStart; `PerfDiagnosticRecorder.FlushPackage(string)->PerfDiagnosticExportResult`;
`ctx.Diagnostics.CaptureStamp`, ReportPhase/View/Conditions/WorkloadReady и InvalidateWorkload,
каждый с проверкой Accepted/EventId/Reason. Сигнатуры DTO — принятый gpu-launch-dto-v2.

- [ ] Обеспечить фактически загруженную canonical Map через существующий server flow/
  MapLoader, не считать `-Map` доказательством загрузки. Readiness: MapRunAuthority,
  MapBootstrap.ForScene и IsLocallyReady для того же MapRunKey.
- [ ] Подписка до старта, реальные Begin/End и generation, свежая measured-phase поза
  после settle без второго переноса, actual fingerprint и условия, затем ready.
  Один provider event writer; recorder не создаёт/удерживает фазы.
- [ ] Invalidate при смене map/view/settings, focus, restart/disconnect/stop; dispose
  в finally и flush после остановки, включая неуспешный старт. На кадре — без IO/CSV.
- [ ] Зарегистрировать raw artifacts через существующий harvest, раздельно по run/role/
  process; source/build/tool/profile hashes, events/receipts/completeness не теряются.
- [ ] Сделать версионированную applicability policy для Windows XRoff/серверов:
  Quest-only FFR/levels/eye fields явно notApplicable с причиной. Сейчас r2 требует
  non-null всех полей и блокирует ready. Не ставить выдуманные0 и не ослаблять Q3 guards.
  При изменении provider уточнить DTO/spec и сохранить legacy E2E без RunSpec.

**Выход:** реальный цикл phase→fresh view/conditions→ready→samples→invalidate/end→harvest
для rendered client; server не выдаёт аппаратный GPU PASS. Пока без обещания точного source-clock.

## 3. Runtime identity, нормализация и достоверность времени

**Файлы:** exporter/SceneDetail DTO по необходимости; новые узкие
`Tools/Perf/gpu_diagnostics/import_runtime.py` и `compare_series.py`; model/report;
runtime adapter/recorder. Планируемые новые файлы identity:
`Assets/Editor/VR_Battlegrounds/Performance/GpuSceneIdentityBuildProcessor.cs` и
`Assets/Scripts/Debug/StressTest/PerfSceneIdentityTable.cs` с Unity-generated meta.
Единственный формат пакета — согласованный gpu-package-v1
с явно описанной совместимой версией расширений.

- [ ] Создать build-time authoring/resource manifest из exact source: scene GlobalObjectId,
  mesh/material/texture GUID+localID/hash, prefab/source identity. Доставить его в build
  существующим build route; runtime run-local ID связать доказанным способом, без поиска
  по имени/порядку. Неудачный join остаётся unresolved с contributors.
  Конкретный путь: build processor добавляет одну opt-in identity table с serialized
  object refs/authoring IDs в генерируемую копию сцены диагностической сборки; исходную
  сцену/профиль не сохраняет. `PerfSceneIdentityTable.TryResolve(Object,out string)`
  обслуживает только чтение; dynamic/spawned без доказанного source join остаются
  run-local. В build попадает компактный индекс, не полный56МБ JSON/HTML. Участие
  processor и hash таблицы отражаются в provenance, измерительный overhead проверяется.
- [ ] `import_runtime_bundle(source:Path,out:Path)->Path`: raw inventory/manifest/receipts
  → run/samples/events/scene/quality, immutable raw, отдельные units/scope/cadence.
  Poll count не равен числу уникальных GPU frames; summary не превращать в raw.
  Для нынешнего рекордера добавить версионированный observational вариант:
  `observations.csv(metric,observedTicks,value)` с clock/tickFrequency, timeMeaning=
  observation, sourceFrameId/sourceInterval=null и cadence=observation в metadata.
  `samples.csv` содержит только источники с доказанной семантикой интервала; из
  polling не создавать start/end якобы выполнения GPU. Model/report отдельно
  читают observation series как limited и показывают polling count, не frame count.
  Отсутствие и samples, и observations остаётся invalid, а не пустым успешным пакетом.
- [ ] Сохранить процессный Stopwatch clock и native/source clock отдельно; реализовать
  и проверить alignment/uncertainty/latency там, где backend действительно даёт данные.
  Нынешний Oculus polling source frame/timestamp не даёт: без доказательства strict
  verified window остаётся false. Stable-workload observational A/B помечать limited,
  не объявлять точной покадровой атрибуцией; границы фаз обрезать только обоснованно.
- [ ] `compare_series(pairs,aa)->dict`: между-run шум и ≥3 matched counterbalanced пары,
  p50/p95/count/distribution/timeline, delta ms/%, без значимости из тысяч зависимых кадров.
  Единственная разрешённая variant difference проверяется по actual settings/snapshots,
  а не только обходом qualityHash. Stale/empty/phase-crossing/missing остаются видны.

**Выход:** повторно открываемый пакет с проверяемым происхождением и честным классом
достоверности; готовность процесса и точность GPU timestamp — разные доказательства.

## 4. Быстрый Windows/Editor цикл — раньше глубоких Quest-примеров

**Файлы:** существующие `Tools/e2e/{Run-E2E.ps1,E2EDiagnosticInputs.psm1}`,
E2EPlayerBuilder/BuildProvenance только если обнаружен реальный gap; Perf tools/adapters.

- [x] Проверить свободный `build-test-win64` root, immutable SHA, locks/processes/LFS/
  cleanliness и зависимости. Переиспользовать build только при matching provenance.
- [x] До первого Windows capture проверить установленный backend/version/API и
  доступные команды/экспорт; выбрать поддержанный графический API сборки, сохранить
  capabilities. Глубокая Q3-атрибуция для этого не является зависимостью.
- [x] Собрать rendered E2E Player через `VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch`;
  артефакт `Build/e2e/VrBattlegrounds.exe` вместе с `_Data`, не GameBuilder.Server.
- [x] Заморозить RunSpec+build.json+views+phasePlan hashes. Запустить существующий
  `Run-E2E.ps1 -Player <exe> -RunSpec <spec> -Clients 1 ...`; клиент без nographics.
  Фактические resolution/API/quality/XRoff/projection/MapRun/pose сохранить.
- [ ] Подтвердить source kind полным immutable input/build proof: accepted source или
  технический checkpoint. Сейчас builder может оставить sourceKind=unknown; при
  необходимости уточнить BuildProvenance/Build-Game, а не выводить принятие из HEAD
  или имени EXE. Unknown остаётся ограничением отчёта, пока proof не получен.
- [x] Проверить runtime snapshot/harvest и отчёт; получить доступный GPU capture выбранного
  backend и связать события/ресурсы. Editor overhead отделить от standalone timings.
- [x] Демонстрационный Windows A/B: видно изменение работы/ресурсов и actual settings,
  можно перейти к объекту и выбрать следующую правку. Неприменимые Quest поля не блокируют
  структурный режим и не маскируются как measured.

**Выход:** основной короткий shift-left цикл работает; новые Android builds не нужны
для каждой проверки структуры или отдельного изменения уже управляемого LOD.

19.10: Testd5db59ea, instrumented capture pair GREEN. Native-r11 и offline joins-r4/
comparison-r3 подтверждают7geometry changes и−38358submitted indices; GPU benefit=null.
Read-only аудит двух арифметических/snapshot замечаний закрыт. Normal A/A/repeated A/B,
полный immutable build-kind proof и пользовательская quality validation ещё впереди.

## 5. Настоящий Quest3 baseline и чистый LOD A/B

**Данные:** `contracts/lod-ab-control-v1.json`; временная тестовая конфигурация,
не постоянное изменение production LobbyDetailProfile.asset.

- [ ] Preflight `build-test-android`, exact checkpoint/IL2CPP/API/build manifest; собрать
  через существующий RunQuestBatch. APK — `Build/Quest/VrBattlegrounds_Quest.apk`.
  Затем freeze bundle и `Run-QuestE2E.ps1 -Apk <apk> -ServerPlayer <exe> -RunSpec <spec> ...`.
  RunSpec нельзя совмещать с -Build/-DryRunBuild: build раньше запуска.
- [ ] Выполнять A/B в одном binary, если параметры задаются существующим control path:
  до warmup применить Live/config через Applier.Apply, после теста вернуть исходное;
  diagnostic accessor/exporter не становятся writers. Во время замера UI изменения
  либо исключены сценарием, либо вызывают drift/invalidation. Записать actual diff.
- [ ] A:farStart25; B:20; midStart10, mid.skip0, far.skip9; textureBias/cullSize0 в обоих
  планах обоих вариантов.40групп меняют правило; effective mesh каждого подтверждается
  отдельно. far.skip9 уже clamp: увеличение9 — не тест.
- [ ] Зафиксировать вид на GapFillFootSlope/SM_Cliff_03(group1035138807,24.958735м),
  реальную pose/projection/видимость. Если исходный mesh уже последний — выбрать
  подходящий вид/кандидат; ForceLOD и догадка по distance не заменяют capture.
- [ ] Warmup/thermal/частоты/FFR/display/eye resolution известны. Сначала A/A, затем
  ≥3 matched пары с чередованием порядка. Normal detailedOFF отдельно; detailed capture
  имеет собственное overhead/clock evidence и не заменяет normal delta.
- [ ] До приёмки этого этапа проверить фактические Q3 backend/version/API capabilities
  и сделать минимальный capture adapter/import: конкретный captured mesh/resource
  → original/effective LOD → scene identity. Это часть этапа5, не ожидание этапа6.
  Если candidate скрыт combined mesh, выбрать подходящий диагностический кандидат,
  не объявлять effective structural mesh фактически исполненным draw.

**Выход:** отчёт с реальным changed mesh, перечнем всех affected groups и normal GPU
delta/шумом. Нулевая достоверная дельта допустима; экономия не гарантируется числом полигонов.

## 6. Детальная GPU атрибуция и проверка переносимости Windows

**Файлы:** узкие adapters в `Tools/Perf/gpu_diagnostics/capture_adapters/`,
join/coverage/report модули; scene/resource exporter только для необходимых данных.

- [ ] Проверить фактическую backend/version/API capability на PC и Q3; выбрать доступные
  frame/pass/stage/draw/counters, сохранить catalog/raw/command/units/status. Не обещать
  отсутствующие shader stats и не разрабатывать собственный GPU profiler.
- [ ] Реальный capture→event/pass/group→resource→scene object join. Static batching,
  instancing/multiview/shared surfaces дают group/unresolved, не ложные per-object ms.
- [ ] JSON: отдельно самые дорогие baseline группы и самые большие A/B изменения;
  стабильные IDs/paths/capture references, доказательства A/B и выбранного события,
  original/effective/observed mesh, lights/casters/atlas, unknown/unattributed/coverage.
- [ ] Четыре проверки: dense mesh/instance; texture/mip (память отдельно от времени,
  shared near texture protection); extra pass/transparent/RT copy; light/shadow.
  Для каждой наблюдение→диагноз→одна правка→normal Q3 A/B→визуальная проверка.
- [ ] Сопоставить одинаковые изменения на Windows/Q3: знак/размер delta и ranking
  ограничены workload/API/settings/class. Отчёт exploratory/validatedForConditions;
  универсального PC→Quest коэффициента и переноса процентов нет.

**Выход:** автор объясняет, где искать проблему и что изменить; измеренная область и
гипотеза различимы. Очень точный отчёт не означает искусственное разложение всего GPU
на аддитивные миллисекунды каждой текстуры/сетки/лампы.

## 7. Применить анализ к сцене и завершить поставку

- [ ] По отчёту выбрать и внести обоснованные изменения сцены по одному фактору;
  сохранять budgets/исключения/PhysicalArena и качество ближнего плана. Не менять сцену
  вслепую до готовности измерений; сохранять baseline и каждый вариант.
- [ ] Проверить8стандартных видов Lobby и визуальные соседние/ближние виды; worst view,
  p50/p95, variations/noise. Прежний10,83мс — исторический baseline, нужен новый.
  Цель≤7мс относится только к базе Lobby/MapOnly без кукол, Test/Development Q3,
  GPUlevel2/FFR0.75; не переносить на другие workloads или Q2.
- [ ] Если target не достигнут — сохранить результат и следующие измеримые гипотезы,
  не объявлять сцену прошедшей бюджет. Итерации продолжать в рамках авторской задачи.
- [ ] Проверить затронутые controls/Live/JSON roundtrip и поэлементный Restore LOD/
  renderers/DetailBand, особенно после тестового варианта и unload/reload. Startup
  ProfileRestored сам по себе этого не доказывает. Существующий accepted UI не переписывать.
- [ ] Получить целевое экспертное review готового diff/evidence: performance — методика/
  атрибуция; launch-infra — изменённые launch/build/runtime inputs; UI — только если
  менялась UI-композиция; network/gameplay — только если затронуты соответствующие invariants.
  Исправления выполняю здесь сам. Нет передачи реализации обратно экспертам.
- [ ] Пользователь проверяет полезность отчётов/поведение/визуал на Editor/Windows/Q3;
  затем обновить tests по принятым фактам, выполнить актуальные checks, сохранить
  точные SHA/evidence. FF-публикация в dev по штатному review/merge route, без force-push;
  обновлённая base и broker publication подтверждены, если требуются дальнейшие leases.

**Финал:** инструменты реально работают, автор воспроизводит анализ самостоятельно,
все проверки имеют область/источник, выполненная работа опубликована после приёмки.
Состояние бюджета сцены, pending hardware/unknown и Q2notMeasured указаны явно.

## Основные риски для ревью

1. Windows ready блокируется Quest-only полями: нужен явный platform applicability,
   а не фиктивные числа или глобальное ослабление требований.
2. OVR polling не даёт source frame/time: clock confidence должен пережить importer/JSON.
3. Правило LOD и renderer.enabled принимаются за реально исполненный draw: нужен capture.
4. A/B одновременно меняет texture/cull/profile/MapRun: actual diff/conditions reject.
5. Старые compile/tests/summary/capture timeout принимаются за текущий end-to-end PASS:
   точный source SHA, scope доказательств и пользовательская приёмка обязательны.

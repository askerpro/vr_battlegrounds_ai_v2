# Tools/Perf — замеры и диагностика GPU (владелец performance)

Python 3.10+, только stdlib. План/границы: `tasks/scene-detail/gpu-diagnostics-completion-plan.md`
(детальная диагностика) и `tasks/quest-perf-pipeline/gpu-launch-implementation-plan.md` (запуск на шлеме).
Форматы: контракты `tasks/scene-detail/contracts/` (scene-index-v1, runtime-adapter-v1, renderdoc-bridge-v1,
build-identity-v1, quality-discovery-v1) и `tasks/quest-perf-pipeline/contracts/gpu-launch-inputs.md`.
Отдельный документ формата пакета gpu-package-v1 в dev не влит: точная схема пакета — проверки
`gpu_diagnostics/model.py` и `gpu_diagnostics/import_runtime.py`.

## Offline импорт, проверка и отчёт

`gpu_analyze.py` работает с сохранёнными пакетами; Unity/ADB/шлем не нужны.

```powershell
python -B -X utf8 Tools/Perf/gpu_analyze.py import-summary <старый-summary.json> --out <reports/imported>
python -B -X utf8 Tools/Perf/gpu_analyze.py import-runtime <process/perf-runtime> --out <новый-reports/imported-runtime>
python -B -X utf8 Tools/Perf/gpu_analyze.py validate <reports/run-A>
python -B -X utf8 Tools/Perf/gpu_analyze.py compare <reports/run-A> <reports/run-B> --out <reports/comparison>
python -B -X utf8 Tools/Perf/gpu_analyze.py compare-series <series-plan.json> --out <новый-reports/series>
```

В `comparison/` записывается `comparison.json`; это основной интерфейс анализа для агента.
Прежний HTML доступен только по явному `--html` и для работы не требуется.
Статус и причины несопоставимости показаны раньше чисел. App GPU, CPU, compositor и interval
hardware counters сохраняют отдельные provenance/единицы. Raw timeline использует реальные samples.
Для групп доступны собственные якоря, contributors → объекты → ресурсы и raw capture.
Baseline и дельты групп ранжируются отдельно по одному unit/scope/source/metric; суммы стоимости
текстур/объектов не вычисляются. Одинаковый capture-local ID не связывает разные прогоны:
для этого нужен явный стабильный `comparisonKey`.

Импорт старого summary создаёт отдельный каталог на фазу, сохраняет исходный файл/SHA и
процентили, но не генерирует сырые samples и clock. Такие результаты **limited**; known mismatch
фазы/вида/устройства даёт **incomparable**. Повторное использование одного runId не является A/A.
Источник копируется byte-for-byte в `raw/summary.json`, его локальный SHA256 проверяется;
непроверенный источник даёт limited, mismatch — invalid. Clock uncertainty сужает, а не расширяет
принимаемый интервал фазы. `eyeResolution` явно содержит mono/stereo и реальные размеры;
старые размеры без режима остаются limited.
`invalid`/`incomparable` возвращают exit2; limited — exit0 с явными ограничениями. Exit0 не является
принятым улучшением или бюджетом. Одна пара не подтверждает шум/повторяемость.

Для допустимой правки conditions можно явно передать `--allow-condition qualityHash` (повторяемая
опция); отчёт сохраняет это разрешение. Устройство, API, build kind, роль, вид, фаза и режим
не обходятся этим флагом. Неизвестные обязательные условия остаются limited.

Runtime importer сверяет source/runtime/event identity, исходные CSV hashes, количество
и порядок строк, переходы фаз. Для каждой measured generation сохраняет исходные raw,
run.json/quality.json/events.json и observations.csv; samples.csv остаётся пустым, пока
GPU source intervals неизвестны. Принятые локальные ready/invalidation ограничивают
время чтения API, не доказывая GPU source window. API polls могут повторно читать один
исходный GPU кадр: count/p50/p95 находятся в observationDistribution, а не raw samples.
Сравнение таких распределений — observationMetrics.deltaP50, status остаётся limited.
Без закрытого локального ready window или при несопоставимых условиях дельта не выдаётся.
Неполный/пустой источник, неизвестные условия и причины ready refusal не скрываются.
Runtime recorder заранее резервирует буфер кадров, поэтому `mem_mb` диагностического прогона
несопоставим с обычным стресс-тестом: `Docs/perf-stress-test.md`, раздел «Метрики».

`compare-series` читает schemaVersion1, pairs/aa — массивы `{a,b,order}` (пути относительно
плана, order AB/BA сверяется с actual startedUtc), allowedSettings — JSON pointers
(по умолчанию только /sceneDetail/farStart). Для LOD-only требуется одна подтверждённая
сборка и exact actual settings diff; effective LOD/renderer state считается следствием
правила и сохраняется в отдельных quality snapshots. Межпрогонные дельты группируются
по устройству/API/виду/фазе/метрике/источнику. Критерий повторения: ≥3независимых A/B,
оба порядка, A/A, одинаковые primary settings каждой стороны, без повторного parentRun.
Диапазон A/A — диагностическая эвристика, не статистическая значимость. Для observations
требуется закрытое локальное ready window не короче minimumWindowSeconds RunSpec.

## Машиночитаемая структура сцены

```powershell
python -B -X utf8 Tools/Perf/gpu_analyze.py inspect-scene <scene.json> --out <новый-reports/scene-index>
python -B -X utf8 Tools/Perf/gpu_analyze.py scene-query <reports/scene-index> --kind lod --search GapFill --limit 5
python -B -X utf8 Tools/Perf/gpu_analyze.py scene-query <reports/scene-index> --id <точный-ID> --full --limit 1
python -B -X utf8 Tools/Perf/gpu_analyze.py scene-links <reports/scene-index> <точный-ID> --limit 25
```

Пакет: `manifest.json` с SHA256/размерами; `summary.json` с инвентарём и структурными
кандидатами; `entities.jsonl` с полными object/renderer/resource/LOD/light записями;
`relations.jsonl` с направленными ссылками и исходными JSON pointers; точный исходник
`raw/scene.json`. Выходной каталог должен быть новым. Ничего не исключается из-за
лимита выдачи: `total/nextOffset` позволяют дочитать все записи. Поиск работает по
имени/пути/ID/type, `--full` возвращает исходные поля. `scene-links` показывает входящие
и исходящие связи; следующий ID читается через `scene-query`. Перед выдачей проверяется
SHA256 читаемого JSONL. Ограничения `limit`:1–100; `offset` неотрицательный.

Кандидаты ранжируются отдельно по вершинам shared mesh, площади mip0 поверхности,
декларированным material passes и числу материалов на shader. Это **структурные
признаки**, не GPU cost: `gpuTimeMs=null`, `evidence=structuralCandidate`, у каждого
списка есть scope и следующий диагностический шаг. Все кандидаты остаются в entities;
в summary показаны первые25 каждого списка и их полное количество. Размер FBX не
считается VRAM, количество passes — actual draws, ссылки — временем шейдера.
Сохранены authored/original/effective/observed и неизвестные значения. Структура не
подтверждает видимость камерой, выбранный LOD, shadow casters или стоимость на GPU.

## Windows RenderDoc replay для анализа агентом

Официальный SDK1.46 bridge собирается из `gpu_diagnostics/capture_adapters/`.
`Run-E2E.ps1 -RenderDocBridge <exe>` снимает один кадр ранним SDK injection только
для instrumented/draw RunSpec. Жизненный цикл процессов остаётся у E2E runner.
После cleanup: `renderdoc_bridge export <capture.rdc> <новый-каталог>`.
Сохраняются actions/resources/bindings JSONL, capture.json и replay.log выбранного GPU.

```powershell
python -B -X utf8 Tools/Perf/gpu_analyze.py replay-query <export> --limit 5
python -B -X utf8 Tools/Perf/gpu_analyze.py replay-query <export> --event-id 310 --full
python -B -X utf8 Tools/Perf/gpu_analyze.py replay-query <export> --table resources --event-id 310 --limit 10
python -B -X utf8 Tools/Perf/gpu_analyze.py replay-query <export> --resource-id resource-320 --limit 10
```

Первая выборка ранжирует команды по индивидуальному replay duration и возвращает
цепочку проходов, связанные buffers/shaders/textures и их роли. Events для выбранного
ресурса читаются отдельной пагинированной выборкой. `offset/limit/nextOffset`,1–100
записей; длинный массив API events включается только с `--full`. Счётчики, уникальность
ID, дерево, durations и references проверяются; в ответе фактические SHA256 JSONL,
capture и сохранённого GPU/driver log. Эти hash фиксируют прочитанный snapshot.

Resource name — кандидат для поиска Unity asset, а не доказательство identity.
Нет суммы parent/child или стоимости отдельной texture/light по связанному draw.
GPU timings относятся к replay, instrumented кадр отдельно от normal A/B и Quest.
Размеры, format/mips/MSAA texture и длины buffers берутся из replay descriptors.
`shaderProperty` содержит имя reflection resource из RenderDoc: например texture0,
это не подтверждённое имя свойства Unity Material. Binding и staticallyUnused=false
не доказывают фактическое чтение ресурса GPU и не назначают ему стоимость draw.

`join-replay-scene <export> --scene-index <index> --runtime <client/perf-runtime> --out <new>`
проверяет capture hash/receipt, run/process ID, serialized identity и точный scene SHA.
Asset candidates содержат authoring ID, source path, подтверждающие признаки и связанные
места сцены. Для prefab build-copy IDs есть отдельное наблюдаемое преобразование с
проверкой уникальности/типа; это candidate, не публичный контракт Unity. Коллизии остаются
unresolved. Далее `replay-query <export> --joins <package> ...`; по умолчанию первые3
кандидата/5мест, полные данные — `--table resources --resource-id <id> --full`.

`compare-replay <a-export> <b-export> --joins-a <a-joins> --joins-b <b-joins> --out <new>`
сверяет source/scene/table, фактические условия и primary quality settings. Разрешённая
LOD-разница — только farStart. Comparison JSON и groups.jsonl показывают draw counts,
submitted indexed elements и описательные распределения individual draw replay timings.
GPU времена не складываются; нет вывода об общей выгоде, значимости или Quest budget.
Первый фактический19Oct pair:7geometry groups,−38358submitted indices, statuslimited.
У draw без ActionFlags::Instanced множитель1; у instanced draw берётся фактическое
число instances, включая0. Сравнение проверяет SHA именно использованных JSONL/joins
и записывает их в comparison.json; повторный export одного .rdc не новый замер.

## Подготовка Quest RunSpec

`gpu_analyze.py prepare-quest-run --help` описывает подготовку bundle из raw e2e
и quest build.json одной immutable ревизии. Исходные manifests и dirty evidence
сохраняются отдельно с SHA; combined build.json — входной профиль, не принятая сборка.
`sourceKind=unknown` не превращается в accepted. Выход содержит аргументы прежнего
Run-QuestE2E, который владеет запуском, портами, APK и cleanup. Discovery получает
фактический quality fingerprint; normal/instrumented прогоны требуют этот fingerprint.
Активный Quest Link требует явного разрешения пользователя на закрытие PC VR-сессии.
Detailed mode включается до нового запуска приложения и восстанавливается своим владельцем.

## Quest raw collector

`quest_gpu_breakdown.py` снимает системный `/system_ext/bin/ovrgpuprofiler` в нужной фазе.
Само приложение запускается отдельно существующим `Tools/e2e/Run-QuestE2E.ps1`.
Collector не запускает/не останавливает приложение, не меняет FFR, CPU/GPU levels или сцену.

```powershell
python -B -X utf8 Tools/Perf/quest_gpu_breakdown.py --help
python -B -X utf8 Tools/Perf/quest_gpu_breakdown.py --pass realtime --serial <serial> --phases <phases.json> --settle 2 --seconds 5 --out <новый-reports/gpu/A>
```

`phases.json`: JSON массив точных имён, например `["база: center-north", "куклы: center-north"]`.
Без `--phases` сохраняются четыре legacy имени; путь предпочтительнее inline JSON из-за PowerShell quoting.
`--metrics` принимает **точные runtime имена**, а не номера другого устройства. Каталог и его hash
сохраняются; неизвестные/неоднозначные имена отклоняются. Default settle2/sample5 всё равно требует
подходящей фактической длительности фазы: collector не удерживает/не продлевает workload.

- `--pass realtime`: interval counters (~1Hz); app GPU p50/p95 отсюда не выводятся.
- `--pass detailed --capture-kind stage`: один stage capture с доступными metric names.
  Режим уже должен быть включён, либо явно задаётся `--enable-detailed` до запуска приложения.
  При opt-in проверяется отсутствие запущенного приложения и исходно выключенный mode;
  восстанавливается только режим, включённый этим collector. Чужой включённый mode не выключается.
  Realtime при detailed ON отклоняется. Неизвестный исход enable требует ручного разбора evidence.
- Draw adapter пока **unsupported**: для runtime CLI известен синтаксис `-x=<IDs>`,
  однако реальный ограниченный capture и его схема ещё не проверены на устройстве.
  Каталог/документация не заменяют эту проверку. Clocks не переводятся в ms без частоты.
  Per-draw на шлеме снимает отдельный адаптер — см. [Per-draw захват Meta RenderDoc на Quest](#per-draw-захват-meta-renderdoc-на-quest).

Logcat читается независимо от capture. Смена фазы/итог/EOF во время settle/capture, пустой ответ,
ошибка counter или timeout дают partial, а не успешный замер. Общий `--max-wait` действует также
при молчании logcat. В `capture.json` — requested/complete/missing, argv, runtime catalogs/hash,
границы host receive time и причины. Собственные subprocess завершаются; старый output не затирается.
Legacy logcat по-прежнему **не доказывает** runtime workload.ready, точный native clock или
стабильность вида всю фазу; receipt имеет эту границу достоверности и не является normal run package.

## Per-draw захват Meta RenderDoc на Quest

`gpu_diagnostics/capture_adapters/meta_renderdoc_quest.py` снимает такты и столлы каждого draw одного
кадра на шлеме через Meta RenderDoc Fork (`renderdoccmd`, проверен 68.18). Режим **opt-in**: включается
только явными флагами `Tools/e2e/Run-QuestE2E.ps1`, без них приложение запускается обычным `am start`.

```powershell
Tools/e2e/Run-QuestE2E.ps1 ... -MetaRenderDocCmd <renderdoccmd.exe> -MetaRenderDocOut <новая-пустая-папка> -MetaCapturePhase "база: center-north"
```

`-MetaRenderDocCmd` и `-MetaRenderDocOut` задаются вместе; нужен python3 в PATH и adb из `<sdk>/platform-tools`
(тот же, что у runner: renderdoccmd ищет adb через SDK root). Адаптер меняет состояние шлема, поэтому
**каждый такой прогон требует разрешения пользователя на изменения шлема**. Перф-метрики прогона с
инъекцией недействительны.

Что меняется на шлеме на время прогона:
- settings global `enable_gpu_debug_layers`, `gpu_debug_app`, `gpu_debug_layer_app`, `gpu_debug_layers`,
  `gpu_debug_layers_gles` — слой захвата RenderDoc для пакета приложения;
- свойства `debug.vulkan.layers`, `debug.rdoc.*`, `security.perf_harden`, `debug.oculus.guardian_pause`,
  `debug.vr.profiler`, `debug.oculus.usepackagedvvl.com.oculus.renderdoccmd.*`;
- установленные серверы `com.oculus.renderdoccmd.arm64/arm32`, `adb forward tcp:N → localabstract:renderdoc_N`,
  режим detailed `ovrgpuprofiler`;
- переходные эффекты: пробуждение шлема, `prox_close`, пропуск рендера компоновщика.
- `persist.debug.dalvik.vm.jdwp.enabled` не меняется никогда (ветка root+reboot запрещена); частоты kgsl
  только записываются в журнал (их меняет и троттлинг).

Порядок (фазы адаптера вызывает Run-QuestE2E):
1. `launch` — снимок: полный `getprop` и `settings list global` плюс отслеживаемые ключи, атомарно в
   `<out>/journal.json` **до любых изменений**. Предусловия чистого шлема: gpu_debug_* пусты, серверы не
   установлены, приложение не запущено, detailed выключен, adb forward нет, JDWP выключен. Нарушение —
   отказ без изменений. Затем `renderdoccmd adb-launch` с теми же Unity-аргументами (`--intent-args`).
2. `capture` (в фоне) — ждёт в logcat начало фазы `-MetaCapturePhase`, settle, `adb-capture` одного кадра в
   новую `<out>/captures/`; ровно один `.rdc`, его размер и SHA256 в журнале.
3. `finish` — из `finally` Run-QuestE2E, в том числе при Ctrl+C/исключении: force-stop приложения,
   `adb-session --drawcall-profiling` (`info`, `fetch_drawcall_states`, `draws`, `passes`; ответы —
   `<out>/tool-logs/*adb-session.replies.jsonl`), затем откат.
4. Откат — остановить процессы прогона, отменить переходные эффекты, удалить установленные серверы,
   выключить detailed, удалить появившиеся gpu_debug_*, вернуть свойства с известными вендорскими значениями,
   снять forward `renderdoc_*`. Чужие изменения (ключ уже был задан, неизвестное значение) не трогаются и
   попадают в `restore.foreign`. Затем снимок снимается повторно и сравнивается с исходным:
   `restore.verified=true` только без расхождений (`remainingDiffs`), ошибок и foreign. После отката шлем усыпляется.

`finish` возвращает 0 только при draws > 0 и verified. Коды фаз — в manifest прогона `launch.device.metaRenderDoc`
(`launchExit`, `captureExit`, `finishExit`, `restoreOnlyExit`).

Сбой отката: Run-QuestE2E сам повторяет идемпотентный `restore-only` по снимку из журнала. Если и он не verified
(код 3), runner печатает готовую команду `python ... meta_renderdoc_quest.py restore-only ...` — выполнить её,
разобрать `restore.remainingDiffs/foreign/errors` в `journal.json`, до подтверждённого отката не использовать шлем
для замеров и сообщить пользователю. Повреждённый журнал делает автоматический откат невозможным: `finish`
(код 3) печатает список ключей для ручной проверки. Известный остаток: ранее отсутствовавшее `debug.*` свойство
после `setprop key ''` остаётся пустым до перезагрузки (getprop их не различает); `prox_close` и
`oom_score_adj` процессов renderdoc истекают сами.

Чтение: `Clocks` и столлы — replay захваченного кадра на шлеме (instrumented). Они показывают **пропорции**
между draw — где искать, но не цену. Бюджет и эффект правки — только realtime-тайминги normal прогона
(`ovr_gpu`/AppGPUTime, A/B): например, Procedural-скайбокс с 8 % тактов replay дал −0,05 мс в A/B
(`experts/performance/journal/2026-10-20-scene-detail-quest-gpu-lobby.md`). Рейтинг draw по ответам сессии —
`tasks/scene-detail/tools/rank_quest_drawcalls.py`.

## Граница готовности

Есть offline model/отчёт, runtime recorder/readiness, scene index, raw collector и
реальные Windows capture/query/asset candidates. Точные GPU-source clocks и per-object
атрибуция остаются unresolved. Первый настоящий Q3 LOD A/B с captured
mesh, A/A noise, повторными парами и full ViewsFile coverage обязателен для приёмки всей задачи.
Windows сейчас применяется для первичного структурного анализа; подробные выводы требуют проверки
против Q3. Quest2 — notMeasured, без подтверждённой экстраполяции.

Новые функциональные тесты добавляются после пользовательской оценки поведения по AGENTS.md.
Сейчас допустимы компиляция, неизменённые существующие проверки и диагностика для ревью. Условия каждого
реального замера: immutable source/build, устройство, API, FFR/levels, сцена, workload и фиксированный
вид. Сгенерированные данные — только в ignored `tasks/<task>/reports/`.

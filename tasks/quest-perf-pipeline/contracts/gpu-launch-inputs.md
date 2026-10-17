# GPU diagnostics: launch/runtime inputs r2

2026-10-15. Владелец: launch-infra, задача quest-perf-pipeline. Потребитель: performance,
perf-stress-scenario. Основание: запрос8600, предложение8511, gpu-diagnostics-spec.md,
gpu-package-v1.md и gpu-implementation-plan.md в задаче performance.
Статус: proposed r2; уточнение DTO по review8649 до первого ACK. r1 не согласована;
API и флаги ниже ещё не реализованы. Согласование r2 не принимает код.

## Назначение и совместимость

Существующие Run-E2E и Run-QuestE2E получают необязательный immutable RunSpec, доставляют
его тем же процессам и собирают source evidence для нормализатора performance.
Без RunSpec остаётся прежний E2E route. Новый launcher, сборочный брокер, camera/phase/quality
writer не создаются. Capture, samples, нормализация и GPU-вердикт принадлежат performance.

Нормализованные run.json/samples.csv/events.json принадлежат gpu-package-v1. Они не являются
прямым сериализованным результатом launch. Существующий Quest run.json сохраняет формат;
его source copy в пакете называется source/launch-run.json. Новые source-файлы:
source/run-spec.json, source/build.json, source/processes/<processInstanceId>/actual.json
и events.jsonl. Нормализатор явно ссылается на source hashes, сохраняет unknown/reasons,
собирает events.json и не заменяет summary псевдосэмплами.
Package source.sha256 хеширует импортируемый raw evidence; sourceSha Git, artifact SHA256
и views SHA256 остаются отдельными provenance. Они не заменяют друг друга.

## Уже доступные поля и gaps

| Поля | Текущий источник и ограничение |
|---|---|
| role, developmentBuild, buildGuid, appVersion, unityVersion, platform | Raw E2EResult: CaptureBuildConditions/JSON. Переиспользовать текущий результат, не второй exporter этих значений |
| Quest deviceModel/deviceId | run.json device.model/serial; DeviceToken сценария не является hardware ID |
| APK/view-file hashes, short revision | Quest run.json и build cross-check; short revision не доказывает полный source SHA |
| requested map/scenario/workload args | Context/launcher manifest; не являются observed scene/workload identity |
| actual view/phase/settings, graphicsApi, hardware levels, qualityHash | Generic E2E сейчас не экспортирует; phase/conditions adapter performance, unavailable=null+reason |
| events/time window | E2EResult Steps имеют UTC и bounded buffer; это не lossless events и не verified GPU window |

Полный аудит с source anchors:
reports/build-checkouts-validation/gpu-launch-field-audit-8600.md. В нём описан текущий source,
а новые API этого r2 пока только предложены. Для одних Windows Views/HeadPose/harvest достаточно
двух дирижёров, Context/Runner и общего input helper; full build provenance ниже — отдельная
явная часть r2, не условие исправления существующего E2EPlayerBuilder.

## Вход RunSpec v1

UTF-8 JSON: schemaVersion=1; обязательны runId, pairId, variant, repetition, mode
(live/instrumented), sourceSha (40hex), buildManifestSha256, scenario, requestedSceneId,
workloadId, phasePlanSha256. Поля viewId/phaseId относятся к конкретным runtime событиям,
а не фиксируют весь многофазный run одним ракурсом.

views: null либо {path,sha256}; path относителен каталогу RunSpec, перед staging проверяются
канонический путь, принадлежность bundle и SHA256. headPose: {mode:off|eye,eyeMeters:null|number}.
windows: {width,height,windowed}; размеры положительные, применяются только rendered клиентам.
quality: {profileId,requestedFingerprint}; это ожидание, не команда QualitySettings/URP setter.
capture: {kind:none|stage|draw,minimumWindowSeconds}; длительность workload задаёт performance
до запуска. kind не включает backend и не разрешает install/захват.

Launch считает SHA256 точных входных байтов, сохраняет их неизменными, доставляет файл и hash
каждой роли. Предлагаемые общие флаги: -RunSpec у обоих PS-дирижёров;
-e2eRunSpec <staged-path> и -e2eRunSpecSha256 <hash> у Player.
ViewsFile/HeadPose в Windows используют текущий Quest формат и E2EHeadPose.
При RunSpec единственный requested источник — spec: явно переданный конфликтующий CLI input
отвергается, значения defaults не перезаписывают spec. Без spec legacy defaults сохраняются.
Неизвестная версия, неверный hash, выход из bundle, nonfinite/неверные значения, конфликт роли,
scenario или build identity — launch input error до старта процессов; без silent fallback.

## Build provenance

BuildProvenance экспортируется обоими существующими Editor builders через один helper.
Build-Game/E2E build route дополняет source artifact hashes после terminal builder.
build.json содержит schemaVersion, полный sourceSha, sourceKind accepted/checkpoint,
dirty/source-evidence, Unity version, package/config fingerprints, build target/subtarget,
Development/options, scripting backend, graphics APIs, scene identities, profile identity,
builder/tool hashes, результат и UTC границы builder. Artifact inventory содержит относительные
пути/размер/SHA256, без самовключения build.json. Revision stamp не заменяет полный sourceSha.
Не наблюдённые build/settings поля имеют null и reason; принятый SHA не выводится из имени файла.
Уже существующий artifact без matching provenance не считается диагностическим build proof.

## Runtime API и события

Предлагаемый E2EContext.Diagnostics — null для legacy run; иначе E2EDiagnosticSession,
один writer source actual.json/events.jsonl в lifecycle E2ERunner. Session содержит frozen
RunSpec и предоставляет performance методы ReportPhase, ReportView, ReportConditions,
ReportWorkloadReady и InvalidateWorkload. Точные сигнатуры и DTO заморожены в
[приложении r2](gpu-launch-dto-v2.md). Таблица ниже — только обзор:

| Метод E2EDiagnosticSession | Read-only DTO / результат |
|---|---|
| ReportPhase(PhaseObservation value) | phase/observed transition; DiagnosticReportResult |
| ReportView(ViewObservation value) | actual pose/outcome и stamp; DiagnosticReportResult |
| ReportConditions(ConditionsObservation value) | observed values/null reasons и fingerprint; DiagnosticReportResult |
| ReportWorkloadReady(WorkloadReadyObservation value) | ссылки на принятые observations, окно/ready identity; DiagnosticReportResult |
| InvalidateWorkload(string readyId, string reason) | idempotent инвалидизация своего readyId; DiagnosticReportResult |

DTO и result value types размещаются в E2EDiagnosticSession.cs; session добавляет identity,
sequence и собственные timestamps. Report методы не исполняют workload. Значения generation
и phaseId берутся из performance phase adapter, session не придумывает собственные фазы.
Reject возвращается явно; если writer исправен, отказ также пишется как diagnostic.rejected.
Ошибка I/O завершает diagnostic канал с reason и возвращает Accepted=false; write success не
выдумывается. Изменение замороженного API требует следующей proposed ревизии.

Performance владеет переходами stress phases и вызывает API при фактически наблюдённом
событии. E2EViews/E2EHeadPose остаются владельцами переноса/симуляции камеры; ReportView
передаёт outcome их API, не применяет Transform. Смена карты идёт через gameplay/MapLoader;
сравнение actual сцены с requested не вызывает LoadMap из collector или второго bootstrap.
Новый applier quality этим договором не разрешается.

Общий event envelope: schemaVersion, runId, specSha256, buildManifestSha256,
processInstanceId, role, sequence, kind, frame, monotonicTicks, tickFrequency, utcAnchor,
actualSceneId, viewId, phaseId, phaseGeneration, payload. UTC — anchor, не доказательство
синхронности разных процессов. Для native clock mapping performance хранит метод,
uncertaintySeconds и verified; без измеренной погрешности verified=false.

Kinds: process.started, conditions.observed, phase.begin, phase.end, view.observed,
workload.ready, workload.invalidated, process.ended, diagnostic.rejected. Server phase transition и client receipt
различаются в payload.source/observedRole. Sequence и restart identity позволяют обнаруживать
пропуски. JSONL дописывается/flush при событии; частично записанная последняя строка после
crash сохраняется как incomplete evidence. E2EResult хранит artifact links и обычный вердикт.
На PC роли используют один persistentDataPath: source runtime path обязательно включает
runId/role/processInstanceId. Один общий actual.json/events.jsonl для всех процессов запрещён.
Runtime AddArtifact проверяет принадлежность persistentDataPath; harvesting сохраняет receipt
каждого заявленного файла, relative path/размер/SHA256 и явную completeness/error. Ошибка copy/pull
не заменяется пустым успешным package. Raw evidence и ошибки сохраняются и при неуспешном E2E.

workload.ready разрешён после observed actual map/view/settings, завершённых settle/warmup
и идентифицированной измеряемой фазы. Payload: readyId, phaseGeneration, actualFingerprint,
remainingWindowSeconds, timeBounds/uncertainty, reportedConditions. Performance задаёт критерий
workload и сообщает готовность; session проверяет identity/envelope и пишет событие.
Готовность не выводится из единственного view.ok или молчания логов. Map/phase/view/settings
drift, потеря focus, restart и завершение инвалидируют generation. Session сообщает process/end
при собственном cleanup; performance отвечает за своевременную инвалидизацию stress workload.

capture.ready пишет только performance adapter после backend armed и свежего workload.ready;
сохраняются readyId, generation, actual capture start/end и native clock mapping. Захват,
пересекающий конец/инвалидизацию окна, получает rejected/partial. Collector не удерживает
фазы, не перезапускает приложение и не меняет FFR/quality/камеру/CPU/GPU levels.
Недоступное обязательное observed условие блокирует readiness с причиной; это не отменяет
обычный E2E result и не становится аппаратным performance PASS. Нормализатор применяет
правила limited/invalid/incomparable из gpu-package-v1.

## Exact scope и владельцы

Предоставляющий этап quest-perf-pipeline/gpu-launch-inputs, только launch-infra:

- Tools/e2e/Run-E2E.ps1; Tools/e2e/Run-QuestE2E.ps1;
  новый Tools/e2e/E2EDiagnosticInputs.psm1 — общий validation/staging/harvesting, без top-level launch.
- Tools/release/Build-Game.ps1; Assets/Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs;
  Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs;
  новый Assets/Editor/VR_Battlegrounds/Release/BuildProvenance.cs и его Unity-generated .meta.
- Assets/Scripts/Debug/E2E/E2EContext.cs; E2ERunner.cs;
  новый Assets/Scripts/Debug/E2E/E2EDiagnosticSession.cs и его Unity-generated .meta.
- tasks/quest-perf-pipeline/** и experts/launch-infra/** — только материал собственной задачи.

Performance пишет PerfStressScenario/StressTest phase/sample adapters и Tools/Perf. Launch не
меняет эти файлы, performance не меняет Runner/Context/launchers. Один event writer получает
данные от adapter; schemas/importer/raw samples/capture остаются performance.
SceneDetail snapshot API и quality applier требуют отдельного договора с их владельцами.
При необходимости изменения E2EViews/E2EHeadPose — отдельное расширение scope и avatar review;
в r2 достаточно их существующих outcomes, эти source-файлы не включены в writes.
E2EResult.cs также не включён: текущих CaptureBuildConditions/AddArtifact достаточно.

## Порядок и приёмка

1. Performance ACK точной r2 и plan stage, подтверждает нормализацию source evidence,
   owner callbacks/conditions и согласует сигнатуры DTO. Владелец завершает contract-agree.
2. Launch реализует additive input/export/harvest; performance независимо продолжает offline
   модель и collector. Реальный совместный прогон зависит от принятого launch/provider input
   либо отдельного согласованного immutable technical input, не от сообщения «готово».
3. Проверяются parser/compile, существующие применимые проверки, path/hash rejection,
   Windows rendered и Quest telemetry, рестарт/phase invalidation, artifact cleanup.
   Новые/переписанные behavior tests — после пользовательской проверки по AGENTS.md.
4. Пользователь принимает изменённое поведение, затем scoped commit/FF и contract activation.
   Q3 live/capture/A-B и GPU budget принимает performance по своей spec; Q2 notMeasured.

Сейчас implementation/runtime/Quest проверки r2 не выполнялись. Доступность полей описана
отдельным audit report; ни cold/warm build matrix, ни прежний DevTools PASS не доказывают r2.

# GPU diagnostics: launch/runtime inputs r3 (дельта к r2)

2026-10-22. Владелец: launch-infra. Задача-источник: scene-detail-followups (follow-up P2 ревью
launch-infra задачи scene-detail, раунды 1–3). Потребители: performance, perf-stress-scenario.
Статус: proposed r3. Документ описывает **уже существующее** поведение кода в origin/dev;
код этим документом не меняется и не принимается.

Почему здесь, а не в `tasks/quest-perf-pipeline/contracts/`: хаб запрещает этой задаче запись
в чужую папку задачи. Владелец quest-perf-pipeline переносит r3 туда (или ссылается на этот файл)
штатной ревизией контракта; до этого нормативны r2 + эта дельта.

## База

| Документ | SHA256 |
|---|---|
| tasks/quest-perf-pipeline/contracts/gpu-launch-inputs.md (r2) | c41b67d66586e19bde043fe2d55f1e85c36c2f7e36b722526ac47d1b45d24445 |
| tasks/quest-perf-pipeline/contracts/gpu-launch-dto-v2.md (приложение r2) | a64c9625ee3cf5416f765c493b736334ff9a133f2393fe8872b695f28d490e64 |
| tasks/scene-detail/contracts/quality-discovery-v1.md | 81fb20cf2fc6bb183ee961791fa15cd37c211b050e1768b50e3e36b1ea346d25 |
| tasks/scene-detail/contracts/runtime-adapter-v1.md | 2bc22de2d565d5112ec2d858fe59b31cd87c38514aa7da6037e8c82601a5e8a6 |

Номера строк ниже — по коммиту c7b8d6ab3 (origin/dev на 2026-10-22):
E2EDiagnosticSession.cs blob 89d09c4a, E2EDiagnosticInputs.psm1 blob 4e7afe1c,
PerfDiagnosticConditions.cs blob 3237ccf9.

Всё, что не перечислено ниже, остаётся как в r2 и dto-v2 без изменений: DTO, конструкторы,
DiagnosticReportResult, коды отказов, event envelope, kinds, владельцы и scope.

## Δ1. quality.discoveryOnly (RunSpec v1, дополнение к r2 «Вход RunSpec v1»)

r2 задаёт `quality: {profileId, requestedFingerprint}` с обязательным SHA256. r3 добавляет
необязательное поле `discoveryOnly` (закрепляет quality-discovery-v1 на уровне launch-контракта):

- Форма: `quality: {profileId, requestedFingerprint, discoveryOnly?}`. Других ключей нет.
  `discoveryOnly` — только JSON bool. Отсутствие поля равно `false`: прежняя валидация r2
  (requestedFingerprint — 64 hex) и readiness неизменны.
- `discoveryOnly=true` требует `requestedFingerprint=null`, непустой `profileId`,
  `mode=live` и `capture.kind=none`. Иное — launch input error до старта процессов.
- Кто выставляет: только producer performance (`Tools/Perf/gpu_diagnostics/run_spec.py:55-56`,
  `gpu_analyze.py prepare-windows-run --discovery`). Launch поле не выставляет и не выводит,
  а только валидирует и доставляет неизменные байты RunSpec.
- Валидация в обоих местах, согласованно:
  дирижёр — `Tools/e2e/E2EDiagnosticInputs.psm1:160-174`;
  Player — `E2EDiagnosticSession.cs:477-493` (ключи, тип bool, null-fingerprint, live/none);
  чтение флага — `E2EDiagnosticSession.cs:401` (`DiscoveryOnly`, отсутствие = false).
- Что допустимо при discoveryOnly=true: process.started/ended, phase.begin/end, view.observed,
  conditions.observed (в т.ч. неполные условия и впервые наблюдённый actual fingerprint),
  diagnostic.rejected, harvest raw evidence. **Недопустимо:** workload.ready —
  `ReportWorkloadReady` отвергает до любых других проверок кодом
  `missing-evidence: discovery-only session cannot publish workload.ready`
  (`E2EDiagnosticSession.cs:788`). Следовательно, нет и capture.ready.
- Discovery-прогон не является GPU/A/B замером и не даёт GPU PASS; importer помечает его
  limitation «Discovery-only» (`Tools/Perf/gpu_diagnostics/import_runtime.py:145-146`).
  Actual fingerprint из него (quality-canonical.json) — вход следующего, обычного RunSpec
  с `requestedFingerprint`=этот SHA256.

## Δ2. Значение not-applicable для ffr/cpuLevel/gpuLevel (дополнение к dto-v2 ConditionsObservation)

dto-v2 (строки 141-153) перечисляет required keys и говорит: неизвестный элемент — null+reason,
неполные условия принимаются как evidence, но не разрешают ready. r3 вводит одно точное
исключение — политику `vrb-gpu-platform-v1` (runtime-adapter-v1, раздел о платформе):

- Ключи: только `ffr`, `cpuLevel`, `gpuLevel`. Для них `Value=null` с **точной** Reason
  `not-applicable: rendered Windows client with XR disabled` означает «величина не существует
  на этой платформе», а не «неизвестно».
- Producer: `PerfDiagnosticConditions.cs:153` пишет `applicabilityPolicy=vrb-gpu-platform-v1`
  (плюс observed `runtimePlatform`, `xrEnabled`), `:181-184` — not-applicable для трёх ключей
  при WindowsPlayer, не batchmode, XR выключен.
- Проверка session (`E2EDiagnosticSession.cs:936-946`, вызов из `:807-811`) не доверяет словарю
  caller и требует все условия одновременно: `Application.platform==WindowsPlayer`,
  `!Application.isBatchMode`, `XRSettings.enabled==false`, роль `client-*`,
  `SystemInfo.graphicsDeviceType != Null`, в conditions `applicabilityPolicy` равно
  `vrb-gpu-platform-v1`, у ключа `Value==null` и Reason совпадает побайтно.
  Отсутствующий ключ (default condition) исключением не является.
- Для Quest, сервера (batchmode/headless), Editor и любого rendered клиента с XR исключения нет:
  null у ffr/cpuLevel/gpuLevel там по-прежнему unknown → `unknown-conditions: <key>` при ready.
- Сам required-список в коде (`E2EDiagnosticSession.cs:674-675`) — 15 ключей dto-v2 плюс
  `qualityFingerprintMethod` (dto-v2:151-152 уже требует versioned method для ActualFingerprint);
  r3 фиксирует его как обязательный для ready.

## Совместимость с правилом ready (dto-v2:144-155)

Правило dto-v2 «неполные условия не разрешают ready» сохраняется без ослабления:

1. Not-applicable — не неполнота: для rendered Windows клиента без XR FFR и уровни CPU/GPU Quest
   физически отсутствуют, и их null с политикой — полное наблюдение этой платформы. Любой другой
   null required key (включая ffr/cpuLevel/gpuLevel вне политики) по-прежнему даёт
   `unknown-conditions` (`E2EDiagnosticSession.cs:808-811`).
2. discoveryOnly ready не ослабляет: ready запрещён целиком, раньше проверки условий (`:788`).
   Приём неполных conditions в discovery — уже разрешённый dto-v2:153 случай «evidence без ready».
3. Остальные проверки ready (`:789-817`: measured phase, свежие view/conditions той же generation,
   scene/view/role/workload, совпадение actual fingerprint с requested, focus) не меняются.
   Requested fingerprint по-прежнему не выдаётся за actual.
4. Нормализатор (gpu-package-v1) сохраняет reason not-applicable как есть; это не 0 и не
   известное значение уровня. Результат Windows rendered несравним с Quest по FFR/уровням.

## Совместимость с scene-detail контрактами

- quality-discovery-v1: Δ1 — его перенос в launch-контракт без изменения смысла (те же поля,
  ограничения live/none, запрет ready, следующий отдельный прогон с фактическим SHA256).
- runtime-adapter-v1: Δ2 — его политика `vrb-gpu-platform-v1` дословно (тот же Reason,
  проверка платформы/роли/XR провайдером, без исключения для Quest и сервера). Fingerprint method
  `unity-urp-scene-detail-settings-v1` и прочие пункты adapter не затрагиваются.
- Обе дельты additive: RunSpec без `discoveryOnly` и conditions без `applicabilityPolicy`
  обрабатываются ровно как в r2. Новые DTO, поля конструкторов и коды отказов не вводятся.

## Проверки и приёмка

Документ сверен с кодом чтением указанных строк; runtime/Quest прогоны для r3 не выполнялись.
Поведение уже в origin/dev через scene-detail (ревью launch-infra/gameplay/performance approve,
раунды 1–3). ACK r3 потребителем — штатный contract-ack владельцу quest-perf-pipeline после
переноса; ACK не принимает код и не является GPU-приёмкой.

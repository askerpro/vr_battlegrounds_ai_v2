# Реализация согласованного GPU launch provider r2

2026-10-15. Задача quest-perf-pipeline/gpu-launch-inputs; ведущий launch-infra.
Основание: gpu-launch-inputs r2 agreed (ACK performance plan12, provider plan32), canonical8735.
User «начинай реализацию» подтверждён в текущем performance чате; повторное разрешение на
план/метод исполнения не запрашивается. Применяются writing-plans/subagent-driven-development.
Разрешённая реализация продолжается до конкретного результата и его проверок; accepted commit
и активирование контракта требуют пользовательской проверки поведения отдельно.

Источники истины: contracts/gpu-launch-inputs.{md,json}, contracts/gpu-launch-dto-v2.md.
Хеши нормативных документов не меняются. Worktree только launch-infra, HEAD/base ed8b79f17;
fresh fetch перед началом подтвердил ту же origin/dev. Никаких чужих физических WIP inputs.

## Решения интеграции

- Ruling: canonical scene identity — наблюдённый loaded scene.path `Assets/...unity`,
  с фактическим map-ready от performance; filename/short GUID не подменяют identity.
- Ruling: source bundle рядом с RunSpec содержит `build.json` и исходный views JSON.
  RunSpec bytes не переписываются. Оба PS-дирижёра stage spec/build/views в уникальный
  persistentDataPath/runId/role/process area, сохраняют исходные SHA256.
- Ruling: build manifest schemaVersion1 имеет sourceSha/sourceKind/dirty evidence,
  profiles[]; profile = server/quest/tablet/e2e. Каждый profile содержит фактически
  наблюдённые build options/backend/APIs/scenes/Unity/config/tool hashes и files[]
  {path,bytes,sha256}, относительно outputRoot. Сам manifest в files[] не включается.
  Unknown поля остаются null+reason. Каждый Editor builder пишет свой build.json;
  Build-Game после terminal собирает pair manifest из profile records и сообщает путь.
  Windows e2e один profile используется runtime server/client ролями одного artifact.
- Ruling: profile wire содержит profile/outputRoot/files, actual{}, terminal{}, success.
  Actual keys: developmentBuild, scenePaths, buildGuid, appVersion, unityVersion,
  companyName, productName, buildTarget, subtarget, scriptingBackend, graphicsApis,
  buildOptions, unavailableReasons. Terminal: startedUtc/endedUtc/result. Pair manifest
  сохраняется в caller Build/manifests/build-<UTC>-<guid>/build.json. Company/product
  нужны Windows staging: requested/current-project settings не заменяют build metadata.
- Ruling: manifest/settings/Git/full source identity не выводятся из short stamp или
  requested CLI. Source kind accepted/checkpoint должен быть явно подтверждён input;
  неподтверждённый source остаётся unknown, без притворной accepted атрибуции.
- Ruling: для runtime сериализации используется уже установленный Newtonsoft.Json3.2.2,
  package/config не меняются. Session/DTO immutable, только один JSONL writer.
- Ruling: новые .meta создаёт Unity в собственной аренде после checkpoint, вручную GUID
  не генерируются. До пользовательской проверки новые behavior tests/probes не пишутся;
  применяются существующие проверки, parser/compile и inspection реального runtime evidence.
- Ruling: global mode off (свежий config подтверждён) разрешает local product tools без
  registration/begin ради допуска. Optional begin отказал из-за другого planned этапа
  map-runtime-bootstrap/series-smoke-e2e; fresh status подтверждает оба planned, активного
  конкурирующего writer нет. Порядок запрошен canonical; чужой план/checkout не меняются.
  Код реализуется в собственной точной области, Unity lease этим не выдаётся.

## Задачи и непересекающиеся writers

1. **Runtime — отдельный implementer.** E2EContext.cs, E2ERunner.cs,
   новый E2EDiagnosticSession.cs. Реализовать точный DTO/API r2, strict RunSpec/hash parsing,
   early refusal до scenario/network startup, artifact links, actual/JSONL lifecycle,
   sequence/clock/identity/replay/readiness guards, cleanup/invalidation. Legacy без spec
   сохраняется. Не менять E2EResult/Views/HeadPose и performance scenario/StressTest.
   Интерфейс другим частям — исключительно замороженный DTO appendix и spec CLI flags.
2. **Transport/harvest — отдельный implementer.** Run-E2E.ps1, Run-QuestE2E.ps1,
   новый E2EDiagnosticInputs.psm1. Общий strict validation/staging, Windows ViewsFile/
   HeadPose/window flags, explicit-input conflict refusal, per-role persistent paths,
   raw source manifests и safe receipt/hash/completeness harvesting. Существующий Quest
   run.json совместим; normalized package run.json не производить. Не запускать adb/Unity/build.
3. **Build provenance — отдельный implementer.** GameBuilder.cs, E2EPlayerBuilder.cs,
   новый BuildProvenance.cs, Build-Game.ps1. Один Editor helper для actual snapshot и
   inventory обоих builders, failure/terminal metadata, pair aggregation без изменения
   UXR reader/admission/SDK/source settings. Согласовать concrete manifest shape с2 через
   principal до зависимого glue. Нельзя добавлять второй writer scene/quality/source SHA.
4. **Principal integration/review.** Проверить contract compliance и relevant diffs,
   согласовать module/runtime/builder file names и schema. При finding вернуть scoped fix
   владельцу файла; не редактировать его параллельно. Source scope из plan32 не расширять.

## Проверки и выпуск

- После каждой части: фактический existing parser/compile check, diff/EOL только owned paths;
  отдельно фиксировать недоступную Unity-компиляцию. Spec/quality review независимым reviewer.
- После интеграции: existing build-launcher-check.ps1 (18cases), PowerShell AST всех changed
  scripts/module, JSON/schema/hashes inspection. Новый harness не добавлять до user validation.
- Перед Unity: fresh inbox/worker state/fetch/rebase; immutable technical checkpoint,
  подготовленный точный пакет compile + existing DevTools checks, FIFO request/claim/begin/guard.
  Не копировать performance WIP; joint input только с explicit grant/proof отдельно.
- После terminal Unity/cleanup сразу finish/receive; failure/unknown не replay. Meta/results
  получить штатным result checkpoint. Полные логи/reports вне Git, compact counts пользователю.
- Rendered Windows/Q3 реальное workload поведение вместе с performance adapter — отдельный
  следующий пакет по доступному accepted input; отсутствующий hardware/result не назвать PASS.
- Пользовательская приёмка → tests expectation updates при необходимости → scoped commit/FF,
  remote SHA verification, request-base и contract activation. Без приёмки только technical checkpoint.

Review focus: bundle traversal/hash/CLI conflict; stale/cross-field/replay semantics;
process lifecycle/incomplete JSONL; per-role path collisions и copy failures; ложная build
provenance/unknown clock/GPU conditions. Все эти риски сверяются с r2, а не новыми обещаниями.

# Runtime diagnostic adapter v1

18.10.2026. Добавление к принятому gpu-launch-dto-v2; прежние DTO/конструкторы сохранены.

PerfStressScenario создаёт adapter только при валидном RunSpec/Diagnostics. Подписка
ObserveRuns устанавливается до TryStart. finally освобождает подписку и сохраняет raw
observations в source/processes/<processId>/perf-runtime внутри уже зарегистрированного
stage artifact; существующий harvest собирает его без нового launcher.

Phase begin/end — фактические client-observation callbacks, монотонная generation.
Готовность требует settled E2EViews target, свежую actual camera pose в measured phase,
canonical loaded scene, текущий Ready MapRunKey и локальный MapBootstrap.IsLocallyReady,
условия и fingerprint с принятыми receipts. Переносом камеры владеет E2EViews.
Предел стабильности —0.05м/1градус, floorY проверяется только когда задан в views.
После ready проверяются disconnect/focus/map-generation/quality/view/levels/FFR;
изменение инвалидирует ready до конца фазы, без тихого повторного открытия окна.

`vrb-gpu-platform-v1`: только фактический rendered WindowsPlayer client с XRSettings.enabled=false
и ненулевым graphics backend разрешает null для ffr/cpuLevel/gpuLevel с точной Reason
`not-applicable: rendered Windows client with XR disabled`. Policy записывается в conditions;
provider самостоятельно сверяет платформу/роль/XR, не доверяя одному словарю caller.
Для Quest и сервера исключения нет. Mono resolution берётся из Screen, refreshHz —
реальное наблюдение дисплея без fallback72; неизвестные значения остаются null+reason.

Fingerprint method `unity-urp-scene-detail-settings-v1`: SHA256 UTF8 компактного JSON
quality snapshot. Покрывает перечисленные quality/URP параметры, реально прочитанные
effective LOD-пороги/числа renderers/отображение на original, renderer.enabled и texture
group limits. Не является content hash всех shader variants/resources и не доказывает
actual draw. Snapshot снимается до разгона; SceneDetailApplier.DiagnosticsRevision
увеличивается после собственных Apply/Restore, adapter только читает. Requested hash
RunSpec не выдаётся за actual: несовпадение отклоняет ready штатным provider.

Raw source.json/phases.csv/frames.csv — прежний recorder format. runtime.json связывает
run/role/process/clock/source/spec/build hashes, фактический quality snapshot, readyCount
и первый отказ. Recorder complete/drop counters сохраняются независимо от readyCount.
GPU source interval/latency неизвестны: sourceIntervalsKnown=false, gpuPhaseMappingVerified=false;
local ready не подтверждает GPU source frame, native clock mapping или GPU budget.

Явный opt-in параметр сценария `scene_detail=lod-a|lod-b` требует RunSpec. Это только
объявленная автором пара lod-ab-control-v1: midStart10, farStart25/20, midSkip0/farSkip9,
textureBias/cullSize0. Профиль asset не сохраняется. До разгона правится Live и вызывается
единственный Apply; после остановки записи возвращаются прежние правила через тот же
applier. При его выгрузке/замене чужой экземпляр не трогается; restore status записан.
Requested mode сохраняется отдельно; importer отвергает несовпадение recorder mode.
quality-canonical.json содержит точные bytes, по которым посчитан fingerprint; importer
проверяет SHA256, не пытаясь повторить форматирование float между C# и Python.

Новые тесты/исполняемые ожидания до пользовательской quality validation не добавлены.
Проверка текущей реализации: compilation и реальные продуктовые прогоны; технический
PASS и наличие файлов не являются пользовательской приёмкой.

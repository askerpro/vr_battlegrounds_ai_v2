# Узкий RenderDoc bridge: D3D11 replay → JSON

19.10.2026. Реализация этапа6 принятого плана: официальный RenderDoc1.46 SDK,
консольная обёртка, без собственного GPU profiler и без GUI qrenderdoc. Его UI.config
и analytics dialog не являются частью автоматизации. SDK/source/portable binaries
хранятся в ignored reports; C++ source и воспроизводимый build script — в Tools/Perf.

`Build-RenderDocBridge.ps1 -BackendRoot <portable> -SdkRoot <api/replay> -Out <new>`
использует установленный MSVCx64 и Windows SDK, проверяет подпись/version DLL,
создаёт import library по официальной export table. Output новый, без перезаписи.
`renderdoc_bridge version` возвращает actual SDK backend version, без GUI.
`renderdoc_bridge export <capture.rdc> <new-directory>` создаёт JSONL actions,
resources и bindings, затем capture.json как последний маркер полноты.

Action eventId/actionId/parentEventId/API events и resource references берутся из
официального replay API. GPUCounter.EventGPUDuration запрашивается только если
backend его перечисляет; проверяются Float8/Seconds. Отсутствие/отрицательное/NaN/Inf
→ null с причиной, не ноль. Родительские и дочерние времена не суммируются.
Timing domain всегда renderdoc-replay; это не исходные live GPU samples и не
оценка capture overhead. Shader/texture/mesh/light не получают выдуманных отдельных ms.

Resource identity — равенство ResourceId внутри конкретного capture; JSON labels
локальны для этого экспорта. Они не равны Unity InstanceID/GUID/GlobalObjectId.
Точные scene/object joins остаются unresolved. Сравнение двух capture требует отдельного
доказательства соответствия ресурсов, а не совпадения resource-N или имени.

Replay EXE экспортирует официальный REPLAY_PROGRAM_MARKER; enumerateGPUs=false
исключает лишние proxy devices. Graphics/compute bindings разделены: неактивный
D3D11 state не приписывается текущей команде. Actions содержат elementCount/indexed,
copySource/copyDestination/depthOut; стадии — read-only/read-write ресурсы.
API properties и capture metadata записаны раздельно. Выбранный GPU/driver пока
подтверждается исходной строкой собственного replay.log, не default proxy driver.
Log копируется до SDK shutdown, который удаляет успешный временный файл.
Первый реальный capture19Oct: E2E GREEN, native-r10 export exit0,446events,
364work/660resources/368timed,0unresolved refs. Scene joins пока unresolved.

Следующий launch/capture actuator должен вызываться только из прежнего Run-E2E,
который сохраняет владение PID, портами, таймаутом и cleanup. ExecuteAndInject —
ранний запуск доD3D11; поздний inject в уже работающий Player не поддерживается.
TargetControl ident не является PID. PID получает TargetControl.GetPID; force=false,
нельзя вытеснять чужое соединение. Неопределённый исход не повторяется автоматически.
Capture требует явного instrumented/draw RunSpec и принятого ready того же процесса.

Recorder mode передаётся из actual RunSpec; прежний defaultlive сохраняется для
старых callers. Importer по-прежнему отвергает requested/recorder mismatch. Изменение
mode hook после frozen223da422 ещё требует новой компиляции/реального прогона.

Проверено: native export-код компилируется, backend version1.46 прочитан реально.
Новых tests/expectation scripts нет; пользовательская quality validation и итоговые
performance/launch ревью впереди. Статус первоначальных r10 шагов выше — исторический.

19.10, native-r11: descriptors текстур (dimensions,format,mips,array,MSAA,estimated bytes)
и buffers, topology и reflection bindings с subresource ranges. shaderProperty —
имя из SDK reflection, нередко texture0; Unity Material property из него не выводится.
staticallyUnused=false и наличие binding не доказывают физическое чтение GPU.

На Testd5db59ea реальные A/B instrumented runs GREEN; каждый export exit0,
446actions/364work/660resources/368timed,0missing refs. Runtime identity14802refs,
195ресурсов с asset candidates через точный scene SHA и serialized asset identity.
Query выдаёт pass ancestry/bindings/candidates/scene locations ограниченными порциями.
Join различает исходный authoring ID и prefab build-copy candidate, не угадывает collisions.
Сравнение проверяет фактические quality/conditions и SHA использованных bytes всех входов.
У indexed draw без Instanced flag множитель1, у instanced — actual count, включая0.
Фактическая farStart25→20 пара:743694→705336 submitted indexed elements,
7geometry groups изменились; gpuFrameTimeDeltaMilliseconds=null. Это одна capture-пара,
без normal A/A/repeats; descriptive replay distributions не доказывают GPU benefit.

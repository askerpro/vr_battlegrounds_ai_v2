# Build identity table v1

18.10.2026. Opt-in `Build-Game.ps1 -GpuSceneIdentity -Config Test` передаёт Unity
`-gpuSceneIdentity`. IProcessSceneWithReport работает только при Development/BuildPipeline
и изменяет временную build-копию сцены, без SaveScene/SaveAssets исходника.

Переносятся serialized Unity refs → GlobalObjectId (проверены scene GUID/type/nonzero ID)
либо asset GUID/localID. Включены GameObject/Renderer/LODGroup/Light и ресурсы мешей,
материалов, шейдеров, texture bindings/cookies. При неоднозначности/недоступности ID
запись unresolved; по именам/иерархическим индексам идентичность не восстанавливается.
Scene source SHA256 и hash отсортированных ID записываются в таблицу. При runtime
alias/null конфликт также не выбирается молча; опубликованы source entry/runtime
resolved/unresolved counts. TryResolve(Object,out string) читает словари на главном потоке.

Adapter снимает provenance и process-local instance refs до разгона, сохраняет
scene-identities.json после остановки. Importer переносит точные bytes в raw пакета.
InstanceID не становится authoring/GPU ID. Dynamic spawns, static batching, instancing,
multiview и capture resource handles требуют отдельных наблюдений/joins; автоматической
атрибуции GPU-времени ресурсам таблица не даёт. Она не содержит56МБ редакторского JSON.

Lookup/строки/serialized refs имеют startup/memory overhead; нулевая стоимость не
объявляется, opt-in должен пройти отдельную проверку на устройстве. Первый Windows
pilot7e07a430 создан до этого source и строится без таблицы; identity потребует нового
собственного compilation/meta checkpoint и следующей диагностической сборки.

19.10: Testd5db59ea Windows и Android собраны с0ошибок; Windows runtime содержит
14802refs/51unresolved. Scene SHA совпал с историческим structural index.
Важно: GlobalObjectId scene entries снимается с временной build-копии. У prefab instances
Unity native unpack меняет ID; поле authoringId таблицы само по себе не доказывает
равенство исходному edit-time ID. Offline join сохраняет исходный ID отдельно от
buildCopyId и runtimeInstanceId. Наблюдаемая XOR-свёртка допускается только как
prefabUnpackFoldCandidate при точном scene SHA и единственности preimage/типа/runtime ref.
Это не публичный контракт Unity; collision/неизвестный случай остаётся unresolved.
По именам GPU ресурсов создаются только asset candidates, не точная identity или ms.

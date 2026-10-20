using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps.Detail;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Performance
{
    /// <summary>
    /// Структурный индекс уже загруженной сцены. Не измеряет GPU и не меняет Unity-объекты.
    /// Вызывать явно на главном потоке; результат не доказывает видимость, draw или активный LOD.
    /// </summary>
    public static class GpuSceneManifestExporter
    {
        public static string ExportLoadedScene(string scenePath, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(scenePath)) throw new ArgumentException("Нужен путь загруженной сцены.", nameof(scenePath));
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Сцена не загружена: " + scenePath);
            string directory = ValidateOutputDirectory(outputDirectory);
            var manifest = new Capture(scene).Build();
            string json = manifest.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
            // Проверяем путь повторно перед единственной записью; файл никогда не перезаписывается.
            ValidateOutputDirectory(directory);
            Directory.CreateDirectory(directory);
            RejectReparsePoints(directory);
            string destination = Path.Combine(directory, "scene.json");
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
            return destination;
        }

        private static string ValidateOutputDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Нужна новая папка reports.", nameof(value));
            foreach (string part in value.Replace('\\', '/').Split('/'))
                if (part == ".." || part == ".") throw new ArgumentException("Traversal в пути отчёта запрещён.", nameof(value));
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(project, value));
            string prefix = project.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Путь должен быть внутри reports текущего проекта.", nameof(value));
            string[] segments = directory.Substring(prefix.Length).Replace('\\', '/').Split('/');
            if (segments.Length < 4 || !string.Equals(segments[0], "tasks", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(segments[1]) || segments[1].StartsWith(".", StringComparison.Ordinal) ||
                !string.Equals(segments[2], "reports", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Нужна новая папка tasks/<task-id>/reports/<run> текущего проекта.", nameof(value));
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Папка отчёта уже существует; укажите новую: " + directory);
            RejectReparsePoints(directory);
            return directory;
        }

        private static void RejectReparsePoints(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse point в пути отчёта запрещён: " + current);
            }
        }

        private sealed class Capture
        {
            private readonly Scene _scene;
            private readonly string _project;
            private readonly bool _runtime;
            private readonly string _captureId = Guid.NewGuid().ToString("N");
            private readonly JArray _objects = new JArray();
            private readonly JArray _resources = new JArray();
            private readonly JArray _lights = new JArray();
            private readonly JArray _lods = new JArray();
            private readonly Dictionary<Object, string> _resourceIds = new Dictionary<Object, string>();
            private readonly Dictionary<Object, JObject> _references = new Dictionary<Object, JObject>();
            private readonly Dictionary<string, JObject> _resourceRows = new Dictionary<string, JObject>();
            private readonly Dictionary<string, JObject> _fileEvidence = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<LODGroup, List<JObject>> _groupDistances = new Dictionary<LODGroup, List<JObject>>();
            private readonly Dictionary<Renderer, List<JObject>> _rendererDistances = new Dictionary<Renderer, List<JObject>>();
            private readonly Dictionary<LODGroup, List<SceneDetailDiagnosticsSnapshot.Group>> _snapshotGroups = new Dictionary<LODGroup, List<SceneDetailDiagnosticsSnapshot.Group>>();
            private readonly Dictionary<Renderer, List<SceneDetailDiagnosticsSnapshot.RendererState>> _snapshotRenderers = new Dictionary<Renderer, List<SceneDetailDiagnosticsSnapshot.RendererState>>();
            private readonly List<SceneDetailApplier> _appliers = new List<SceneDetailApplier>();
            private SceneDetailDiagnosticsSnapshot _snapshot;
            private string _snapshotReason;

            public Capture(Scene scene)
            {
                _scene = scene;
                _runtime = Application.isPlaying;
                _project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }

            public JObject Build()
            {
                GameObject[] roots = _scene.GetRootGameObjects();
                foreach (GameObject root in roots) _appliers.AddRange(root.GetComponentsInChildren<SceneDetailApplier>(true));
                CaptureDetail();
                foreach (GameObject root in roots) Visit(root.transform);
                return new JObject
                {
                    ["schemaVersion"] = 1, ["captureId"] = _captureId,
                    ["source"] = new JObject
                    {
                        ["mode"] = _runtime ? "runtime" : "editTime", ["scenePath"] = _scene.path,
                        ["sceneName"] = _scene.name, ["sceneDirty"] = _scene.isDirty,
                        ["sceneFile"] = FileEvidence(_scene.path), ["unityVersion"] = Application.unityVersion,
                        ["capturedUtc"] = DateTime.UtcNow.ToString("O"),
                        ["frame"] = Time.frameCount, ["realtime"] = Number(Time.realtimeSinceStartupAsDouble),
                        ["availableAppliers"] = References(_appliers),
                        ["globalTextureMipmapLimit"] = QualitySettings.globalTextureMipmapLimit,
                        ["lodBias"] = Number(QualitySettings.lodBias),
                        ["nonFiniteNumberPolicy"] = "null; исходное нечисловое значение не заменяется числом",
                        ["authoringIdentityPolicy"] = "GlobalObjectId только в editTime; runtime требует сверки с authoring manifest"
                    },
                    ["objects"] = _objects, ["resources"] = _resources, ["lights"] = _lights,
                    ["lodGroups"] = _lods, ["sceneDetail"] = Detail(),
                    ["observations"] = new JObject
                    {
                        ["observedLod"] = null, ["expectedLod"] = null, ["gpuMilliseconds"] = null,
                        ["cameraDistance"] = null, ["screenRelativeHeight"] = null,
                        ["reason"] = "Нет синхронизированной камеры, проекции или GPU capture; isVisible — только сырое поле Unity.",
                        ["snapshotAvailable"] = _snapshot != null,
                        ["mappingQuality"] = _snapshot == null ? (_runtime ? "unavailable" : "authoredOnly") : (_snapshot.complete ? "snapshotComplete" : "snapshotPartial")
                    }
                };
            }

            private void CaptureDetail()
            {
                if (_runtime)
                {
                    SceneDetailApplier current = SceneDetailApplier.Current;
                    if (current == null || current.gameObject.scene != _scene)
                        _snapshotReason = "Нет active Current применителя в целевой сцене.";
                    else if (!current.TryCaptureDiagnostics(out _snapshot, out _snapshotReason)) _snapshot = null;
                    if (_snapshot == null) return;
                    foreach (var group in _snapshot.groups)
                        if (group != null && group.group != null) AddRecord(_snapshotGroups, group.group, group);
                    foreach (var renderer in _snapshot.renderers)
                        if (renderer != null && renderer.renderer != null) AddRecord(_snapshotRenderers, renderer.renderer, renderer);
                    return;
                }

                _snapshotReason = "Edit-time: runtime кэш применителя не запрашивается; authored LOD сняты отдельно.";
                foreach (SceneDetailApplier applier in _appliers)
                {
                    // Только чтение записей, ранее сохранённых владельцем сцены; Collect не вызывается.
                    using (var serialized = new SerializedObject(applier))
                    {
                        SerializedProperty groups = serialized.FindProperty("_lods");
                        if (groups != null && groups.isArray)
                            for (int i = 0; i < groups.arraySize; i++)
                            {
                                SerializedProperty entry = groups.GetArrayElementAtIndex(i);
                                var group = entry.FindPropertyRelative("group")?.objectReferenceValue as LODGroup;
                                SerializedProperty distance = entry.FindPropertyRelative("distance");
                                if (group != null && distance != null) AddRecord(_groupDistances, group, new JObject
                                {
                                    ["applier"] = Reference(applier), ["recordIndex"] = i,
                                    ["arenaEdgeDistance"] = Number(distance.floatValue), ["distanceSource"] = "serializedAuthoringRecord"
                                });
                            }
                        SerializedProperty renderers = serialized.FindProperty("_renderers");
                        if (renderers != null && renderers.isArray)
                            for (int i = 0; i < renderers.arraySize; i++)
                            {
                                SerializedProperty entry = renderers.GetArrayElementAtIndex(i);
                                var renderer = entry.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer;
                                SerializedProperty distance = entry.FindPropertyRelative("distance");
                                SerializedProperty size = entry.FindPropertyRelative("size");
                                if (renderer != null && distance != null && size != null)
                                    AddRecord(_rendererDistances, renderer, new JObject
                                    {
                                        ["applier"] = Reference(applier), ["recordIndex"] = i,
                                        ["arenaEdgeDistance"] = Number(distance.floatValue), ["size"] = Number(size.floatValue),
                                        ["distanceSource"] = "serializedAuthoringRecord"
                                    });
                            }
                    }
                }
            }

            private void Visit(Transform transform)
            {
                GameObject gameObject = transform.gameObject;
                var rendererRows = new JArray();
                foreach (Renderer renderer in gameObject.GetComponents<Renderer>()) rendererRows.Add(RendererRow(renderer));
                var resourceIds = new JArray();
                var seenResources = new HashSet<string>();
                foreach (JObject renderer in rendererRows)
                    foreach (JToken id in (JArray)renderer["resourceIds"])
                        if (seenResources.Add((string)id)) resourceIds.Add((string)id);
                var lodRefs = new JArray();
                foreach (LODGroup group in gameObject.GetComponents<LODGroup>())
                {
                    lodRefs.Add(Reference(group));
                    _lods.Add(LodRow(group));
                }
                foreach (Light light in gameObject.GetComponents<Light>()) _lights.Add(LightRow(light));
                JObject identity = Reference(gameObject);
                _objects.Add(new JObject
                {
                    ["id"] = identity["id"], ["identityConfidence"] = identity["confidence"],
                    ["identity"] = identity, ["path"] = ObjectPath(transform), ["resourceIds"] = resourceIds,
                    ["siblingChain"] = SiblingChain(transform), ["parent"] = Reference(transform.parent != null ? transform.parent.gameObject : null),
                    ["activeSelf"] = gameObject.activeSelf, ["activeInHierarchy"] = gameObject.activeInHierarchy,
                    ["layer"] = gameObject.layer, ["isStatic"] = gameObject.isStatic,
                    ["prefabSource"] = AssetIdentity(PrefabUtility.GetCorrespondingObjectFromSource(gameObject)),
                    ["transform"] = new JObject
                    {
                        ["position"] = Vector(transform.position), ["rotation"] = QuaternionRow(transform.rotation),
                        ["lossyScale"] = Vector(transform.lossyScale), ["localPosition"] = Vector(transform.localPosition),
                        ["localRotation"] = QuaternionRow(transform.localRotation), ["localScale"] = Vector(transform.localScale)
                    },
                    ["renderers"] = rendererRows, ["lodGroups"] = lodRefs
                });
                for (int i = 0; i < transform.childCount; i++) Visit(transform.GetChild(i));
            }

            private JObject RendererRow(Renderer renderer)
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                Material[] materials = renderer.sharedMaterials;
                var meshRenderer = renderer as MeshRenderer;
                bool staticBatch = renderer.isPartOfStaticBatch;
                int? submeshStart = meshRenderer != null ? meshRenderer.subMeshStartIndex : (int?)null;
                bool rangeKnown = mesh != null && !staticBatch &&
                    ((meshRenderer != null && submeshStart == 0) || renderer is SkinnedMeshRenderer);
                string rangeReason = rangeKnown ? null :
                    (staticBatch ? "Static batch: диапазон renderer в combined shared mesh не подтверждён public API." :
                    (mesh == null ? "Нет известного shared mesh этого renderer." : "Начало/диапазон submesh этого типа renderer не подтверждён."));
                var slots = new JArray();
                for (int i = 0; i < materials.Length; i++)
                    slots.Add(new JObject { ["slot"] = i, ["material"] = Resource(materials[i]) });
                var submeshes = new JArray();
                // Связи по авторским правилам — ожидаемая структура, не наблюдение draw.
                // Для combined mesh нельзя приписывать renderer полный список submesh ресурса.
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    bool resolved = rangeKnown && mesh.subMeshCount > 0;
                    bool extra = resolved && slot >= mesh.subMeshCount;
                    submeshes.Add(new JObject
                    {
                        ["submesh"] = resolved ? (JToken)new JValue(Math.Min(slot, mesh.subMeshCount - 1)) : JValue.CreateNull(),
                        ["mesh"] = Resource(mesh), ["materialSlot"] = slot, ["material"] = Resource(materials[slot]),
                        ["mappingQuality"] = resolved ? "expectedStructural" : "unresolved",
                        ["source"] = resolved ? (extra ? "UnityAuthoringRuleExtraMaterialsUseLastSubmesh" : "UnityAuthoringMaterialSlotOrder") : null,
                        ["reason"] = resolved ? null : (rangeReason ?? "Shared mesh не содержит submesh."),
                        ["actualDraw"] = null,
                        ["mappingMeaning"] = "Ожидаемая авторская связь slot/submesh; actual draw/pass не наблюдались."
                    });
                }
                if (rangeKnown)
                    for (int submesh = materials.Length; submesh < mesh.subMeshCount; submesh++)
                        submeshes.Add(new JObject
                        {
                            ["submesh"] = submesh, ["mesh"] = Resource(mesh), ["materialSlot"] = null, ["material"] = null,
                            ["mappingQuality"] = "unresolved", ["source"] = null, ["actualDraw"] = null,
                            ["reason"] = "Материалов меньше submesh; публичная авторская запись не задаёт этот слот, fallback не предполагается."
                        });
                var states = new JArray();
                if (_snapshotRenderers.TryGetValue(renderer, out var snapshotStates))
                    foreach (var state in snapshotStates) states.Add(SnapshotRendererRow(state));
                if (_rendererDistances.TryGetValue(renderer, out var authoringStates))
                    foreach (JObject state in authoringStates) states.Add(state.DeepClone());
                var resourceIds = new JArray();
                var seenResources = new HashSet<string>();
                AddResourceTree(Resource(mesh), seenResources, resourceIds);
                foreach (JObject slot in slots) AddResourceTree(slot["material"], seenResources, resourceIds);
                return new JObject
                {
                    ["identity"] = Reference(renderer), ["object"] = Reference(renderer.gameObject),
                    ["type"] = renderer.GetType().Name, ["enabled"] = renderer.enabled, ["isVisibleRaw"] = renderer.isVisible,
                    ["bounds"] = BoundsRow(renderer.bounds), ["mesh"] = Resource(mesh), ["materialSlots"] = slots,
                    ["isPartOfStaticBatch"] = staticBatch,
                    ["subMeshStartIndex"] = submeshStart.HasValue ? (JToken)new JValue(submeshStart.Value) : JValue.CreateNull(),
                    ["submeshRange"] = rangeKnown ? new JObject { ["first"] = 0, ["count"] = mesh.subMeshCount, ["meaning"] = "structuralSharedMeshRange" } : null,
                    ["submeshRangeReason"] = rangeReason,
                    ["submeshBindings"] = submeshes, ["resourceIds"] = resourceIds,
                    ["sceneDetailStates"] = states, ["sceneDetailState"] = states.Count == 1 ? states[0].DeepClone() : null,
                    ["sceneDetailStateReason"] = states.Count == 0 ? "Нет сохранённой managed записи или snapshot entry." :
                        (states.Count > 1 ? "Неоднозначность: сохранены все записи; одна запись не выбиралась." : null),
                    ["shadowCastingMode"] = renderer.shadowCastingMode.ToString(), ["receiveShadows"] = renderer.receiveShadows,
                    ["lightmapIndex"] = renderer.lightmapIndex, ["lightmapScaleOffset"] = Vector(renderer.lightmapScaleOffset),
                    ["cameraDistance"] = null, ["screenRelativeHeight"] = null, ["observedLod"] = null,
                    ["expectedLod"] = null, ["projectionReason"] = "Camera-specific наблюдение отсутствует."
                };
            }

            private JObject LodRow(LODGroup group)
            {
                _snapshotGroups.TryGetValue(group, out var snapshotStates);
                var state = snapshotStates != null && snapshotStates.Count == 1 ? snapshotStates[0] : null;
                var states = new JArray();
                var distances = new JArray();
                if (snapshotStates != null)
                    foreach (var entry in snapshotStates)
                    {
                        states.Add(SnapshotGroupRow(entry));
                        distances.Add(new JObject { ["arenaEdgeDistance"] = Number(entry.arenaEdgeDistance), ["distanceSource"] = "runtimeSnapshotManagedRecord" });
                    }
                if (_groupDistances.TryGetValue(group, out var authoringDistances))
                    foreach (JObject entry in authoringDistances) distances.Add(entry.DeepClone());
                bool ambiguous = (snapshotStates != null && snapshotStates.Count > 1) || distances.Count > 1;
                return new JObject
                {
                    ["identity"] = Reference(group), ["object"] = Reference(group.gameObject),
                    ["enabled"] = group.enabled, ["size"] = Number(group.size), ["localReferencePoint"] = Vector(group.localReferencePoint),
                    ["fadeMode"] = group.fadeMode.ToString(), ["animateCrossFading"] = group.animateCrossFading,
                    ["authored"] = !_runtime ? Levels(group.GetLODs()) : JValue.CreateNull(),
                    ["original"] = state != null ? SnapshotLevels(state.original) : JValue.CreateNull(),
                    ["effective"] = state != null ? SnapshotLevels(state.effective) : (_runtime ? Levels(group.GetLODs()) : JValue.CreateNull()),
                    ["effectiveSource"] = state != null ? "runtimeSnapshot" : (_runtime ? "currentGetLODsWithoutOriginal" : null),
                    ["originalMeaning"] = "Кэш до Apply, требует сверки с edit-time authoring manifest.",
                    ["effectiveToOriginal"] = state?.effectiveToOriginal != null ? new JArray(state.effectiveToOriginal) : JValue.CreateNull(),
                    ["mappingVerified"] = !ambiguous && state != null && state.mappingVerified,
                    ["snapshotStates"] = states, ["arenaDistanceRecords"] = distances,
                    ["reason"] = ambiguous ? "Неоднозначность: сохранены все записи; точная запись не выбиралась." : (state != null ? state.reason : _snapshotReason),
                    ["arenaEdgeDistance"] = distances.Count == 1 ? distances[0]["arenaEdgeDistance"].DeepClone() : JValue.CreateNull(),
                    ["arenaDistanceSource"] = distances.Count == 1 ? distances[0]["distanceSource"].DeepClone() : new JValue(ambiguous ? "ambiguous" : "unavailable"),
                    ["band"] = state?.band, ["rule"] = state != null ? Rule(state.rule) : null,
                    ["observed"] = null, ["expected"] = null, ["cameraDistance"] = null, ["screenRelativeHeight"] = null,
                    ["projectionReason"] = "Выбор камерой, culling и draw не наблюдались."
                };
            }

            private JObject Detail()
            {
                if (_snapshot == null) return new JObject { ["snapshot"] = null, ["unavailableReason"] = _snapshotReason };
                // Сохраняем и записи с потерянными ссылками: они не исчезают при построении индекса сцены.
                var groups = new JArray();
                foreach (var group in _snapshot.groups)
                    groups.Add(group == null ? (JToken)JValue.CreateNull() : SnapshotGroupRow(group));
                var renderers = new JArray();
                foreach (var renderer in _snapshot.renderers)
                    renderers.Add(renderer == null ? (JToken)JValue.CreateNull() : SnapshotRendererRow(renderer));
                var textures = new JArray();
                foreach (var group in _snapshot.textureGroups)
                    textures.Add(group == null ? JValue.CreateNull() : new JObject
                    {
                        ["name"] = group.name, ["exists"] = group.exists, ["baselineKnown"] = group.baselineKnown,
                        ["baseline"] = group.baselineKnown ? TextureSettings(group.baseline) : null,
                        ["current"] = group.exists ? TextureSettings(group.current) : null
                    });
                return new JObject
                {
                    ["snapshot"] = new JObject
                    {
                        ["schemaVersion"] = _snapshot.schemaVersion, ["scenePath"] = _snapshot.scenePath, ["sceneName"] = _snapshot.sceneName,
                        ["applier"] = Reference(_snapshot.applier), ["profile"] = AssetIdentity(_snapshot.profile),
                        ["observationFrame"] = _snapshot.observationFrame, ["observationRealtime"] = Number(_snapshot.observationRealtime),
                        ["midStart"] = Number(_snapshot.midStart), ["farStart"] = Number(_snapshot.farStart),
                        ["mid"] = Rule(_snapshot.mid), ["far"] = Rule(_snapshot.far),
                        ["globalTextureMipmapLimit"] = _snapshot.globalTextureMipmapLimit, ["lodBias"] = Number(_snapshot.lodBias),
                        ["groups"] = groups, ["renderers"] = renderers, ["textureGroups"] = textures, ["complete"] = _snapshot.complete,
                        ["limitations"] = _snapshot.limitations == null ? new JArray() : new JArray(_snapshot.limitations)
                    }, ["unavailableReason"] = null
                };
            }

            private JObject LightRow(Light light)
            {
                LightBakingOutput baking = light.bakingOutput;
                JObject identity = Reference(light);
                return new JObject
                {
                    ["id"] = identity["id"], ["path"] = ObjectPath(light.transform), ["identityConfidence"] = identity["confidence"],
                    ["identity"] = identity, ["object"] = Reference(light.gameObject), ["type"] = light.type.ToString(),
                    ["enabled"] = light.enabled, ["activeInHierarchy"] = light.gameObject.activeInHierarchy,
                    ["lightmapBakeType"] = light.lightmapBakeType.ToString(),
                    ["bakingOutput"] = new JObject
                    {
                        ["isBaked"] = baking.isBaked, ["lightmapBakeType"] = baking.lightmapBakeType.ToString(),
                        ["mixedLightingMode"] = baking.mixedLightingMode.ToString(),
                        ["occlusionMaskChannel"] = baking.occlusionMaskChannel, ["probeOcclusionLightIndex"] = baking.probeOcclusionLightIndex
                    },
                    ["shadows"] = light.shadows.ToString(), ["shadowStrength"] = Number(light.shadowStrength),
                    ["shadowResolution"] = light.shadowResolution.ToString(), ["shadowCustomResolution"] = light.shadowCustomResolution,
                    ["shadowBias"] = Number(light.shadowBias), ["shadowNormalBias"] = Number(light.shadowNormalBias),
                    ["shadowNearPlane"] = Number(light.shadowNearPlane), ["cullingMask"] = light.cullingMask,
                    ["renderingLayerMask"] = light.renderingLayerMask, ["range"] = Number(light.range),
                    ["intensity"] = Number(light.intensity), ["color"] = ColorRow(light.color),
                    ["spotAngle"] = Number(light.spotAngle), ["innerSpotAngle"] = Number(light.innerSpotAngle),
                    ["cookie"] = Resource(light.cookie), ["casters"] = null, ["shadowAtlasAllocation"] = null,
                    ["captureEvents"] = null, ["unresolvedReason"] = "Нет GPU capture и camera-specific shadow/caster evidence."
                };
            }

            private JToken Resource(Object resource)
            {
                if (resource == null) return JValue.CreateNull();
                if (_resourceIds.TryGetValue(resource, out string existing)) return new JValue(existing);
                JObject identity = AssetIdentity(resource);
                bool stable = identity["guid"].Type == JTokenType.String && identity["localId"].Type == JTokenType.Integer;
                string id = stable ? "asset:" + (string)identity["guid"] + ":" + identity["localId"] : LocalId(resource);
                _resourceIds.Add(resource, id);
                string assetPath = (string)identity["assetPath"];
                var row = new JObject
                {
                    ["id"] = id, ["path"] = string.IsNullOrEmpty(assetPath) ? "runtime-or-builtin://" + _captureId + "/" + resource.GetType().Name + "/" + resource.GetInstanceID() + "/" + resource.name : assetPath,
                    ["guid"] = identity["guid"].DeepClone(), ["localId"] = identity["localId"].DeepClone(),
                    ["contentHash"] = identity["file"]["sha256"].DeepClone(),
                    ["contentHashMeaning"] = "SHA256 физических source bytes; не GPU/импортированный blob.",
                    ["identityConfidence"] = stable ? "assetGuidAndLocalId" : "runLocal",
                    ["name"] = resource.name, ["type"] = resource.GetType().Name, ["identity"] = identity
                };
                _resourceRows.Add(id, row);
                _resources.Add(row);
                if (resource is Mesh mesh)
                {
                    row["geometryCountScope"] = "Весь shared mesh resource; не диапазон renderer, не actual draw.";
                    row["vertexCount"] = mesh.vertexCount;
                    row["indexFormat"] = mesh.indexFormat.ToString();
                    row["isReadable"] = mesh.isReadable;
                    row["bounds"] = BoundsRow(mesh.bounds);
                    var submeshes = new JArray();
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        submeshes.Add(new JObject { ["index"] = i, ["indexCount"] = mesh.GetIndexCount(i), ["topology"] = mesh.GetTopology(i).ToString() });
                    row["submeshes"] = submeshes;
                }
                else if (resource is Material material)
                {
                    row["shader"] = Resource(material.shader);
                    row["renderQueue"] = material.renderQueue;
                    row["keywords"] = new JArray(material.shaderKeywords);
                    row["enableInstancing"] = material.enableInstancing;
                    row["passCount"] = material.passCount;
                    var passes = new JArray();
                    for (int i = 0; i < material.passCount; i++) passes.Add(new JObject { ["index"] = i, ["name"] = material.GetPassName(i) });
                    row["passes"] = passes;
                    var bindings = new JArray();
                    foreach (string property in material.GetTexturePropertyNames())
                        bindings.Add(new JObject
                        {
                            ["property"] = property, ["texture"] = Resource(material.GetTexture(property)),
                            ["scale"] = Vector(material.GetTextureScale(property)), ["offset"] = Vector(material.GetTextureOffset(property))
                        });
                    row["textureBindings"] = bindings;
                }
                else if (resource is Texture texture)
                {
                    row["width"] = texture.width; row["height"] = texture.height;
                    row["dimension"] = texture.dimension.ToString(); row["mipmapCount"] = texture.mipmapCount;
                    row["graphicsFormat"] = texture.graphicsFormat.ToString();
                    row["format"] = texture is Texture2D texture2D ? (JToken)new JValue(texture2D.format.ToString()) : JValue.CreateNull();
                    row["formatReason"] = texture is Texture2D ? null : "Не Texture2D; graphicsFormat сохранён отдельно.";
                    if (texture is Texture2D limited)
                    {
                        row["mipmapLimitGroup"] = limited.mipmapLimitGroup;
                        row["ignoreMipmapLimit"] = limited.ignoreMipmapLimit;
                        row["activeMipmapLimit"] = limited.activeMipmapLimit;
                    }
                    row["depthOrLayers"] = texture is Texture3D volume ? new JValue(volume.depth) :
                        texture is Texture2DArray array ? new JValue(array.depth) :
                        texture is CubemapArray cubes ? new JValue(cubes.cubemapCount) : JValue.CreateNull();
                    row["mipMapBias"] = Number(texture.mipMapBias); row["filterMode"] = texture.filterMode.ToString();
                    row["wrapMode"] = texture.wrapMode.ToString(); row["anisoLevel"] = texture.anisoLevel;
                }
                return new JValue(id);
            }

            private JObject AssetIdentity(Object resource)
            {
                if (resource == null) return null;
                string path = AssetDatabase.GetAssetPath(resource);
                bool known = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resource, out string guid, out long localId) &&
                    !string.IsNullOrEmpty(guid) && guid != "00000000000000000000000000000000" && localId != 0;
                return new JObject
                {
                    ["instanceId"] = resource.GetInstanceID(), ["instanceIdScope"] = "runLocal",
                    ["guid"] = known ? guid : null, ["localId"] = known ? (JToken)new JValue(localId) : JValue.CreateNull(),
                    ["assetPath"] = string.IsNullOrEmpty(path) ? null : path,
                    ["dependencyHash"] = !string.IsNullOrEmpty(path) ? AssetDatabase.GetAssetDependencyHash(path).ToString() : null,
                    ["file"] = FileEvidence(path), ["identityReason"] = known ? null : "GUID/localID недоступны; ресурс может быть built-in или runtime."
                };
            }

            private JObject FileEvidence(string assetPath)
            {
                if (string.IsNullOrEmpty(assetPath)) return new JObject { ["sha256"] = null, ["reason"] = "Нет физического asset path." };
                if (_fileEvidence.TryGetValue(assetPath, out JObject cached)) return (JObject)cached.DeepClone();
                var result = new JObject { ["sha256"] = null, ["isLfsPointer"] = false, ["reason"] = null };
                try
                {
                    string path = Path.GetFullPath(Path.Combine(_project, assetPath));
                    if (!File.Exists(path)) result["reason"] = "Физический файл недоступен (включая virtual Packages/built-in).";
                    else
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            byte[] prefix = new byte[256];
                            int count = stream.Read(prefix, 0, prefix.Length);
                            bool pointer = Encoding.UTF8.GetString(prefix, 0, count).StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal);
                            result["byteLength"] = stream.Length;
                            result["isLfsPointer"] = pointer;
                            if (pointer) result["reason"] = "LFS pointer: SHA256 содержимого ресурса недоступен.";
                            else
                            {
                                stream.Position = 0;
                                using (var algorithm = SHA256.Create())
                                    result["sha256"] = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                            }
                        }
                    }
                }
                catch (IOException error) { result["reason"] = error.GetType().Name + ": " + error.Message; }
                catch (UnauthorizedAccessException error) { result["reason"] = error.GetType().Name + ": " + error.Message; }
                _fileEvidence[assetPath] = result;
                return (JObject)result.DeepClone();
            }

            private JObject Reference(Object value)
            {
                if (value == null) return null;
                if (_references.TryGetValue(value, out JObject cached)) return (JObject)cached.DeepClone();
                string stable = null;
                string reason = _runtime ? "Runtime identity не сверена с authoring manifest." : "Нет сохранённого ненулевого GlobalObjectId.";
                if (!_runtime)
                {
                    GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
                    if (id.identifierType != 0 && id.targetObjectId != 0 && id.assetGUID.ToString() != "00000000000000000000000000000000")
                    {
                        stable = id.ToString();
                        reason = null;
                    }
                }
                GameObject gameObject = value as GameObject;
                if (value is Component component) gameObject = component.gameObject;
                var reference = new JObject
                {
                    ["id"] = stable ?? LocalId(value), ["confidence"] = stable != null ? "authoringGlobalObjectId" : "runLocal",
                    ["authoringID"] = stable, ["authoringIdentityReason"] = reason,
                    ["instanceId"] = value.GetInstanceID(), ["instanceIdScope"] = "runLocal",
                    ["type"] = value.GetType().Name, ["name"] = value.name,
                    ["path"] = gameObject != null ? ObjectPath(gameObject.transform) : null,
                    ["siblingChain"] = gameObject != null ? SiblingChain(gameObject.transform) : null
                };
                _references.Add(value, reference);
                return (JObject)reference.DeepClone();
            }

            private string LocalId(Object value) => "runLocal:" + _captureId + ":" + value.GetType().Name + ":" + value.GetInstanceID();

            private static void AddRecord<TKey, TValue>(Dictionary<TKey, List<TValue>> records, TKey key, TValue value)
            {
                if (!records.TryGetValue(key, out List<TValue> entries))
                {
                    entries = new List<TValue>();
                    records.Add(key, entries);
                }
                entries.Add(value);
            }

            private JObject SnapshotGroupRow(SceneDetailDiagnosticsSnapshot.Group group) => new JObject
            {
                ["group"] = Reference(group.group), ["arenaEdgeDistance"] = Number(group.arenaEdgeDistance),
                ["band"] = group.band, ["rule"] = Rule(group.rule),
                ["original"] = SnapshotLevels(group.original), ["effective"] = SnapshotLevels(group.effective),
                ["effectiveToOriginal"] = group.effectiveToOriginal == null ? (JToken)JValue.CreateNull() : new JArray(group.effectiveToOriginal),
                ["mappingVerified"] = group.mappingVerified, ["reason"] = group.reason,
                ["observed"] = null, ["expected"] = null
            };

            private JObject SnapshotRendererRow(SceneDetailDiagnosticsSnapshot.RendererState state) => new JObject
            {
                ["renderer"] = Reference(state.renderer), ["baselineKnown"] = state.baselineKnown,
                ["baselineEnabled"] = state.baselineKnown ? (JToken)new JValue(state.baselineEnabled) : JValue.CreateNull(),
                ["currentEnabled"] = state.renderer != null ? (JToken)new JValue(state.currentEnabled) : JValue.CreateNull(),
                ["currentEnabledReason"] = state.renderer != null ? null : "Renderer отсутствует/удалён; текущее enabled не наблюдалось.",
                ["hasManagedDistance"] = state.hasManagedDistance,
                ["arenaEdgeDistance"] = state.hasManagedDistance ? Number(state.arenaEdgeDistance) : JValue.CreateNull(),
                ["size"] = state.hasManagedDistance ? Number(state.size) : JValue.CreateNull(),
                ["distanceSource"] = state.hasManagedDistance ? "runtimeSnapshotManagedRecord" : "unavailable"
            };

            private void AddResourceTree(JToken token, HashSet<string> seen, JArray identities)
            {
                if (token == null || token.Type != JTokenType.String) return;
                string id = (string)token;
                if (!seen.Add(id)) return;
                identities.Add(id);
                if (!_resourceRows.TryGetValue(id, out JObject row)) return;
                AddResourceTree(row["shader"], seen, identities);
                if (row["textureBindings"] is JArray bindings)
                    foreach (JObject binding in bindings) AddResourceTree(binding["texture"], seen, identities);
            }

            private JArray References<T>(IEnumerable<T> values) where T : Object
            {
                var result = new JArray();
                if (values != null) foreach (T value in values) result.Add(Reference(value));
                return result;
            }

            private JArray Levels(LOD[] levels)
            {
                var result = new JArray();
                for (int i = 0; i < levels.Length; i++)
                    result.Add(new JObject { ["index"] = i, ["screenRelativeTransitionHeight"] = Number(levels[i].screenRelativeTransitionHeight),
                        ["fadeTransitionWidth"] = Number(levels[i].fadeTransitionWidth), ["renderers"] = References(levels[i].renderers) });
                return result;
            }

            private JToken SnapshotLevels(SceneDetailDiagnosticsSnapshot.Level[] levels)
            {
                if (levels == null) return JValue.CreateNull();
                var result = new JArray();
                for (int i = 0; i < levels.Length; i++)
                {
                    var level = levels[i];
                    result.Add(level == null ? JValue.CreateNull() : new JObject { ["index"] = i,
                        ["screenRelativeTransitionHeight"] = Number(level.screenRelativeTransitionHeight),
                        ["fadeTransitionWidth"] = Number(level.fadeTransitionWidth), ["renderers"] = References(level.renderers) });
                }
                return result;
            }
        }

        private static JToken Number(double value) => double.IsNaN(value) || double.IsInfinity(value) ? JValue.CreateNull() : new JValue(value);
        private static JArray Vector(Vector2 value) => new JArray(Number(value.x), Number(value.y));
        private static JArray Vector(Vector3 value) => new JArray(Number(value.x), Number(value.y), Number(value.z));
        private static JArray Vector(Vector4 value) => new JArray(Number(value.x), Number(value.y), Number(value.z), Number(value.w));
        private static JArray QuaternionRow(Quaternion value) => new JArray(Number(value.x), Number(value.y), Number(value.z), Number(value.w));
        private static JArray ColorRow(Color value) => new JArray(Number(value.r), Number(value.g), Number(value.b), Number(value.a));
        private static JObject BoundsRow(Bounds bounds) => new JObject { ["center"] = Vector(bounds.center), ["size"] = Vector(bounds.size) };
        private static JObject Rule(SceneDetailProfile.PlaneRule rule) => new JObject { ["lodSkip"] = rule.lodSkip, ["textureBias"] = rule.textureBias, ["cullSize"] = Number(rule.cullSize) };
        private static JObject TextureSettings(TextureMipmapLimitSettings settings) => new JObject { ["limitBias"] = settings.limitBias, ["limitBiasMode"] = settings.limitBiasMode.ToString() };

        private static string ObjectPath(Transform transform)
        {
            var parts = new List<string>();
            for (Transform current = transform; current != null; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static JArray SiblingChain(Transform transform)
        {
            var indices = new List<int>();
            for (Transform current = transform; current != null; current = current.parent) indices.Add(current.GetSiblingIndex());
            indices.Reverse();
            return new JArray(indices);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars.Workbench;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Снимок внешних ссылок для одной перестройки prefab. YAML читается только для поиска
    /// потребителей; ссылки и переопределения записываются исключительно через Unity API.
    /// Владелец операции обязан включить ConsumerPaths в общий rollback до изменения источников.
    /// </summary>
    internal sealed class CyborgPrefabReferenceRemap
    {
        internal const string BrokenRegistryPath = "Assets/Data/Roles/DevicePrefabRegistry.asset";
        private const string BrokenRegistryAssetHash = "8D116541C7696F30EE85FCA08CE01DC08E23A8013A818F37C9788AFA726ECD99";
        private const string BrokenRegistryMetaHash = "260725CF812FD37A51DDF53B4260D71874FCEA49A917D056A818FBDB40938732";
        private readonly Dictionary<Identity, Source> sources = new Dictionary<Identity, Source>();
        private readonly Dictionary<string, string> targetGuids = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Consumer> consumers = new List<Consumer>();
        private readonly List<PreservedBrokenConsumer> preservedBrokenConsumers = new List<PreservedBrokenConsumer>();
        private readonly StringBuilder report = new StringBuilder();
        private Dictionary<Identity, Object> replacements;
        private bool applied;

        internal string[] ConsumerPaths => consumers.Select(c => c.Path)
            .Concat(preservedBrokenConsumers.Select(c => c.Path)).Distinct().ToArray();
        internal string[] TargetPaths => targetGuids.Keys.ToArray();
        internal string Description => report.ToString();

        /// <summary>
        /// Только read-only доказательство исторического orphan. Capture и Apply используют
        /// его для сохранения исходных байтов без remap недоступной schema. Итоговый raw guard
        /// обязан отдельно подтвердить обе исходные ссылки; исключения для него здесь нет.
        /// </summary>
        internal static string DescribeBrokenRegistryProof()
        {
            const string path = BrokenRegistryPath;
            const string assetGuid = "2c044795947c7584b9b8c7c17277de4b";
            const string scriptGuid = "7db30042dd7df8041b7d196806e8a52d";
            const string prefabGuid = "8e0e06323e4754f479a9f19788554652";
            const long prefabId = 4613916165338528910;
            const string assetHash = BrokenRegistryAssetHash;
            const string metaHash = BrokenRegistryMetaHash;
            var diagnostic = new StringBuilder();
            diagnostic.AppendLine("READ-ONLY orphan proof; исключение guard НЕ активировано: " + path);
            if (!File.Exists(path) || !File.Exists(path + ".meta"))
                throw Fail(path, "нет точного asset/meta для доказательства orphan");
            byte[] assetBytes = File.ReadAllBytes(path);
            byte[] metaBytes = File.ReadAllBytes(path + ".meta");
            string actualAssetHash = HashBytes(assetBytes);
            string actualMetaHash = HashBytes(metaBytes);
            diagnostic.AppendLine("assetSHA256=" + actualAssetHash + "; metaSHA256=" + actualMetaHash);
            if (actualAssetHash != assetHash || actualMetaHash != metaHash)
                throw Fail(path, "изменились закреплённые байты orphan:\n" + diagnostic);
            if (AssetDatabase.AssetPathToGUID(path) != assetGuid || AssetDatabase.GUIDToAssetPath(assetGuid) != path)
                throw Fail(path, "не совпал native GUID/path orphan:\n" + diagnostic);

            var pointers = Regex.Matches(Encoding.UTF8.GetString(assetBytes),
                @"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*\d+\}")
                .Cast<Match>().Select(m => new Identity(m.Groups[2].Value, long.Parse(m.Groups[1].Value))).ToArray();
            var oldPrefab = new Identity(prefabGuid, prefabId);
            if (pointers.Length != 3 || pointers.Count(p => p.Equals(new Identity(scriptGuid, 11500000))) != 1 ||
                pointers.Count(p => p.Equals(oldPrefab)) != 2)
                throw Fail(path, "не совпала точная схема: один script PPtr и два Controllers root PPtr:\n" + diagnostic);
            diagnostic.AppendLine("raw devicePrefabs[0].prefab=" + oldPrefab);
            diagnostic.AppendLine("raw devicePrefabs[1].prefab=" + oldPrefab);

            string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
            diagnostic.AppendLine("native MonoScriptPath=" + (string.IsNullOrEmpty(scriptPath) ? "<missing>" : scriptPath));
            if (!string.IsNullOrEmpty(scriptPath))
                throw Fail(path, "MonoScript восстановлен; orphan-классификация запрещена:\n" + diagnostic);
            Object main = AssetDatabase.LoadMainAssetAtPath(path);
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(path);
            string mainType = main ? main.GetType().FullName : "<null/missing>";
            string[] loadedTypes = loaded.Where(a => a).Select(a => a.GetType().FullName).ToArray();
            diagnostic.AppendLine("native mainType=" + mainType + "; validLoadedObjects=" + loadedTypes.Length +
                "; types=" + string.Join(",", loadedTypes));
            // Не считаем неизвестный placeholder доказанно недоступной schema. Для данной
            // диагностики допускается только полное отсутствие доступных native objects.
            if (main || loadedTypes.Length != 0)
                throw Fail(path, "native object/schema доступен; нужен обычный разбор полей:\n" + diagnostic);

            // Один проход актуального imported Assets graph: покрывает также бинарные consumers,
            // для которых текстовый поиск GUID ничего не доказывает. Кэш локален этому снимку.
            var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string candidate in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                p != path && !AssetDatabase.IsValidFolder(p)))
                dependencies.Add(candidate, AssetDatabase.GetDependencies(candidate, false));
            string[] inbound = dependencies.Where(p => p.Value.Contains(path, StringComparer.Ordinal))
                .Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            diagnostic.AppendLine("native reverseDependencyCandidates=" + dependencies.Count + "; inbound=" + inbound.Length);
            if (inbound.Length != 0)
                throw Fail(path, "обнаружены входящие Unity dependencies: " + string.Join(",", inbound.Take(10)) + "\n" + diagnostic);

            // Native чтение само по себе не считается гарантией сохранения исходных файлов.
            if (!File.ReadAllBytes(path).SequenceEqual(assetBytes) || !File.ReadAllBytes(path + ".meta").SequenceEqual(metaBytes))
                throw Fail(path, "во время read-only диагностики изменились asset/meta:\n" + diagnostic);
            diagnostic.AppendLine("asset/meta byte-identical после native чтения; итоговый raw guard обязателен для обеих исходных ссылок.");
            return diagnostic.ToString();
        }

        private static string HashBytes(byte[] value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "");
        }

        /// <summary>Удаление точного исторического orphan внутри общего SourceBackup.</summary>
        internal static string DeleteConfirmedBrokenRegistry()
        {
            string proof = DescribeBrokenRegistryProof();
            try
            {
                if (!AssetDatabase.DeleteAsset(BrokenRegistryPath))
                    throw Fail(BrokenRegistryPath, "native DeleteAsset вернул отказ");
                bool assetExists = File.Exists(BrokenRegistryPath);
                bool metaExists = File.Exists(BrokenRegistryPath + ".meta");
                string liveGuid = AssetDatabase.AssetPathToGUID(BrokenRegistryPath, AssetPathToGUIDOptions.OnlyExistingAssets);
                // Unity сохраняет историческое GUID->path после DeleteAsset до закрытия Editor.
                // Проверяем существующий asset, а не наличие исторической записи в кэше.
                string cachedPath = AssetDatabase.GUIDToAssetPath("2c044795947c7584b9b8c7c17277de4b");
                string liveCachedGuid = string.IsNullOrEmpty(cachedPath) ? string.Empty :
                    AssetDatabase.AssetPathToGUID(cachedPath, AssetPathToGUIDOptions.OnlyExistingAssets);
                string state = "assetExists=" + assetExists + "; metaExists=" + metaExists +
                    "; liveGuid=" + liveGuid + "; cachedPath=" + cachedPath + "; liveCachedGuid=" + liveCachedGuid;
                if (assetExists || metaExists || !string.IsNullOrEmpty(liveGuid) || !string.IsNullOrEmpty(liveCachedGuid))
                    throw Fail(BrokenRegistryPath, "не подтверждено удаление существующего asset/meta: " + state);
                return proof + "\nNative DeleteAsset: " + state + "; активные PPtr переснимаются заново.";
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Удаление orphan не подтверждено; свежий proof:\n" + proof, error);
            }
        }

        /// <summary>Штатная пауза SDK auto-ID на batch editing; preference восстанавливается последним.</summary>
        internal sealed class EditorIdentityScope : IDisposable
        {
            private readonly string key = UltimateXR.Core.UxrConstants.Editor.AutomaticIdGenerationPrefs;
            private readonly bool existed;
            private readonly bool value;
            internal EditorIdentityScope()
            {
                existed = EditorPrefs.HasKey(key);
                value = EditorPrefs.GetBool(key, true);
                EditorPrefs.SetBool(key, false);
            }
            public void Dispose()
            {
                if (existed) EditorPrefs.SetBool(key, value);
                else EditorPrefs.DeleteKey(key);
            }
        }

        private static void RequirePreservedBrokenBytes(PreservedBrokenConsumer snapshot)
        {
            if (snapshot.Path != BrokenRegistryPath || HashBytes(snapshot.AssetBytes) != BrokenRegistryAssetHash ||
                HashBytes(snapshot.MetaBytes) != BrokenRegistryMetaHash || snapshot.Raw.Length != 2)
                throw Fail(snapshot.Path, "снимок orphan не совпал с точными исходными asset/meta и двумя PPtr");
            if (!File.Exists(snapshot.Path) || !File.Exists(snapshot.Path + ".meta") ||
                !File.ReadAllBytes(snapshot.Path).SequenceEqual(snapshot.AssetBytes) ||
                !File.ReadAllBytes(snapshot.Path + ".meta").SequenceEqual(snapshot.MetaBytes))
                throw Fail(snapshot.Path, "сохранённый без remap orphan изменился; требуется rollback");
        }

        internal static CyborgPrefabReferenceRemap Capture(IEnumerable<string> targetPaths)
        {
            using (new EditorIdentityScope()) return CaptureWithStableIdentity(targetPaths);
        }

        private static CyborgPrefabReferenceRemap CaptureWithStableIdentity(IEnumerable<string> targetPaths)
        {
            var result = new CyborgPrefabReferenceRemap();
            var originalTargets = new HashSet<string>(targetPaths, StringComparer.Ordinal);
            var targets = new HashSet<string>(originalTargets, StringComparer.Ordinal);
            // Область источников задана вызывающим кодом. Обычные потомки и потребители
            // не расширяют её транзитивно и не подключают посторонние сцены к миграции.
            foreach (string path in targets)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw Fail(path, "у источника отсутствует GUID");
                result.targetGuids.Add(path, guid);
                foreach (var item in Objects(RequirePrefab(path)))
                {
                    if (!TryIdentity(item.Value, out Identity id) || id.Guid != guid) continue;
                    if (result.sources.ContainsKey(id)) throw Fail(path, "неоднозначный GUID/fileID");
                    result.sources.Add(id, new Source { Path = path, Key = item.Key });
                }
            }

            var pattern = new Regex(@"\{fileID:\s*(-?\d+),\s*guid:\s*(" +
                string.Join("|", result.targetGuids.Values) + @"),\s*type:\s*\d+\}");
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p =>
                p.StartsWith("Assets/", StringComparison.Ordinal) && !originalTargets.Contains(p) &&
                (p.EndsWith(".prefab", StringComparison.Ordinal) || p.EndsWith(".asset", StringComparison.Ordinal) ||
                 p.EndsWith(".unity", StringComparison.Ordinal))).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!File.Exists(path)) continue;
                var raw = new List<RawReference>();
                using (var reader = new StreamReader(path))
                {
                    string line = reader.ReadLine();
                    if (line == null || !line.StartsWith("%YAML", StringComparison.Ordinal)) continue;
                    while ((line = reader.ReadLine()) != null)
                        foreach (Match match in pattern.Matches(line))
                        {
                            var id = new Identity(match.Groups[2].Value, long.Parse(match.Groups[1].Value));
                            // Сломанные до миграции ссылки не становятся её доказательством.
                            if (!result.sources.ContainsKey(id)) continue;
                            string label = line.Substring(0, match.Index).Trim().TrimStart('-').Trim();
                            if (label.EndsWith(":", StringComparison.Ordinal)) label = label.Substring(0, label.Length - 1);
                            raw.Add(new RawReference { Id = id, Label = label });
                        }
                }
                if (path == BrokenRegistryPath)
                {
                    // Только этот подтверждённо незагружаемый orphan сохраняется без попытки
                    // SerializedObject remap. Он не становится valid consumer и не получает
                    // доказательство CanResolveReference; старые PPtr проверит обычный raw guard.
                    string proof = DescribeBrokenRegistryProof();
                    if (raw.Count != 2 || raw.Any(r => r.Label != "prefab" ||
                        !r.Id.Equals(new Identity("8e0e06323e4754f479a9f19788554652", 4613916165338528910))))
                        throw Fail(path, "нет двух ожидаемых доступных исходных Controllers PPtr");
                    var snapshot = new PreservedBrokenConsumer
                    {
                        Path = path, AssetBytes = File.ReadAllBytes(path), MetaBytes = File.ReadAllBytes(path + ".meta"),
                        Raw = raw.ToArray()
                    };
                    RequirePreservedBrokenBytes(snapshot);
                    result.preservedBrokenConsumers.Add(snapshot);
                    result.report.AppendLine(proof);
                    result.report.AppendLine("Orphan сохранён без remap: " + path + "; исходных PPtr=" + snapshot.Raw.Length +
                        ". Не включён в восстановленные bindings; обе ссылки обязан подтвердить raw guard.");
                    continue;
                }
                if (raw.Count == 0) continue;
                if (path.EndsWith(".unity", StringComparison.Ordinal))
                    throw Fail(path, "сцена содержит ссылки на изменяемые объекты; remap сцен не поддержан, операция не начиналась");
                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(a => a && EditorUtility.IsDirty(a)))
                    throw Fail(path, "потребитель имеет несохранённые изменения");

                var consumer = new Consumer { Path = path, Guid = AssetDatabase.AssetPathToGUID(path), Raw = raw };
                if (path.EndsWith(".prefab", StringComparison.Ordinal))
                {
                    RequireWritablePrefab(path);
                    consumer.OriginalObjects = Objects(RequirePrefab(path)).ToDictionary(p => p.Key,
                        p => TryIdentity(p.Value, out Identity id) ? id : default, StringComparer.Ordinal);
                    foreach (var item in Objects(RequirePrefab(path)))
                    {
                        Object inherited = PrefabUtility.GetCorrespondingObjectFromSource(item.Value);
                        var visited = new HashSet<Object>();
                        while (inherited && visited.Add(inherited))
                        {
                            if (TryIdentity(inherited, out Identity id) && result.sources.TryGetValue(id, out Source source) &&
                                source.Path == AvatarHandBases.PlayerBase && source.Key.StartsWith("$/Cyborg[0]/", StringComparison.Ordinal))
                            { consumer.InheritedBodyObjects.Add(item.Key); break; }
                            inherited = PrefabUtility.GetCorrespondingObjectFromSource(inherited);
                        }
                    }
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try { result.CapturePrefab(consumer, root); }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else result.CaptureAsset(consumer);
                foreach (RawReference reference in raw)
                    if (!result.HasCapturedBinding(consumer, reference))
                        throw Fail(path, "PPtr не представлен доступным полем или prefab override: " + reference.Id);
                result.consumers.Add(consumer);
            }
            result.report.AppendLine("Снимок внешних потребителей: " + result.consumers.Count +
                "; полей: " + result.consumers.Sum(c => c.Fields.Count) +
                "; наборов overrides: " + result.consumers.Sum(c => c.Modifications.Count) +
                "; отдельно сохранённых orphan без remap: " + result.preservedBrokenConsumers.Count + ".");
            return result;
        }

        /// <summary>Вызывается после пересоздания всех источников, внутри общего rollback.</summary>
        internal string Apply()
        {
            if (applied) throw new InvalidOperationException("Снимок remap уже применён.");
            foreach (PreservedBrokenConsumer snapshot in preservedBrokenConsumers) RequirePreservedBrokenBytes(snapshot);
            replacements = new Dictionary<Identity, Object>();
            foreach (var target in targetGuids)
            {
                if (AssetDatabase.AssetPathToGUID(target.Key) != target.Value)
                    throw Fail(target.Key, "GUID источника изменился");
                var objects = Objects(RequirePrefab(target.Key));
                foreach (var old in sources.Where(p => p.Value.Path == target.Key))
                    if (objects.TryGetValue(old.Value.Key, out Object value)) replacements.Add(old.Key, value);
            }
            // Проверка всех действующих ссылок до первой записи потребителя.
            foreach (Consumer consumer in consumers)
            {
                Dictionary<string, Object> consumerGraph = consumer.Path.EndsWith(".prefab", StringComparison.Ordinal)
                    ? Objects(RequirePrefab(consumer.Path)) : AssetObjects(consumer.Path);
                foreach (Field field in consumer.Fields)
                {
                    RequireContainer(consumerGraph, field.Container, consumer.Path);
                    Resolve(field.Value, consumerGraph, consumer.Path, false);
                }
                foreach (Field link in consumer.SourceLinks)
                {
                    if (CanRetireSourceLink(consumer, link, consumerGraph)) consumer.RetiredSourceLinks.Add(link);
                    else Resolve(link.Value, null, consumer.Path, false);
                }
                foreach (ModificationSet set in consumer.Modifications)
                    foreach (Modification modification in set.Items)
                    {
                        Resolve(modification.Target, consumerGraph, consumer.Path, true);
                        if (!IsRetired(modification.Target)) Resolve(modification.Value, consumerGraph, consumer.Path, false);
                    }
            }
            foreach (Consumer consumer in OrderedConsumers())
            {
                if (consumer.Path.EndsWith(".prefab", StringComparison.Ordinal)) ApplyPrefab(consumer);
                else ApplyAsset(consumer);
                RefreshReplacements();
                // Ранний readback не продолжает независимые saves после первого отказа.
                // Итоговая проверка после записи всех потомков остаётся обязательной.
                Verify(consumer);
            }
            // Повторное чтение всех файлов после записи потомков: родительские saves не должны
            // нарушить ранее проверенные привязки или создать ложное доказательство для guard.
            foreach (Consumer consumer in consumers)
            {
                Verify(consumer);
                consumer.Verified = true;
            }
            foreach (PreservedBrokenConsumer snapshot in preservedBrokenConsumers)
            {
                RequirePreservedBrokenBytes(snapshot);
                report.AppendLine("Повторный native proof после всех saves; orphan НЕ remapped:");
                report.AppendLine(DescribeBrokenRegistryProof());
                RequirePreservedBrokenBytes(snapshot);
            }
            applied = true;
            return Description;
        }

        internal bool CanResolveReference(string consumer, string oldGuid, long oldId)
        {
            if (!applied || preservedBrokenConsumers.Any(c => c.Path == consumer)) return false;
            var id = new Identity(oldGuid, oldId);
            Consumer captured = consumers.SingleOrDefault(c => c.Path == consumer);
            if (captured == null || !captured.Verified || !captured.Raw.Any(r => r.Id.Equals(id))) return false;
            if (replacements.TryGetValue(id, out Object value) && value && sources.TryGetValue(id, out Source source))
            {
                var graph = Objects(RequirePrefab(source.Path));
                return graph.TryGetValue(source.Key, out Object expected) && expected == value &&
                    captured.Covered.Contains(id);
            }
            // Для source link дополнительно доказано исчезновение конкретного наследуемого
            // контейнера; для обоих видов metadata readback проверил отсутствие PPtr на диске.
            return (captured.RetiredTargets.Contains(id) || captured.RetiredSourceLinks.Any(f => f.Value.Id.Equals(id))) &&
                !captured.Fields.Any(f => f.Value.Id.Equals(id)) &&
                !captured.Modifications.SelectMany(s => s.Items).Any(m => m.Value.Id.Equals(id)) &&
                captured.Raw.Where(r => r.Id.Equals(id)).All(r => r.Label == "target" ? captured.RetiredTargets.Contains(id) :
                    r.Label == "m_CorrespondingSourceObject" && captured.SourceLinks.Where(f => f.Value.Id.Equals(id))
                        .All(f => captured.RetiredSourceLinks.Contains(f)));
        }

        private void CapturePrefab(Consumer consumer, GameObject root)
        {
            var objects = Objects(root);
            // Source links не переписываются вручную: соответствие обязана восстановить Unity.
            // Проверка ниже не разрешит guard пропустить потерянную связь stripped object.
            foreach (var item in objects)
            {
                Ref source = Reference(PrefabUtility.GetCorrespondingObjectFromSource(item.Value), null);
                if (!IsSource(source)) continue;
                consumer.SourceLinks.Add(new Field { Container = item.Key, Value = source });
                consumer.Covered.Add(source.Id);
            }
            foreach (var item in objects) CaptureFields(consumer, item.Key, item.Value, objects);
            foreach (var item in objects.Where(p => p.Value is GameObject &&
                PrefabUtility.IsAnyPrefabInstanceRoot((GameObject)p.Value)))
            {
                PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(item.Value);
                if (modifications == null) continue;
                var set = new ModificationSet { Container = item.Key };
                bool relevant = false;
                foreach (PropertyModification modification in modifications)
                {
                    // Неизвестный null target нельзя безопасно перенести на другой риг.
                    var copy = new Modification
                    {
                        Target = Reference(modification.target, objects),
                        Property = modification.propertyPath,
                        Text = modification.value,
                        Value = Reference(modification.objectReference, objects)
                    };
                    set.Items.Add(copy);
                    if (IsSource(copy.Target)) { consumer.Covered.Add(copy.Target.Id); relevant = true; }
                    if (IsSource(copy.Value)) { consumer.Covered.Add(copy.Value.Id); relevant = true; }
                }
                if (relevant) consumer.Modifications.Add(set);
            }
        }

        private void CaptureAsset(Consumer consumer)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(consumer.Path))
            {
                if (!asset) continue;
                int before = consumer.Fields.Count;
                if (!TryIdentity(asset, out Identity id)) throw Fail(consumer.Path, "нет fileID subasset");
                CaptureFields(consumer, id.Id.ToString(), asset, null);
                if (!(asset is ScriptableObject) && consumer.Fields.Count != before)
                    throw Fail(consumer.Path, "ссылки в asset этого типа не поддержаны: " + asset.GetType().FullName);
            }
        }

        private void CaptureFields(Consumer consumer, string key, Object container, Dictionary<string, Object> objects)
        {
            bool inherited = container is Component || container is GameObject;
            inherited = inherited && PrefabUtility.GetCorrespondingObjectFromSource(container) != null;
            var iterator = new SerializedObject(container).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference || Skip(iterator.propertyPath)) continue;
                // Значение, хранимое у предка, не становится override потомка ради remap.
                if (inherited && !iterator.prefabOverride) continue;
                Ref reference = Reference(iterator.objectReferenceValue, objects);
                if (!IsSource(reference)) continue;
                consumer.Fields.Add(new Field { Container = key, Property = iterator.propertyPath, Value = reference });
                consumer.Covered.Add(reference.Id);
            }
        }

        private Ref Reference(Object value, Dictionary<string, Object> local)
        {
            if (!value) return new Ref();
            if (EditorUtility.IsPersistent(value) && TryIdentity(value, out Identity id) && !string.IsNullOrEmpty(id.Guid))
                return new Ref { Id = id, Original = value };
            if (local != null)
                foreach (var item in local)
                    if (item.Value == value)
                    {
                        var reference = new Ref { LocalKey = item.Key };
                        // Локальный PPtr на наследуемое тело не содержит GUID предка в YAML.
                        // Сохраняем происхождение для запрета удаления действующей привязки,
                        // но восстанавливаем значение именно в локальном графе потребителя.
                        Object inherited = PrefabUtility.GetCorrespondingObjectFromSource(value);
                        var visited = new HashSet<Object>();
                        while (inherited && visited.Add(inherited))
                        {
                            if (TryIdentity(inherited, out Identity sourceId) && sources.ContainsKey(sourceId))
                            { reference.Id = sourceId; break; }
                            inherited = PrefabUtility.GetCorrespondingObjectFromSource(inherited);
                        }
                        return reference;
                    }
            throw new InvalidOperationException("Ссылка вне persistent asset и графа потребителя: " + value.name);
        }

        private bool IsSource(Ref reference) => sources.ContainsKey(reference.Id);

        private bool HasCapturedBinding(Consumer consumer, RawReference raw)
        {
            if (raw.Label == "m_CorrespondingSourceObject") return consumer.SourceLinks.Any(f => f.Value.Id.Equals(raw.Id));
            if (raw.Label == "target") return consumer.Modifications.SelectMany(s => s.Items).Any(m => m.Target.Id.Equals(raw.Id));
            if (raw.Label == "objectReference") return consumer.Modifications.SelectMany(s => s.Items).Any(m => m.Value.Id.Equals(raw.Id));
            return consumer.Fields.Any(f => f.Value.Id.Equals(raw.Id) &&
                (f.Property == raw.Label || f.Property.EndsWith("." + raw.Label, StringComparison.Ordinal) ||
                 raw.Label == "" && f.Property.Contains(".Array.data[")));
        }

        private bool IsRetired(Ref reference)
        {
            return IsSource(reference) && !replacements.ContainsKey(reference.Id) &&
                sources[reference.Id].Path == AvatarHandBases.PlayerBase &&
                sources[reference.Id].Key.StartsWith("$/Cyborg[0]/", StringComparison.Ordinal);
        }

        private bool CanRetireSourceLink(Consumer consumer, Field link, Dictionary<string, Object> currentGraph)
        {
            // Это удаление унаследованного объекта вместе с телом предка, а не remap
            // действующей ссылки. Происхождение контейнера снято до первой записи источников.
            return IsRetired(link.Value) && consumer.InheritedBodyObjects.Contains(link.Container) &&
                currentGraph != null && !currentGraph.ContainsKey(link.Container) &&
                !consumer.Fields.Any(f => f.Value.Id.Equals(link.Value.Id) || f.Container == link.Container) &&
                !consumer.Modifications.SelectMany(s => s.Items).Any(m => m.Value.Id.Equals(link.Value.Id));
        }

        private static void RequireRetiredMetadataAbsent(Consumer consumer, IEnumerable<Identity> identities)
        {
            Identity[] retired = identities.Distinct().ToArray();
            if (retired.Length == 0) return;
            string nativeText = File.ReadAllText(consumer.Path);
            foreach (Identity id in retired)
            {
                // Читаем только native output. Исчезнувший объект с оставшимся stripped PPtr
                // не считается завершённым удалением и не выдаёт доказательство для guard.
                if (Regex.IsMatch(nativeText, ReferencePattern(id)))
                    throw Fail(consumer.Path, "после native save осталось metadata удалённого тела: " + id);
            }
        }

        private static string ReferencePattern(Identity id) => @"\{fileID:\s*" + id.Id +
            @",\s*guid:\s*" + Regex.Escape(id.Guid) + @",\s*type:\s*\d+\}";

        private Object Resolve(Ref reference, Dictionary<string, Object> local, string path, bool metadataTarget)
        {
            if (reference.LocalKey != null)
            {
                // При глобальном preflight локальные ссылки ещё не загружены.
                if (local == null) return null;
                if (!local.TryGetValue(reference.LocalKey, out Object value)) throw Fail(path, "исчез локальный объект " + reference.LocalKey);
                return value;
            }
            if (IsSource(reference))
            {
                if (replacements.TryGetValue(reference.Id, out Object value) && value) return value;
                if (metadataTarget && IsRetired(reference)) return null;
                throw Fail(path, "не найден тот же смысловой объект " + reference.Id + " " + sources[reference.Id].Key);
            }
            if (reference.Original) return reference.Original;
            if (reference.Id.Id == 0) return null;
            // Не подменяем потерянный persistent object объектом с похожим именем.
            string sourcePath = AssetDatabase.GUIDToAssetPath(reference.Id.Guid);
            IEnumerable<Object> candidates = sourcePath.EndsWith(".prefab", StringComparison.Ordinal)
                ? (IEnumerable<Object>)Objects(RequirePrefab(sourcePath)).Values : AssetDatabase.LoadAllAssetsAtPath(sourcePath);
            Object found = candidates.FirstOrDefault(a => TryIdentity(a, out Identity id) && id.Equals(reference.Id));
            if (!found) throw Fail(path, "потерян неизменяемый объект " + reference.Id);
            return found;
        }

        private void ApplyPrefab(Consumer consumer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(consumer.Path);
            try
            {
                var objects = Objects(root);
                bool changed = ApplyFields(consumer, objects);
                foreach (Field link in consumer.RetiredSourceLinks)
                {
                    if (!CanRetireSourceLink(consumer, link, objects))
                        throw Fail(consumer.Path, "не подтверждено исчезновение наследуемого source-link: " + link.Container);
                    // Save нужен также для удаления obsolete stripped metadata с диска.
                    changed = true;
                }
                foreach (ModificationSet set in consumer.Modifications)
                {
                    GameObject container = RequireContainer(objects, set.Container, consumer.Path) as GameObject;
                    if (!container || !PrefabUtility.IsAnyPrefabInstanceRoot(container))
                        throw Fail(consumer.Path, "утрачен prefab instance root " + set.Container);
                    PropertyModification[] expected = Materialize(consumer, set, objects);
                    PropertyModification[] current = PrefabUtility.GetPropertyModifications(container) ?? Array.Empty<PropertyModification>();
                    if (SameModifications(current, expected)) continue;
                    PrefabUtility.SetPropertyModifications(container, expected);
                    report.AppendLine(consumer.Path + ": overrides " + set.Container + ", записей " + current.Length + " -> " + expected.Length);
                    changed = true;
                }
                if (!changed && consumer.RetiredTargets.Count != 0)
                {
                    // Unity может уже скрыть obsolete overrides в API, сохранив их в YAML.
                    // В этом случае native save нужен для физического удаления metadata.
                    string nativeText = File.ReadAllText(consumer.Path);
                    changed = consumer.RetiredTargets.Any(id => Regex.IsMatch(nativeText, ReferencePattern(id)));
                }
                if (changed)
                {
                    string guid = AssetDatabase.AssetPathToGUID(consumer.Path);
                    if (consumer.Path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal))
                        AvatarMaintenanceTools.SavePrefab(root, consumer.Path);
                    else
                    {
                        AvatarScopedActions.SetCanonicalAssetId(root, consumer.Path);
                        if (!PrefabUtility.SaveAsPrefabAsset(root, consumer.Path)) throw Fail(consumer.Path, "Unity не сохранила prefab");
                    }
                    if (AssetDatabase.AssetPathToGUID(consumer.Path) != guid) throw Fail(consumer.Path, "изменился GUID потребителя");
                    if (root.GetComponent<Mirror.NetworkIdentity>() &&
                        VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(consumer.Path) !=
                        Mirror.NetworkIdentity.AssetGuidToUint(new Guid(guid)))
                        throw Fail(consumer.Path, "канонический Mirror assetId не записан");
                    if (consumer.RetiredSourceLinks.Count != 0)
                        report.AppendLine(consumer.Path + ": удалено наследуемых source-links тела: " + consumer.RetiredSourceLinks.Count);
                    report.AppendLine("Native remap: " + consumer.Path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.ImportAsset(consumer.Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private void ApplyAsset(Consumer consumer)
        {
            var objects = AssetObjects(consumer.Path);
            if (!ApplyFields(consumer, objects)) return;
            foreach (string key in consumer.Fields.Select(f => f.Container).Distinct())
                AssetDatabase.SaveAssetIfDirty(RequireContainer(objects, key, consumer.Path));
            report.AppendLine("Native asset remap: " + consumer.Path);
        }

        private bool ApplyFields(Consumer consumer, Dictionary<string, Object> objects)
        {
            bool changed = false;
            foreach (Field field in consumer.Fields)
            {
                Object container = RequireContainer(objects, field.Container, consumer.Path);
                var serialized = new SerializedObject(container);
                SerializedProperty property = serialized.FindProperty(field.Property);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    throw Fail(consumer.Path, "исчезло поле " + field.Container + "." + field.Property);
                Object expected = Resolve(field.Value, objects, consumer.Path, false);
                if (property.objectReferenceValue == expected) continue;
                property.objectReferenceValue = expected;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                TryIdentity(expected, out Identity mapped);
                report.AppendLine(consumer.Path + ": поле " + field.Container + "." + field.Property + ": " + field.Value.Id + " -> " + mapped);
                changed = true;
            }
            return changed;
        }

        private PropertyModification[] Materialize(Consumer consumer, ModificationSet set, Dictionary<string, Object> objects)
        {
            var result = new List<PropertyModification>();
            foreach (Modification item in set.Items)
            {
                if (IsRetired(item.Target))
                {
                    consumer.RetiredTargets.Add(item.Target.Id);
                    continue;
                }
                result.Add(new PropertyModification
                {
                    target = Resolve(item.Target, objects, consumer.Path, true), propertyPath = item.Property,
                    value = item.Text, objectReference = Resolve(item.Value, objects, consumer.Path, false)
                });
            }
            return result.ToArray();
        }

        private void Verify(Consumer consumer)
        {
            GameObject root = null;
            try
            {
                Dictionary<string, Object> objects;
                if (consumer.Path.EndsWith(".prefab", StringComparison.Ordinal))
                {
                    root = PrefabUtility.LoadPrefabContents(consumer.Path);
                    objects = Objects(root);
                }
                else objects = AssetObjects(consumer.Path);
                if (AssetDatabase.AssetPathToGUID(consumer.Path) != consumer.Guid) throw Fail(consumer.Path, "GUID изменён при readback");
                if (root)
                {
                    var savedObjects = Objects(RequirePrefab(consumer.Path));
                    foreach (var original in consumer.OriginalObjects)
                    {
                        // Только намеренно удалённое наследуемое тело может исчезнуть из
                        // потребителя. Его действующие поля/metadata всё равно проверяются ниже.
                        if (!savedObjects.TryGetValue(original.Key, out Object saved))
                        {
                            if (consumer.InheritedBodyObjects.Contains(original.Key)) continue;
                            throw Fail(consumer.Path, "исчез собственный объект потребителя " + original.Key);
                        }
                        if (TryIdentity(saved, out Identity current) && original.Value.Guid == consumer.Guid &&
                            !current.Equals(original.Value)) throw Fail(consumer.Path, "изменился собственный fileID " + original.Key);
                    }
                }
                foreach (Field field in consumer.Fields)
                {
                    Object container = RequireContainer(objects, field.Container, consumer.Path);
                    SerializedProperty property = new SerializedObject(container).FindProperty(field.Property);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.objectReferenceValue != Resolve(field.Value, objects, consumer.Path, false))
                        throw Fail(consumer.Path, "readback поля не совпал: " + field.Container + "." + field.Property);
                }
                foreach (Field link in consumer.SourceLinks)
                {
                    if (consumer.RetiredSourceLinks.Contains(link))
                    {
                        if (!CanRetireSourceLink(consumer, link, objects))
                            throw Fail(consumer.Path, "readback удаления source-link не совпал: " + link.Container);
                        continue;
                    }
                    Object container = RequireContainer(objects, link.Container, consumer.Path);
                    Object actual = PrefabUtility.GetCorrespondingObjectFromSource(container);
                    if (actual != Resolve(link.Value, objects, consumer.Path, false))
                        throw Fail(consumer.Path, "readback prefab source link не совпал: " + link.Container);
                }
                foreach (ModificationSet set in consumer.Modifications)
                {
                    Object container = RequireContainer(objects, set.Container, consumer.Path);
                    var actual = PrefabUtility.GetPropertyModifications(container) ?? Array.Empty<PropertyModification>();
                    var expected = Materialize(consumer, set, objects);
                    if (!SameModifications(actual, expected))
                        throw Fail(consumer.Path, "readback overrides не совпал: " + set.Container +
                            "\n" + DescribeModificationDifference(actual, expected));
                }
                RequireRetiredMetadataAbsent(consumer, consumer.RetiredTargets.Concat(consumer.RetiredSourceLinks.Select(f => f.Value.Id)));
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
        }

        private IEnumerable<Consumer> OrderedConsumers()
        {
            var remaining = new List<Consumer>(consumers);
            while (remaining.Count != 0)
            {
                Consumer next = remaining.FirstOrDefault(c => !AssetDatabase.GetDependencies(c.Path, true)
                    .Any(p => p != c.Path && remaining.Any(other => other.Path == p)));
                if (next == null) throw new InvalidOperationException("Цикл зависимостей внешних prefab consumers.");
                remaining.Remove(next);
                yield return next;
            }
        }

        private void RefreshReplacements()
        {
            foreach (var target in targetGuids)
            {
                var objects = Objects(RequirePrefab(target.Key));
                foreach (var old in sources.Where(p => p.Value.Path == target.Key))
                    if (objects.TryGetValue(old.Value.Key, out Object value)) replacements[old.Key] = value;
                    else replacements.Remove(old.Key);
            }
        }

        private static Dictionary<string, Object> AssetObjects(string path)
        {
            var result = new Dictionary<string, Object>(StringComparer.Ordinal);
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset && TryIdentity(asset, out Identity id)) result.Add(id.Id.ToString(), asset);
            return result;
        }

        private static Dictionary<string, Object> Objects(GameObject root)
        {
            var result = new Dictionary<string, Object>(StringComparer.Ordinal);
            Add(root.transform, "$");
            return result;

            void Add(Transform transform, string path)
            {
                result.Add(path + "/GameObject", transform.gameObject);
                var ordinals = new Dictionary<Type, int>();
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (!component) continue;
                    Type type = component.GetType();
                    ordinals.TryGetValue(type, out int ordinal);
                    ordinals[type] = ordinal + 1;
                    result.Add(path + "/" + type.FullName + "[" + ordinal + "]", component);
                }
                var siblings = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Transform child in transform)
                {
                    siblings.TryGetValue(child.name, out int ordinal);
                    siblings[child.name] = ordinal + 1;
                    Add(child, path + "/" + Uri.EscapeDataString(child.name) + "[" + ordinal + "]");
                }
            }
        }

        private static bool SameModifications(PropertyModification[] first, PropertyModification[] second)
        {
            // Unity вправе изменить порядок записей; количество и все четыре поля сохраняются.
            var remaining = new List<PropertyModification>(second);
            foreach (PropertyModification item in first)
            {
                int index = remaining.FindIndex(other => item.target == other.target && item.propertyPath == other.propertyPath &&
                    item.value == other.value && item.objectReference == other.objectReference);
                if (index < 0) return false;
                remaining.RemoveAt(index);
            }
            return remaining.Count == 0;
        }

        private static string DescribeModificationDifference(PropertyModification[] actual, PropertyModification[] expected)
        {
            // Диагностика сохраняет точные значения, включая null/пустую строку. Guard не смягчается.
            var missing = new List<PropertyModification>(expected);
            var extra = new List<PropertyModification>();
            foreach (var item in actual)
            {
                int index = missing.FindIndex(other => item.target == other.target && item.propertyPath == other.propertyPath &&
                    item.value == other.value && item.objectReference == other.objectReference);
                if (index < 0) extra.Add(item);
                else missing.RemoveAt(index);
            }
            string DescribeObject(Object value)
            {
                if (!value) return "<null>";
                if (TryIdentity(value, out Identity id)) return id + ":" + value.GetType().FullName + ":" + value.name;
                return "instance:" + value.GetEntityId() + ":" + value.GetType().FullName + ":" + value.name;
            }
            string DescribeItem(PropertyModification item) => "target=" + DescribeObject(item.target) +
                "; property=" + Newtonsoft.Json.JsonConvert.SerializeObject(item.propertyPath) +
                "; value=" + Newtonsoft.Json.JsonConvert.SerializeObject(item.value) +
                "; objectReference=" + DescribeObject(item.objectReference);
            return "expected=" + expected.Length + "; actual=" + actual.Length +
                "; missing=" + missing.Count + "; extra=" + extra.Count + "\n" +
                string.Join("\n", missing.Select(m => "MISSING " + DescribeItem(m)).Concat(extra.Select(m => "EXTRA " + DescribeItem(m))));
        }

        private static bool Skip(string property) => property == "m_CorrespondingSourceObject" ||
            property == "m_PrefabInstance" || property == "m_PrefabAsset" || property == "m_Script" || property == "m_GameObject";

        private static bool TryIdentity(Object value, out Identity id)
        {
            if (value && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long fileId))
            { id = new Identity(guid, fileId); return !string.IsNullOrEmpty(guid); }
            id = default;
            return false;
        }

        private static Object RequireContainer(Dictionary<string, Object> objects, string key, string path)
        {
            if (!objects.TryGetValue(key, out Object value) || !value) throw Fail(path, "исчез объект потребителя " + key);
            return value;
        }

        private static GameObject RequirePrefab(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) throw Fail(path, "prefab не загружен");
            return root;
        }

        private static void RequireWritablePrefab(string path)
        {
            if (path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal)) return;
            const string samples = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/";
            if (new[] { "Shotgun", "Machinegun", "Gun", "Grenade" }.Any(name => path == samples + name + ".prefab")) return;
            throw Fail(path, "внешний prefab вне разрешённой области remap");
        }

        private static InvalidOperationException Fail(string path, string message) =>
            new InvalidOperationException("External prefab remap: " + path + ": " + message);

        private readonly struct Identity : IEquatable<Identity>
        {
            internal readonly string Guid;
            internal readonly long Id;
            internal Identity(string guid, long id) { Guid = guid; Id = id; }
            public bool Equals(Identity other) => Guid == other.Guid && Id == other.Id;
            public override bool Equals(object value) => value is Identity other && Equals(other);
            public override int GetHashCode() => (Guid == null ? 0 : Guid.GetHashCode()) * 397 ^ Id.GetHashCode();
            public override string ToString() => Guid + "/" + Id;
        }

        private sealed class Source { internal string Path; internal string Key; }
        private sealed class Ref { internal Identity Id; internal Object Original; internal string LocalKey; }
        private sealed class Field { internal string Container; internal string Property; internal Ref Value; }
        private sealed class Modification { internal Ref Target; internal string Property; internal string Text; internal Ref Value; }
        private sealed class ModificationSet { internal string Container; internal readonly List<Modification> Items = new List<Modification>(); }
        private sealed class RawReference { internal Identity Id; internal string Label; }
        private sealed class PreservedBrokenConsumer
        {
            internal string Path;
            internal byte[] AssetBytes;
            internal byte[] MetaBytes;
            internal RawReference[] Raw;
        }
        private sealed class Consumer
        {
            internal string Path;
            internal string Guid;
            internal List<RawReference> Raw;
            internal bool Verified;
            internal Dictionary<string, Identity> OriginalObjects;
            internal readonly HashSet<string> InheritedBodyObjects = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<Field> Fields = new List<Field>();
            internal readonly List<Field> SourceLinks = new List<Field>();
            internal readonly HashSet<Field> RetiredSourceLinks = new HashSet<Field>();
            internal readonly List<ModificationSet> Modifications = new List<ModificationSet>();
            internal readonly HashSet<Identity> Covered = new HashSet<Identity>();
            internal readonly HashSet<Identity> RetiredTargets = new HashSet<Identity>();
        }
    }
}

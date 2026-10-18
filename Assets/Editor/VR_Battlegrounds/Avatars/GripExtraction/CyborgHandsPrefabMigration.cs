using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars.Workbench;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Однократный перенос существующих разрешённых аватаров на чистую базу поз.
    /// Источники сохраняются до изменения предков; YAML не редактируется.
    /// </summary>
    public static class CyborgHandsPrefabMigration
    {
        private const string LegacyBase = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        private const string ArchivedBase = "Assets/Prefabs/Player/Unused/PlayerBase_SdkHands.prefab";
        private static readonly string[] TreePaths =
        {
            AvatarHandBases.PlayerBase, GripTargetProfile.CanonicalCyborgHandsBasePath,
            GripTargetProfile.CyborgAvatarPath, GripTargetProfile.MefCyborgPlayerPath,
            GripTargetProfile.MefCyborgPlayerBlackPath,
            "Assets/Prefabs/Player/Ghost/GhostAvatar.prefab"
        };

        /// <summary>Диагностика текущих источников; ничего не сохраняет и не принимает качество.</summary>
        public static string Describe()
        {
            var report = new StringBuilder();
            foreach (string path in TreePaths)
            {
                var root = RequirePrefab(path);
                var avatar = RequireAvatar(root);
                var parent = PrefabUtility.GetCorrespondingObjectFromSource(root);
                report.AppendLine(path + ": UnityParent=" + AssetDatabase.GetAssetPath(parent) +
                    "; UxrParent=" + AssetDatabase.GetAssetPath(avatar.ParentPrefab) +
                    "; ownPoses=" + avatar.GetHandPoses().Count() +
                    "; resolvedPoses=" + avatar.GetAllHandPoses().Count() +
                    "; rigTransforms=" + avatar.AvatarRig.Transforms.Count() +
                    "; children=" + string.Join(",", root.transform.Cast<Transform>().Select(t => t.name)));
            }
            report.AppendLine("Входящие внешние PPtr: " + IncomingReferences.Capture(TreePaths).Count);
            return report.ToString();
        }

        /// <summary>Точный план внешних native-переносов до первой записи префабов.</summary>
        public static string DescribeReferencePlan()
        {
            var remap = CyborgPrefabReferenceRemap.Capture(TreePaths);
            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                sourcePaths = remap.TargetPaths,
                consumerPaths = remap.ConsumerPaths,
                description = remap.Description
            }, Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Перестраивает только заданное дерево. Данные тела, skinning, калибровки и настроек
        /// берутся из снимков конечных аватаров, а не из заново добавленного MEF_Rig.
        /// </summary>
        public static string Rebuild()
        {
            RequireIdleEditor();
            // SDK использует такую паузу при batch editing. Preview contents не должны
            // менять сохранённые UID/provenance. Восстановление preference — после cleanup/rollback.
            using (new CyborgPrefabReferenceRemap.EditorIdentityScope()) return RebuildWithStableIdentity();
        }

        private static string RebuildWithStableIdentity()
        {
            foreach (string path in TreePaths) RequirePrefab(path);
            if (AssetDatabase.AssetPathToGUID(TreePaths[1]) != GripTargetProfile.CyborgBaseGuid)
                throw new InvalidOperationException("GUID CyborgHandsBase не совпадает; источники не изменены.");

            var source = RequireAvatar(RequirePrefab(TreePaths[1]));
            UxrHandPoseAsset[] poses = source.GetAllHandPoses().ToArray();
            UxrHandPoseAsset defaultPose = source.DefaultHandPose;
            if (poses.Length == 0) throw new InvalidOperationException("У базы нет разрешённых поз кисти.");
            foreach (string path in TreePaths.Skip(2))
            {
                var extra = RequireAvatar(RequirePrefab(path)).GetAllHandPoses()
                    .Where(p => !poses.Any(b => b.name == p.name && b == p)).ToArray();
                if (extra.Length > 0)
                    throw new InvalidOperationException(path + ": собственные позы требуют явного переноса: " +
                        string.Join(",", extra.Select(p => p.name)));
            }

            var referenceRemap = CyborgPrefabReferenceRemap.Capture(TreePaths);
            var backupPaths = TreePaths.Concat(referenceRemap.ConsumerPaths)
                .Concat(new[] { LegacyBase, ArchivedBase, CyborgPrefabReferenceRemap.BrokenRegistryPath }).Distinct().ToArray();
            using (var backup = new SourceBackup(backupPaths))
            {
                var scene = EditorSceneManager.NewPreviewScene();
                string registryRemovalProof = "Native cleanup ещё не завершён.";
                string result = null;
                try
                {
                    registryRemovalProof = CyborgPrefabReferenceRemap.DeleteConfirmedBrokenRegistry();
                    backup.Record("Native cleanup obsolete registry:\n" + registryRemovalProof);
                    referenceRemap = CyborgPrefabReferenceRemap.Capture(TreePaths);
                    var missingBackup = referenceRemap.ConsumerPaths.Except(backupPaths, StringComparer.Ordinal).ToArray();
                    if (missingBackup.Length != 0)
                        throw new InvalidOperationException("Потребители вне исходного backup: " + string.Join(", ", missingBackup));
                    var incoming = IncomingReferences.Capture(TreePaths);
                    // Все снимки потомков снимаются раньше первой записи предка. OutermostRoot сохраняет
                    // вложенный MEF_Rig со всеми graft overrides и модельные prefab connections.
                    var roots = TreePaths.Skip(1).ToDictionary(p => p, p => Snapshot(p, scene));
                    var defaults = TreePaths.ToDictionary(p => p, p => RequireAvatar(RequirePrefab(p)).DefaultHandPose);
                    // Самостоятельный PlayerBase не требует смены родителя. Редактируем его
                    // native contents с исходным path/GUID, без распаковки копии. Общие объекты
                    // могут иметь одинаковые имена (IndexThumbProximity левой и правой руки).
                    var playerBaseContents = PrefabUtility.LoadPrefabContents(TreePaths[0]);
                    try
                    {
                        CleanPlayerBase(playerBaseContents);
                        Save(playerBaseContents, TreePaths[0]);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(playerBaseContents); }

                    var cleanBase = (GameObject)PrefabUtility.InstantiatePrefab(RequirePrefab(TreePaths[0]), scene);
                    cleanBase.name = Path.GetFileNameWithoutExtension(TreePaths[1]);
                    ConfigureAvatar(cleanBase, TreePaths[1], TreePaths[0], poses, defaultPose);
                    CopyCommonComponentIdentity(roots[TreePaths[1]], cleanBase);
                    Save(cleanBase, TreePaths[1]);
                    AssetDatabase.ImportAsset(TreePaths[1], ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var savedBase = PrefabUtility.LoadPrefabContents(TreePaths[1]);
                    try { CopyCommonComponentIdentity(roots[TreePaths[1]], savedBase, true); }
                    finally { PrefabUtility.UnloadPrefabContents(savedBase); }

                    ConvertAndSave(roots[TreePaths[2]], TreePaths[2], TreePaths[1], defaults[TreePaths[2]]);
                    RemoveUnusedCyborgBody(roots[TreePaths[3]]);
                    ConvertAndSave(roots[TreePaths[3]], TreePaths[3], TreePaths[1], defaults[TreePaths[3]]);
                    RemoveUnusedCyborgBody(roots[TreePaths[4]]);
                    ConvertAndSave(roots[TreePaths[4]], TreePaths[4], TreePaths[3], defaults[TreePaths[4]]);
                    // Ghost наследует Controllers: его разрешённый снимок нужен до изменения
                    // IDs предка, иначе можно потерять собственные ghost overrides и stripped refs.
                    ConvertAndSave(roots[TreePaths[5]], TreePaths[5], TreePaths[2], defaults[TreePaths[5]]);

                    RequireCleanBase(TreePaths[0]);
                    RequireCleanBase(TreePaths[1]);
                    RequireParent(TreePaths[1], TreePaths[0]);
                    RequireParent(TreePaths[2], TreePaths[1]);
                    RequireParent(TreePaths[3], TreePaths[1]);
                    RequireParent(TreePaths[4], TreePaths[3]);
                    RequireParent(TreePaths[5], TreePaths[2]);
                    referenceRemap.Apply();
                    incoming.RequireResolvable(referenceRemap);
                    ArchiveLegacyBase();
                    string description = Describe();
                    result = "Native prefab migration сохранена. Проверка качества пользователем ещё требуется.\n" +
                        "Резервная копия: " + backup.DirectoryPath + "\n" + registryRemovalProof + "\n" + referenceRemap.Description + "\n" + description;
                    backup.Record("Авторинг завершён; пользовательская проверка качества ожидается.\n" +
                        registryRemovalProof + "\n" + referenceRemap.Description + "\n" + description);
                }
                catch (Exception error)
                {
                    backup.Record("Отказ авторинга, собственные outputs возвращаются при выходе:\n" + error +
                        "\nNative cleanup obsolete registry:\n" + registryRemovalProof +
                        "\nNative remap до отказа:\n" + referenceRemap.Description);
                    throw new InvalidOperationException("Миграция остановлена. Диагностика и исходники: " + backup.DirectoryPath, error);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                // Commit транзакции — только после всех диагностик и удаления preview contents.
                backup.Complete();
                return result;
            }
        }

        /// <summary>Переподчинение выбранного варианта через Unity API с защитой внешних fileID.</summary>
        public static void ReparentVariant(string variantPath, string basePath)
        {
            RequireIdleEditor();
            var parent = RequirePrefab(basePath);
            for (var current = parent; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
                if (AssetDatabase.GetAssetPath(current) == variantPath)
                    throw new InvalidOperationException("Новый родитель образует цикл наследования.");
            var avatar = RequireAvatar(RequirePrefab(variantPath));
            var inherited = RequireAvatar(parent).GetAllHandPoses().ToArray();
            var poses = avatar.GetAllHandPoses().Where(p => !inherited.Contains(p)).ToArray();
            var defaultPose = avatar.DefaultHandPose;
            var incoming = IncomingReferences.Capture(new[] { variantPath });
            using (new CyborgPrefabReferenceRemap.EditorIdentityScope())
            using (var backup = new SourceBackup(new[] { variantPath }))
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = Snapshot(variantPath, scene);
                    Convert(root, parent);
                    ConfigureAvatar(root, variantPath, basePath, poses, defaultPose);
                    Save(root, variantPath);
                    incoming.RequireResolvable();
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                backup.Complete();
            }
        }

        private static GameObject Snapshot(string path, UnityEngine.SceneManagement.Scene scene)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(RequirePrefab(path), scene);
            // Для Variant один OutermostRoot оставляет connection к его base. Снимаем
            // каждый уровень только на корне; вложенный MEF_Rig не распаковываем.
            while (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            return root;
        }

        private static void CopyCommonComponentIdentity(GameObject original, GameObject cleanBase, bool verify = false)
        {
            // Чистая база создаётся от PlayerBase, но сохранённые ID её общих компонентов
            // принадлежат прежней CyborgHandsBase. Копируем только совпавшие компоненты,
            // не возвращая удалённые модели и не генерируя новые идентификаторы.
            string Key(UltimateXR.Core.Components.UxrComponent component, GameObject root)
            {
                string Hierarchy(Transform node)
                {
                    if (node == root.transform) return "$";
                    int ordinal = 0;
                    for (int i = 0; i < node.GetSiblingIndex(); i++)
                        if (node.parent.GetChild(i).name == node.name) ordinal++;
                    return Hierarchy(node.parent) + "/" + Uri.EscapeDataString(node.name) + "[" + ordinal + "]";
                }
                var peers = component.GetComponents(component.GetType());
                return Hierarchy(component.transform) + "/" +
                    component.GetType().FullName + "[" + Array.IndexOf(peers, component) + "]";
            }
            var source = original.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true)
                .ToDictionary(c => Key(c, original), c => c, StringComparer.Ordinal);
            foreach (var component in cleanBase.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true))
            {
                if (!source.TryGetValue(Key(component, cleanBase), out var old))
                    throw new InvalidOperationException("Не найден исходный UID общего компонента: " + Key(component, cleanBase));
                var serialized = new SerializedObject(old);
                string text = serialized.FindProperty("_uxrUniqueId").stringValue;
                if (!Guid.TryParse(text, out Guid id) || id == Guid.Empty)
                    throw new InvalidOperationException("Исходный UID пуст или некорректен: " + Key(old, original));
                bool isInPrefab = serialized.FindProperty("__isInPrefab").boolValue;
                string prefabGuid = serialized.FindProperty("__prefabGuid").stringValue;
                if (verify)
                {
                    var actual = new SerializedObject(component);
                    if (!Guid.TryParse(actual.FindProperty("_uxrUniqueId").stringValue, out Guid savedId) || savedId != id ||
                        actual.FindProperty("__isInPrefab").boolValue != isInPrefab ||
                        actual.FindProperty("__prefabGuid").stringValue != prefabGuid)
                        throw new InvalidOperationException("Native readback UID/provenance базы не совпал: " + Key(component, cleanBase));
                }
                else
                {
                    component.SetEditorUniqueId(id, isInPrefab, prefabGuid);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }

        private static void ConvertAndSave(GameObject root, string path, string parent, UxrHandPoseAsset defaultPose)
        {
            Convert(root, RequirePrefab(parent));
            ConfigureAvatar(root, path, parent, Array.Empty<UxrHandPoseAsset>(), defaultPose);
            Save(root, path);
        }

        private static void Convert(GameObject root, GameObject parent)
        {
            // Unity 6000.4 запрещает Convert для корня любой preview scene, даже после
            // полного снятия ancestry connection. Собственный transient snapshot временно
            // помещаем под контейнер; настоящий Prefab Stage таким способом не меняем.
            if (EditorUtility.IsPersistent(root) || PrefabUtility.IsPartOfPrefabInstance(root) ||
                !EditorSceneManager.IsPreviewScene(root.scene))
                throw new InvalidOperationException("Convert принимает только собственный распакованный preview snapshot.");
            var transform = root.transform;
            Transform originalParent = transform.parent;
            Vector3 position = transform.localPosition;
            Quaternion rotation = transform.localRotation;
            Vector3 scale = transform.localScale;
            int sibling = transform.GetSiblingIndex();
            var host = EditorUtility.CreateGameObjectWithHideFlags("CyborgMigrationHost", HideFlags.HideAndDontSave);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, root.scene);
            try
            {
                transform.SetParent(host.transform, false);
                PrefabUtility.ConvertToPrefabInstance(root, parent, new ConvertToPrefabInstanceSettings
                {
                    objectMatchMode = ObjectMatchMode.ByHierarchy,
                    componentsNotMatchedBecomesOverride = true,
                    gameObjectsNotMatchedBecomesOverride = true,
                    recordPropertyOverridesOfMatches = true,
                    changeRootNameToAssetName = false,
                    logInfo = false
                }, InteractionMode.AutomatedAction);
            }
            finally
            {
                transform.SetParent(originalParent, false);
                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
                transform.SetSiblingIndex(sibling);
                Object.DestroyImmediate(host);
            }
        }

        private static void ConfigureAvatar(GameObject root, string path, string parent,
            IReadOnlyList<UxrHandPoseAsset> poses, UxrHandPoseAsset defaultPose)
        {
            var avatar = new SerializedObject(RequireAvatar(root));
            avatar.FindProperty("_prefabGuid").stringValue = AssetDatabase.AssetPathToGUID(path);
            avatar.FindProperty("_parentPrefab").objectReferenceValue = RequirePrefab(parent);
            var list = avatar.FindProperty("_handPoses");
            list.arraySize = poses.Count;
            for (int i = 0; i < poses.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = poses[i];
            avatar.FindProperty("_defaultHandPose").objectReferenceValue = defaultPose;
            avatar.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CleanPlayerBase(GameObject root)
        {
            var body = root.transform.Find("Cyborg");
            if (body)
            {
                var removed = new HashSet<Object>(body.GetComponentsInChildren<Component>(true).Select(c => (Object)c));
                foreach (var transform in body.GetComponentsInChildren<Transform>(true)) removed.Add(transform.gameObject);
                foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c != null && !removed.Contains(c)))
                {
                    var serialized = new SerializedObject(component);
                    var iterator = serialized.GetIterator();
                    while (iterator.Next(true))
                        if (iterator.propertyType == SerializedPropertyType.ObjectReference && removed.Contains(iterator.objectReferenceValue))
                            iterator.objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Object.DestroyImmediate(body.gameObject);
            }
            var avatar = RequireAvatar(root);
            avatar.AvatarRig.ClearRigElements();
            var data = new SerializedObject(avatar);
            data.FindProperty("_rigInfo").boxedValue = new UltimateXR.Avatar.Rig.UxrAvatarRigInfo();
            data.FindProperty("_handPoses").arraySize = 0;
            data.FindProperty("_avatarRenderers").arraySize = 0;
            data.FindProperty("_defaultHandPose").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c != null))
            {
                var serialized = new SerializedObject(component);
                // Калибровка тела не принадлежит общим сенсорам. Числа конечных аватаров
                // сохраняются в снятых ранее snapshots; заново их здесь не вычисляем.
                foreach (string field in new[] { "_leftCalibrationData", "_rightCalibrationData" })
                {
                    var property = serialized.FindProperty(field);
                    if (property != null && property.isArray) property.arraySize = 0;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void RemoveUnusedCyborgBody(GameObject root)
        {
            var body = root.transform.Find("Cyborg");
            if (!body) return;
            var removed = new HashSet<Object>(body.GetComponentsInChildren<Component>(true).Select(c => (Object)c));
            foreach (var transform in body.GetComponentsInChildren<Transform>(true)) removed.Add(transform.gameObject);
            foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c != null && !removed.Contains(c)))
            {
                var iterator = new SerializedObject(component).GetIterator();
                while (iterator.Next(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                        removed.Contains(iterator.objectReferenceValue))
                        throw new InvalidOperationException(root.name + ": действующая ссылка на старое тело " +
                            component.GetType().Name + "." + iterator.propertyPath + "; автоматическое удаление остановлено.");
            }
            Object.DestroyImmediate(body.gameObject);
        }

        private static void RequireCleanBase(string path)
        {
            var root = RequirePrefab(path);
            if (RequireAvatar(root).AvatarRig.Transforms.Any() || root.transform.Find("Cyborg") || root.transform.Find("MEF_Rig"))
                throw new InvalidOperationException(path + ": после сохранения в общей базе осталась привязка тела.");
        }

        private static void RequireParent(string path, string parent)
        {
            var root = RequirePrefab(path);
            if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root)) != parent ||
                AssetDatabase.GetAssetPath(RequireAvatar(root).ParentPrefab) != parent)
                throw new InvalidOperationException(path + ": Unity parent и UXR parent должны совпадать с " + parent);
        }

        private static void Save(GameObject root, string path)
        {
            root.name = Path.GetFileNameWithoutExtension(path);
            AvatarMaintenanceTools.SavePrefab(root, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void ArchiveLegacyBase()
        {
            if (!File.Exists(LegacyBase)) return;
            if (File.Exists(ArchivedBase)) throw new InvalidOperationException("Место архива SDK-базы уже занято.");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Player/Unused"))
                throw new InvalidOperationException("Нет подготовленной папки Unused.");
            string error = AssetDatabase.MoveAsset(LegacyBase, ArchivedBase);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }

        private static GameObject RequirePrefab(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) throw new InvalidOperationException("Нет исходного prefab: " + path);
            return root;
        }

        private static UxrAvatar RequireAvatar(GameObject root) => root.GetComponent<UxrAvatar>() ??
            throw new InvalidOperationException("Нет корневого UxrAvatar: " + root.name);

        private static void RequireIdleEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Миграция требует свободного Editor без Play, импорта и Prefab Stage.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Есть несохранённая сцена; миграция не начиналась.");
        }

        private sealed class SourceBackup : IDisposable
        {
            private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            private bool complete;
            internal string DirectoryPath { get; }

            internal SourceBackup(IEnumerable<string> paths)
            {
                DirectoryPath = "tasks/hand-rig-quality/reports/prefab-hierarchy/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                    "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                foreach (string path in paths.Distinct())
                {
                    if (File.Exists(path) && AssetDatabase.LoadAllAssetsAtPath(path).Any(a => a && EditorUtility.IsDirty(a)))
                        throw new InvalidOperationException("Исходный asset dirty: " + path);
                    foreach (string file in new[] { path, path + ".meta" })
                    {
                        files.Add(file, File.Exists(file) ? File.ReadAllBytes(file) : null);
                        if (files[file] == null) continue;
                        string saved = Path.Combine(DirectoryPath, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        File.WriteAllBytes(saved, files[file]);
                    }
                }
            }

            internal void Complete() => complete = true;

            internal void Record(string text)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(Path.Combine(DirectoryPath, "outcome.txt"), text + "\n", new UTF8Encoding(false));
            }

            public void Dispose()
            {
                if (complete) return;
                // Точный возврат исходных байтов после отказа собственной операции; это не YAML rewrite.
                foreach (var file in files)
                    if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                    else File.WriteAllBytes(file.Key, file.Value);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var changed = files.Where(file => file.Value == null ? File.Exists(file.Key) :
                    !File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value)).Select(file => file.Key).ToArray();
                if (changed.Length > 0)
                    throw new InvalidOperationException("Не подтверждён точный возврат outputs: " + string.Join(",", changed) +
                        ". Исходные байты сохранены: " + DirectoryPath);
            }
        }

        private sealed class IncomingReferences
        {
            private readonly List<(string consumer, string guid, long id)> references = new List<(string, string, long)>();
            private Dictionary<(string, long), string> originalObjects;
            internal int Count => references.Count;

            internal static IncomingReferences Capture(IEnumerable<string> paths)
            {
                var outputs = new HashSet<string>(paths);
                var guids = outputs.Select(AssetDatabase.AssetPathToGUID).Where(g => !string.IsNullOrEmpty(g)).ToArray();
                var pattern = new Regex(@"\{fileID:\s*(-?\d+),\s*guid:\s*(" + string.Join("|", guids) + @"),\s*type:\s*\d+\}");
                var result = new IncomingReferences();
                foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !outputs.Contains(p) && (p.EndsWith(".prefab") || p.EndsWith(".asset") || p.EndsWith(".unity"))))
                {
                    // Новый снимок после разрешённого DeleteAsset: исторические пути AssetDatabase
                    // могут сохраняться в кэше. Исчезновение уже снятого consumer guard не разрешает.
                    if (!File.Exists(path)) continue;
                    // Только текстовые Unity assets; бинарные файлы не декодируем.
                    using (var reader = new StreamReader(path))
                    {
                        string first = reader.ReadLine();
                        if (first == null || !first.StartsWith("%YAML", StringComparison.Ordinal)) continue;
                        string line;
                        while ((line = reader.ReadLine()) != null)
                            foreach (Match match in pattern.Matches(line))
                            {
                                long id = long.Parse(match.Groups[1].Value);
                                if (id != 0 && id != 100100000)
                                    result.references.Add((path, match.Groups[2].Value, id));
                            }
                    }
                }
                // Исторически сломанная ссылка не должна ошибочно приписываться миграции.
                var available = result.ObjectIds();
                result.references.RemoveAll(r => !available.ContainsKey((r.guid, r.id)));
                result.originalObjects = available;
                return result;
            }

            private Dictionary<(string, long), string> ObjectIds()
            {
                var result = new Dictionary<(string, long), string>();
                foreach (string guid in references.Select(r => r.guid).Distinct())
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!root) continue;
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        string key = ObjectKey(transform, root.transform);
                        Add(transform.gameObject, key + "/GameObject");
                        var components = transform.GetComponents<Component>();
                        var ordinals = new Dictionary<Type, int>();
                        foreach (var component in components.Where(c => c != null))
                        {
                            Type type = component.GetType();
                            ordinals.TryGetValue(type, out int ordinal);
                            ordinals[type] = ordinal + 1;
                            Add(component, key + "/" + type.FullName + "#" + ordinal);
                        }
                    }
                }
                return result;

                void Add(Object value, string key)
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id)) result[(guid, id)] = key;
                }
            }

            private static string ObjectKey(Transform value, Transform root)
            {
                if (value == root) return "$";
                int ordinal = 0;
                for (int i = 0; i < value.GetSiblingIndex(); i++)
                    if (value.parent.GetChild(i).name == value.name) ordinal++;
                return ObjectKey(value.parent, root) + "/" + value.name + "#" + ordinal;
            }

            internal void RequireResolvable(CyborgPrefabReferenceRemap remap = null)
            {
                var available = ObjectIds();
                var lost = references.Where(r => (!available.TryGetValue((r.guid, r.id), out string key) ||
                    key != originalObjects[(r.guid, r.id)]) &&
                    (remap == null || !remap.CanResolveReference(r.consumer, r.guid, r.id))).Take(10).ToArray();
                if (lost.Length != 0)
                    throw new InvalidOperationException("Unity изменила fileID внешних ссылок; нужен отдельный точный remap, outputs возвращаются:\n" +
                        string.Join("\n", lost.Select(r => r.consumer + ": " + r.guid + "/" + r.id)));
            }
        }
    }
}

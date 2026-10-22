using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Переподчинение существующего аватара на базу кистей через Unity Prefab API: разрешённые позы
    /// сохраняются, внешние fileID проверяются, при отказе исходные байты возвращаются.
    /// Разовая миграция дерева CyborgHandsBase (2026-10-18, 9c79ccb58) из кода удалена; её история — в Git.
    /// </summary>
    public static class CyborgHandsPrefabMigration
    {
        /// <summary>Резервные копии исходных байтов (ignored, вне сборки); при отказе сюда указывает сообщение.</summary>
        private const string BackupRoot = "Logs/AvatarRebaseBackup/";

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
            using (new EditorIdentityScope())
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

        private static void Save(GameObject root, string path)
        {
            root.name = Path.GetFileNameWithoutExtension(path);
            AvatarMaintenanceTools.SavePrefab(root, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
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
                DirectoryPath = BackupRoot + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
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

            internal void RequireResolvable()
            {
                var available = ObjectIds();
                var lost = references.Where(r => !available.TryGetValue((r.guid, r.id), out string key) ||
                    key != originalObjects[(r.guid, r.id)]).Take(10).ToArray();
                if (lost.Length != 0)
                    throw new InvalidOperationException("Unity изменила fileID внешних ссылок; нужен отдельный точный remap, outputs возвращаются:\n" +
                        string.Join("\n", lost.Select(r => r.consumer + ": " + r.guid + "/" + r.id)));
            }
        }

        /// <summary>Штатная пауза SDK auto-ID на batch editing; preference восстанавливается последним.</summary>
        private sealed class EditorIdentityScope : IDisposable
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
    }
}

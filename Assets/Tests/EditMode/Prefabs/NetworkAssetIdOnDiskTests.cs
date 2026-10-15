using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// У каждого сетевого префаба на диске записан канонический <c>_assetId</c> —
    /// <see cref="NetworkIdentity.AssetGuidToUint" /> от GUID самого префаба.
    ///
    /// <para>
    /// Mirror сохраняет канон самого префаба и нулевой sceneId также в изолированной
    /// сцене <c>LoadPrefabContents</c>: сохранение и повторный импорт не должны менять файл.
    /// Ранее обычному префабу назначался 0, варианту — id базы, появлялся ложный sceneId.
    /// </para>
    ///
    /// <para>
    /// <c>NetworkAssetIdNormalizer</c> исправляет старые значения только в Assets/Prefabs.
    /// Проверка каталога сохраняет это требование; отдельная регрессия вне этой папки
    /// проверяет собственное authoring-поведение Mirror без маскировки постпроцессором.
    /// </para>
    /// </summary>
    public class NetworkAssetIdOnDiskTests
    {
        private static readonly string[] Roots = { "Assets/Prefabs" };

        private static readonly Regex OwnValue =
            new Regex(@"^  _assetId: (\d+)", RegexOptions.Multiline);

        private static readonly Regex OverrideValue =
            new Regex(@"propertyPath: _assetId\r?\n\s+value: (\d+)", RegexOptions.Multiline);

        /// <summary>
        /// Значение на диске: у обычного префаба — строка компонента, у варианта —
        /// переопределение в <c>m_Modifications</c>. Нет ни того, ни другого — null
        /// (вариант наследует id базы, то есть заведомо не свой).
        /// </summary>
        public static uint? ReadOnDisk(string path)
        {
            string text = File.ReadAllText(path);

            Match own = OwnValue.Match(text);
            if (own.Success) return uint.Parse(own.Groups[1].Value);

            Match over = OverrideValue.Match(text);
            if (over.Success) return uint.Parse(over.Groups[1].Value);

            return null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Prefab_contents_сохраняют_собственный_канон_без_перезаписи(bool variant)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null)
                Assert.Ignore("Нужен idle Edit Mode без PrefabStage.");

            string folder = "Assets/__NetworkAssetIdRegression_" + Guid.NewGuid().ToString("N");
            string regularPath = folder + "/Regular.prefab";
            string variantPath = folder + "/Variant.prefab";
            var ownedGuids = new Dictionary<string, string>();
            var ownedObjects = new List<GameObject>();
            var preview = default(Scene);
            int originalPreviewCount = EditorSceneManager.previewSceneCount;
            var originalActive = SceneManager.GetActiveScene();
            bool originalActiveDirty = originalActive.isDirty;
            var originalSelection = Selection.objects;
            try
            {
                Assert.That(Directory.Exists(folder) || File.Exists(folder + ".meta") || AssetDatabase.IsValidFolder(folder), Is.False);
                string folderGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                Assert.That(folderGuid, Is.Not.Null.And.Not.Empty);
                ownedGuids.Add(folder, folderGuid);
                preview = EditorSceneManager.NewPreviewScene();

                var regular = new GameObject("NetworkAssetIdRegressionRegular");
                ownedObjects.Add(regular);
                SceneManager.MoveGameObjectToScene(regular, preview);
                regular.AddComponent<NetworkIdentity>();
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(regular, regularPath, out saved);
                Assert.That(saved, Is.True);
                ownedGuids.Add(regularPath, AssetDatabase.AssetPathToGUID(regularPath));
                UnityEngine.Object.DestroyImmediate(regular);
                SeedCanonicalPrefab(regularPath);

                string path = regularPath;
                if (variant)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(regularPath), preview);
                    ownedObjects.Add(instance);
                    PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out saved);
                    Assert.That(saved, Is.True);
                    ownedGuids.Add(variantPath, AssetDatabase.AssetPathToGUID(variantPath));
                    UnityEngine.Object.DestroyImmediate(instance);
                    Assert.That(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath)), Is.EqualTo(PrefabAssetType.Variant));
                    SeedCanonicalPrefab(variantPath);
                    path = variantPath;
                    Assert.That(CanonicalPrefabId(path), Is.Not.EqualTo(CanonicalPrefabId(regularPath)));
                }

                uint canonical = CanonicalPrefabId(path);
                Assert.That(canonical, Is.Not.Zero);
                var onValidate = typeof(NetworkIdentity).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onValidate, Is.Not.Null);
                byte[] firstSave = null;
                byte[] firstImport = null;
                for (int cycle = 0; cycle < 2; ++cycle)
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var identity = contents.GetComponent<NetworkIdentity>();
                        AssertCanonicalIdentity(identity, canonical, "сразу после LoadPrefabContents");
                        onValidate.Invoke(identity, null);
                        AssertCanonicalIdentity(identity, canonical, "после OnValidate");
                        PrefabUtility.SaveAsPrefabAsset(contents, path, out saved);
                        Assert.That(saved, Is.True);
                        AssertCanonicalIdentity(identity, canonical, "после SaveAsPrefabAsset");
                        Assert.That(ReadOnDisk(path), Is.EqualTo(canonical), "Канон на диске до повторного импорта");
                        byte[] bytes = File.ReadAllBytes(path);
                        if (cycle == 0) firstSave = bytes;
                        else CollectionAssert.AreEqual(firstSave, bytes, "Повторное сохранение без правок изменило файл");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    AssertCanonicalIdentity(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkIdentity>(), canonical, "после повторного импорта");
                    Assert.That(ReadOnDisk(path), Is.EqualTo(canonical));
                    byte[] importedBytes = File.ReadAllBytes(path);
                    if (cycle == 0) firstImport = importedBytes;
                    else CollectionAssert.AreEqual(firstImport, importedBytes, "Повторный импорт без правок изменил файл");
                }
            }
            finally
            {
                foreach (var instance in ownedObjects)
                    if (instance != null && !EditorUtility.IsPersistent(instance) && preview.IsValid() && instance.scene == preview)
                        UnityEngine.Object.DestroyImmediate(instance);
                if (preview.IsValid())
                {
                    Assert.That(preview.rootCount, Is.Zero, "Не закрываем preview scene с чужими roots");
                    EditorSceneManager.ClosePreviewScene(preview);
                }
                DeleteOwnedFixtureFolder(folder, ownedGuids);
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(originalPreviewCount));
                Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(originalActive.handle));
                Assert.That(originalActive.isDirty, Is.EqualTo(originalActiveDirty), "Не меняем dirty-состояние исходной сцены");
                CollectionAssert.AreEqual(originalSelection, Selection.objects);
            }
        }

        private static uint CanonicalPrefabId(string path)
        {
            return NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
        }

        private static void AssertCanonicalIdentity(NetworkIdentity identity, uint canonical, string phase)
        {
            Assert.That(identity, Is.Not.Null, phase);
            var serialized = new SerializedObject(identity);
            serialized.Update();
            Assert.That(unchecked((uint)serialized.FindProperty("_assetId").longValue), Is.EqualTo(canonical), phase);
            Assert.That(identity.sceneId, Is.Zero, phase);
        }

        private static void SeedCanonicalPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var identity = prefab.GetComponent<NetworkIdentity>();
            uint canonical = CanonicalPrefabId(path);
            Assert.That(canonical, Is.Not.Zero);
            // Исходно корректный импортированный префаб: изолируем порчу последующим authoring.
            var serialized = new SerializedObject(identity);
            serialized.Update();
            serialized.FindProperty("_assetId").longValue = canonical;
            serialized.FindProperty("sceneId").longValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(identity);
            bool saved;
            PrefabUtility.SavePrefabAsset(prefab, out saved);
            Assert.That(saved, Is.True);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssertCanonicalIdentity(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkIdentity>(), canonical, "исходный канон");
            Assert.That(ReadOnDisk(path), Is.EqualTo(canonical));
        }

        private static void DeleteOwnedFixtureFolder(string folder, Dictionary<string, string> ownedGuids)
        {
            if (!ownedGuids.ContainsKey(folder)) return;
            var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in ownedGuids)
            {
                Assert.That(entry.Value, Is.Not.Null.And.Not.Empty, "Неизвестный GUID: " + entry.Key);
                Assert.That(AssetDatabase.AssetPathToGUID(entry.Key), Is.EqualTo(entry.Value), "GUID изменился: " + entry.Key);
                if (entry.Key == folder) continue;
                knownFiles.Add(Path.GetFullPath(entry.Key));
                knownFiles.Add(Path.GetFullPath(entry.Key + ".meta"));
            }
            foreach (string file in Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories))
                Assert.That(knownFiles.Contains(Path.GetFullPath(file)), Is.True, "Чужой файл — папку не удаляем: " + file);
            Assert.That(AssetDatabase.DeleteAsset(folder), Is.True);
            Assert.That(Directory.Exists(folder) || File.Exists(folder + ".meta") || AssetDatabase.IsValidFolder(folder), Is.False);
        }

        [Test]
        public void Сетевые_префабы_хранят_канонический_assetId()
        {
            var wrong = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<NetworkIdentity>() == null) continue;

                checks++;
                uint canonical = NetworkIdentity.AssetGuidToUint(new Guid(guid));
                uint? onDisk = ReadOnDisk(path);

                if (onDisk != canonical)
                    wrong.Add($"{path}: на диске {(onDisk.HasValue ? onDisk.ToString() : "нет значения")}, канон {canonical}");
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного сетевого префаба — тест ничего не проверил.");
            Assert.IsEmpty(wrong,
                "У сетевых префабов на диске не канонический _assetId — будет скакать в диффах.\n" +
                "Исправляет Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids.\n" +
                string.Join("\n", wrong));
        }
    }
}

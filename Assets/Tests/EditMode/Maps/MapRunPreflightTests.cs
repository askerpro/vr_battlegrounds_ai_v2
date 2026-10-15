using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Регрессии принятого preflight: независимость хеша Unity YAML от CRLF и сохранность
    /// загруженной карты. Не пересохраняет assets и не доказывает сетевой запуск или работу на Quest.
    /// </summary>
    public class MapRunPreflightTests
    {
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        private const string ManagersPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string PreflightReportPath = "Docs/tasks/report/map-runtime-bootstrap/details/preflight.json";

        // Тестовая asmdef не может ссылаться на predefined Assembly-CSharp-Editor.
        private static MethodInfo PreflightMethod(string name, params Type[] parameters)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VrBattlegrounds.EditorTools.MapRunPreflight"))
                .FirstOrDefault(candidate => candidate != null);
            Assert.IsNotNull(type, "Editor assembly с MapRunPreflight не загружена.");
            MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            Assert.IsNotNull(method, "Не найден метод MapRunPreflight." + name);
            return method;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static string FileContentHash(byte[] bytes)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tasks",
                "catalog-fingerprint", "reports", "hash-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "source.bin");
            try
            {
                File.WriteAllBytes(path, bytes);
                string result = (string)PreflightMethod("FileContentHash", typeof(string)).Invoke(null, new object[] { path });
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), "Хеширование изменило исходные байты.");
                return result;
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }

        [TestCase("%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: Lobby\n")]
        [TestCase("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n")]
        public void Unity_YAML_и_meta_дают_одинаковый_хеш_с_LF_и_CRLF(string text)
        {
            byte[] lf = Encoding.UTF8.GetBytes(text);
            byte[] crlf = Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n"));
            Assert.AreEqual(Sha256(lf), FileContentHash(lf));
            Assert.AreEqual(Sha256(lf), FileContentHash(crlf), "CRLF рабочей копии изменил отпечаток.");
        }

        [Test]
        public void Бинарное_содержимое_хешируется_без_нормализации_CRLF()
        {
            byte[] crlf = { 0, 255, 13, 10, 37, 89, 65, 77, 76, 13, 10 };
            byte[] lf = { 0, 255, 10, 37, 89, 65, 77, 76, 10 };
            Assert.AreEqual(Sha256(crlf), FileContentHash(crlf));
            Assert.AreEqual(Sha256(lf), FileContentHash(lf));
            Assert.AreNotEqual(FileContentHash(crlf), FileContentHash(lf), "Бинарные CRLF были нормализованы.");
        }

        [TestCase("%YAML 1.1\nvalue: left\rright\n")]
        [TestCase("fileFormatVersion: 2\nvalue: left\rright\n")]
        public void Одиночный_CR_в_YAML_и_meta_сохраняется(string text)
        {
            byte[] lf = Encoding.UTF8.GetBytes(text);
            byte[] crlf = Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n"));
            Assert.AreEqual(Sha256(lf), FileContentHash(crlf));
            Assert.AreNotEqual(FileContentHash(crlf), FileContentHash(Encoding.UTF8.GetBytes(text.Replace("\r", ""))),
                "Одиночный CR потерян вместе с переводами строк.");
        }

        [Test]
        public void Загруженная_чистая_Lobby_проходит_preflight_без_изменений()
        {
            WithLoadedLobby((lobby, catalog) =>
            {
                Assert.IsFalse(lobby.isDirty, "Открытая с диска Lobby уже dirty.");
                Assert.IsNotEmpty(SceneIdentities(lobby), "Проверка сохранности sceneId не должна быть пустой.");
                string before = SceneSnapshot();
                Dictionary<string, byte[]> disk = SourceBytes(catalog);
                object result = Validate(catalog, out bool passed);

                AssertPreserved(before, disk);
                Assert.IsTrue(passed, string.Join(", ", Failures(result)));
                CollectionAssert.IsEmpty(Failures(result));
                Assert.IsFalse(lobby.isDirty, "Preflight пометил чистую Lobby dirty.");
            });
        }

        [Test]
        public void Загруженная_dirty_Lobby_отвергается_именованно_без_изменений()
        {
            WithLoadedLobby((lobby, catalog) =>
            {
                Assert.IsTrue(EditorSceneManager.MarkSceneDirty(lobby));
                string before = SceneSnapshot();
                Dictionary<string, byte[]> disk = SourceBytes(catalog);
                LogAssert.Expect(LogType.Error, new Regex(@"\[MapRunPreflight\] Scene\.Dirty:Lobby \("));
                object result = Validate(catalog, out bool passed);

                AssertPreserved(before, disk);
                Assert.IsFalse(passed, "Preflight принял несохранённую карту.");
                CollectionAssert.AreEqual(new[] { "Scene.Dirty:Lobby" }, Failures(result));
                Assert.IsTrue(lobby.isDirty, "Preflight очистил несохранённое состояние Lobby.");
            });
        }

        private static object Validate(MapRuntimeCatalog catalog, out bool passed)
        {
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPath);
            var manager = managers != null ? managers.GetComponentInChildren<GameNetworkManager>(true) : null;
            Assert.IsNotNull(manager, "Нет GameNetworkManager в постоянных менеджерах.");
            object[] arguments = { catalog, manager.spawnPrefabs, false };
            byte[] originalReport = File.Exists(PreflightReportPath) ? File.ReadAllBytes(PreflightReportPath) : null;
            try
            {
                object result = PreflightMethod("Validate", typeof(MapRuntimeCatalog), typeof(IReadOnlyList<GameObject>),
                    typeof(bool).MakeByRefType()).Invoke(null, arguments);
                passed = (bool)arguments[2];
                return result;
            }
            finally
            {
                // Validate пишет отчёт: fixture не должен затереть доказательства прежнего прогона.
                if (originalReport == null) File.Delete(PreflightReportPath);
                else File.WriteAllBytes(PreflightReportPath, originalReport);
            }
        }

        private static string[] Failures(object result) =>
            (string[])result.GetType().GetProperty("failures").GetValue(result);

        private static MapRuntimeCatalog FreshCatalogCopy()
        {
            var original = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            Assert.IsNotNull(original, "Нет центрального каталога запуска карт.");
            var copy = UnityEngine.Object.Instantiate(original);
            copy.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (MapRuntimeCatalog.ContentEntry entry in copy.EditorContent)
                {
                    entry.ContentFingerprint = (string)PreflightMethod("ContentFingerprint", typeof(string),
                        typeof(VrBattlegrounds.Maps.MapData), typeof(VrBattlegrounds.GameModes.GameModeRegistry))
                        .Invoke(null, new object[] { entry.ScenePath, entry.Map, copy.Modes });
                    entry.ArsenalFingerprint = (string)PreflightMethod("ArsenalFingerprint", typeof(VrBattlegrounds.Arsenal.ArsenalPreset))
                        .Invoke(null, new object[] { entry.Map.arsenalPreset });
                }
                return copy;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        private static void WithLoadedLobby(Action<Scene, MapRuntimeCatalog> check)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                Assert.Ignore("Проверка сцены требует idle Editor без prefab stage.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("Исходные несохранённые сцены нельзя закрывать ради fixture.");

            SceneSetup[] original = EditorSceneManager.GetSceneManagerSetup();
            Scene active = SceneManager.GetActiveScene();
            bool keepEmpty = active.IsValid() && active.isLoaded && string.IsNullOrEmpty(active.path);
            if (keepEmpty && SceneManager.sceneCount != 1)
                Assert.Ignore("Fixture сохраняет безымянную исходную сцену только при единственной открытой сцене.");
            if (original.Length == 0 && !keepEmpty)
                Assert.Ignore("Нужна исходная пустая сцена: Unity не позволяет выгрузить последнюю открытую Lobby.");
            string[] originalRootIds = keepEmpty
                ? active.GetRootGameObjects().Select(root => root.GetEntityId().ToString()).ToArray()
                : Array.Empty<string>();
            MapRuntimeCatalog catalog = null;
            Scene lobby = default;
            try
            {
                lobby = EditorSceneManager.OpenScene(LobbyPath, keepEmpty ? OpenSceneMode.Additive : OpenSceneMode.Single);
                Assert.AreEqual(LobbyPath, lobby.path);
                // Создаём transient-каталог после смены сцены и явно удерживаем до finally.
                catalog = FreshCatalogCopy();
                Assert.IsNotNull(catalog, "Fixture потерял временный каталог до вызова preflight.");
                check(lobby, catalog);
            }
            finally
            {
                if (catalog != null) UnityEngine.Object.DestroyImmediate(catalog);
                if (keepEmpty)
                {
                    if (lobby.IsValid() && lobby.isLoaded) EditorSceneManager.CloseScene(lobby, true);
                    if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                }
                else
                {
                    // Lobby принадлежит fixture: закрываем своё dirty состояние без сохранения.
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.RestoreSceneManagerSetup(original);
                }
            }
            CollectionAssert.AreEqual(SetupSnapshot(original), SetupSnapshot(EditorSceneManager.GetSceneManagerSetup()),
                "Fixture не восстановил исходный setup сцен.");
            if (keepEmpty)
            {
                Assert.IsTrue(active.IsValid() && active.isLoaded, "Fixture выгрузил исходную безымянную сцену.");
                Assert.AreEqual(active.handle, SceneManager.GetActiveScene().handle, "Fixture не восстановил исходную active scene.");
                CollectionAssert.AreEquivalent(originalRootIds,
                    active.GetRootGameObjects().Select(root => root.GetEntityId().ToString()).ToArray(),
                    "Fixture изменил набор корневых объектов исходной безымянной сцены.");
            }
        }

        private static string[] SceneIdentities(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NetworkIdentity>(true))
            .OrderBy(identity => identity.GetEntityId().ToString(), StringComparer.Ordinal)
            .Select(identity => identity.GetEntityId() + ":" + identity.sceneId).ToArray();

        private static string[] SetupSnapshot(SceneSetup[] setup) => setup
            .Select(scene => scene.path + "|" + scene.isLoaded + "|" + scene.isActive).ToArray();

        private static string SceneSnapshot()
        {
            var parts = new List<string>(SetupSnapshot(EditorSceneManager.GetSceneManagerSetup()))
            {
                "active:" + SceneManager.GetActiveScene().handle,
                "previewCount:" + EditorSceneManager.previewSceneCount
            };
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                parts.Add(scene.handle + "|" + scene.path + "|" + scene.isLoaded + "|" + scene.isDirty);
                if (scene.isLoaded) parts.AddRange(SceneIdentities(scene));
            }
            return string.Join("\n", parts);
        }

        private static Dictionary<string, byte[]> SourceBytes(MapRuntimeCatalog catalog)
        {
            IEnumerable<string> paths = catalog.EditorContent.Select(entry => entry.ScenePath)
                .Concat(catalog.EditorContent.Select(entry => AssetDatabase.GetAssetPath(entry.Map)))
                .Concat(catalog.EditorContent.Select(entry => AssetDatabase.GetAssetPath(entry.Map.arsenalPreset)))
                .Concat(new[] { CatalogPath, AssetDatabase.GetAssetPath(catalog.Modes) }).Distinct();
            return paths.SelectMany(path => new[] { path, path + ".meta" })
                .ToDictionary(path => path, File.ReadAllBytes);
        }

        private static void AssertPreserved(string snapshot, Dictionary<string, byte[]> disk)
        {
            Assert.AreEqual(snapshot, SceneSnapshot(), "Preflight изменил setup, active scene, dirty flags или sceneId.");
            foreach (KeyValuePair<string, byte[]> file in disk)
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), "Preflight изменил файл " + file.Key);
        }
    }
}

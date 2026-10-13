using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Read-only проверка полного registry. Каждая сцена каталога проверяется в preview scene; если та же
    /// сцена уже загружена в Editor — проверяется она сама (preview-копия дала бы дубликаты sceneId Mirror).
    /// Пользовательские сцены не сохраняет и не закрывает; загруженная сцена с несохранёнными изменениями —
    /// отказ <c>Scene.Dirty:&lt;sceneName&gt;</c>.
    /// </summary>
    public static class MapRunPreflight
    {
        /// <summary>
        /// Отпечаток содержимого карты: сцена, MapData и реестр режимов. Не зависит от окружения —
        /// см. <see cref="AssetHash"/>: одинаков в любой рабочей копии одного коммита, поэтому серверная и
        /// клиентская сборки (каталог пересобирается при сборке, <c>MapCatalogBuildStep</c>) совпадают.
        /// </summary>
        public static string ContentFingerprint(string scenePath, Maps.MapData map, GameModes.GameModeRegistry modes)
        {
            var parts = new List<string> { "map-content-v2", AssetHash(scenePath), AssetHash(AssetDatabase.GetAssetPath(map)),
                AssetHash(AssetDatabase.GetAssetPath(modes)) };
            return Hash(string.Join("\n", parts));
        }

        /// <summary>Отпечаток пресета арсенала карты — так же не зависит от окружения (<see cref="AssetHash"/>).</summary>
        public static string ArsenalFingerprint(Arsenal.ArsenalPreset preset) =>
            Hash("arsenal-content-v2\n" + AssetHash(AssetDatabase.GetAssetPath(preset)));

        public static object Validate(MapRuntimeCatalog catalog, IReadOnlyList<GameObject> registeredPrefabs) =>
            Validate(catalog, registeredPrefabs, out _);

        /// <summary>
        /// Preflight каталога: ошибки каталога, свежесть отпечатков и содержимое каждой сцены карты.
        /// Сцена не загружена в Editor — открывается preview-копия и закрывается после проверки.
        /// Загружена и сохранена — проверяется загруженная сцена, preview не открывается.
        /// Загружена с несохранёнными изменениями — отказ <c>Scene.Dirty:&lt;sceneName&gt;</c> без проверки
        /// содержимого: оно не совпадает с тем, что попадёт в сборку и в отпечаток.
        /// </summary>
        public static object Validate(MapRuntimeCatalog catalog, IReadOnlyList<GameObject> registeredPrefabs, out bool passed)
        {
            passed = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Preflight требует idle Editor.");
            var failures = new List<string>(); var maps = new List<object>();
            if (catalog == null) failures.Add("Catalog.Missing");
            else
            {
                failures.AddRange(catalog.Validate(registeredPrefabs).Errors);
                foreach (var entry in catalog.EditorContent)
                {
                    if (entry == null || entry.Map == null || string.IsNullOrEmpty(entry.ScenePath)) continue;
                    try
                    {
                        if (entry.ContentFingerprint != ContentFingerprint(entry.ScenePath, entry.Map, catalog.Modes))
                            failures.Add("Catalog.Content.Stale:" + entry.Map.sceneName);
                        if (entry.ArsenalFingerprint != ArsenalFingerprint(entry.Map.arsenalPreset)) failures.Add("Catalog.Arsenal.Stale:" + entry.Map.sceneName);
                    }
                    catch (Exception error)
                    {
                        failures.Add("Catalog.Source.Invalid:" + entry.Map.sceneName + "/" + error.Message);
                        maps.Add(new { scene = entry.Map.sceneName, passed = false, failureCount = 1 });
                        continue;
                    }
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.ScenePath) == null)
                    { failures.Add("Scene.Missing:" + entry.Map.sceneName); continue; }
                    // Сцена уже загружена в Editor: preview-копия дала бы объектам те же sceneId,
                    // и Mirror (NetworkIdentity.AssignSceneID) отверг бы их как дубликаты. Проверяем
                    // саму загруженную сцену; она принадлежит пользователю — не сохраняем и не закрываем.
                    var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(entry.ScenePath);
                    if (loaded.IsValid() && loaded.isLoaded)
                    {
                        if (loaded.isDirty)
                        {
                            // Несохранённое состояние не совпадает с тем, что попадёт в сборку и в отпечаток.
                            const string reason = "Сцена открыта в Editor с несохранёнными изменениями — сохраните её перед сборкой/preflight.";
                            failures.Add("Scene.Dirty:" + entry.Map.sceneName);
                            maps.Add(new { scene = entry.Map.sceneName, passed = false, failureCount = 1, reason });
                            Core.GameLog.Error("[MapRunPreflight] Scene.Dirty:" + entry.Map.sceneName + " (" + entry.ScenePath + "): " + reason);
                            continue;
                        }
                        ValidateSceneContent(loaded, entry, catalog, registeredPrefabs, failures, maps);
                        continue;
                    }
                    var scene = EditorSceneManager.OpenPreviewScene(entry.ScenePath);
                    try { ValidateSceneContent(scene, entry, catalog, registeredPrefabs, failures, maps); }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
            }
            const string path = "Docs/tasks/report/map-runtime-bootstrap/details/preflight.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = failures.Count == 0, failures, maps }, Newtonsoft.Json.Formatting.Indented));
            passed = failures.Count == 0;
            return new { passed = failures.Count == 0, failureCount = failures.Count, mapCount = maps.Count,
                failures = failures.Take(10).ToArray(), reportPath = path };
        }

        /// <summary>
        /// Проверка содержимого сцены карты — единая для preview-копии и для сцены, уже загруженной в Editor:
        /// ровно один <see cref="MapRoot"/> среди корней, его привязки, совпадение MapData с записью каталога
        /// и разрешение запуска через <see cref="MapRuntimeCatalog.Resolve"/>. Сцену не открывает и не закрывает.
        /// </summary>
        private static void ValidateSceneContent(UnityEngine.SceneManagement.Scene scene, MapRuntimeCatalog.ContentEntry entry,
            MapRuntimeCatalog catalog, IReadOnlyList<GameObject> registeredPrefabs, List<string> failures, List<object> maps)
        {
            var roots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapRoot>(true)).ToArray();
            if (roots.Length != 1) { failures.Add("MapRoot.Count:" + entry.Map.sceneName); return; }
            var bindings = roots[0].ValidateBindings();
            if (roots[0].Map != entry.Map) failures.Add("Map.Bindings.MapDataMismatch:" + entry.Map.sceneName);
            failures.AddRange(bindings.Errors.Select(e => entry.Map.sceneName + "/" + e));
            if (bindings.Passed)
            {
                var resolved = catalog.Resolve(new MapRunRequest(new MapRunKey(Guid.NewGuid(), 1), entry.Map.sceneName,
                    "", entry.ContentFingerprint), bindings.Bindings, registeredPrefabs);
                failures.AddRange(resolved.Errors.Select(e => entry.Map.sceneName + "/" + e));
            }
            maps.Add(new { scene = entry.Map.sceneName, passed = bindings.Passed, failureCount = bindings.Errors.Count });
        }
        /// <summary>
        /// Хеш содержимого ассета на диске: файл и его <c>.meta</c>.
        ///
        /// <para>
        /// Раньше здесь был <c>AssetDatabase.GetAssetDependencyHash</c> — хеш сырых байтов. Он зависел от
        /// переводов строк рабочей копии: в Git YAML хранится в LF (eol=lf), а в старых рабочих копиях файлы
        /// лежат в CRLF, пока Unity их не пересохранит. Тот же коммит давал разные отпечатки: ложный
        /// <c>Catalog.*.Stale</c> в preflight и разные каталоги у сборок с разных машин.
        /// </para>
        ///
        /// <para>
        /// Теперь текстовый YAML Unity (файл начинается с <c>%YAML</c>; <c>.meta</c> — всегда YAML) хешируется
        /// с CRLF, приведёнными к LF, — ровно как его хранит Git. Бинарный ассет — байты как есть.
        /// </para>
        /// </summary>
        private static string AssetHash(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException("Content.Source.Missing:" + path);
            string meta = path + ".meta";
            if (!File.Exists(path) || !File.Exists(meta)) throw new InvalidOperationException("Content.Source.Missing:" + path);
            return Hash("asset-content-v2\n" + FileContentHash(path) + "\n" + FileContentHash(meta));
        }

        private static string FileContentHash(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (IsUnityYaml(bytes)) bytes = CrlfToLf(bytes);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static readonly byte[] YamlHeader = System.Text.Encoding.ASCII.GetBytes("%YAML");
        private static readonly byte[] MetaHeader = System.Text.Encoding.ASCII.GetBytes("fileFormatVersion:");

        /// <summary>Текстовая сериализация Unity: ассет с заголовком <c>%YAML</c> или <c>.meta</c>.</summary>
        private static bool IsUnityYaml(byte[] bytes) => StartsWith(bytes, YamlHeader) || StartsWith(bytes, MetaHeader);

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }

        /// <summary>Только пары CR LF → LF; одиночный CR (внутри строковых значений) не трогается.</summary>
        private static byte[] CrlfToLf(byte[] bytes)
        {
            var result = new byte[bytes.Length];
            int n = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') continue;
                result[n++] = bytes[i];
            }
            if (n == bytes.Length) return bytes;
            Array.Resize(ref result, n);
            return result;
        }
        private static string Hash(string value)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}

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
    /// <summary>Read-only проверка полного registry в preview scenes; пользовательские сцены не сохраняет.</summary>
    public static class MapRunPreflight
    {
        public static string ContentFingerprint(string scenePath, Maps.MapData map, GameModes.GameModeRegistry modes)
        {
            var parts = new List<string> { "map-content-v1", AssetHash(scenePath), AssetHash(AssetDatabase.GetAssetPath(map)),
                AssetHash(AssetDatabase.GetAssetPath(modes)) };
            return Hash(string.Join("\n", parts));
        }
        public static string ArsenalFingerprint(Arsenal.ArsenalPreset preset) => AssetHash(AssetDatabase.GetAssetPath(preset));

        public static object Validate(MapRuntimeCatalog catalog, IReadOnlyList<GameObject> registeredPrefabs) =>
            Validate(catalog, registeredPrefabs, out _);

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
                    var scene = EditorSceneManager.OpenPreviewScene(entry.ScenePath);
                    try
                    {
                        var roots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapRoot>(true)).ToArray();
                        if (roots.Length != 1) { failures.Add("MapRoot.Count:" + entry.Map.sceneName); continue; }
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
        private static string AssetHash(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException("Content.Source.Missing:" + path);
            return AssetDatabase.GetAssetDependencyHash(path).ToString();
        }
        private static string Hash(string value)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}

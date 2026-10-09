using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Один resolver release/automation списков. Запуск Play не меняет EditorBuildSettings.</summary>
    public static class BuildSceneResolver
    {
        public static string[] Resolve(bool automation, IEnumerable<string> requiredDebugScenes = null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>("Assets/Data/Maps/MapRuntimeCatalog.asset");
            if (catalog == null) throw new InvalidOperationException("BuildSceneCatalogMissing");
            var debug = new HashSet<string>(catalog.DebugMaps.Where(m => m != null).Select(m => m.sceneName), StringComparer.Ordinal);
            var required = new HashSet<string>(requiredDebugScenes ?? Array.Empty<string>(), StringComparer.Ordinal);
            string[] enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            foreach (string scene in required)
                if (!debug.Contains(scene) || !enabled.Any(p => Path.GetFileNameWithoutExtension(p) == scene))
                    throw new InvalidOperationException("AutomationSceneMissing:" + scene);
            return enabled.Where(path => !debug.Contains(Path.GetFileNameWithoutExtension(path)) ||
                (automation && required.Contains(Path.GetFileNameWithoutExtension(path))))
                .Where(path => !AssetDatabase.GetDependencies(path, true).Contains("Assets/Scripts/Core/StandaloneSceneMarker.cs"))
                .Distinct(StringComparer.Ordinal).ToArray();
        }
    }
}

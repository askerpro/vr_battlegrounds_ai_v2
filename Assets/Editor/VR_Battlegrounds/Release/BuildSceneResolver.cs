using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
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
            if (automation && required.Count > 0)
            {
                var player = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                    .Select(assembly => assembly.name), StringComparer.Ordinal);
                var editor = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                    .Select(assembly => assembly.name), StringComparer.Ordinal);
                foreach (string path in enabled.Where(p => required.Contains(Path.GetFileNameWithoutExtension(p))))
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (!dependency.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    // Editor-only метаданные ассетов допустимы; проверяем именно компоненты сцен и префабов.
                    Type type = AssetDatabase.LoadAssetAtPath<MonoScript>(dependency)?.GetClass();
                    if (type == null || !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) continue;
                    string assembly = type.Assembly.GetName().Name;
                    if (editor.Contains(assembly) && !player.Contains(assembly))
                        throw new InvalidOperationException("AutomationScenePlayerAssemblyMissing:" +
                            Path.GetFileNameWithoutExtension(path) + ":" + assembly + ":" + dependency);
                }
            }
            return enabled.Where(path => !debug.Contains(Path.GetFileNameWithoutExtension(path)) ||
                (automation && required.Contains(Path.GetFileNameWithoutExtension(path))))
                .Where(path => !AssetDatabase.GetDependencies(path, true).Contains("Assets/Scripts/Core/StandaloneSceneMarker.cs"))
                .Distinct(StringComparer.Ordinal).ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// При импорте модели переразворачивает box-UV сабмеши из <see cref="BoxUvTargets"/>.
    /// Каждая модель зависит от custom dependency <c>VrbBoxUv/&lt;путь&gt;</c>: когда конфиг меняется
    /// (сканер или git), отпечаток пересчитывается и Unity переимпортирует только затронутые модели.
    /// </summary>
    public sealed class BoxUvModelPostprocessor : AssetPostprocessor
    {
        /// <summary>Увеличить при изменении алгоритма <see cref="BoxUvProjector"/> — переимпорт всех моделей.</summary>
        public const int Version = 1;

        private const string DependencyPrefix = "VrbBoxUv/";
        private const string RegisteredFile = "Library/VrbBoxUv/registered.txt";

        public override uint GetVersion() => Version;

        private void OnPreprocessModel()
        {
            context.DependsOnCustomDependency(DependencyPrefix + assetPath);
        }

        private void OnPostprocessModel(GameObject root)
        {
            BoxUvTargets config = BoxUvTargets.Load();
            var targets = new Dictionary<string, BoxUvTargets.Target>(StringComparer.Ordinal);
            foreach (BoxUvTargets.Target target in config.ForModel(assetPath)) targets[target.Mesh] = target;
            if (targets.Count == 0) return;

            var done = new HashSet<Mesh>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !done.Add(mesh) || !targets.TryGetValue(mesh.name, out BoxUvTargets.Target target)) continue;
                BoxUvProjector.Result result = BoxUvProjector.Project(mesh, target.Submeshes);
                if (result.Skipped != null)
                    GameLog.Perf.Warning("[BoxUv] " + assetPath + " / " + mesh.name + ": пропущен (" + result.Skipped + ")");
                targets.Remove(mesh.name);
            }
            foreach (string missing in targets.Keys)
                GameLog.Perf.Warning("[BoxUv] " + assetPath + ": меш " + missing + " из конфига не найден при импорте");
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!string.Equals(path, BoxUvTargets.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                // Регистрировать зависимости посреди импорта нельзя — сразу после него.
                EditorApplication.delayCall += () => RegisterDependencies(refresh: true);
                return;
            }
        }

        /// <summary>
        /// Записать отпечатки целей в custom dependencies: моделей из конфига и тех, что были в нём раньше
        /// (список ведётся в Library этой копии). Unity сама переимпортирует модели с изменившимся отпечатком.
        /// Возвращает модели, для которых отпечаток записан.
        /// </summary>
        public static List<string> RegisterDependencies(bool refresh)
        {
            BoxUvTargets config = BoxUvTargets.Load();
            var models = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BoxUvTargets.Target target in config.Targets) models.Add(target.Model);
            string registeredFull = Path.GetFullPath(RegisteredFile);
            if (File.Exists(registeredFull))
                foreach (string line in File.ReadAllLines(registeredFull))
                    if (line.Length > 0) models.Add(line);

            var registered = new List<string>();
            foreach (string model in models)
            {
                AssetDatabase.RegisterCustomDependency(DependencyPrefix + model, config.HashFor(model));
                registered.Add(model);
            }
            var current = new List<string>();
            foreach (BoxUvTargets.Target target in config.Targets)
                if (!current.Contains(target.Model)) current.Add(target.Model);
            Directory.CreateDirectory(Path.GetDirectoryName(registeredFull));
            File.WriteAllText(registeredFull, string.Join("\n", current) + "\n");
            if (refresh) AssetDatabase.Refresh();
            return registered;
        }
    }
}

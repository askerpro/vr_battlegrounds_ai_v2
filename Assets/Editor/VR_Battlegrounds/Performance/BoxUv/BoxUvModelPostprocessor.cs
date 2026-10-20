using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// При импорте модели переразворачивает box-UV сабмеши из <see cref="BoxUvTargets"/>.
    /// Импорт каждой модели из конфига зависит от самого файла конфига (<c>DependsOnSourceAsset</c>): ключ артефакта
    /// включает его содержимое, поэтому кэш артефактов не вернёт развёртку по устаревшему конфигу. Модели, которые
    /// попадают в конфиг впервые, такой зависимости ещё не имеют — их переимпортирует обработчик импорта конфига.
    /// </summary>
    public sealed class BoxUvModelPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Увеличить при изменении алгоритма <see cref="BoxUvProjector"/> — переимпорт всех моделей. Версия входит в ключ
        /// кэша артефактов: 2 отсекает артефакты v1, импортированные до появления конфига (при смене ветки модели
        /// импортируются раньше компиляции постпроцессора, и без новой версии Unity возвращал их из кэша).
        /// </summary>
        public const int Version = 2;

        public override uint GetVersion() => Version;

        private void OnPreprocessModel()
        {
            foreach (BoxUvTargets.Target _ in BoxUvTargets.Load().ForModel(assetPath))
            {
                context.DependsOnSourceAsset(BoxUvTargets.FilePath);
                return;
            }
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
                // Импортировать модели посреди импорта нельзя — сразу после него.
                EditorApplication.delayCall += () => ReimportTargets();
                return;
            }
        }

        /// <summary>Переимпортировать все модели из конфига (новые цели ещё не зависят от файла конфига).</summary>
        public static List<string> ReimportTargets()
        {
            var models = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BoxUvTargets.Target target in BoxUvTargets.Load().Targets) models.Add(target.Model);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string model in models) AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return new List<string>(models);
        }
    }
}

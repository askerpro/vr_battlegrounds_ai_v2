using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Ассеты охвата, разложенные по видам проверок. Строится из <see cref="AuditRequest"/>, только чтение базы ассетов.</summary>
    public sealed class AuditInventory
    {
        /// <summary>.mat и модели (их встроенные материалы).</summary>
        public SortedSet<string> MaterialSources { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> Textures { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> Shaders { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>Префабы и модели (в охвате «сцена» не заполняется — проверяются объекты сцены).</summary>
        public SortedSet<string> GameObjects { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public string ScenePath { get; private set; }
        public int TotalAssets { get; private set; }
        public List<string> Problems { get; } = new List<string>();

        public static AuditInventory Collect(AuditRequest request)
        {
            var inventory = new AuditInventory();
            var paths = new SortedSet<string>(StringComparer.Ordinal);

            switch (request.Kind)
            {
                case AuditScopeKind.Folder:
                    AddFolders(request.Paths, paths, inventory);
                    break;
                case AuditScopeKind.Assets:
                    var direct = new List<string>();
                    foreach (string path in request.Paths)
                    {
                        if (AssetDatabase.IsValidFolder(path))
                            AddFolders(new[] { path }, paths, inventory);
                        else if (File.Exists(Path.GetFullPath(path)))
                            direct.Add(path);
                        else
                            inventory.Problems.Add("нет ассета: " + path);
                    }
                    foreach (string path in direct)
                        paths.Add(path);
                    if (request.IncludeDependencies && direct.Count > 0)
                    {
                        foreach (string dependency in AssetDatabase.GetDependencies(direct.ToArray(), true))
                            paths.Add(dependency);
                    }
                    break;
                case AuditScopeKind.Scene:
                    string scenePath = request.Paths.Count > 0 ? request.Paths[0] : null;
                    if (string.IsNullOrEmpty(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(Path.GetFullPath(scenePath)))
                    {
                        inventory.Problems.Add("нет сцены: " + scenePath);
                        break;
                    }
                    inventory.ScenePath = scenePath;
                    foreach (string dependency in AssetDatabase.GetDependencies(scenePath, true))
                    {
                        if (dependency != scenePath)
                            paths.Add(dependency);
                    }
                    break;
            }

            foreach (string path in paths)
            {
                if (IsExcluded(path, request))
                    continue;
                inventory.TotalAssets++;
                inventory.Classify(path, request.Kind);
            }
            return inventory;
        }

        private static void AddFolders(IEnumerable<string> folders, SortedSet<string> paths, AuditInventory inventory)
        {
            var valid = new List<string>();
            foreach (string folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                    valid.Add(folder);
                else
                    inventory.Problems.Add("нет папки: " + folder);
            }
            if (valid.Count == 0)
                return;
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, valid.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                    paths.Add(path);
            }
        }

        private static bool IsExcluded(string path, AuditRequest request)
        {
            foreach (string prefix in request.ExcludePrefixes)
            {
                if (!string.IsNullOrEmpty(prefix) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void Classify(string path, AuditScopeKind kind)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".mat":
                    MaterialSources.Add(path);
                    return;
                case ".shader":
                case ".shadergraph":
                    Shaders.Add(path);
                    return;
                case ".prefab":
                    if (kind != AuditScopeKind.Scene)
                        GameObjects.Add(path);
                    return;
                case ".unity":
                case ".cs":
                case ".shadersubgraph":
                    return;
            }

            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter)
            {
                Textures.Add(path);
            }
            else if (importer is ModelImporter)
            {
                MaterialSources.Add(path);
                if (kind != AuditScopeKind.Scene)
                    GameObjects.Add(path);
            }
        }
    }
}

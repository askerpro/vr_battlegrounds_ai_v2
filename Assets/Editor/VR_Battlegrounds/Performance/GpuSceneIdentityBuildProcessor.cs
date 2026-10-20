using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.DevTools.StressTest;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Performance
{
    /// <summary>Opt-in таблица refs/ID только в копии сцены Development build. Исходная сцена не сохраняется.</summary>
    public sealed class GpuSceneIdentityBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 100;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer || report == null || (report.summary.options & BuildOptions.Development) == 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-gpuSceneIdentity") < 0 || string.IsNullOrEmpty(scene.path)) return;
            try { BuildSceneIdentity(scene); }
            catch (Exception error)
            {
                throw new BuildFailedException("Не удалось создать GPU identity сцены " + scene.path + ": " + error);
            }
        }

        private static void BuildSceneIdentity(Scene scene)
        {
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            var identities = new Dictionary<Object, string>();
            var claimed = new Dictionary<string, Object>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            var unresolved = new HashSet<Object>();

            void Add(Object value, bool asset = false)
            {
                if (value == null || identities.ContainsKey(value) || unresolved.Contains(value)) return;
                string identity = null;
                if (asset)
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string assetGuid, out long localId) &&
                        !string.IsNullOrEmpty(assetGuid) && assetGuid != "00000000000000000000000000000000" && localId != 0)
                        identity = "asset:" + assetGuid + ":" + localId.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
                    if (id.identifierType == 2 && id.targetObjectId != 0 && id.assetGUID.ToString() == guid) identity = id.ToString();
                }
                if (identity == null) { unresolved.Add(value); return; }
                if (claimed.TryGetValue(identity, out Object previous) && previous != value) ambiguous.Add(identity);
                else claimed[identity] = value;
                identities.Add(value, identity);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) Add(child.gameObject);
                foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true)) Add(group);
                foreach (Light light in root.GetComponentsInChildren<Light>(true)) { Add(light); Add(light.cookie, true); }
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Add(renderer);
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                    else if (renderer.TryGetComponent(out MeshFilter filter) && filter != null) mesh = filter.sharedMesh;
                    Add(mesh, true);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Add(material, true); Add(material.shader, true);
                        foreach (string property in material.GetTexturePropertyNames()) Add(material.GetTexture(property), true);
                    }
                }
            }
            var entries = new List<PerfSceneIdentityTable.Entry>(identities.Count);
            foreach (KeyValuePair<Object, string> row in identities)
            {
                if (ambiguous.Contains(row.Value)) { unresolved.Add(row.Key); continue; }
                entries.Add(new PerfSceneIdentityTable.Entry { target = row.Key, authoringId = row.Value });
            }
            entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.authoringId, b.authoringId));
            string sceneSha = Hash(File.ReadAllBytes(Path.Combine(Application.dataPath, "..", scene.path)));
            var canonical = new StringBuilder(scene.path).Append('\n').Append(guid).Append('\n').Append(sceneSha).Append('\n');
            foreach (PerfSceneIdentityTable.Entry entry in entries) canonical.Append(entry.authoringId).Append('\n');
            var holder = new GameObject("GpuSceneIdentity");
            SceneManager.MoveGameObjectToScene(holder, scene);
            PerfSceneIdentityTable table = holder.AddComponent<PerfSceneIdentityTable>();
            table.SetBuildData(scene.path, guid, sceneSha, Hash(Encoding.UTF8.GetBytes(canonical.ToString())), unresolved.Count, entries.ToArray());
            EditorUtility.SetDirty(table);
            // Не вызываем SaveScene/SaveAssets: Unity сериализует только переданную build-копию.
        }

        private static string Hash(byte[] value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant();
        }
    }
}

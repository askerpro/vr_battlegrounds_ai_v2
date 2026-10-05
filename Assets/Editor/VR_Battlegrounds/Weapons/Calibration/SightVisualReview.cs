using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Снимает геометрию и виды без игровых компонентов/Awake и без записи префаба.</summary>
    public static class SightVisualReview
    {
        public const string Folder = "tmp/weapon-sight-calibration/visual-review";

        [Serializable] public sealed class Inventory
        {
            public bool passed;
            public string observedAtUtc;
            public List<string> failures = new List<string>();
            public List<Model> weapons = new List<Model>();
            public List<Model> donors = new List<Model>();
        }

        [Serializable] public sealed class Model
        {
            public string id;
            public string assetPath;
            public string dependencyHash;
            public Bounds boundsMeters;
            public Vector3 shotOriginMeters;
            public Vector3 shotForward;
            public List<string> images = new List<string>();
            public List<Surface> surfaces = new List<Surface>();
        }

        [Serializable] public sealed class Surface
        {
            public string nodePath;
            public string meshGuid;
            public long meshLocalFileId;
            public string meshPath;
            public string nodeGuid;
            public long nodeLocalFileId;
            public Matrix4x4 localToPrefabMeters;
            public Vector3[] vertices;
            public List<Submesh> subMeshes = new List<Submesh>();
        }

        [Serializable] public sealed class Submesh { public int[] triangles; }

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sight Calibration/Capture Visual Inventory")]
        public static void RunMenu()
        {
            var r = CaptureInventory();
            GameLog.WeaponSystem.Info($"[SightVisualReview] {(r.passed ? "PASS capture" : "FAIL capture")}, weapons={r.weapons.Count}, donors={r.donors.Count}, errors={r.failures.Count}; {Folder}/inventory.json");
        }

        public static Inventory CaptureInventory()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture requires Edit Mode");
            Directory.CreateDirectory(Folder);
            var result = new Inventory { observedAtUtc = DateTime.UtcNow.ToString("O") };
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath);
            if (registry == null) throw new InvalidOperationException("Missing WeaponRegistry");
            foreach (var info in registry.Weapons)
            {
                try { result.weapons.Add(CaptureModel(info.WeaponId, info.WeaponPrefab)); }
                catch (Exception e) { result.failures.Add((info == null ? "MissingInfo" : info.WeaponId) + ": " + e.Message); }
            }
            string[] donors = AssetDatabase.FindAssets("t:Prefab", new [] { "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Prefabs/Weapons_Details" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith("Sight_", StringComparison.Ordinal))
                .Concat(new [] {
                    "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Prefabs/Attachments/IronSights.prefab",
                    "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Prefabs/Attachments/Holograph_Sight.prefab"
                }).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (string path in donors)
            {
                try { result.donors.Add(CaptureModel(Path.GetFileNameWithoutExtension(path), AssetDatabase.LoadAssetAtPath<GameObject>(path))); }
                catch (Exception e) { result.failures.Add(path + ": " + e.Message); }
            }
            result.passed = result.failures.Count == 0 && result.weapons.Count == registry.Count;
            File.WriteAllText(Folder + "/inventory.json", JsonUtility.ToJson(result, true));
            return result;
        }

        public static Model CaptureModel(string id, GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            string directory = Folder + "/" + id;
            Directory.CreateDirectory(directory);
            var model = new Model { id = id, assetPath = AssetDatabase.GetAssetPath(prefab) };
            model.dependencyHash = AssetDatabase.GetAssetDependencyHash(model.assetPath).ToString();
            var source = prefab.GetComponent<UltimateXR.Mechanics.Weapons.UxrProjectileSource>();
            if (source != null && source.ShotTypes.Count > 0)
            {
                model.shotOriginMeters = source.ShotTypes[0].ShotSource.position;
                model.shotForward = source.ShotTypes[0].ShotSource.forward;
            }
            var preview = new PreviewRenderUtility();
            var root = new GameObject("SightRenderOnly") { hideFlags = HideFlags.HideAndDontSave };
            bool haveBounds = false;
            try
            {
                preview.AddSingleGO(root);
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (!Included(renderer.transform, prefab.transform) || !renderer.enabled) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                    if (mesh == null) continue;
                    var go = new GameObject(renderer.name) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    go.transform.localScale = renderer.transform.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var copy = go.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                    if (!haveBounds) { model.boundsMeters = copy.bounds; haveBounds = true; }
                    else { var bounds = model.boundsMeters; bounds.Encapsulate(copy.bounds); model.boundsMeters = bounds; }
                    // Каждый треугольник и индекс остаётся связан с исходным persistent mesh.
                    var surface = new Surface { nodePath = SemanticPath(renderer.transform, prefab.transform), meshPath = AssetDatabase.GetAssetPath(mesh), localToPrefabMeters = renderer.transform.localToWorldMatrix };
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out surface.meshGuid, out surface.meshLocalFileId)
                        || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.transform, out surface.nodeGuid, out surface.nodeLocalFileId))
                        throw new InvalidOperationException("Missing persistent surface identity: " + surface.nodePath);
                    using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                    using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
                    {
                        data[0].GetVertices(vertices); surface.vertices = vertices.ToArray();
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            if (mesh.GetTopology(s) != UnityEngine.MeshTopology.Triangles) throw new InvalidOperationException("Unsupported mesh topology");
                            var sub = data[0].GetSubMesh(s);
                            var indices = new int[sub.indexCount];
                            if (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                            {
                                var input = data[0].GetIndexData<ushort>();
                                for (int k = 0; k < indices.Length; k++) indices[k] = input[sub.indexStart + k] + sub.baseVertex;
                            }
                            else
                            {
                                var input = data[0].GetIndexData<int>();
                                for (int k = 0; k < indices.Length; k++) indices[k] = input[sub.indexStart + k] + sub.baseVertex;
                            }
                            surface.subMeshes.Add(new Submesh { triangles = indices });
                        }
                    }
                    model.surfaces.Add(surface);
                }
                if (!haveBounds) throw new InvalidOperationException("No visible geometry");
                preview.lights[0].intensity = 1.5f; preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0);
                preview.lights[1].intensity = 1f; preview.lights[1].transform.rotation = Quaternion.Euler(340f, 215f, 0);
                preview.ambientColor = new Color(.35f, .35f, .35f);
                Camera camera = preview.camera;
                camera.orthographic = true; camera.nearClipPlane = .001f; camera.farClipPlane = 100f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f,.18f,.21f);
                Vector3 c = model.boundsMeters.center;
                float fullSize = Mathf.Max(model.boundsMeters.size.y, model.boundsMeters.size.z * .5f) * .58f;
                Render("side", c, Vector3.left, Vector3.up, fullSize, 1024, 512);
                Render("top", c, Vector3.up, Vector3.left, Mathf.Max(model.boundsMeters.size.x, model.boundsMeters.size.z * .5f) * .58f, 1024, 512);
                Vector3 sight = new Vector3(c.x, model.boundsMeters.max.y - Mathf.Min(.02f, model.boundsMeters.size.y * .15f), c.z);
                Render("rear", sight, Vector3.back, Vector3.up, Mathf.Max(.06f, model.boundsMeters.size.x * .6f), 768, 768);
                File.WriteAllText(directory + "/geometry.json", JsonUtility.ToJson(model, true));
                return model;

                void Render(string view, Vector3 center, Vector3 direction, Vector3 up, float size, int width, int height)
                {
                    camera.orthographicSize = Mathf.Max(size, .02f);
                    camera.transform.SetPositionAndRotation(center + direction * 5f, Quaternion.LookRotation(-direction, up));
                    preview.BeginStaticPreview(new Rect(0, 0, width, height)); preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    try { string path = directory + "/" + view + ".png"; File.WriteAllBytes(path, texture.EncodeToPNG()); model.images.Add(path); }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                preview.Cleanup();
            }
        }

        private static bool Included(Transform node, Transform root)
        {
            for (Transform current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf || current.name.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0
                    || current.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static string SemanticPath(Transform node, Transform root)
        {
            var parts = new List<string>();
            while (node != null)
            {
                parts.Add(node.name + "[" + node.GetSiblingIndex() + "]");
                if (node == root) { parts.Reverse(); return string.Join("/", parts); }
                node = node.parent;
            }
            throw new InvalidOperationException("Node outside root");
        }
    }
}

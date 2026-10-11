using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Связная двухскатная крыша вдоль Z; выбранный чистый участок ткани MilitaryCamp.</summary>
    public static class LobbyTentRoofBuilder
    {
        private const string MeshFolder = "Assets/Art/Lobby/Interior/Roof";
        private const string MaterialFolder = "Assets/Materials/Lobby/Interior/Roof";
        [Serializable] public sealed class LodReport
        {
            public int level, cellsX, cellsZ, vertices, triangles, boundaryEdges, internalBoundaryEdges, nonManifoldEdges;
            public Bounds outputBounds;
            public string generatedAsset;
        }
        [Serializable] public sealed class BuildReport
        {
            public string status, failure, geometry = "connected_own_two_slope_grid", ridgeAxis = "Z", baseMap, normalMap;
            public Bounds tent;
            public float floorY, eaveY, ridgeY, maximumSag = .035f;
            public Vector2 textureScale = new Vector2(.22f, .15f), textureOffset = new Vector2(.13f, .30f);
            public int frameBeams;
            public List<LodReport> lods = new List<LodReport>();
        }
        public static BuildReport LastReport { get; private set; }

        /// <summary>Фактическая высота ткани для визуальных надставок; физические колонны этот helper не меняет.</summary>
        public static float HeightAt(Bounds tent, float floorY, float x, float z)
        {
            if (tent.extents.x <= 0 || tent.extents.z <= 0) throw new ArgumentException("Нужен footprint крыши.");
            float across = Mathf.Clamp01(Mathf.Abs(x - tent.center.x) / tent.extents.x);
            float t = 0;
            var frames = FrameZ(tent);
            for (int i = 0; i + 1 < frames.Length; i++)
                if (z >= frames[i] && z <= frames[i + 1]) { t = Mathf.InverseLerp(frames[i], frames[i + 1], z); break; }
            float wave = Mathf.Sin(t * Mathf.PI);
            float sag = .035f * Mathf.Sin(across * Mathf.PI) * wave * wave;
            return floorY + 4.2f + 1.4f * (1 - across) - sag;
        }

        public static void Build(Transform parent, Bounds tent, float floorY)
        {
            if (parent == null || !parent.gameObject.scene.IsValid() || !parent.gameObject.scene.isLoaded
                || Vector3.Distance(parent.lossyScale, Vector3.one) > .001f)
                throw new ArgumentException("Нужен немасштабированный родитель загруженной сцены.");
            if (parent.Find("TentRoof") != null) throw new InvalidOperationException("TentRoof уже есть; заменой управляет главный авторинг.");
            if (tent.size.x <= 0 || tent.size.z <= 0 || !Finite(tent.size.x) || !Finite(tent.size.z) || !Finite(floorY))
                throw new ArgumentException("Некорректные реальные размеры крыши.");
            LastReport = new BuildReport { status = "preparing", tent = tent, floorY = floorY, eaveY = floorY + 4.2f, ridgeY = floorY + 5.6f };
            GameObject root = null;
            try
            {
                var diffuse = LobbyMilitaryCampSources.Texture("T_Tent_a_BC");
                var normal = LobbyMilitaryCampSources.Texture("T_Tent_a_N");
                if (diffuse == null || normal == null) throw new InvalidOperationException("Нужны проверенные T_Tent_a_BC/N.");
                LastReport.baseMap = AssetDatabase.GetAssetPath(diffuse); LastReport.normalMap = AssetDatabase.GetAssetPath(normal);
                EnsureFolder(MeshFolder); EnsureFolder(MaterialFolder);
                var cloth = ClothMaterial(diffuse, normal); var metal = MetalMaterial();
                root = new GameObject("TentRoof"); root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(new Vector3(tent.center.x, 0, tent.center.z), Quaternion.identity);
                int[] cellsX = { 12, 8, 6, 4, 2 }, cellsZ = { 24, 16, 12, 8, 4 };
                float[] thresholds = { .55f, .28f, .12f, .045f, .008f };
                var levels = new LOD[5];
                for (int i = 0; i < 5; i++)
                {
                    var report = new LodReport { level = i, cellsX = cellsX[i], cellsZ = cellsZ[i] };
                    Mesh mesh = Grid(tent, floorY, cellsX[i], cellsZ[i]); Audit(mesh, tent, report);
                    string path = MeshFolder + "/TentRoof_LOD" + i + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing != null)
                    {
                        // CopySerialized обновлял CPU-данные, оставляя устаревший GPU buffer у уже импортированного Mesh.
                        existing.Clear(false); existing.indexFormat = mesh.indexFormat;
                        existing.vertices = mesh.vertices; existing.normals = mesh.normals; existing.tangents = mesh.tangents;
                        existing.uv = mesh.uv; existing.subMeshCount = mesh.subMeshCount;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++) existing.SetTriangles(mesh.GetTriangles(sub),sub);
                        existing.bounds = mesh.bounds; existing.UploadMeshData(false);
                        UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(existing);
                    }
                    else AssetDatabase.CreateAsset(mesh, path);
                    report.generatedAsset = path; LastReport.lods.Add(report);
                    var item = new GameObject("TentRoof_LOD" + i); item.transform.SetParent(root.transform, false);
                    item.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = item.AddComponent<MeshRenderer>(); renderer.sharedMaterial = cloth; ConfigureRenderer(renderer);
                    levels[i] = new LOD(thresholds[i], new Renderer[] { renderer });
                }
                var group = root.AddComponent<LODGroup>(); group.fadeMode = LODFadeMode.None;
                group.SetLODs(levels); group.RecalculateBounds();
                float edge = tent.extents.x - .06f;
                foreach (float z in FrameZ(tent))
                    foreach (int side in new[] { -1, 1 })
                    {
                        var a = new Vector3(tent.center.x + side * edge, HeightAt(tent, floorY, tent.center.x + side * edge, z) - .10f, z);
                        var b = new Vector3(tent.center.x, HeightAt(tent, floorY, tent.center.x, z) - .10f, z);
                        Beam(root.transform, a, b, metal, "Поперечная балка ската");
                    }
                foreach (float x in new[] { tent.center.x - edge, tent.center.x, tent.center.x + edge })
                    Beam(root.transform, new Vector3(x, HeightAt(tent, floorY, x, tent.min.z) - .10f, tent.min.z + .08f),
                        new Vector3(x, HeightAt(tent, floorY, x, tent.max.z) - .10f, tent.max.z - .08f), metal, "Продольная балка");
                LastReport.status = "built_connected_requires_native_preview";
                GameLog.Debug.Info("[LobbyDecoration] Связная крыша: 5 LOD, конёк Z, внутренних boundary edges 0; нужна нативная визуальная приёмка.");
            }
            catch (Exception exception)
            { LastReport.status = "failed"; LastReport.failure = exception.Message; if (root != null) UnityEngine.Object.DestroyImmediate(root); throw; }
            finally
            {
                string path = Path.Combine(Application.dataPath, "../tasks/lobby-decoration/reports/implementation/interior-roof/latest-build.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(LastReport, true));
            }
        }

        private static Mesh Grid(Bounds tent, float floorY, int nx, int nz)
        {
            var vertices = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[vertices.Length];
            var indices = new int[nx * nz * 6]; int write = 0;
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, v = z / (float)nz;
                float px = Mathf.Lerp(tent.min.x, tent.max.x, u), pz = Mathf.Lerp(tent.min.z, tent.max.z, v);
                int index = z * (nx + 1) + x;
                vertices[index] = new Vector3(px - tent.center.x, HeightAt(tent, floorY, px, pz), pz - tent.center.z);
                uv[index] = new Vector2(u, v);
            }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                indices[write++] = a; indices[write++] = c; indices[write++] = b;
                indices[write++] = b; indices[write++] = c; indices[write++] = d;
            }
            var mesh = new Mesh { name = "ConnectedTentRoof_" + nx + "x" + nz };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }

        private static void Audit(Mesh mesh, Bounds tent, LodReport report)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles; var edges = new Dictionary<long, int>();
            void Edge(int a, int b)
            { long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b); edges.TryGetValue(key, out int count); edges[key] = count + 1; }
            for (int i = 0; i < triangles.Length; i += 3)
            { Edge(triangles[i], triangles[i + 1]); Edge(triangles[i + 1], triangles[i + 2]); Edge(triangles[i + 2], triangles[i]); }
            foreach (var edge in edges)
            {
                if (edge.Value > 2) report.nonManifoldEdges++;
                if (edge.Value != 1) continue;
                report.boundaryEdges++;
                Vector3 a = vertices[(int)(edge.Key >> 32)], b = vertices[(int)(edge.Key & uint.MaxValue)];
                bool perimeter = (Mathf.Abs(a.x + tent.extents.x) < .001f && Mathf.Abs(b.x + tent.extents.x) < .001f)
                    || (Mathf.Abs(a.x - tent.extents.x) < .001f && Mathf.Abs(b.x - tent.extents.x) < .001f)
                    || (Mathf.Abs(a.z + tent.extents.z) < .001f && Mathf.Abs(b.z + tent.extents.z) < .001f)
                    || (Mathf.Abs(a.z - tent.extents.z) < .001f && Mathf.Abs(b.z - tent.extents.z) < .001f);
                if (!perimeter) report.internalBoundaryEdges++;
            }
            report.vertices = vertices.Length; report.triangles = triangles.Length / 3; report.outputBounds = mesh.bounds;
            if (report.internalBoundaryEdges != 0 || report.nonManifoldEdges != 0 || report.boundaryEdges != 2 * (report.cellsX + report.cellsZ))
                throw new InvalidOperationException("Крыша имеет повреждённую связность/внутреннюю границу.");
        }
        private static float[] FrameZ(Bounds tent) => new[] { tent.min.z + .28f, tent.center.z - 3.6f, tent.center.z, tent.center.z + 3.6f, tent.max.z - .28f };

        private static Material ClothMaterial(Texture2D diffuse, Texture2D normal)
        {
            var material = OwnMaterial("RoofCloth");
            material.SetTexture("_BaseMap", diffuse); material.SetTexture("_BumpMap", normal);
            foreach (string property in new[] { "_BaseMap", "_BumpMap" })
            { material.SetTextureScale(property, new Vector2(.22f, .15f)); material.SetTextureOffset(property, new Vector2(.13f, .30f)); }
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_BumpScale", .25f);
            material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .10f); material.SetFloat("_Cull", 0);
            material.EnableKeyword("_NORMALMAP"); EditorUtility.SetDirty(material); return material;
        }
        private static Material MetalMaterial()
        {
            var material = OwnMaterial("RoofMetal"); material.SetColor("_BaseColor", new Color(.16f, .18f, .15f));
            material.SetFloat("_Metallic", .65f); material.SetFloat("_Smoothness", .22f); material.SetFloat("_Cull", 2);
            EditorUtility.SetDirty(material); return material;
        }
        private static Material OwnMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit"); if (shader == null) throw new InvalidOperationException("Нет URP/Lit.");
            string path = MaterialFolder + "/" + name + ".mat";
            var generated = new Material(shader) { name = name }; var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            { EditorUtility.CopySerialized(generated, existing); UnityEngine.Object.DestroyImmediate(generated); return existing; }
            AssetDatabase.CreateAsset(generated, path); return generated;
        }
        private static void Beam(Transform parent, Vector3 start, Vector3 end, Material metal, string name)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = name; item.transform.SetParent(parent, false);
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            item.transform.SetPositionAndRotation((start + end) * .5f, Quaternion.LookRotation(end - start));
            item.transform.localScale = new Vector3(.08f, .10f, Vector3.Distance(start, end));
            var renderer = item.GetComponent<MeshRenderer>(); renderer.sharedMaterial = metal; ConfigureRenderer(renderer); LastReport.frameBeams++;
        }
        private static void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.OccludeeStatic);
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(parent)) throw new InvalidOperationException("Нет родительской папки " + parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

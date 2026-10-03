using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Отдельный вариант забора: сплошной низ Low 1.2 м, сетка выше, исходник пака сохраняется.</summary>
    public static class ConcreteFenceLowBase
    {
        public const string PrefabPath = "Assets/Prefabs/LevelDesign/Decorated/ConcreteFence_LowBase_Soft.prefab";
        private const string SourcePath = "Assets/RPG_FPS_game_assets_industrial/Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_S.prefab";
        private const string MeshFolder = "Assets/Art/Models/CatalogVariants";
        public const float LowHeight = 1.2f;
        public const float TotalHeight = 1.6f;

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Менять вариант забора можно только вне Play Mode.");
            EnsureFolder(MeshFolder);
            EnsureFolder(System.IO.Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var root = PrefabUtility.LoadPrefabContents(SourcePath);
            try
            {
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                var body = filters.First(f => f.name == "Concrete_fence_v2_S");
                var lattice = filters.First(f => f.name.Contains("lattice"));
                var ys = body.sharedMesh.vertices.Select(v => body.transform.TransformPoint(v).y).ToArray();
                float min = ys.Min(), max = ys.Max();
                float cut = ys.Where(y => y < 0).Max();
                float horizontalScale = TotalHeight / (max - min);
                float upperRatio = (TotalHeight - LowHeight) / ((max - cut) * horizontalScale);
                root.name = "ConcreteFence_LowBase_Soft";
                foreach (var filter in filters)
                {
                    var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                    mesh.name = filter.name + "_LowBase";
                    var vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var p = filter.transform.TransformPoint(vertices[i]);
                        p.x *= horizontalScale;
                        p.z *= horizontalScale;
                        p.y = p.y <= cut
                            ? (p.y - min) * LowHeight / (cut - min)
                            : LowHeight + (p.y - cut) * (TotalHeight - LowHeight) / (max - cut);
                        vertices[i] = filter.transform.InverseTransformPoint(p);
                    }
                    mesh.vertices = vertices;
                    if (filter == lattice)
                    {
                        var uv = mesh.uv;
                        float origin = uv.Min(p => p.y);
                        for (int i = 0; i < uv.Length; i++) uv[i].y = origin + (uv[i].y - origin) * upperRatio;
                        mesh.uv = uv;
                    }
                    mesh.RecalculateBounds();
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    string path = MeshFolder + "/" + mesh.name + ".asset";
                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                    else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                    filter.sharedMesh = saved;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                root.transform.rotation = Quaternion.Euler(0, 90, 0);
                var box = root.AddComponent<BoxCollider>();
                float length = body.sharedMesh.bounds.size.z;
                box.size = new Vector3(0.1f, LowHeight, length);
                box.center = new Vector3(0, LowHeight * 0.5f, 0);
                CoverClass expected;
                if (!CoverClassRules.TryExpectedClass(root, out expected)) throw new InvalidOperationException("Неверное имя Soft-варианта.");
                (root.GetComponent<CoverSurface>() ?? root.AddComponent<CoverSurface>()).Class = expected;
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new IOException("Вариант забора не сохранён.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // Декоративный вариант не изменяет эталоны блокинга LD_Alphabet.
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}

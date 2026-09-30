using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Одевает блоки карт <c>LD_Alphabet</c> в сетку пака <c>UnityStarter_Robot/Environment</c> без растяжения.
    ///
    /// <para>
    /// Блоки — стандартные куб, цилиндр и <c>DoritoMesh</c> с неравномерным масштабом корня (стена 2×2.5×0.15 м).
    /// Штатные UV кладут текстуру 0..1 на каждую грань, и на разных гранях сетка растянута по-разному; тайлинг
    /// материала один на все грани и этого не исправит. Инструмент строит для каждого блока свой меш
    /// (<see cref="MeshFolder" />) с UV в метрах: плоские грани — проекция на плоскость грани, бока цилиндра —
    /// развёртка по окружности. Один повтор текстуры = <see cref="TileMeters" /> м, клетка сетки — 0.5 м.
    /// Масштаб корня, коллайдеры и <c>MeshCollider</c> Dorito не трогаются: меняются только <c>MeshFilter</c>
    /// и материал.
    /// </para>
    /// <para>
    /// Меш запекается под масштаб префаба. Экземпляр блока, растянутый в сцене, снова растянет сетку — нужен
    /// другой размер, заводи новый префаб в <see cref="Blocks" /> и перезапусти. Проверка —
    /// <c>LevelDesignBlockTextureTests</c>.
    /// </para>
    /// </summary>
    public static class LevelDesignBlockTexturer
    {
        private const string MenuPath       = "Tools/VR Battlegrounds/Gameplay/Texture LD Blocks";
        private const string BlocksFolder   = "Assets/Prefabs/LevelDesign/LD_Alphabet/";
        private const string MeshFolder     = "Assets/Art/Models/LevelDesign/";
        private const string MaterialFolder = "Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/";
        private const string DoritoMesh     = "Assets/Art/Models/DoritoMesh.asset";

        /// <summary>Метров на один повтор текстуры: в текстуре 6×6 клеток, клетка — 0.5 м.</summary>
        public const float TileMeters = 3f;

        private enum Shape { Cube, Cylinder, Dorito }

        private const string Walls  = "GridWhite_01_Mat.mat";
        private const string Covers = "GridOrange_01_Mat.mat";
        private const string Round  = "GridBlue_01_Mat.mat";

        /// <summary>
        /// Блок → исходная форма и материал. Стены — белая сетка, низкие укрытия — оранжевая (видны издалека),
        /// цилиндры и столбы — синяя.
        /// </summary>
        private static readonly (string prefab, Shape shape, string material)[] Blocks =
        {
            ("LD_Wall_Tall",     Shape.Cube,     Walls),
            ("LD_Wall_Mid",      Shape.Cube,     Walls),
            ("LD_Block_Low",     Shape.Cube,     Covers),
            ("LD_Snake_Segment", Shape.Cube,     Covers),
            ("LD_Crate",         Shape.Cube,     Covers),
            ("LD_Dorito_Mid",    Shape.Dorito,   Covers),
            ("LD_PillarBox",     Shape.Cube,     Round),
            ("LD_Beam_Low",      Shape.Cylinder, Round),
            ("LD_Can_Mid",       Shape.Cylinder, Round),
            ("LD_Tree_Tall",     Shape.Cylinder, Round),
            // Перешагиваемый заборчик (LD-48) — синий, чтобы не путать с оранжевым Low, который перешагивать нельзя.
            ("LD_Fence_Vault",   Shape.Cube,     Round),
            // Проёмы (LD-18): подоконник, перемычки окна и двери — белые, как стена, в которую встают.
            ("LD_Window_Sill",   Shape.Cube,     Walls),
            ("LD_Window_Lintel", Shape.Cube,     Walls),
            ("LD_Door_Lintel",   Shape.Cube,     Walls),
            // Прострел (T-41, LD-28): Soft — гладкий серо-синий (тонкое, пробивается), Visual — гладкий синий (сетка).
            ("LD_Wall_Tall_Soft",  Shape.Cube,   SoftCover),
            ("LD_Wall_Mid_Soft",   Shape.Cube,   SoftCover),
            ("LD_Net_Tall_Visual", Shape.Cube,   VisualCover),
            // Плитка пола 10×10 м на слое Ground — стенды вне арены (TestMap3), по ней телепорт.
            ("LD_Floor_Tile",      Shape.Cube,   Walls),
        };

        private const string SoftCover   = "GreyBlue_Mat.mat";
        private const string VisualCover = "Blue_Mat.mat";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder.TrimEnd('/')))
                AssetDatabase.CreateFolder("Assets/Art/Models", "LevelDesign");

            foreach (var (name, shape, materialFile) in Blocks)
            {
                string prefabPath = BlocksFolder + name + ".prefab";
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + materialFile);
                if (material == null)
                {
                    GameLog.Error($"[LevelDesignBlockTexturer] Нет материала {MaterialFolder + materialFile}");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var filter = root.GetComponent<MeshFilter>();
                    var renderer = root.GetComponent<MeshRenderer>();
                    if (filter == null || renderer == null)
                    {
                        GameLog.Error($"[LevelDesignBlockTexturer] {name}: у корня нет MeshFilter/MeshRenderer");
                        continue;
                    }

                    Mesh mesh = Build(SourceMesh(shape), shape, root.transform.localScale, name + "_Mesh");
                    filter.sharedMesh = SaveMesh(mesh, MeshFolder + name + "_Mesh.asset");

                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[LevelDesignBlockTexturer] Блоков одето: {Blocks.Length}");
        }

        private static Mesh SourceMesh(Shape shape) => shape switch
        {
            Shape.Cube     => Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
            Shape.Cylinder => Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"),
            _              => AssetDatabase.LoadAssetAtPath<Mesh>(DoritoMesh),
        };

        /// <summary>
        /// Раскрывает меш в несвязанные треугольники (у каждого свои UV) и считает UV в метрах масштабированной
        /// геометрии. Позиции и нормали — как у исходника, в локальных единицах.
        /// </summary>
        private static Mesh Build(Mesh source, Shape shape, Vector3 scale, string meshName)
        {
            Vector3[] srcV = source.vertices;
            Vector3[] srcN = source.normals;
            Bounds b = source.bounds;
            Vector3 min = Vector3.Scale(b.min, scale);
            Vector3 center = Vector3.Scale(b.center, scale);
            float radius = Vector3.Scale(b.extents, scale).x;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();

            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                var outTris = new int[tris.Length];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    var p = new Vector3[3];
                    for (int k = 0; k < 3; k++) p[k] = Vector3.Scale(srcV[tris[t + k]], scale) - min;
                    Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;

                    Vector2[] uv = shape == Shape.Cylinder && Mathf.Abs(n.y) < 0.5f
                        ? CylinderSide(p, center - min, radius)
                        : Planar(p, n);

                    for (int k = 0; k < 3; k++)
                    {
                        outTris[t + k] = verts.Count;
                        verts.Add(srcV[tris[t + k]]);
                        normals.Add(srcN.Length > 0 ? srcN[tris[t + k]] : n);
                        uvs.Add(uv[k] / TileMeters);
                    }
                }
                submeshes.Add(outTris);
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Проекция на плоскость грани: u — по горизонтали грани, v — вверх по грани (или по Z для горизонтальной).</summary>
        private static Vector2[] Planar(Vector3[] p, Vector3 n)
        {
            Vector3 u, v;
            if (Mathf.Abs(n.y) > 0.99f)
            {
                u = Vector3.right;
                v = Vector3.forward;
            }
            else
            {
                u = Vector3.Cross(Vector3.up, n).normalized;
                v = Vector3.Cross(n, u);
            }
            var uv = new Vector2[3];
            for (int k = 0; k < 3; k++) uv[k] = new Vector2(Vector3.Dot(p[k], u), Vector3.Dot(p[k], v));
            return uv;
        }

        /// <summary>Развёртка бока цилиндра: u — длина дуги, v — высота. Треугольник на шве не рвётся.</summary>
        private static Vector2[] CylinderSide(Vector3[] p, Vector3 axis, float radius)
        {
            var angle = new float[3];
            for (int k = 0; k < 3; k++) angle[k] = Mathf.Atan2(p[k].z - axis.z, p[k].x - axis.x);
            for (int k = 1; k < 3; k++)
            {
                if (angle[k] - angle[0] > Mathf.PI) angle[k] -= 2f * Mathf.PI;
                if (angle[0] - angle[k] > Mathf.PI) angle[k] += 2f * Mathf.PI;
            }
            var uv = new Vector2[3];
            for (int k = 0; k < 3; k++) uv[k] = new Vector2(angle[k] * radius, p[k].y);
            return uv;
        }

        /// <summary>Перезаписывает меш в существующий ассет, чтобы GUID и ссылки префаба не менялись.</summary>
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}

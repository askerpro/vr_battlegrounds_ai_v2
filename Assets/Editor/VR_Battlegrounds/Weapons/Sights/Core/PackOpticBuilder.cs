using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Writer входов оптического комплекта из модели прицела пака (задача arsenal-combat-lineup, 2026-10-23): меш модели
    /// запекается в метрах и в осях комплекта (+Z — к дулу, +Y — вверх; сборщик комплекта масштаб не ставит), стекло
    /// окуляра — отдельный submesh для сетки конечного фокуса и картинки камеры кратности. Стекло — задняя половина
    /// стекла пака (<see cref="Spec.GlassPart"/>, переднее остаётся в корпусе) или, если стекла у модели нет (пак Low Poly),
    /// диск в заднем торце трубы по кольцу вершин торца. Выход — меш с двумя submesh (корпус, стекло) и материал сетки;
    /// их берёт <see cref="SightKitBuilder.Optics"/>.
    /// </summary>
    public static class PackOpticBuilder
    {
        public sealed class Spec
        {
            public string Name, SourceModel, MeshPath, ReticleMaterialPath;
            /// <summary>Объект модели со стеклом (обе линзы); null — стекла нет, окуляр — сгенерированный диск.</summary>
            public string GlassPart;
            /// <summary>Множитель к единицам импортированного меша пака.</summary>
            public float Scale = 1f;
            /// <summary>Поворот меша пака в оси комплекта: после него ось трубы — +Z (объектив впереди), верх — +Y.</summary>
            public Vector3 Euler;
            /// <summary>Только для диска: доля длины трубы у заднего торца, вершины которой задают радиус окуляра.</summary>
            public float RingBand = 0.04f;
            /// <summary>Только для диска: стекло внутри кольца торца, чуть утоплено, чтобы не мерцать с корпусом.</summary>
            public float RadiusFactor = 0.85f, Inset = 0.002f;
            /// <summary>Сетка: 0 — кольцо с точкой (как XPS2), 1 — перекрестие; радиусы — углы, рад.</summary>
            public float ReticleStyle, RingRadius = 0.01f, RingWidth = 0.0004f, DotRadius = 0.0003f;
            public Color ReticleColor = new Color(1f, 0.025f, 0.012f, 1f);
            /// <summary>Цвет точки в центре; alpha 0 — цвет сетки.</summary>
            public Color DotColor = new Color(0f, 0f, 0f, 0f);
            /// <summary>Стекло вне увеличения; alpha 1 — непрозрачное (корпус пака не просвечивает).</summary>
            public Color GlassTint = new Color(0.08f, 0.12f, 0.15f, 0.06f);
        }

        public const string Folder = "Assets/Art/Weapons/Sights/Pack";
        private const string HandsDetails = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Weapons_Details/";
        public const int LensMaterialIndex = 1;
        private const int Segments = 32;

        /// <summary>
        /// Призменный прицел ×2 пака Hands (<c>Sight_08</c>) для SniperRifleAuto (решение пользователя 2026-10-23). Размер
        /// пака — реальный (труба 133 мм), оси пака совпадают с осями комплекта: объектив — +Z (торец шире, 51,5 против
        /// 50 мм), окуляр — −Z. Сетка снайперская (шлем 2026-10-23: «ближе к снайперской винтовке», не точка коллиматора):
        /// тонкое чёрное перекрестие, толстые столбики дальше 6 мрад, красная точка 0,6 мрад. Стекло вне увеличения
        /// непрозрачное тёмное: модель пака односторонняя, сквозь прозрачное стекло просвечивал корпус изнутри.
        /// </summary>
        public static readonly Spec Sight08 = new Spec
        {
            Name = "Sight08", SourceModel = HandsDetails + "Sight/Mesh/Sight_08_Mesh.FBX", GlassPart = "Sight_Glasses_8_Mesh",
            MeshPath = Folder + "/Sight08_Optic.asset", ReticleMaterialPath = Folder + "/Sight08_Reticle.mat",
            ReticleStyle = 1f, RingRadius = 0.006f, RingWidth = 0.0005f, DotRadius = 0.0006f,
            ReticleColor = new Color(0.01f, 0.01f, 0.01f, 1f), DotColor = new Color(1f, 0.025f, 0.012f, 1f),
            GlassTint = new Color(0.03f, 0.045f, 0.06f, 1f),
        };

        public static readonly Spec[] All = { Sight08 };

        public static string[] OutputPaths() => All.SelectMany(s => new[] { s.MeshPath, s.ReticleMaterialPath }).ToArray();

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sights/Build Pack Optics")]
        private static void BuildMenu()
        {
            try { GameLog.WeaponSystem.Info("[PackOpticBuilder] " + Build()); }
            catch (Exception e) { GameLog.Error("[PackOpticBuilder] " + e.Message); throw; }
        }

        public static string Build() => string.Join("\n", All.Select(Build));

        public static string Build(Spec spec)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Сборка оптики — только в Edit Mode");
            var parts = ReadSource(spec);
            Mesh mesh = Bake(spec, parts, out float radius, out float length);
            // До записи: SaveMesh переносит данные в существующий ассет и уничтожает временный меш.
            string triangles = $"{mesh.GetIndexCount(0) / 3} + {mesh.GetIndexCount(1) / 3} треугольников";
            EnsureFolder(Folder);
            SaveMesh(mesh, spec.MeshPath);
            Material reticle = EnsureReticle(spec);
            return $"{spec.Name}: труба {length * 1000:0} мм, окуляр Ø{radius * 2000:0.0} мм, {triangles}; " +
                   $"{spec.MeshPath}, {AssetDatabase.GetAssetPath(reticle)}";
        }

        /// <summary>Все меши модели пака с их позой относительно корня модели.</summary>
        private static List<(string name, Mesh mesh, Matrix4x4 toRoot)> ReadSource(Spec spec)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.SourceModel);
            if (model == null) throw new InvalidOperationException($"{spec.Name}: нет модели {spec.SourceModel}");
            var parts = new List<(string, Mesh, Matrix4x4)>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                    throw new InvalidOperationException($"{spec.Name}: меш {mesh.name} без Read/Write — включить в импорте {spec.SourceModel}");
                parts.Add((filter.name, mesh, model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix));
            }
            if (parts.Count == 0) throw new InvalidOperationException($"{spec.Name}: в {spec.SourceModel} нет мешей");
            if (spec.GlassPart != null && parts.All(p => p.Item1 != spec.GlassPart))
                throw new InvalidOperationException($"{spec.Name}: нет стекла {spec.GlassPart}. Есть: {string.Join(", ", parts.Select(p => p.Item1))}");
            return parts;
        }

        /// <summary>Корпус в submesh 0, стекло окуляра — submesh 1.</summary>
        private static Mesh Bake(Spec spec, List<(string name, Mesh mesh, Matrix4x4 toRoot)> parts, out float radius, out float length)
        {
            Matrix4x4 bake = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(spec.Euler), Vector3.one * spec.Scale);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var housing = new List<int>();
            var lens = new List<int>();
            radius = 0f;
            foreach (var (name, part, toRoot) in parts)
            {
                Matrix4x4 m = bake * toRoot;
                int offset = vertices.Count;
                Vector3[] v = part.vertices, n = part.normals;
                Vector2[] t = part.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    vertices.Add(m.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length == v.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvs.Add(t.Length == v.Length ? t[i] : Vector2.zero);
                }
                var triangles = new List<int>();
                for (int sub = 0; sub < part.subMeshCount; sub++) triangles.AddRange(part.GetTriangles(sub).Select(index => index + offset));
                if (name != spec.GlassPart) { housing.AddRange(triangles); continue; }

                // Стекло пака: задняя половина (окуляр) — линза, передняя (объектив) остаётся в корпусе.
                float middle = 0f;
                for (int i = offset; i < vertices.Count; i++) middle += vertices[i].z;
                middle /= v.Length;
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    float z = (vertices[triangles[i]].z + vertices[triangles[i + 1]].z + vertices[triangles[i + 2]].z) / 3f;
                    (z < middle ? lens : housing).AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
                }
                var rear = lens.Select(i => vertices[i]).ToArray();
                if (rear.Length == 0) throw new InvalidOperationException($"{spec.Name}: у стекла {name} нет задней половины");
                var center = new Vector2(rear.Average(p => p.x), rear.Average(p => p.y));
                radius = rear.Max(p => (new Vector2(p.x, p.y) - center).magnitude);
            }

            float minZ = vertices.Min(p => p.z), maxZ = vertices.Max(p => p.z);
            length = maxZ - minZ;
            if (spec.GlassPart == null) radius = AddEyepieceDisc(spec, vertices, normals, uvs, lens, minZ, length);

            var mesh = new Mesh { name = spec.Name + "_Optic" };
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(housing, 0);
            mesh.SetTriangles(lens, LensMaterialIndex);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Стекла у модели нет: диск в заднем торце трубы, радиус — по кольцу вершин торца.</summary>
        private static float AddEyepieceDisc(Spec spec, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> lens,
                                             float minZ, float length)
        {
            float band = minZ + length * spec.RingBand;
            var ring = vertices.Where(p => p.z <= band).ToArray();
            if (ring.Length < 6) throw new InvalidOperationException($"{spec.Name}: у заднего торца {ring.Length} вершин — ось трубы не +Z?");
            var center = new Vector2(ring.Average(p => p.x), ring.Average(p => p.y));
            var distances = ring.Select(p => (new Vector2(p.x, p.y) - center).magnitude).OrderBy(d => d).ToArray();
            float radius = distances[distances.Length / 2] * spec.RadiusFactor;
            if (radius < 0.003f) throw new InvalidOperationException($"{spec.Name}: радиус окуляра {radius * 1000:0.0} мм — не кольцо");

            int first = vertices.Count;
            float z = minZ + spec.Inset;
            vertices.Add(new Vector3(center.x, center.y, z));
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                vertices.Add(new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, z));
            }
            for (int i = 0; i <= Segments; i++)
            {
                normals.Add(Vector3.back);
                uvs.Add(Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                lens.Add(first);
                lens.Add(first + 1 + (i + 1) % Segments);
                lens.Add(first + 1 + i);
            }
            return radius;
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return; }
            // На месте: GUID и ссылки комплекта сохраняются.
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        private static Material EnsureReticle(Spec spec)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(spec.ReticleMaterialPath);
            Shader shader = Shader.Find(WeaponOpticView.ShaderName);
            if (shader == null) throw new InvalidOperationException("Нет шейдера " + WeaponOpticView.ShaderName);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(spec.ReticleMaterialPath) };
                AssetDatabase.CreateAsset(material, spec.ReticleMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_ReticleColor", spec.ReticleColor);
            material.SetColor("_DotColor", spec.DotColor);
            material.SetColor("_GlassTint", spec.GlassTint);
            material.SetFloat("_ReticleStyle", spec.ReticleStyle);
            material.SetFloat("_RingRadius", spec.RingRadius);
            material.SetFloat("_RingWidth", spec.RingWidth);
            material.SetFloat("_DotRadius", spec.DotRadius);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}

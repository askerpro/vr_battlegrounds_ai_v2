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
    /// Writer входов оптического комплекта из пака Low Poly Weapons VOL.2 (задача arsenal-combat-lineup, 2026-10-23).
    /// Атлас пака без альфы, поэтому окно не выделяется маской, как у XPS2 (<c>Tr15OpticBuilder</c>): меш оптики пака
    /// запекается в метрах и в осях комплекта (+Z — к дулу, +Y — вверх; сборщик комплекта масштаб не ставит), а стекло
    /// окуляра — отдельный submesh-диск в заднем торце трубы. Радиус диска — по кольцу вершин торца. Выход — меш с
    /// двумя submesh (корпус, стекло) и материал сетки; их берёт <see cref="SightKitBuilder.Optics"/>.
    /// </summary>
    public static class LowPolyOpticBuilder
    {
        public sealed class Spec
        {
            public string Name, SourceModel, MeshPath, ReticleMaterialPath;
            /// <summary>Множитель к единицам импортированного меша пака (масштаб корня оружия × масштаб оптики в примере пака).</summary>
            public float Scale;
            /// <summary>Поворот меша пака в оси комплекта: после него ось трубы — +Z (объектив впереди), верх — +Y.</summary>
            public Vector3 Euler;
            /// <summary>Доля длины трубы у заднего торца, вершины которой задают радиус окуляра.</summary>
            public float RingBand = 0.04f;
            /// <summary>Стекло — внутри кольца торца, чуть утоплено, чтобы не мерцать с корпусом.</summary>
            public float RadiusFactor = 0.85f, Inset = 0.002f;
            public Color ReticleColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        }

        public const string Folder = "Assets/Art/Weapons/Sights/LowPoly";
        private const string PackFolder = "Assets/ThirdParty/Low Poly Weapons VOL.2/";
        public const int LensMaterialIndex = 1;
        private const int Segments = 32;

        // Оптика как в примере пака Custom_sniper_03: ×1,223 к корпусу sniper_03, а корень Barrett — ×1,24 / 1,2352
        // (длина прототипа к длине модели). Объектив пака смотрит по −Z (к дулу, как ствол пака) — поворот 180° по Y.
        public static readonly Spec Barrett = new Spec
        {
            Name = "M82Scope", SourceModel = PackFolder + "Models/optic_02_black.fbx",
            MeshPath = Folder + "/M82Scope_Optic.asset", ReticleMaterialPath = Folder + "/M82Scope_Reticle.mat",
            Scale = 1.223f * 1.24f / 1.2352f, Euler = new Vector3(0f, 180f, 0f),
        };

        public static readonly Spec[] All = { Barrett };

        public static string[] OutputPaths() => All.SelectMany(s => new[] { s.MeshPath, s.ReticleMaterialPath }).ToArray();

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sights/Build Low Poly Optics")]
        private static void BuildMenu()
        {
            try { GameLog.WeaponSystem.Info("[LowPolyOpticBuilder] " + Build()); }
            catch (Exception e) { GameLog.Error("[LowPolyOpticBuilder] " + e.Message); throw; }
        }

        public static string Build() => string.Join("\n", All.Select(Build));

        public static string Build(Spec spec)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Сборка оптики — только в Edit Mode");
            var parts = ReadSource(spec);
            Mesh mesh = Bake(spec, parts, out float radius, out float length);
            EnsureFolder(Folder);
            SaveMesh(mesh, spec.MeshPath);
            Material reticle = EnsureReticle(spec);
            return $"{spec.Name}: труба {length * 1000:0} мм, окуляр Ø{radius * 2000:0.0} мм, " +
                   $"{mesh.GetIndexCount(0) / 3} + {mesh.GetIndexCount(1) / 3} треугольников; {spec.MeshPath}, {AssetDatabase.GetAssetPath(reticle)}";
        }

        /// <summary>Все меши модели пака (труба, кольца) с их позой относительно корня модели.</summary>
        private static List<(Mesh mesh, Matrix4x4 toRoot)> ReadSource(Spec spec)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.SourceModel);
            if (model == null) throw new InvalidOperationException($"{spec.Name}: нет модели {spec.SourceModel}");
            var parts = new List<(Mesh, Matrix4x4)>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                    throw new InvalidOperationException($"{spec.Name}: меш {mesh.name} без Read/Write — включить в импорте {spec.SourceModel}");
                parts.Add((mesh, model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix));
            }
            if (parts.Count == 0) throw new InvalidOperationException($"{spec.Name}: в {spec.SourceModel} нет мешей");
            return parts;
        }

        /// <summary>Корпус (все треугольники пака) в submesh 0, стекло окуляра — submesh 1.</summary>
        private static Mesh Bake(Spec spec, List<(Mesh part, Matrix4x4 toRoot)> parts, out float radius, out float length)
        {
            Matrix4x4 bake = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(spec.Euler), Vector3.one * spec.Scale);
            var vertexList = new List<Vector3>();
            var normalList = new List<Vector3>();
            var uvList = new List<Vector2>();
            var housing = new List<int>();
            foreach (var (part, toRoot) in parts)
            {
                Matrix4x4 m = bake * toRoot;
                int offset = vertexList.Count;
                Vector3[] v = part.vertices, n = part.normals;
                Vector2[] t = part.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    vertexList.Add(m.MultiplyPoint3x4(v[i]));
                    normalList.Add(n.Length == v.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvList.Add(t.Length == v.Length ? t[i] : Vector2.zero);
                }
                for (int sub = 0; sub < part.subMeshCount; sub++)
                    housing.AddRange(part.GetTriangles(sub).Select(index => index + offset));
            }
            Vector3[] vertices = vertexList.ToArray();
            Vector3[] normals = normalList.ToArray();
            Vector2[] uv = uvList.ToArray();

            float minZ = vertices.Min(v => v.z), maxZ = vertices.Max(v => v.z);
            length = maxZ - minZ;
            float band = minZ + length * spec.RingBand;
            var ring = vertices.Where(v => v.z <= band).ToArray();
            if (ring.Length < 6) throw new InvalidOperationException($"{spec.Name}: у заднего торца {ring.Length} вершин — ось трубы не +Z?");
            var center = new Vector2(ring.Average(v => v.x), ring.Average(v => v.y));
            var distances = ring.Select(v => (new Vector2(v.x, v.y) - center).magnitude).OrderBy(d => d).ToArray();
            radius = distances[distances.Length / 2] * spec.RadiusFactor;
            if (radius < 0.003f) throw new InvalidOperationException($"{spec.Name}: радиус окуляра {radius * 1000:0.0} мм — не кольцо");

            var allVertices = new List<Vector3>(vertices);
            var allNormals = new List<Vector3>(normals);
            var allUv = new List<Vector2>(uv);
            var lens = new List<int>();
            int first = allVertices.Count;
            float z = minZ + spec.Inset;
            allVertices.Add(new Vector3(center.x, center.y, z));
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                allVertices.Add(new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, z));
            }
            for (int i = 0; i <= Segments; i++)
            {
                allNormals.Add(Vector3.back);
                allUv.Add(Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                lens.Add(first);
                lens.Add(first + 1 + (i + 1) % Segments);
                lens.Add(first + 1 + i);
            }

            var mesh = new Mesh { name = spec.Name + "_Optic" };
            mesh.indexFormat = allVertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(allVertices);
            mesh.SetNormals(allNormals);
            mesh.SetUVs(0, allUv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(housing, 0);
            mesh.SetTriangles(lens, LensMaterialIndex);
            mesh.RecalculateBounds();
            return mesh;
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
            material.SetFloat("_ReticleStyle", 1f);   // перекрестие
            material.SetFloat("_RingRadius", 0.004f); // за этим углом нити становятся столбиками
            material.SetFloat("_RingWidth", 0.00025f);
            material.SetFloat("_DotRadius", 0.0002f);
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

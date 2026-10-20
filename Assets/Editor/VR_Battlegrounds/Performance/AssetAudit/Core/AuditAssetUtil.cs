using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Общие измерения для правил: треугольники, габариты, альфа-тест, пути. Только чтение.</summary>
    public static class AuditAssetUtil
    {
        public const int AlphaTestQueueMin = (int)RenderQueue.AlphaTest;   // 2450
        public const int AlphaTestQueueMax = (int)RenderQueue.GeometryLast; // 2500

        /// <summary>Треугольники меша по индексам (без чтения вершин; Read/Write не нужен).</summary>
        public static long TriangleCount(Mesh mesh)
        {
            if (mesh == null)
                return 0;
            long triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    triangles += mesh.GetIndexCount(i) / 3;
            }
            return triangles;
        }

        /// <summary>Меш рендерера: MeshFilter для MeshRenderer, sharedMesh для Skinned.</summary>
        public static Mesh GetMesh(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    return skinned.sharedMesh;
                case MeshRenderer _:
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    return filter != null ? filter.sharedMesh : null;
                default:
                    return null;
            }
        }

        public static long TriangleCount(Renderer renderer) => TriangleCount(GetMesh(renderer));

        /// <summary>Мировые габариты по bounds меша и матрице объекта: работает и для ассета префаба, не только в сцене.</summary>
        public static Bounds WorldBounds(Renderer renderer)
        {
            Mesh mesh = GetMesh(renderer);
            if (mesh == null)
                return renderer.bounds;
            Bounds local = mesh.bounds;
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 world = matrix.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Площадь наибольшей грани AABB, м² — грубая оценка экранной площади крупной поверхности.</summary>
        public static float LargestFaceArea(Bounds bounds)
        {
            Vector3 s = bounds.size;
            return Mathf.Max(s.x * s.y, Mathf.Max(s.x * s.z, s.y * s.z));
        }

        /// <summary>Материал с альфа-тестом (clip/discard): очередь AlphaTest, _ALPHATEST_ON или _AlphaClip.</summary>
        public static bool IsAlphaTested(Material material)
        {
            if (material == null)
                return false;
            if (material.renderQueue >= AlphaTestQueueMin && material.renderQueue < AlphaTestQueueMax)
                return true;
            if (material.IsKeywordEnabled("_ALPHATEST_ON"))
                return true;
            return material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f;
        }

        /// <summary>LODGroup, в LOD-уровнях которого числится рендерер; уровень — через out (-1, если нет).</summary>
        public static LODGroup FindOwningLodGroup(Renderer renderer, out int lodIndex)
        {
            lodIndex = -1;
            LODGroup group = renderer.GetComponentInParent<LODGroup>(true);
            while (group != null)
            {
                LOD[] lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    foreach (Renderer member in lods[i].renderers)
                    {
                        if (member == renderer)
                        {
                            lodIndex = i;
                            return group;
                        }
                    }
                }
                Transform parent = group.transform.parent;
                group = parent != null ? parent.GetComponentInParent<LODGroup>(true) : null;
            }
            return null;
        }

        /// <summary>Путь объекта в иерархии от корня.</summary>
        public static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            for (Transform t = transform; t != null; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Путь ассета объекта или «имя (встроенный)» для встроенных ресурсов.</summary>
        public static string AssetLabel(Object asset)
        {
            if (asset == null)
                return "(нет)";
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return asset.name + " (не ассет)";
            if (path.StartsWith("Resources/unity_builtin", System.StringComparison.Ordinal) || path.StartsWith("Library/", System.StringComparison.Ordinal))
                return asset.name + " (встроенный)";
            return AssetDatabase.IsSubAsset(asset) ? path + "#" + asset.name : path;
        }

        public static string FormatCount(long value)
        {
            if (value >= 10000)
                return (value / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k";
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string JoinCounts(IEnumerable<long> values)
        {
            var builder = new StringBuilder();
            foreach (long value in values)
            {
                if (builder.Length > 0)
                    builder.Append(" / ");
                builder.Append(FormatCount(value));
            }
            return builder.ToString();
        }
    }
}

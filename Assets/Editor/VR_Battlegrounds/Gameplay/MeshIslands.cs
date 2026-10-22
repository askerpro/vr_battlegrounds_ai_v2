using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Острова меша — связные компоненты треугольников после сварки вершин по позиции: швы UV и нормалей остров не
    /// разрывают. Деталь, смоделированная отдельной оболочкой и слитая в меш корпуса (пистолетная рукоять AK105, Mk14,
    /// TR15), — свой остров.
    /// </summary>
    internal static class MeshIslands
    {
        /// <summary>Квант сварки вершин, единицы вершин (у <see cref="PartGeometry" /> — метры осей корня).</summary>
        public const float WeldStep = 1e-5f;

        public sealed class Island
        {
            /// <summary>Номера треугольников (тройка в массиве индексов) по возрастанию.</summary>
            public int[] Triangles;

            /// <summary>Bounds вершин острова в осях вершин.</summary>
            public Bounds Bounds;

            /// <summary>Площадь, квадратные единицы вершин.</summary>
            public float Area;
        }

        /// <summary>Метка острова каждого треугольника: union-find по сваренным вершинам.</summary>
        public static int[] Labels(IReadOnlyList<Vector3> vertices, int[] triangles)
        {
            var ids = new Dictionary<Vector3Int, int>();
            var weld = new int[vertices.Count];
            for (int i = 0; i < weld.Length; i++)
            {
                Vector3 v = vertices[i] / WeldStep;
                var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
                weld[i] = id;
            }

            var parent = new int[ids.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) parent[b] = a;
            }

            int count = triangles.Length / 3;
            for (int t = 0; t < count; t++)
            {
                Union(weld[triangles[t * 3]], weld[triangles[t * 3 + 1]]);
                Union(weld[triangles[t * 3]], weld[triangles[t * 3 + 2]]);
            }
            var labels = new int[count];
            for (int t = 0; t < count; t++) labels[t] = Find(weld[triangles[t * 3]]);
            return labels;
        }

        /// <summary>Остров треугольника <paramref name="seed" />.</summary>
        public static Island Of(IReadOnlyList<Vector3> vertices, int[] triangles, int seed) =>
            Of(vertices, triangles, Labels(vertices, triangles), seed);

        /// <summary>Остров треугольника <paramref name="seed" /> по готовым <paramref name="labels" />.</summary>
        public static Island Of(IReadOnlyList<Vector3> vertices, int[] triangles, int[] labels, int seed)
        {
            var list = new List<int>();
            var bounds = new Bounds(vertices[triangles[seed * 3]], Vector3.zero);
            float area = 0f;
            for (int t = 0; t < labels.Length; t++)
            {
                if (labels[t] != labels[seed]) continue;
                list.Add(t);
                Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return new Island { Triangles = list.ToArray(), Bounds = bounds, Area = area };
        }
    }
}

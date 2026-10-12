using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay.SightSplit
{
    /// <summary>
    /// Острова меша тем же алгоритмом, что разборы iron-split: позиции свариваются по ключу round(p / eps) по каждой
    /// оси (eps = 1e-6 единиц меша), треугольники связываются через общие сваренные вершины (union-find).
    /// Острова считаются по всем подмешам сразу. Vertices — различные индексы вершин Unity, на которые ссылаются
    /// треугольники острова; AABB — по этим вершинам.
    /// </summary>
    public sealed class MeshIslands
    {
        public const double WeldEpsilon = 1e-6;

        public sealed class Island
        {
            /// <summary>Порядковый номер в этом разборе (по минимальному индексу вершины) — только для отчётов.</summary>
            public int Index;
            public int Vertices;
            public int Triangles;
            public Vector3 Min, Max;
            /// <summary>Глобальные номера треугольников (сквозная нумерация по подмешам, см. <see cref="MeshIslands.TriangleSubmesh"/>).</summary>
            public readonly List<int> TriangleIds = new List<int>();
            public readonly HashSet<int> Submeshes = new HashSet<int>();

            public override string ToString() => $"остров {Index} nv{Vertices}/nt{Triangles} [{Min.x:0.######}, {Min.y:0.######}, {Min.z:0.######}]…[{Max.x:0.######}, {Max.y:0.######}, {Max.z:0.######}]";
        }

        public readonly IReadOnlyList<Island> Islands;
        /// <summary>Индексы треугольников по глобальному номеру: tri*3 .. tri*3+2.</summary>
        public readonly int[] Indices;
        /// <summary>Подмеш каждого глобального треугольника.</summary>
        public readonly int[] TriangleSubmesh;
        public int TriangleCount => TriangleSubmesh.Length;

        /// <param name="positions">Позиции вершин в единицах меша.</param>
        /// <param name="submeshIndices">Индексы треугольников по подмешам (с учётом baseVertex).</param>
        public MeshIslands(IReadOnlyList<Vector3> positions, IReadOnlyList<int[]> submeshIndices)
        {
            int total = 0;
            foreach (int[] s in submeshIndices)
            {
                if (s.Length % 3 != 0) throw new ArgumentException("Число индексов подмеша не кратно 3");
                total += s.Length / 3;
            }
            Indices = new int[total * 3];
            TriangleSubmesh = new int[total];
            int t = 0;
            for (int s = 0; s < submeshIndices.Count; s++)
            {
                int[] src = submeshIndices[s];
                Array.Copy(src, 0, Indices, t * 3, src.Length);
                for (int i = 0; i < src.Length / 3; i++) TriangleSubmesh[t + i] = s;
                t += src.Length / 3;
            }

            // Сварка: вершина → номер сваренного ключа.
            var keyOf = new int[positions.Count];
            var keys = new Dictionary<(long, long, long), int>();
            for (int v = 0; v < positions.Count; v++)
            {
                Vector3 p = positions[v];
                var key = (Weld(p.x), Weld(p.y), Weld(p.z));
                if (!keys.TryGetValue(key, out int id)) keys[key] = id = keys.Count;
                keyOf[v] = id;
            }

            var parent = new int[keys.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int tri = 0; tri < total; tri++)
            {
                int a = keyOf[Check(Indices[tri * 3], positions.Count)];
                Union(parent, a, keyOf[Check(Indices[tri * 3 + 1], positions.Count)]);
                Union(parent, a, keyOf[Check(Indices[tri * 3 + 2], positions.Count)]);
            }

            var byRoot = new Dictionary<int, Island>();
            var vertexSets = new Dictionary<Island, HashSet<int>>();
            for (int tri = 0; tri < total; tri++)
            {
                int root = Find(parent, keyOf[Indices[tri * 3]]);
                if (!byRoot.TryGetValue(root, out Island island))
                {
                    byRoot[root] = island = new Island();
                    vertexSets[island] = new HashSet<int>();
                }
                island.TriangleIds.Add(tri);
                island.Submeshes.Add(TriangleSubmesh[tri]);
                for (int k = 0; k < 3; k++) vertexSets[island].Add(Indices[tri * 3 + k]);
            }

            var list = new List<(int minVertex, Island island)>();
            foreach (var pair in vertexSets)
            {
                Island island = pair.Key;
                island.Triangles = island.TriangleIds.Count;
                island.Vertices = pair.Value.Count;
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                int minVertex = int.MaxValue;
                foreach (int v in pair.Value)
                {
                    min = Vector3.Min(min, positions[v]);
                    max = Vector3.Max(max, positions[v]);
                    minVertex = Math.Min(minVertex, v);
                }
                island.Min = min;
                island.Max = max;
                list.Add((minVertex, island));
            }
            list.Sort((x, y) => x.minVertex.CompareTo(y.minVertex));
            var islands = new List<Island>(list.Count);
            foreach (var item in list)
            {
                item.island.Index = islands.Count;
                islands.Add(item.island);
            }
            Islands = islands;
        }

        /// <summary>Единственный остров под селектор; иначе исключение с перечнем кандидатов.</summary>
        public Island Select(SightIslandSelector selector, string context)
        {
            Island found = null;
            int count = 0;
            foreach (Island island in Islands)
            {
                if (!selector.Matches(island)) continue;
                found = island;
                count++;
            }
            if (count == 1) return found;
            throw new InvalidOperationException($"{context}: селектор {selector} нашёл островов {count}, нужен ровно 1");
        }

        private static long Weld(float value) => (long)Math.Round(value / WeldEpsilon, MidpointRounding.ToEven);

        private static int Check(int index, int vertexCount)
        {
            if (index < 0 || index >= vertexCount) throw new ArgumentException($"Индекс вершины {index} вне меша ({vertexCount})");
            return index;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
    }
}

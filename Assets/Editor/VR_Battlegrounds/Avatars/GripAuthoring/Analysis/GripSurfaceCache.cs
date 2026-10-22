using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>Каким способом получен знак «внутри/снаружи».</summary>
    internal enum GripSignMethod
    {
        /// <summary>По нормали ближайшего треугольника (как в «Обхватить»).</summary>
        Normal,
        /// <summary>Уточнён обобщённым winding number по треугольникам в радиусе.</summary>
        Winding,
        /// <summary>Winding вычислен, но близок к нулю; оставлен знак по нормали.</summary>
        WindingAmbiguous
    }

    internal struct GripSurfaceHit
    {
        public bool Valid;
        public float Distance;
        public float Signed;
        public Vector3 Closest;
        public Vector3 Normal;
        public GripSignMethod Method;
        public float Winding;
    }

    /// <summary>
    /// Кэш треугольников предмета в его системе координат. Ближайшая точка ищется по двум равномерным сеткам
    /// (мелкая и крупная, ×8) обходом слоёв ячеек от точки запроса с ранним остановом: после слоя r все
    /// непосещённые треугольники дальше r·h. Результат совпадает с полным перебором (при равных расстояниях
    /// выигрывает меньший индекс, как в переборе «Обхватить»). Winding считается только по треугольникам в радиусе.
    /// </summary>
    internal sealed class GripSurfaceCache
    {
        // |w| ниже этого значения считается неоднозначным: точка почти на поверхности или сетка незамкнута
        const float WindingAmbiguity = 0.1f;
        const float MinCellSize = 0.002f;
        const float MaxCellSize = 0.02f;
        const int CoarseFactor = 8;
        const int FineMaxRing = 4;
        const int MaxCellRefs = 3000000;

        GripTriangle[] _triangles = Array.Empty<GripTriangle>();
        Vector3[] _centers = Array.Empty<Vector3>();
        float[] _radii = Array.Empty<float>();
        int[] _visited = Array.Empty<int>();
        int _stamp;
        TriangleGrid _fine;
        TriangleGrid _coarse;

        // Кластеры для winding: каждый треугольник ровно в одном кластере (ячейка центроида размером ClusterFactor·h)
        const int ClusterFactor = 1;
        // Кластер дальше этого числа своих радиусов заменяется диполем (fast winding number, Barill et al. 2018)
        const float DipoleDistanceFactor = 2.5f;
        Vector3[] _clusterCentroid = Array.Empty<Vector3>();
        Vector3[] _clusterAreaNormal = Array.Empty<Vector3>();
        float[] _clusterRadius = Array.Empty<float>();
        int[] _clusterStart = Array.Empty<int>();
        int[] _clusterCount = Array.Empty<int>();
        int[] _clusterItems = Array.Empty<int>();

        public int Count => _triangles.Length;
        public ulong Key { get; private set; }
        public bool HasKey { get; private set; }
        public float CellSize => _fine != null ? _fine.CellSize : 0f;

        /// <summary>Равномерная разреженная сетка: ячейка → индексы треугольников, чей AABB её задевает.</summary>
        sealed class TriangleGrid
        {
            public readonly float CellSize;
            readonly Vector3 _origin;
            readonly Dictionary<long, int> _cellToBucket;
            readonly int[] _bucketStart;
            readonly int[] _bucketCount;
            readonly int[] _items;

            public TriangleGrid(GripTriangle[] triangles, Vector3 origin, float cellSize)
            {
                CellSize = cellSize;
                _origin = origin;
                var keys = new List<long>(triangles.Length * 4);
                var items = new List<int>(triangles.Length * 4);
                for (int i = 0; i < triangles.Length; i++)
                {
                    GripTriangle t = triangles[i];
                    Vector3 min = Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                    Vector3 max = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
                    Cell(min, out int x0, out int y0, out int z0);
                    Cell(max, out int x1, out int y1, out int z1);
                    for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        keys.Add(Pack(x, y, z));
                        items.Add(i);
                    }
                }

                long[] keyArray = keys.ToArray();
                int[] itemArray = items.ToArray();
                Array.Sort(keyArray, itemArray);
                _items = itemArray;
                _cellToBucket = new Dictionary<long, int>();
                var starts = new List<int>();
                var counts = new List<int>();
                for (int i = 0; i < keyArray.Length; i++)
                {
                    if (i == 0 || keyArray[i] != keyArray[i - 1])
                    {
                        _cellToBucket[keyArray[i]] = starts.Count;
                        starts.Add(i);
                        counts.Add(0);
                    }

                    counts[counts.Count - 1]++;
                }

                _bucketStart = starts.ToArray();
                _bucketCount = counts.ToArray();
            }

            public static long CountRefs(GripTriangle[] triangles, float cellSize)
            {
                long refs = 0;
                foreach (GripTriangle t in triangles)
                {
                    Vector3 size = Vector3.Max(t.A, Vector3.Max(t.B, t.C)) - Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                    refs += (long)(Mathf.FloorToInt(size.x / cellSize) + 2) * (Mathf.FloorToInt(size.y / cellSize) + 2) * (Mathf.FloorToInt(size.z / cellSize) + 2);
                }

                return refs;
            }

            public void Cell(Vector3 p, out int x, out int y, out int z)
            {
                Vector3 local = (p - _origin) / CellSize;
                x = Mathf.FloorToInt(local.x);
                y = Mathf.FloorToInt(local.y);
                z = Mathf.FloorToInt(local.z);
            }

            static long Pack(int x, int y, int z)
            {
                const long Offset = 1L << 20;
                const long Mask = (1L << 21) - 1;
                return (((x + Offset) & Mask) << 42) | (((y + Offset) & Mask) << 21) | ((z + Offset) & Mask);
            }

            /// <summary>Индексы треугольников ячейки; false — ячейка пуста.</summary>
            public bool TryGet(int x, int y, int z, out int start, out int count)
            {
                if (_cellToBucket.TryGetValue(Pack(x, y, z), out int bucket))
                {
                    start = _bucketStart[bucket];
                    count = _bucketCount[bucket];
                    return true;
                }

                start = count = 0;
                return false;
            }

            public int Item(int index) => _items[index];
        }

        public void Rebuild(List<GripTriangle> triangles, ulong key)
        {
            int n = triangles.Count;
            _triangles = triangles.ToArray();
            _centers = new Vector3[n];
            _radii = new float[n];
            _visited = new int[n];
            _stamp = 0;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            float extentSum = 0f;
            for (int i = 0; i < n; i++)
            {
                GripTriangle t = _triangles[i];
                Vector3 c = (t.A + t.B + t.C) * (1f / 3f);
                _centers[i] = c;
                _radii[i] = Mathf.Sqrt(Mathf.Max((t.A - c).sqrMagnitude, Mathf.Max((t.B - c).sqrMagnitude, (t.C - c).sqrMagnitude)));
                min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                extentSum += _radii[i] * 2f;
            }

            _fine = null;
            _coarse = null;
            if (n > 0)
            {
                // Ячейка — порядка среднего размера треугольника; ограничиваем общее число ссылок
                float cell = Mathf.Clamp(extentSum / n, MinCellSize, MaxCellSize);
                while (cell < MaxCellSize && TriangleGrid.CountRefs(_triangles, cell) > MaxCellRefs)
                {
                    cell *= 2f;
                }

                Vector3 origin = min - Vector3.one * cell;
                _fine = new TriangleGrid(_triangles, origin, cell);
                _coarse = new TriangleGrid(_triangles, origin, cell * CoarseFactor);
                BuildClusters(origin, cell * ClusterFactor);
            }

            Key = key;
            HasKey = true;
        }

        void BuildClusters(Vector3 origin, float clusterSize)
        {
            int n = _triangles.Length;
            var keys = new long[n];
            var items = new int[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 local = (_centers[i] - origin) / clusterSize;
                long x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.y), z = Mathf.FloorToInt(local.z);
                keys[i] = ((x & 0x1FFFFF) << 42) | ((y & 0x1FFFFF) << 21) | (z & 0x1FFFFF);
                items[i] = i;
            }

            Array.Sort(keys, items);
            var starts = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || keys[i] != keys[i - 1])
                {
                    starts.Add(i);
                }
            }

            int clusters = starts.Count;
            _clusterItems = items;
            _clusterStart = starts.ToArray();
            _clusterCount = new int[clusters];
            _clusterCentroid = new Vector3[clusters];
            _clusterAreaNormal = new Vector3[clusters];
            _clusterRadius = new float[clusters];
            for (int k = 0; k < clusters; k++)
            {
                int start = _clusterStart[k];
                int end = k + 1 < clusters ? _clusterStart[k + 1] : n;
                _clusterCount[k] = end - start;
                Vector3 areaNormal = Vector3.zero;
                Vector3 weighted = Vector3.zero;
                float areaSum = 0f;
                for (int j = start; j < end; j++)
                {
                    GripTriangle t = _triangles[items[j]];
                    // Площадной вектор 0.5·Cross(B−A, C−A) — та же ориентация, что у нормали «наружу»
                    Vector3 cross = 0.5f * Vector3.Cross(t.B - t.A, t.C - t.A);
                    float area = cross.magnitude;
                    areaNormal += cross;
                    weighted += _centers[items[j]] * area;
                    areaSum += area;
                }

                Vector3 centroid = areaSum > 0f ? weighted / areaSum : _centers[items[start]];
                float radius = 0f;
                for (int j = start; j < end; j++)
                {
                    GripTriangle t = _triangles[items[j]];
                    radius = Mathf.Max(radius, Mathf.Max((t.A - centroid).magnitude, Mathf.Max((t.B - centroid).magnitude, (t.C - centroid).magnitude)));
                }

                _clusterCentroid[k] = centroid;
                _clusterAreaNormal[k] = areaNormal;
                _clusterRadius[k] = radius;
            }
        }

        public void Clear()
        {
            _triangles = Array.Empty<GripTriangle>();
            _centers = Array.Empty<Vector3>();
            _radii = Array.Empty<float>();
            _visited = Array.Empty<int>();
            _fine = null;
            _coarse = null;
            _clusterCentroid = Array.Empty<Vector3>();
            _clusterAreaNormal = Array.Empty<Vector3>();
            _clusterRadius = Array.Empty<float>();
            _clusterStart = Array.Empty<int>();
            _clusterCount = Array.Empty<int>();
            _clusterItems = Array.Empty<int>();
            HasKey = false;
        }

        int NextStamp()
        {
            if (++_stamp == int.MaxValue)
            {
                Array.Clear(_visited, 0, _visited.Length);
                _stamp = 1;
            }

            return _stamp;
        }

        struct Nearest
        {
            public int Best;
            public float MinSq;
            public float BestDist;
            public Vector3 Closest;
        }

        void Visit(Vector3 point, int i, ref Nearest n)
        {
            if (n.Best >= 0)
            {
                float reach = n.BestDist + _radii[i];
                if ((point - _centers[i]).sqrMagnitude > reach * reach)
                {
                    return;
                }
            }

            GripTriangle tri = _triangles[i];
            Vector3 closest = GripAuthoringGeometry.ClosestPointOnTriangle(point, tri.A, tri.B, tri.C);
            float sq = (point - closest).sqrMagnitude;
            // Равные расстояния — меньший индекс, как в полном переборе
            if (sq < n.MinSq || (sq == n.MinSq && i < n.Best))
            {
                n.MinSq = sq;
                n.BestDist = Mathf.Sqrt(sq);
                n.Best = i;
                n.Closest = closest;
            }
        }

        /// <summary>Обход слоёв Чебышёва вокруг ячейки точки; true — ближайший найден точно.</summary>
        bool SearchGrid(TriangleGrid grid, Vector3 point, int maxRing, int stamp, ref Nearest n)
        {
            grid.Cell(point, out int cx, out int cy, out int cz);
            for (int r = 0; r <= maxRing; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    bool edge = dx == -r || dx == r || dy == -r || dy == r;
                    int stepZ = edge ? 1 : 2 * r;
                    for (int dz = -r; dz <= r; dz += Math.Max(1, stepZ))
                    {
                        if (!grid.TryGet(cx + dx, cy + dy, cz + dz, out int start, out int count))
                        {
                            continue;
                        }

                        for (int k = 0; k < count; k++)
                        {
                            int i = grid.Item(start + k);
                            if (_visited[i] == stamp)
                            {
                                continue;
                            }

                            _visited[i] = stamp;
                            Visit(point, i, ref n);
                        }
                    }
                }

                // Непосещённые треугольники целиком в слоях ≥ r+1, то есть дальше r·h
                if (n.Best >= 0 && n.BestDist <= r * grid.CellSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <param name="point">Точка в системе предмета</param>
        /// <param name="windingNear">Ближе этого расстояния знак уточняется winding number (м)</param>
        /// <param name="windingRadius">Радиус отбора треугольников для winding (м)</param>
        public GripSurfaceHit Query(Vector3 point, float windingNear, float windingRadius)
        {
            var hit = new GripSurfaceHit();
            int count = _triangles.Length;
            if (count == 0 || _fine == null)
            {
                return hit;
            }

            var n = new Nearest { Best = -1, MinSq = float.PositiveInfinity, BestDist = float.PositiveInfinity };
            int stamp = NextStamp();
            if (!SearchGrid(_fine, point, FineMaxRing, stamp, ref n))
            {
                // Далёкая точка — крупная сетка (ранний останов по тому же правилу слоёв); если и она не уложилась
                // в предел слоёв — перебор оставшихся с отсечением по сфере
                const int CoarseMaxRing = 24;
                if (!SearchGrid(_coarse, point, CoarseMaxRing, stamp, ref n))
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (_visited[i] == stamp)
                        {
                            continue;
                        }

                        _visited[i] = stamp;
                        Visit(point, i, ref n);
                    }
                }
            }

            Vector3 normal = _triangles[n.Best].Normal;
            float sign = Vector3.Dot(point - n.Closest, normal) < -1e-5f ? -1f : 1f;

            hit.Valid = true;
            hit.Distance = n.BestDist;
            hit.Closest = n.Closest;
            hit.Normal = normal;
            hit.Method = GripSignMethod.Normal;

            if (n.BestDist < windingNear)
            {
                float w = (float)Winding(point, windingRadius);
                hit.Winding = w;
                if (Mathf.Abs(w) >= WindingAmbiguity)
                {
                    // При нормалях Cross(B−A, C−A) наружу: внутри w > 0, снаружи w < 0
                    sign = w > 0f ? -1f : 1f;
                    hit.Method = GripSignMethod.Winding;
                }
                else
                {
                    hit.Method = GripSignMethod.WindingAmbiguous;
                }
            }

            hit.Signed = n.BestDist * sign;
            return hit;
        }

        /// <summary>
        /// Обобщённый winding number (сумма телесных углов / 4π, формула Van Oosterom–Strackee) по треугольникам в радиусе
        /// <paramref name="radius"/> (ограничивающая сфера треугольника пересекает сферу запроса). Ближние кластеры — точно,
        /// дальние (дальше 2,5 своих радиусов и целиком внутри радиуса) — дипольным приближением площадного вектора.
        /// Для замкнутой сетки внутри ≈ 1, снаружи ≈ 0; для усечённой — внутри &gt; 0, снаружи &lt; 0.
        /// </summary>
        double Winding(Vector3 point, float radius)
        {
            double total = 0.0;
            for (int k = 0; k < _clusterCentroid.Length; k++)
            {
                Vector3 toCluster = _clusterCentroid[k] - point;
                float d = toCluster.magnitude;
                float r = _clusterRadius[k];
                if (d - r > radius)
                {
                    continue;
                }

                if (d > DipoleDistanceFactor * r && d + r <= radius)
                {
                    // Телесный угол малой площадки: Ω ≈ (A·(c − p)) / |c − p|³ (снаружи по нормали — отрицательный)
                    total += Vector3.Dot(_clusterAreaNormal[k], toCluster) / ((double)d * d * d);
                    continue;
                }

                int start = _clusterStart[k];
                int count = _clusterCount[k];
                for (int j = 0; j < count; j++)
                {
                    int i = _clusterItems[start + j];
                    float reach = radius + _radii[i];
                    if ((point - _centers[i]).sqrMagnitude > reach * reach)
                    {
                        continue;
                    }

                    total += SolidAngle(point, _triangles[i]);
                }
            }

            return total / (4.0 * Math.PI);
        }

        static double SolidAngle(Vector3 point, GripTriangle tri)
        {
            double ax = tri.A.x - point.x, ay = tri.A.y - point.y, az = tri.A.z - point.z;
            double bx = tri.B.x - point.x, by = tri.B.y - point.y, bz = tri.B.z - point.z;
            double cx = tri.C.x - point.x, cy = tri.C.y - point.y, cz = tri.C.z - point.z;
            double la = Math.Sqrt(ax * ax + ay * ay + az * az);
            double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            double lc = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double det = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
            double den = la * lb * lc
                         + (ax * bx + ay * by + az * bz) * lc
                         + (ax * cx + ay * cy + az * cz) * lb
                         + (bx * cx + by * cy + bz * cz) * la;
            return 2.0 * Math.Atan2(det, den);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Высоты поверхностей набора мешей в заданной системе координат (y — вверх, x — вбок, z — вдоль): верхняя
    /// поверхность <see cref="Top"/> (луч сверху) и нижняя <see cref="Bottom"/> (луч снизу). Треугольники проецируются на
    /// плоскость XZ; точка ищется в корзинах по z. Только чтение мешей (AcquireReadOnlyMeshData — Read/Write не нужен).
    /// </summary>
    public sealed class SurfaceProbe
    {
        private const float Bucket = .005f;
        private readonly Dictionary<int, List<int>> _buckets = new Dictionary<int, List<int>>();
        private readonly List<Vector3> _a = new List<Vector3>(), _b = new List<Vector3>(), _c = new List<Vector3>();

        /// <summary><paramref name="toFrame"/> — матрица из системы меша в систему зонда.</summary>
        public void Add(Mesh mesh, Matrix4x4 toFrame)
        {
            if (mesh == null) return;
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                try
                {
                    data[0].GetVertices(vertices);
                    for (int s = 0; s < data[0].subMeshCount; s++)
                    {
                        if (data[0].GetSubMesh(s).topology != MeshTopology.Triangles) continue;
                        var indices = new NativeArray<int>(data[0].GetSubMesh(s).indexCount, Allocator.Temp);
                        try
                        {
                            data[0].GetIndices(indices, s);
                            for (int i = 0; i + 2 < indices.Length; i += 3)
                                AddTriangle(toFrame.MultiplyPoint3x4(vertices[indices[i]]), toFrame.MultiplyPoint3x4(vertices[indices[i + 1]]),
                                            toFrame.MultiplyPoint3x4(vertices[indices[i + 2]]));
                        }
                        finally { indices.Dispose(); }
                    }
                }
                finally { vertices.Dispose(); }
            }
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int id = _a.Count;
            _a.Add(a); _b.Add(b); _c.Add(c);
            int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Bucket), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Bucket);
            for (int z = z0; z <= z1; z++)
            {
                if (!_buckets.TryGetValue(z, out var list)) _buckets[z] = list = new List<int>();
                list.Add(id);
            }
        }

        public float Top(float x, float z) => Query(x, z, true);
        public float Bottom(float x, float z) => Query(x, z, false);

        private float Query(float x, float z, bool top)
        {
            if (!_buckets.TryGetValue(Mathf.FloorToInt(z / Bucket), out var list)) return float.NaN;
            float best = float.NaN;
            foreach (int id in list)
            {
                Vector3 a = _a[id], b = _b[id], c = _c[id];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-14f) continue; // вертикальный треугольник
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                float w = 1f - u - v;
                if (u < -1e-5f || v < -1e-5f || w < -1e-5f) continue;
                float y = u * a.y + v * b.y + w * c.y;
                if (float.IsNaN(best) || (top ? y > best : y < best)) best = y;
            }
            return best;
        }
    }

    /// <summary>Крепление комплекта к планке: плоскость контакта и поперечный упор (система узла основания).</summary>
    public sealed class SightMountInfo
    {
        /// <summary>Точка: x — ось канала, y — потолок канала (ложится на вершины зубцов), z — упор или центр контакта.</summary>
        public Vector3 Point;
        public bool HasLug;
        /// <summary>Ширина канала между губками у их низа, м.</summary>
        public float ChannelWidth;
        /// <summary>Глубина канала: от низа губок до потолка, м.</summary>
        public float ChannelDepth;
        public float ContactZMin, ContactZMax;
        public override string ToString() =>
            $"контакт y {Point.y * 1000:0.00} мм, ось x {Point.x * 1000:0.00}, {(HasLug ? "упор" : "центр контакта")} z {Point.z * 1000:0.0} мм " +
            $"(контакт z {ContactZMin * 1000:0.0}…{ContactZMax * 1000:0.0}), канал {ChannelWidth * 1000:0.0}×{ChannelDepth * 1000:0.0} мм";
    }

    /// <summary>Планка Пикатинни под точкой: ось, вершины зубцов, ширина верха, поперечные пазы (система ShotSource).</summary>
    public sealed class SightRailInfo
    {
        public float AxisX, TopY, TopWidth, SlotDepth;
        public readonly List<float> SlotCenters = new List<float>();
        public readonly List<float> SlotWidths = new List<float>();
        public float Pitch => SlotCenters.Count < 2 ? 0f : (SlotCenters.Last() - SlotCenters.First()) / (SlotCenters.Count - 1);

        public int NearestSlot(float z)
        {
            int best = -1;
            for (int i = 0; i < SlotCenters.Count; i++)
                if (best < 0 || Mathf.Abs(SlotCenters[i] - z) < Mathf.Abs(SlotCenters[best] - z)) best = i;
            return best;
        }

        public override string ToString() =>
            $"ось x {AxisX * 1000:0.00} мм, зубцы y {TopY * 1000:0.00} мм, верх {TopWidth * 1000:0.0} мм, пазов {SlotCenters.Count}" +
            (SlotCenters.Count > 1 ? $" шаг {Pitch * 1000:0.00} мм, глубина {SlotDepth * 1000:0.0} мм" : "");
    }

    /// <summary>
    /// Геометрия посадки комплекта в паз планки (решение 2026-10-20 «Посадка в паз», раунд 3): интерфейс крепления
    /// комплекта — из меша основания (<see cref="FindMount"/>), планка ствола — из мешей оружия (<see cref="FindRail"/>).
    /// Только вычисления, без записи.
    /// </summary>
    public static class SightRailMount
    {
        public const string MountName = "RailMount";
        private const float Step = .0005f;
        /// <summary>Губка: низ основания не выше нижней точки + 1,5 мм.</summary>
        private const float LipBand = .0015f;

        // ── Крепление комплекта ─────────────────────────────────

        /// <summary>Интерфейс крепления по мешу основания (в системе <paramref name="frame"/>: y вверх, z вдоль планки).</summary>
        public static SightMountInfo FindMount(SurfaceProbe probe, Bounds bounds, string context)
        {
            int nx = Mathf.Max(1, Mathf.FloorToInt(bounds.size.x / Step)), nz = Mathf.Max(1, Mathf.FloorToInt(bounds.size.z / Step));
            float X(int i) => bounds.min.x + Step * (i + .5f);
            float Z(int j) => bounds.min.z + Step * (j + .5f);
            var grid = new float[nx, nz];
            float lipLevel = float.PositiveInfinity;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    grid[i, j] = probe.Bottom(X(i), Z(j));
                    if (!float.IsNaN(grid[i, j])) lipLevel = Mathf.Min(lipLevel, grid[i, j]);
                }
            if (float.IsInfinity(lipLevel)) throw new InvalidOperationException(context + ": у основания нет нижней поверхности");
            bool Lip(int i, int j) => !float.IsNaN(grid[i, j]) && grid[i, j] <= lipLevel + LipBand;

            // Ряды поперёк планки, где губки есть по обе стороны от середины основания (±4 мм): внутренние кромки губок.
            float cx0 = bounds.center.x;
            var rows = new List<(int j, float left, float right)>();
            for (int j = 0; j < nz; j++)
            {
                float innerLeft = float.NegativeInfinity, innerRight = float.PositiveInfinity;
                for (int i = 0; i < nx; i++)
                {
                    if (!Lip(i, j)) continue;
                    if (X(i) < cx0 - .004f) innerLeft = Mathf.Max(innerLeft, X(i));
                    else if (X(i) > cx0 + .004f) innerRight = Mathf.Min(innerRight, X(i));
                }
                if (!float.IsInfinity(innerLeft) && !float.IsInfinity(innerRight)) rows.Add((j, innerLeft, innerRight));
            }
            if (rows.Count == 0) throw new InvalidOperationException(context + ": у основания не найдены губки с двух сторон канала");
            float cx = rows.Select(r => (r.left + r.right) * .5f).OrderBy(v => v).ElementAt(rows.Count / 2);
            float width = rows.Select(r => r.right - r.left).OrderBy(v => v).ElementAt(rows.Count / 2) - Step;

            // Потолок канала по центральной полосе ±3 мм; плоскость контакта — самая частая высота (наибольшая площадь).
            var ceiling = new List<(float z, float y)>();
            foreach (var row in rows)
            {
                var ys = new List<float>();
                for (int i = 0; i < nx; i++)
                    if (Mathf.Abs(X(i) - cx) <= .003f && !float.IsNaN(grid[i, row.j]) && grid[i, row.j] > lipLevel + LipBand) ys.Add(grid[i, row.j]);
                if (ys.Count > 0) ceiling.Add((Z(row.j), ys.OrderBy(v => v).ElementAt(ys.Count / 2)));
            }
            if (ceiling.Count == 0) throw new InvalidOperationException(context + ": над каналом нет потолка");
            float contact = ceiling.GroupBy(c => Mathf.RoundToInt(c.y / .0001f)).OrderByDescending(g => g.Count()).First().Key * .0001f;
            var contactRows = ceiling.Where(c => Mathf.Abs(c.y - contact) <= .00015f).Select(c => c.z).ToList();
            var lugRows = ceiling.Where(c => c.y < contact - .0003f).Select(c => c.z).ToList();
            var info = new SightMountInfo
            {
                ChannelWidth = width, ChannelDepth = contact - lipLevel, HasLug = lugRows.Count > 0,
                ContactZMin = contactRows.Min(), ContactZMax = contactRows.Max()
            };
            float z0 = info.HasLug ? Cluster(lugRows, (info.ContactZMin + info.ContactZMax) * .5f) : (info.ContactZMin + info.ContactZMax) * .5f;
            info.Point = new Vector3(cx, contact, z0);
            return info;
        }

        /// <summary>Центр группы подряд идущих значений, ближайшей к <paramref name="near"/>.</summary>
        private static float Cluster(List<float> zs, float near)
        {
            zs.Sort();
            var groups = new List<List<float>> { new List<float> { zs[0] } };
            for (int i = 1; i < zs.Count; i++)
            {
                if (zs[i] - zs[i - 1] > Step * 1.5f) groups.Add(new List<float>());
                groups.Last().Add(zs[i]);
            }
            return groups.Select(g => (g.First() + g.Last()) * .5f).OrderBy(c => Mathf.Abs(c - near)).First();
        }

        // ── Планка ствола ───────────────────────────────────────

        /// <summary>
        /// Планка около точки z (система ShotSource): ось — середина плато верха по ширине, вершины зубцов — максимум
        /// плато, пазы — участки, где верх по полосе ±4 мм от оси ниже вершин больше чем на 0,8 мм. null — плато нет.
        /// </summary>
        public static SightRailInfo FindRail(SurfaceProbe probe, float z, float halfWindow = .025f)
        {
            // Верх по x: максимум по окну ±6 мм вокруг z.
            var hx = new List<(float x, float y)>();
            for (float x = -.02f; x <= .02f + 1e-6f; x += Step)
            {
                float best = float.NaN;
                for (float dz = -.006f; dz <= .006f + 1e-6f; dz += Step)
                {
                    float y = probe.Top(x, z + dz);
                    if (!float.IsNaN(y) && (float.IsNaN(best) || y > best)) best = y;
                }
                if (!float.IsNaN(best)) hx.Add((x, best));
            }
            if (hx.Count == 0) return null;
            float top = hx.Max(p => p.y);
            // Верх зубцов: участки у максимума (−0,6 мм). Вогнутый верх (MKR9: края 44,0, середина 42,1) и продольная
            // канавка (SniperRifle, 5 мм) дают два участка — сливаются, если разрыв не больше 12 мм.
            var plateau = hx.Where(p => p.y >= top - .0006f).Select(p => p.x).OrderBy(x => x).ToList();
            var segments = new List<List<float>> { new List<float> { plateau[0] } };
            for (int i = 1; i < plateau.Count; i++)
            {
                if (plateau[i] - plateau[i - 1] > .012f) segments.Add(new List<float>());
                segments.Last().Add(plateau[i]);
            }
            var segment = segments.OrderBy(s => Mathf.Abs((s.First() + s.Last()) * .5f)).First();
            if (segment.Last() - segment.First() < .008f) return null; // уже 8 мм — не планка
            float left = segment.First(), right = segment.Last();
            var rail = new SightRailInfo { AxisX = (left + right) * .5f, TopWidth = right - left + Step };

            // Профиль вдоль z: максимум по ширине верха зубцов.
            var hz = new List<(float z, float y)>();
            for (float zz = z - halfWindow; zz <= z + halfWindow + 1e-6f; zz += Step)
            {
                float best = float.NaN;
                for (float x = left; x <= right + 1e-6f; x += .001f)
                {
                    float y = probe.Top(x, zz);
                    if (!float.IsNaN(y) && (float.IsNaN(best) || y > best)) best = y;
                }
                hz.Add((zz, best));
            }
            var valid = hz.Where(p => !float.IsNaN(p.y)).ToList();
            if (valid.Count == 0) return null;
            // Вершины зубцов — самая частая высота среди верхних 0,6 мм (рамки и ступени выше зубцов не берутся).
            float peak = valid.Max(p => p.y);
            rail.TopY = valid.Where(p => p.y >= peak - .0006f).GroupBy(p => Mathf.RoundToInt(p.y / .0001f))
                             .OrderByDescending(g => g.Count()).First().Key * .0001f;
            float minSlot = float.PositiveInfinity;
            List<float> current = null;
            foreach (var p in hz)
            {
                bool slot = !float.IsNaN(p.y) && p.y < rail.TopY - .0008f && p.y > rail.TopY - .004f;
                if (slot) { (current ??= new List<float>()).Add(p.z); minSlot = Mathf.Min(minSlot, p.y); }
                else if (current != null) { Close(rail, current, z - halfWindow, z + halfWindow); current = null; }
            }
            if (current != null) Close(rail, current, z - halfWindow, z + halfWindow);
            rail.SlotDepth = float.IsInfinity(minSlot) ? 0f : rail.TopY - minSlot;
            return rail;
        }

        /// <summary>
        /// Ширина планки на высоте <paramref name="y"/> около z (система ShotSource): непрерывный участок вокруг оси, где
        /// верх (максимум по окну ±6 мм) не ниже <paramref name="y"/>. Сравнивается с каналом крепления у низа губок.
        /// </summary>
        public static float RailWidthAt(SurfaceProbe probe, float axisX, float z, float y)
        {
            float H(float x)
            {
                float best = float.NaN;
                for (float dz = -.006f; dz <= .006f + 1e-6f; dz += Step)
                {
                    float v = probe.Top(x, z + dz);
                    if (!float.IsNaN(v) && (float.IsNaN(best) || v > best)) best = v;
                }
                return best;
            }
            float left = axisX, right = axisX;
            for (float x = axisX; x >= axisX - .03f; x -= Step) { float h = H(x); if (float.IsNaN(h) || h < y) break; left = x; }
            for (float x = axisX; x <= axisX + .03f; x += Step) { float h = H(x); if (float.IsNaN(h) || h < y) break; right = x; }
            return right - left + Step;
        }

        private static void Close(SightRailInfo rail, List<float> zs, float min, float max)
        {
            // Паз, обрезанный окном, не годится как привязка: центр неизвестен.
            if (zs.First() <= min + Step || zs.Last() >= max - Step) return;
            rail.SlotCenters.Add((zs.First() + zs.Last()) * .5f);
            rail.SlotWidths.Add(zs.Last() - zs.First() + Step);
        }
    }
}

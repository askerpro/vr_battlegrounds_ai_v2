using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VrBattlegrounds.Editor.LevelDesign.LevelDesignRules;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Проверки правил левел-дизайна на карте высот <see cref="MapGrid"/>. Чистая математика:
    /// ни сцены, ни физики — поэтому каждая проверка покрыта тестом на искусственной сетке
    /// (<c>MapAnalyzerTests</c>), а на настоящих картах её гоняет <c>MapPrinciplesTests</c>.
    ///
    /// <para>
    /// Игрок моделируется диском на полу: проходима клетка без твёрдого в полосе тела
    /// (<see cref="MapGrid.Blocked"/>), куда помещается центр диска диаметром
    /// <see cref="LevelDesignRules.SqueezeWidth"/> (протиснуться боком). Видимость —
    /// <see cref="MapGrid.LineOfSight"/> между глазами; игроки видят друг друга, если видят хоть
    /// в одной паре поз <see cref="LevelDesignRules.EyeHeights"/> (стоя, присев) — так окно на
    /// уровне груди даёт контакт присевшим.
    /// </para>
    /// </summary>
    public static class MapAnalyzer
    {
        // ── Основа ───────────────────────────────────────────────────────────

        public static bool IsFree(MapGrid g, int i) => !g.Blocked[i];

        /// <summary>
        /// Расстояние от центра свободной клетки до поверхности ближайшего препятствия или края
        /// арены, м. У занятой клетки — 0.
        /// </summary>
        public static float[] Clearance(MapGrid g)
        {
            var blocked = new bool[g.Count];
            for (int i = 0; i < g.Count; i++) blocked[i] = !IsFree(g, i);

            double[] d2 = SquaredDistance(g.Width, g.Depth, blocked, borderIsSource: true);
            var clear = new float[g.Count];
            for (int i = 0; i < g.Count; i++)
                clear[i] = blocked[i] ? 0f : Mathf.Max(0f, (float)Math.Sqrt(d2[i]) * g.Cell - g.Cell * 0.5f);
            return clear;
        }

        /// <summary>Клетка, где может стоять игрок (протиснувшись боком).</summary>
        public static bool IsWalkable(MapGrid g, float[] clear, int i) =>
            IsFree(g, i) && clear[i] >= SqueezeWidth * 0.5f - 1e-4f;

        // ── LD-23: узкие проходы ─────────────────────────────────────────────

        public sealed class NarrowPassage
        {
            public Vector2 Center;
            public float Width;
            public string Between;

            public override string ToString() =>
                $"проход ≈{Width:F1} м у ({Center.x:F1}; {Center.y:F1}) между {Between}";
        }

        /// <summary>
        /// Сквозные проходы уже <see cref="LevelDesignRules.MinPassageWidth"/>, куда всё же можно
        /// протиснуться. «Зажим» — проходимая клетка, через которую просвет между препятствиями
        /// с двух противоположных сторон (≥ 135° между направлениями) уже ширины прохода.
        /// Группа зажимов, которая с двух разных концов выходит на просторное место, — проход.
        /// Тупиковая ниша выходит на простор одним концом, угол между стенами — не зажим
        /// (стены под 90°), поэтому ни то ни другое проходом не считается.
        /// </summary>
        public static List<NarrowPassage> FindNarrowPassages(MapGrid g, float[] clear)
        {
            var gap = new float[g.Count];
            var pinch = new bool[g.Count];
            for (int i = 0; i < g.Count; i++)
            {
                gap[i] = IsWalkable(g, clear, i) ? GapWidth(g, i) : float.PositiveInfinity;
                pinch[i] = gap[i] < MinPassageWidth - 1e-3f;
            }

            var result = new List<NarrowPassage>();
            foreach (List<int> component in Components(g, pinch))
            {
                // Выходы — проходимые клетки вокруг зажима; разных групп выходов две и больше — проход.
                var exit = new bool[g.Count];
                var exits = new List<int>();
                foreach (int i in component)
                foreach (int j in Neighbours8(g, i))
                {
                    if (pinch[j] || exit[j] || !IsWalkable(g, clear, j)) continue;
                    exit[j] = true;
                    exits.Add(j);
                }

                if (Components(g, exit, exits).Count() < 2) continue;

                int narrowest = component.OrderBy(i => gap[i]).First();
                result.Add(new NarrowPassage
                {
                    Center = g.Center(narrowest),
                    Width = gap[narrowest],
                    Between = NearbyObstacles(g, narrowest, gap[narrowest] + g.Cell),
                });
            }
            return result;
        }

        private const int GapDirections = 16;

        /// <summary>
        /// Самый узкий просвет через клетку между препятствиями с двух противоположных сторон, м;
        /// бесконечность — такого просвета уже ширины прохода нет.
        /// </summary>
        private static float GapWidth(MapGrid g, int i)
        {
            float reach = MinPassageWidth + g.Cell;
            Vector2 c = g.Center(i);
            var hit = new float[GapDirections];
            for (int d = 0; d < GapDirections; d++)
            {
                hit[d] = float.PositiveInfinity;
                float angle = d * Mathf.PI * 2f / GapDirections;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                for (float t = g.Cell * 0.5f; t <= reach; t += g.Cell * 0.5f)
                {
                    int j = g.IndexAt(c + dir * t);
                    if (j >= 0 && IsFree(g, j)) continue;
                    hit[d] = t;
                    break;
                }
            }

            // Луч останавливается на полклетки внутри препятствия — вычесть по четверти клетки с каждой стороны.
            float best = float.PositiveInfinity;
            int opposite = Mathf.CeilToInt(GapDirections * 135f / 360f);
            for (int a = 0; a < GapDirections; a++)
            for (int b = a + opposite; b <= a + GapDirections - opposite; b++)
                best = Mathf.Min(best, hit[a] + hit[b % GapDirections] - g.Cell * 0.5f);
            return best;
        }

        // ── LD-25: всё достижимо из своей зоны, не заходя в чужую ───────────

        public sealed class Pocket
        {
            public byte Side;
            public Vector2 Center;
            public float Area;

            public override string ToString() =>
                Area < 0f
                    ? $"сторона {SideName(Side)}: в зоне негде стоять"
                    : $"сторона {SideName(Side)}: {Area:F1} м² у ({Center.x:F1}; {Center.y:F1}) недостижимы из своей зоны в обход чужой";
        }

        /// <summary>
        /// Участки, куда игрок стороны не дойдёт из своей зоны, не заходя в зону противника
        /// (путь мёртвого до своей зоны, LD-25). Заодно ловит отрезанные препятствиями карманы.
        /// </summary>
        public static List<Pocket> FindUnreachable(MapGrid g, float[] clear)
        {
            var result = new List<Pocket>();
            foreach (byte side in new[] { MapGrid.ZoneA, MapGrid.ZoneB })
            {
                byte other = side == MapGrid.ZoneA ? MapGrid.ZoneB : MapGrid.ZoneA;
                bool Allowed(int i) => IsWalkable(g, clear, i) && g.Zone[i] != other;

                var seeds = Enumerable.Range(0, g.Count).Where(i => Allowed(i) && g.Zone[i] == side).ToList();
                if (seeds.Count == 0)
                {
                    result.Add(new Pocket { Side = side, Area = -1f });
                    continue;
                }

                bool[] reached = Flood(g, seeds, Allowed);
                var lost = new bool[g.Count];
                for (int i = 0; i < g.Count; i++) lost[i] = Allowed(i) && !reached[i];

                foreach (List<int> component in Components(g, lost))
                {
                    float area = component.Count * g.Cell * g.Cell;
                    if (area < MinPocketArea) continue;
                    result.Add(new Pocket { Side = side, Area = area, Center = Centroid(g, component) });
                }
            }
            return result;
        }

        // ── Видимость ────────────────────────────────────────────────────────

        /// <summary>Луч от точки на высоте <paramref name="ha"/> до точки на высоте <paramref name="hb"/> не перекрыт.</summary>
        public static bool Visible(MapGrid g, Vector2 a, float ha, Vector2 b, float hb) =>
            g.LineOfSight(new Vector3(a.x, ha, a.y), new Vector3(b.x, hb, b.y));

        /// <summary>
        /// Игроки в точках видят друг друга хоть в одной паре поз (стоя/присев); в какой — в
        /// <paramref name="ha"/>/<paramref name="hb"/>.
        /// </summary>
        public static bool SeeEachOther(MapGrid g, Vector2 a, Vector2 b, out float ha, out float hb)
        {
            foreach (float x in EyeHeights)
            foreach (float y in EyeHeights)
            {
                if (!Visible(g, a, x, b, y)) continue;
                ha = x;
                hb = y;
                return true;
            }
            ha = hb = 0f;
            return false;
        }

        public static bool SeeEachOther(MapGrid g, Vector2 a, Vector2 b) => SeeEachOther(g, a, b, out _, out _);

        /// <summary>
        /// Пуля долетает из одной точки в другую хоть в одной паре поз — сквозь Soft и Visual (T-41). Видеть при
        /// этом не обязательно: сквозь Soft стреляют вслепую.
        /// </summary>
        public static bool CanShoot(MapGrid g, Vector2 a, Vector2 b)
        {
            foreach (float x in EyeHeights)
            foreach (float y in EyeHeights)
                if (g.ShotLine(new Vector3(a.x, x, a.y), new Vector3(b.x, y, b.y))) return true;
            return false;
        }

        /// <summary>
        /// Видимый луч проходит под верхом препятствия — значит, сквозь окно, щель, бойницу или
        /// дверь, а не поверх укрытия.
        /// </summary>
        public static bool ThroughOpening(MapGrid g, Vector2 a, float ha, Vector2 b, float hb)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / (g.Cell * 0.5f)));
            for (int s = 1; s < steps; s++)
            {
                float t = (float)s / steps;
                int i = g.IndexAt(Vector2.Lerp(a, b, t));
                if (i >= 0 && g.Height[i] > Mathf.Lerp(ha, hb, t)) return true;
            }
            return false;
        }

        public sealed class Sightline
        {
            public Vector2 From;
            public Vector2 To;
            public float Length => (To - From).magnitude;

            public override string ToString() =>
                $"({From.x:F1}; {From.y:F1}) → ({To.x:F1}; {To.y:F1}), {Length:F1} м";
        }

        /// <summary>LD-15: линии видимости между спавн-зонами сторон (стоя или присев, в том числе через окна).</summary>
        public static List<Sightline> FindBaseToBaseSightlines(MapGrid g, float[] clear)
        {
            var result = new List<Sightline>();
            foreach (var work in BaseSightlineSteps(g, clear, result, false)) work.Execute();
            return result;
        }
        internal static IEnumerable<MapEvaluationWork> BaseSightlineSteps(MapGrid g, float[] clear, List<Sightline> result, bool blindShot)
        {
            List<int> a = Samples(g, clear, i => g.Zone[i] == MapGrid.ZoneA);
            List<int> b = Samples(g, clear, i => g.Zone[i] == MapGrid.ZoneB);
            foreach (int i in a)
            foreach (int j in b)
            {
                yield return new MapEvaluationWork(blindShot ? 8 : 4, () => {
                    bool seen = SeeEachOther(g, g.Center(i), g.Center(j));
                    if (blindShot ? !seen && CanShoot(g, g.Center(i), g.Center(j)) : seen)
                        result.Add(new Sightline { From = g.Center(i), To = g.Center(j) });
                });
            }
        }

        /// <summary>
        /// LD-15, прострел: пары точек двух зон, которые не видят друг друга, но простреливаются сквозь Soft/Visual —
        /// спавн под огнём вслепую.
        /// </summary>
        public static List<Sightline> FindBaseToBaseShotlines(MapGrid g, float[] clear)
        {
            var result = new List<Sightline>();
            foreach (var work in BaseSightlineSteps(g, clear, result, true)) work.Execute();
            return result;
        }

        // ── LD-20: три класса высоты ─────────────────────────────────────────

        /// <summary>
        /// Верх каждого укрытия блокаута — в одном из классов Low/Mid/Tall, и класс совпадает с
        /// суффиксом имени префаба, если он есть.
        /// </summary>
        public static List<string> CheckCoverHeights(IEnumerable<KeyValuePair<string, float>> tops)
        {
            var problems = new List<string>();
            foreach (KeyValuePair<string, float> top in tops)
            {
                string actual = ClassOf(top.Value);
                string named = CoverClasses.Select(c => c.Suffix).FirstOrDefault(s => top.Key.Contains("_" + s));

                if (actual == null)
                    problems.Add($"{top.Key}: верх {top.Value:F2} м — ни Low (1.0–1.2), ни Mid (1.5–1.6), ни Tall (2.0–2.5)");
                else if (named != null && named != actual)
                    problems.Add($"{top.Key}: по имени {named}, а верх {top.Value:F2} м — это {actual}");
            }
            return problems;
        }

        /// <summary>Перешагиваемая преграда (<c>VaultableObstacle</c>) — для проверки размеров.</summary>
        public sealed class Vaultable
        {
            public string Name;
            public float Height;
            public float Thickness;
        }

        /// <summary>
        /// LD-48: помеченное перешагиваемым действительно перешагивается — не выше
        /// <c>VaultableObstacle.MaxHeight</c> и не толще <c>VaultableObstacle.MaxThickness</c>.
        /// Иначе метка сделала бы законным проход сквозь настоящую стену.
        /// </summary>
        public static List<string> CheckVaultables(IEnumerable<Vaultable> items)
        {
            var problems = new List<string>();
            foreach (Vaultable v in items)
            {
                if (v.Height > VrBattlegrounds.Maps.VaultableObstacle.MaxHeight + CoverTolerance)
                    problems.Add($"{v.Name}: перешагиваемое высотой {v.Height:F2} м — выше {VrBattlegrounds.Maps.VaultableObstacle.MaxHeight} м не перешагнуть");
                if (v.Thickness > VrBattlegrounds.Maps.VaultableObstacle.MaxThickness + CoverTolerance)
                    problems.Add($"{v.Name}: перешагиваемое толщиной {v.Thickness:F2} м — толще {VrBattlegrounds.Maps.VaultableObstacle.MaxThickness} м не перешагнуть");
            }
            return problems;
        }

        public static string ClassOf(float top)
        {
            foreach (CoverClass c in CoverClasses)
                if (top >= c.Min - CoverTolerance && top <= c.Max + CoverTolerance) return c.Suffix;
            return null;
        }

        // ── Метрики для отчёта: LD-09, LD-14, LD-26 ──────────────────────────

        public sealed class VisibilityStats
        {
            /// <summary>LD-09: самая большая доля чужой половины, видимая из одной точки.</summary>
            public float MaxEnemyShare;
            public Vector2 MaxEnemyShareAt;
            /// <summary>LD-14: видимые пары точек между половинами по дистанции.</summary>
            public int Close, Medium, Long;
            /// <summary>Из них — через проём (окно, щель, бойница, дверь), а не поверх укрытия.</summary>
            public int ThroughOpenings;
            /// <summary>Пары между половинами, которые друг друга не видят, но простреливаются (сквозь Soft/Visual) — ось S.</summary>
            public int BlindShots;
            /// <summary>LD-26: доля точек зоны, видимых из чужой половины.</summary>
            public float ZoneAExposure, ZoneBExposure;
            /// <summary>Доля чужой половины, видимая из точки (индекс клетки → доля).</summary>
            public readonly Dictionary<int, float> Share = new Dictionary<int, float>();
        }

        public static VisibilityStats Visibility(MapGrid g, float[] clear)
        {
            var stats = new VisibilityStats();
            foreach (var work in VisibilitySteps(g, clear, stats)) work.Execute();
            return stats;
        }
        internal static IEnumerable<MapEvaluationWork> VisibilitySteps(MapGrid g, float[] clear, VisibilityStats stats)
        {
            List<int> samples = Samples(g, clear, _ => true);
            if (!TryHalves(g, out Vector2 mid, out Vector2 axis)) yield break;

            List<int> halfA = samples.Where(i => Vector2.Dot(g.Center(i) - mid, axis) < 0f).ToList();
            List<int> halfB = samples.Where(i => Vector2.Dot(g.Center(i) - mid, axis) >= 0f).ToList();
            var seen = samples.ToDictionary(i => i, _ => 0);

            foreach (int a in halfA)
            foreach (int b in halfB)
            {
                yield return new MapEvaluationWork(8, () => {
                if (!SeeEachOther(g, g.Center(a), g.Center(b), out float ha, out float hb))
                {
                    if (CanShoot(g, g.Center(a), g.Center(b))) stats.BlindShots++;
                    return;
                }
                if (ThroughOpening(g, g.Center(a), ha, g.Center(b), hb)) stats.ThroughOpenings++;
                seen[a]++;
                seen[b]++;
                float d = (g.Center(b) - g.Center(a)).magnitude;
                if (d < CloseRange) stats.Close++;
                else if (d < LongRange) stats.Medium++;
                else stats.Long++;
                });
            }

            foreach (int i in samples)
            {
                int enemyCount = halfA.Contains(i) ? halfB.Count : halfA.Count;
                float share = enemyCount > 0 ? (float)seen[i] / enemyCount : 0f;
                stats.Share[i] = share;
                if (share > stats.MaxEnemyShare)
                {
                    stats.MaxEnemyShare = share;
                    stats.MaxEnemyShareAt = g.Center(i);
                }
            }

            float Exposure(byte zone)
            {
                List<int> cells = samples.Where(i => g.Zone[i] == zone).ToList();
                return cells.Count == 0 ? 0f : (float)cells.Count(i => seen[i] > 0) / cells.Count;
            }

            stats.ZoneAExposure = Exposure(MapGrid.ZoneA);
            stats.ZoneBExposure = Exposure(MapGrid.ZoneB);
        }

        // ── Вспомогательное ──────────────────────────────────────────────────

        public static string SideName(byte side) => side == MapGrid.ZoneA ? "A" : "B";

        /// <summary>Середина между зонами и направление от A к B — граница половин карты.</summary>
        public static bool TryHalves(MapGrid g, out Vector2 mid, out Vector2 axis)
        {
            var a = Enumerable.Range(0, g.Count).Where(i => g.Zone[i] == MapGrid.ZoneA).ToList();
            var b = Enumerable.Range(0, g.Count).Where(i => g.Zone[i] == MapGrid.ZoneB).ToList();
            mid = axis = Vector2.zero;
            if (a.Count == 0 || b.Count == 0) return false;

            Vector2 ca = Centroid(g, a), cb = Centroid(g, b);
            mid = (ca + cb) * 0.5f;
            axis = (cb - ca).normalized;
            return true;
        }

        /// <summary>Проходимые клетки на решётке с шагом <see cref="LevelDesignRules.SampleStep"/>.</summary>
        private static List<int> Samples(MapGrid g, float[] clear, Func<int, bool> filter)
        {
            int k = Mathf.Max(1, Mathf.RoundToInt(SampleStep / g.Cell));
            var result = new List<int>();
            for (int z = k / 2; z < g.Depth; z += k)
            for (int x = k / 2; x < g.Width; x += k)
            {
                int i = g.Index(x, z);
                if (IsWalkable(g, clear, i) && filter(i)) result.Add(i);
            }
            return result;
        }

        private static bool[] Flood(MapGrid g, List<int> seeds, Func<int, bool> allowed)
        {
            var reached = new bool[g.Count];
            var queue = new Queue<int>();
            foreach (int s in seeds)
            {
                reached[s] = true;
                queue.Enqueue(s);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = g.X(i), z = g.Z(i);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0 || !g.InBounds(x + dx, z + dz)) continue;
                    int j = g.Index(x + dx, z + dz);
                    if (reached[j] || !allowed(j)) continue;
                    if (dx != 0 && dz != 0 && (!allowed(g.Index(x + dx, z)) || !allowed(g.Index(x, z + dz)))) continue;
                    reached[j] = true;
                    queue.Enqueue(j);
                }
            }
            return reached;
        }

        private static IEnumerable<int> Neighbours8(MapGrid g, int i)
        {
            int x = g.X(i), z = g.Z(i);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dz != 0) && g.InBounds(x + dx, z + dz))
                    yield return g.Index(x + dx, z + dz);
        }

        /// <summary>8-связные компоненты отмеченных клеток (по всем или по списку <paramref name="only"/>).</summary>
        private static IEnumerable<List<int>> Components(MapGrid g, bool[] mask, IEnumerable<int> only = null)
        {
            var seen = new bool[g.Count];
            foreach (int start in only ?? Enumerable.Range(0, g.Count))
            {
                if (!mask[start] || seen[start]) continue;
                var component = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    component.Add(i);
                    foreach (int j in Neighbours8(g, i))
                    {
                        if (!mask[j] || seen[j]) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
                yield return component;
            }
        }

        private static Vector2 Centroid(MapGrid g, List<int> cells)
        {
            Vector2 sum = Vector2.zero;
            foreach (int i in cells) sum += g.Center(i);
            return sum / cells.Count;
        }

        private static string NearbyObstacles(MapGrid g, int i, float radius)
        {
            int k = Mathf.CeilToInt(radius / g.Cell);
            int x = g.X(i), z = g.Z(i);
            var names = new SortedSet<string>();
            for (int dz = -k; dz <= k; dz++)
            for (int dx = -k; dx <= k; dx++)
            {
                if (!g.InBounds(x + dx, z + dz)) { names.Add("краем арены"); continue; }
                int j = g.Index(x + dx, z + dz);
                if (!IsFree(g, j)) names.Add(g.OwnerName(j));
            }
            return names.Count == 0 ? "?" : string.Join(" и ", names.Take(3));
        }

        /// <summary>
        /// Точное евклидово преобразование расстояний (Felzenszwalb–Huttenlocher): квадрат
        /// расстояния в клетках до ближайшей клетки-источника. <paramref name="borderIsSource"/> —
        /// за краем сетки тоже источник (край арены — стена).
        /// </summary>
        private static double[] SquaredDistance(int w, int d, bool[] source, bool borderIsSource)
        {
            const double Inf = 1e20;
            int pad = borderIsSource ? 1 : 0;
            int pw = w + 2 * pad, pd = d + 2 * pad;
            var f = new double[pw * pd];
            for (int z = 0; z < pd; z++)
            for (int x = 0; x < pw; x++)
            {
                bool border = x < pad || z < pad || x >= pw - pad || z >= pd - pad;
                bool src = border || source[(z - pad) * w + (x - pad)];
                f[z * pw + x] = src ? 0 : Inf;
            }

            int n = Mathf.Max(pw, pd);
            var line = new double[n];
            var outLine = new double[n];
            var v = new int[n];
            var boundaries = new double[n + 1];

            for (int z = 0; z < pd; z++)
            {
                for (int x = 0; x < pw; x++) line[x] = f[z * pw + x];
                Transform1D(line, pw, outLine, v, boundaries);
                for (int x = 0; x < pw; x++) f[z * pw + x] = outLine[x];
            }

            for (int x = 0; x < pw; x++)
            {
                for (int z = 0; z < pd; z++) line[z] = f[z * pw + x];
                Transform1D(line, pd, outLine, v, boundaries);
                for (int z = 0; z < pd; z++) f[z * pw + x] = outLine[z];
            }

            var result = new double[w * d];
            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
                result[z * w + x] = f[(z + pad) * pw + x + pad];
            return result;
        }

        private static void Transform1D(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }
    }
}

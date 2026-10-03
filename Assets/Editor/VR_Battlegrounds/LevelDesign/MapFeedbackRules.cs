using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Проверки обратной связи: независимые подходы, остаточные зазоры и высоты открытой карты.</summary>
    public static class MapFeedbackRules
    {
        // Консервативная проверка трёх полос: маршрут не должен зависеть от входа соседней полосы.
        public static List<string> CheckApproaches(MapGrid grid, float[] clearance)
        {
            var errors = new List<string>();
            if (!MapAnalyzer.TryHalves(grid, out var mid, out var axis))
            {
                errors.Add("LD-49: не размечены обе базы");
                return errors;
            }
            var lateral = new Vector2(-axis.y, axis.x);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < grid.Count; i++)
            {
                float x = Vector2.Dot(grid.Center(i) - mid, lateral);
                lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
            }
            foreach (byte side in new[] { MapGrid.ZoneA, MapGrid.ZoneB })
            for (int lane = 0; lane < 3; lane++)
            {
                float min = Mathf.Lerp(lo, hi, lane / 3f);
                float max = Mathf.Lerp(lo, hi, (lane + 1) / 3f);
                var reached = new bool[grid.Count];
                var queue = new Queue<int>();
                for (int i = 0; i < grid.Count; i++)
                    if (grid.Zone[i] == side && Allowed(i)) { reached[i] = true; queue.Enqueue(i); }
                bool found = false;
                while (queue.Count > 0 && !found)
                {
                    int i = queue.Dequeue();
                    if (grid.Zone[i] == MapGrid.NoZone &&
                        Mathf.Abs(Vector2.Dot(grid.Center(i) - mid, axis)) <= 1f) { found = true; break; }
                    int x = i % grid.Width, z = i / grid.Width;
                    Visit(x - 1, z); Visit(x + 1, z); Visit(x, z - 1); Visit(x, z + 1);
                }
                if (!found) errors.Add($"LD-49: база {MapAnalyzer.SideName(side)}, полоса {lane + 1} не имеет независимого подхода");

                bool Allowed(int i)
                {
                    float x = Vector2.Dot(grid.Center(i) - mid, lateral);
                    return !grid.Blocked[i] && clearance[i] >= LevelDesignRules.MinPassageWidth * 0.5f - grid.Cell * 0.5f &&
                        (grid.Zone[i] == side || (grid.Zone[i] == MapGrid.NoZone && x >= min && x <= max));
                }
                void Visit(int x, int z)
                {
                    if (x < 0 || z < 0 || x >= grid.Width || z >= grid.Depth) return;
                    int i = grid.Index(x, z);
                    if (reached[i] || !Allowed(i)) return;
                    reached[i] = true; queue.Enqueue(i);
                }
            }
            return errors;
        }

        // Для ортогональных блоков; окна по высоте и диагональные формы требуют отдельного ревью.
        public static List<string> CheckResidualGaps(IEnumerable<Bounds> footprints)
        {
            var items = new List<Bounds>(footprints);
            var errors = new List<string>();
            for (int a = 0; a < items.Count; a++)
            for (int b = a + 1; b < items.Count; b++)
            foreach (int axis in new[] { 0, 2 })
            {
                int cross = axis == 0 ? 2 : 0;
                var left = items[a]; var right = items[b];
                if (left.center[axis] > right.center[axis]) { var tmp = left; left = right; right = tmp; }
                float gap = right.min[axis] - left.max[axis];
                float overlap = Mathf.Min(left.max[cross], right.max[cross]) - Mathf.Max(left.min[cross], right.min[cross]);
                float vertical = Mathf.Min(left.max.y, right.max.y) - Mathf.Max(left.min.y, right.min.y);
                if (gap <= 0.02f || gap >= LevelDesignRules.MinPassageWidth || overlap < 0.5f || vertical < 0.3f) continue;
                var center = (left.center + right.center) * 0.5f;
                center[axis] = (left.max[axis] + right.min[axis]) * 0.5f;
                center[cross] = (Mathf.Max(left.min[cross], right.min[cross]) + Mathf.Min(left.max[cross], right.max[cross])) * 0.5f;
                bool filled = false;
                for (int k = 0; k < items.Count; k++)
                    if (k != a && k != b && items[k].Contains(center)) { filled = true; break; }
                if (!filled) errors.Add($"LD-50: блоки {a}/{b}, остаточный зазор {gap:F2} м");
            }
            return errors;
        }

        public static List<string> CheckOpenCover(MapGrid grid)
        {
            var errors = new List<string>();
            if (!MapAnalyzer.TryHalves(grid, out var mid, out var axis))
            {
                errors.Add("LD-51: не размечены обе базы"); return errors;
            }
            float inner = float.MaxValue;
            for (int i = 0; i < grid.Count; i++)
                if (grid.Zone[i] != MapGrid.NoZone)
                    inner = Mathf.Min(inner, Mathf.Abs(Vector2.Dot(grid.Center(i) - mid, axis)));
            int low = 0, medium = 0, tall = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                if (grid.Zone[i] != MapGrid.NoZone || Mathf.Abs(Vector2.Dot(grid.Center(i) - mid, axis)) > inner * 0.6f) continue;
                float h = grid.Height[i];
                if (h >= 2f - LevelDesignRules.CoverTolerance) tall++;
                else if (h >= 1.5f - LevelDesignRules.CoverTolerance) medium++;
                else if (h >= 1f - LevelDesignRules.CoverTolerance) low++;
            }
            if (low == 0 || medium == 0 || low + medium < tall * 2)
                errors.Add($"LD-51: площадь Low/Mid/Tall {low * grid.Cell * grid.Cell:F2}/{medium * grid.Cell * grid.Cell:F2}/{tall * grid.Cell * grid.Cell:F2} м²; нужны оба класса и Low+Mid ≥ 2×Tall");
            return errors;
        }
        public static bool SuggestSoftCover(bool controlsCriticalContacts, bool protectedFromFlank,
            float protectedWidth, float playerWidth) => controlsCriticalContacts && protectedFromFlank &&
                playerWidth > 0f && protectedWidth >= playerWidth;
    }
}

using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Диск тела вдоль отрезка: точная проверка относительно прямоугольных занятых/запрещённых клеток.</summary>
    public static class PositionRouteGeometry
    {
        public static bool Fits(MapGrid grid, Vector2 a, Vector2 b, float radius, bool[] allowed)
        {
            Vector2 min = Vector2.Min(a, b) - Vector2.one * radius, max = Vector2.Max(a, b) + Vector2.one * radius;
            Vector2 upper = grid.Origin + new Vector2(grid.Width, grid.Depth) * grid.Cell;
            if (min.x < grid.Origin.x - 1e-5f || min.y < grid.Origin.y - 1e-5f || max.x > upper.x + 1e-5f || max.y > upper.y + 1e-5f) return false;
            int x0 = Math.Max(0, Mathf.FloorToInt((min.x - grid.Origin.x) / grid.Cell));
            int z0 = Math.Max(0, Mathf.FloorToInt((min.y - grid.Origin.y) / grid.Cell));
            int x1 = Math.Min(grid.Width - 1, Mathf.FloorToInt((max.x - grid.Origin.x) / grid.Cell));
            int z1 = Math.Min(grid.Depth - 1, Mathf.FloorToInt((max.y - grid.Origin.y) / grid.Cell));
            float squared = radius * radius;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                int i = grid.Index(x, z);
                if (!grid.Blocked[i] && (allowed == null || allowed[i])) continue;
                Vector2 low = grid.Origin + new Vector2(x, z) * grid.Cell;
                if (DistanceSquared(a, b, low, low + Vector2.one * grid.Cell) + 1e-8f < squared) return false;
            }
            return true;
        }

        public static float ClearanceAlong(MapGrid grid, Vector2 a, Vector2 b, bool[] allowed, float upperBound)
        {
            Vector2 upper = grid.Origin + new Vector2(grid.Width, grid.Depth) * grid.Cell;
            Vector2 lowPoint = Vector2.Min(a, b), highPoint = Vector2.Max(a, b);
            float best = Math.Max(0, Math.Min(upperBound, Math.Min(Math.Min(lowPoint.x - grid.Origin.x, lowPoint.y - grid.Origin.y),
                Math.Min(upper.x - highPoint.x, upper.y - highPoint.y))));
            Vector2 min = lowPoint - Vector2.one * best, max = highPoint + Vector2.one * best;
            int x0 = Math.Max(0, Mathf.FloorToInt((min.x - grid.Origin.x) / grid.Cell));
            int z0 = Math.Max(0, Mathf.FloorToInt((min.y - grid.Origin.y) / grid.Cell));
            int x1 = Math.Min(grid.Width - 1, Mathf.FloorToInt((max.x - grid.Origin.x) / grid.Cell));
            int z1 = Math.Min(grid.Depth - 1, Mathf.FloorToInt((max.y - grid.Origin.y) / grid.Cell));
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                int i = grid.Index(x, z);
                if (!grid.Blocked[i] && (allowed == null || allowed[i])) continue;
                Vector2 corner = grid.Origin + new Vector2(x, z) * grid.Cell;
                best = Math.Min(best, (float)Math.Sqrt(DistanceSquared(a, b, corner, corner + Vector2.one * grid.Cell)));
            }
            return best;
        }

        private static float DistanceSquared(Vector2 a, Vector2 b, Vector2 low, Vector2 high)
        {
            float start = 0, end = 1; Vector2 delta = b - a;
            if (Slab(a.x, delta.x, low.x, high.x, ref start, ref end) && Slab(a.y, delta.y, low.y, high.y, ref start, ref end)) return 0;
            float best = Math.Min(PointRectangle(a, low, high), PointRectangle(b, low, high));
            best = Math.Min(best, PointSegment(low, a, b)); best = Math.Min(best, PointSegment(high, a, b));
            best = Math.Min(best, PointSegment(new Vector2(low.x, high.y), a, b));
            return Math.Min(best, PointSegment(new Vector2(high.x, low.y), a, b));
        }
        private static bool Slab(float origin, float delta, float low, float high, ref float start, ref float end)
        {
            if (Math.Abs(delta) < 1e-8f) return origin >= low && origin <= high;
            float a = (low - origin) / delta, b = (high - origin) / delta;
            start = Math.Max(start, Math.Min(a, b)); end = Math.Min(end, Math.Max(a, b)); return start <= end;
        }
        private static float PointRectangle(Vector2 point, Vector2 low, Vector2 high) =>
            (point - new Vector2(Mathf.Clamp(point.x, low.x, high.x), Mathf.Clamp(point.y, low.y, high.y))).sqrMagnitude;
        private static float PointSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float t = delta.sqrMagnitude <= 1e-12f ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
            return (point - (a + delta * t)).sqrMagnitude;
        }
    }
}

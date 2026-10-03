using System;
using UnityEngine;
namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class MapSpatialMetricsResult
    {
        public int version = 1; public string problem;
        public bool complete; public float movementObstacleShare; public float standingClosure; public float crouchingClosure;
        public float freeShare; public int combatCells; public int sampleCount; public int lineQueries;
        public float sampleStep; public int directions; public float distanceTolerance;
        public float cellStep;
        public string occupancyMode;
    }
    public static class MapSpatialMetrics
    {
        public static MapSpatialMetricsResult Measure(MapGrid grid, float[] clearance = null,
            float sampleStep = .5f, int directions = 16, float distanceTolerance = .1f)
        {
            var r = new MapSpatialMetricsResult { sampleStep = sampleStep, directions = directions, distanceTolerance = distanceTolerance };
            if (grid == null || sampleStep <= 0 || float.IsNaN(sampleStep) || float.IsInfinity(sampleStep) ||
                directions < 4 || directions > 128 || distanceTolerance < .001f || float.IsNaN(distanceTolerance) || float.IsInfinity(distanceTolerance))
            { r.problem = "Нет сетки или неверная точность."; return r; }
            r.cellStep = grid.Cell;
            r.occupancyMode = grid.Footprint != null ? "TouchedColliderCells" : "SyntheticSpans";
            int occupied = 0;
            for (int i = 0; i < grid.Count; i++)
                if (grid.Zone[i] == MapGrid.NoZone) { r.combatCells++; if (grid.Blocked[i]) occupied++; }
            if (r.combatCells == 0) { r.problem = "Нет боевой площади вне баз."; return r; }
            r.movementObstacleShare = (float)occupied / r.combatCells;
            r.freeShare = 1 - r.movementObstacleShare;
            clearance = clearance ?? MapAnalyzer.Clearance(grid);
            int stride = Mathf.Max(1, Mathf.RoundToInt(sampleStep / grid.Cell));
            double standing = 0, crouching = 0;
            Vector2 min = grid.Origin, max = grid.Origin + new Vector2(grid.Width * grid.Cell, grid.Depth * grid.Cell);
            for (int z = 0; z < grid.Depth; z += stride)
            for (int x = 0; x < grid.Width; x += stride)
            {
                int i = grid.Index(x, z);
                if (grid.Zone[i] != MapGrid.NoZone || grid.Blocked[i] || clearance[i] < .5f) continue;
                Vector2 p = grid.Center(i);
                r.sampleCount++;
                for (int d = 0; d < directions; d++)
                {
                    float angle = 2 * Mathf.PI * d / directions;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float dx = Mathf.Abs(dir.x) < .00001f ? float.PositiveInfinity : ((dir.x > 0 ? max.x : min.x) - p.x) / dir.x;
                    float dz = Mathf.Abs(dir.y) < .00001f ? float.PositiveInfinity : ((dir.y > 0 ? max.y : min.y) - p.y) / dir.y;
                    float edge = Mathf.Min(dx, dz);
                    standing += Closure(LevelDesignRules.EyeHeight);
                    crouching += Closure(LevelDesignRules.CrouchEyeHeight);
                    double Closure(float eye)
                    {
                        // Конец внутри пола: его внешняя граница не считается интерьерным укрытием.
                        float end = Mathf.Max(0, edge - .001f);
                        bool Visible(float distance)
                        {
                            r.lineQueries++;
                            Vector2 target = p + dir * distance;
                            return grid.LineOfSight(new Vector3(p.x, eye, p.y), new Vector3(target.x, eye, target.y));
                        }
                        if (Visible(end)) return 0;
                        float lo = 0, hi = end;
                        // LOS префикса луча монотонен: первая преграда остаётся на каждом более длинном префиксе.
                        while (hi - lo > distanceTolerance)
                        {
                            float middle = (lo + hi) * .5f;
                            if (Visible(middle)) lo = middle; else hi = middle;
                        }
                        return 1 - Mathf.Clamp01((lo + hi) * .5f / edge);
                    }
                }
            }
            if (r.sampleCount == 0) { r.problem = "Нет доступных образцов боевой зоны с радиусом 0.5 м."; return r; }
            r.standingClosure = (float)(standing / (r.sampleCount * directions));
            r.crouchingClosure = (float)(crouching / (r.sampleCount * directions));
            r.complete = true;
            return r;
        }
    }
}

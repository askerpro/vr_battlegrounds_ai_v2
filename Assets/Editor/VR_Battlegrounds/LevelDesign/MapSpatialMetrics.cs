using System;
using System.Collections.Generic;
using System.Linq;
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
            var result = new MapSpatialMetricsResult();
            foreach (var work in MeasureSteps(grid, result, clearance, sampleStep, directions, distanceTolerance)) work.Execute();
            return result;
        }

        /// <summary>Общая формула закрытости, с точкой приостановки до каждого LOS.</summary>
        internal static IEnumerable<MapEvaluationWork> MeasureSteps(MapGrid grid, MapSpatialMetricsResult r, float[] clearance = null,
            float sampleStep = .5f, int directions = 16, float distanceTolerance = .1f, bool batchLines = false)
        {
            r.sampleStep = sampleStep; r.directions = directions; r.distanceTolerance = distanceTolerance;
            if (grid == null || sampleStep <= 0 || float.IsNaN(sampleStep) || float.IsInfinity(sampleStep) ||
                directions < 4 || directions > 128 || distanceTolerance < .001f || float.IsNaN(distanceTolerance) || float.IsInfinity(distanceTolerance))
            { r.problem = "Нет сетки или неверная точность."; yield break; }
            r.cellStep = grid.Cell;
            r.occupancyMode = grid.Footprint != null ? "TouchedColliderCells" : "SyntheticSpans";
            int occupied = 0;
            for (int i = 0; i < grid.Count; i++)
                if (grid.Zone[i] == MapGrid.NoZone) { r.combatCells++; if (grid.Blocked[i]) occupied++; }
            if (r.combatCells == 0) { r.problem = "Нет боевой площади вне баз."; yield break; }
            r.movementObstacleShare = (float)occupied / r.combatCells;
            r.freeShare = 1 - r.movementObstacleShare;
            clearance = clearance ?? MapAnalyzer.Clearance(grid);
            double standing = 0, crouching = 0;
            var closures = new List<ClosureProbe>();
            bool batched = batchLines && grid.BeginLineBatch != null;
            foreach (var ray in Rays(grid, clearance, sampleStep, directions))
            {
                if (ray.First) r.sampleCount++;
                foreach (bool upright in new[] { true, false })
                {
                    var probe = new ClosureProbe(ray, upright, distanceTolerance);
                    if (batched) { closures.Add(probe); continue; }
                    while (!probe.Done)
                    {
                        yield return new MapEvaluationWork(1, () => { r.lineQueries++; probe.Receive(grid.LineOfSight(probe.From, probe.To)); });
                    }
                    if (upright) standing += probe.Fraction; else crouching += probe.Fraction;
                }
            }
            if (batched)
            {
                // Зависимые префиксы остаются последовательными внутри каждого луча; независимые лучи идут волнами.
                while (closures.Any(p => !p.Done))
                {
                    var pending = closures.Where(p => !p.Done).ToArray();
                    for (int offset = 0; offset < pending.Length; offset += 128)
                    {
                        var portion = pending.Skip(offset).Take(128).ToArray();
                        using (var batch = grid.BeginLineBatch(portion.Select((p, i) => new MapGrowthRayRequest(i, p.From, p.To)).ToArray()))
                            yield return new MapEvaluationWork(portion.Length, () => {
                                foreach (var row in batch.Complete()) { r.lineQueries++; portion[row.Id].Receive(row.Visible); }
                            }, "spatial-report", () => batch.Ready);
                    }
                }
                // Тот же порядок суммирования, что у синхронного backend.
                foreach (var probe in closures)
                    if (probe.Upright) standing += probe.Fraction; else crouching += probe.Fraction;
            }
            if (r.sampleCount == 0) { r.problem = "Нет доступных образцов боевой зоны с радиусом 0.5 м."; yield break; }
            r.standingClosure = (float)(standing / (r.sampleCount * directions));
            r.crouchingClosure = (float)(crouching / (r.sampleCount * directions));
            r.complete = true;
        }

        private readonly struct SpatialRay
        {
            internal readonly Vector2 Point, Direction;
            internal readonly float Edge;
            internal readonly bool First;
            internal SpatialRay(Vector2 point, Vector2 direction, float edge, bool first)
            { Point = point; Direction = direction; Edge = edge; First = first; }
        }
        private static IEnumerable<SpatialRay> Rays(MapGrid grid, float[] clearance, float sampleStep, int directions)
        {
            int stride = Mathf.Max(1, Mathf.RoundToInt(sampleStep / grid.Cell));
            Vector2 min = grid.Origin, max = grid.Origin + new Vector2(grid.Width * grid.Cell, grid.Depth * grid.Cell);
            for (int z = 0; z < grid.Depth; z += stride)
            for (int x = 0; x < grid.Width; x += stride)
            {
                int i = grid.Index(x, z);
                if (grid.Zone[i] != MapGrid.NoZone || grid.Blocked[i] || clearance[i] < .5f) continue;
                Vector2 p = grid.Center(i);
                for (int d = 0; d < directions; d++)
                {
                    float angle = 2 * Mathf.PI * d / directions;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float dx = Mathf.Abs(dir.x) < .00001f ? float.PositiveInfinity : ((dir.x > 0 ? max.x : min.x) - p.x) / dir.x;
                    float dz = Mathf.Abs(dir.y) < .00001f ? float.PositiveInfinity : ((dir.y > 0 ? max.y : min.y) - p.y) / dir.y;
                    yield return new SpatialRay(p, dir, Mathf.Min(dx, dz), d == 0);
                }
            }
        }
        private sealed class ClosureProbe
        {
            private readonly SpatialRay ray;
            private readonly float tolerance, eye;
            private float lo, hi, distance;
            private bool initial = true;
            internal readonly bool Upright;
            internal bool Done { get; private set; }
            internal double Fraction { get; private set; }
            internal Vector3 From => new Vector3(ray.Point.x, eye, ray.Point.y);
            internal Vector3 To { get { Vector2 p = ray.Point + ray.Direction * distance; return new Vector3(p.x, eye, p.y); } }
            internal ClosureProbe(SpatialRay ray, bool upright, float tolerance)
            { this.ray = ray; Upright = upright; this.tolerance = tolerance; eye = upright ? LevelDesignRules.EyeHeight : LevelDesignRules.CrouchEyeHeight; hi = distance = Mathf.Max(0, ray.Edge - .001f); }
            internal void Receive(bool visible)
            {
                if (initial) { initial = false; if (visible) { Done = true; Fraction = 0; return; } }
                else if (visible) lo = distance; else hi = distance;
                if (hi - lo <= tolerance) { Done = true; Fraction = 1 - Mathf.Clamp01((lo + hi) * .5f / ray.Edge); }
                else distance = (lo + hi) * .5f;
            }
        }
    }
}

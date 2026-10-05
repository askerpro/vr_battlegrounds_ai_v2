using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum MapGrowthFilterStatus { Impossible, Possible, NeedsGeometryCheck }
    public sealed class MapGrowthFilterResult
    {
        public MapGrowthFilterStatus status;
        public string[] reasons = Array.Empty<string>(), owners = Array.Empty<string>();
        public int[] touchedCells = Array.Empty<int>(), bodyBlockedCells = Array.Empty<int>();
    }

    /// <summary>След настоящей геометрии на одном выравнивании. Не меняет меш или исходную толщину.</summary>
    public sealed class MapGrowthFootprintTemplate
    {
        public string InputVersion { get; }
        public MapGrowthBlockRecipe Prototype { get; }
        public Vector2 SourceOrigin { get; }
        public float Cell { get; }
        private readonly Vector2Int[] touched, body;
        public MapGrowthFootprintTemplate(string version, MapGrowthBlockRecipe prototype, Vector2 sourceOrigin, float cell,
            Vector2Int[] touched, Vector2Int[] bodyBlocked)
        {
            if (string.IsNullOrWhiteSpace(version) || prototype == null || !PositionImpactValidation.Finite(sourceOrigin)
                || !PositionImpactValidation.Finite(cell) || cell <= 0 || touched == null || bodyBlocked == null)
                throw new ArgumentException("Нужен версионированный след геометрии.");
            var all = new HashSet<Vector2Int>(touched);
            if (all.Count != touched.Length || bodyBlocked.Distinct().Count() != bodyBlocked.Length || bodyBlocked.Any(p => !all.Contains(p)))
                throw new ArgumentException("Следы не содержат дубликатов; блокировка тела входит в след геометрии.");
            InputVersion = version; Prototype = prototype; SourceOrigin = sourceOrigin; Cell = cell;
            this.touched = (Vector2Int[])touched.Clone(); body = (Vector2Int[])bodyBlocked.Clone();
        }
        public bool TryPlacement(MapGrowthSnapshot snapshot, MapGrowthBlockRecipe recipe, out Vector2Int[] cells, out Vector2Int[] bodyCells)
        {
            cells = bodyCells = Array.Empty<Vector2Int>();
            if (snapshot.InputVersion != InputVersion || snapshot.Cell != Cell || recipe.ShapeId != Prototype.ShapeId
                || (recipe.Dimensions - Prototype.Dimensions).sqrMagnitude > 1e-10f || (recipe.TopSize - Prototype.TopSize).sqrMagnitude > 1e-10f
                || Math.Abs(Mathf.DeltaAngle(recipe.Yaw, Prototype.Yaw)) > 1e-5f || Math.Abs(recipe.BottomCenter.y - Prototype.BottomCenter.y) > 1e-5f)
                return false;
            var a = recipe.CopySections(); var b = Prototype.CopySections();
            for (int i = 0; i < a.Length; i++) if (a[i].material != b[i].material || !a[i].openings.Equals(b[i].openings)) return false;
            double dx = ((double)recipe.BottomCenter.x - Prototype.BottomCenter.x - (snapshot.Origin.x - SourceOrigin.x)) / Cell;
            double dz = ((double)recipe.BottomCenter.z - Prototype.BottomCenter.z - (snapshot.Origin.y - SourceOrigin.y)) / Cell;
            double x = Math.Round(dx), z = Math.Round(dz);
            if (Math.Abs(dx - x) > 1e-5 || Math.Abs(dz - z) > 1e-5 || x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue)
                return false;
            if (!Shift(touched, (int)x, (int)z, out cells) || !Shift(body, (int)x, (int)z, out bodyCells)) return false;
            return true;
        }
        private static bool Shift(Vector2Int[] source, int dx, int dz, out Vector2Int[] shifted)
        {
            shifted = new Vector2Int[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                long x = (long)source[i].x + dx, z = (long)source[i].y + dz;
                if (x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue) return false;
                shifted[i] = new Vector2Int((int)x, (int)z);
            }
            return true;
        }
    }

    /// <summary>Консервативный отсев. Общая клетка или неопределённый габарит требуют точной геометрии.</summary>
    public static class MapGrowthCellFilter
    {
        public static MapGrowthFilterResult Check(MapGrowthSnapshot snapshot, MapGrowthCandidate candidate, MapGrowthBlockRecipe proposed)
        {
            if (snapshot == null || candidate == null || proposed == null || !candidate.BelongsTo(snapshot))
                throw new ArgumentException("Кандидат и след должны принадлежать одному снимку.");
            Vector2Int[] cells = null, body = null;
            foreach (var sample in snapshot.Footprints)
                if (sample.TryPlacement(snapshot, proposed, out var touched, out var blocked)) { cells = touched; body = blocked; break; }
            if (cells == null) return new MapGrowthFilterResult { status = MapGrowthFilterStatus.NeedsGeometryCheck,
                reasons = new[] { "Нет следа для этой версии, формы, размеров, секций, yaw или выравнивания; нужен новый геометрический захват." } };
            var result = new MapGrowthFilterResult { status = candidate.OccupancyComplete ? MapGrowthFilterStatus.Possible : MapGrowthFilterStatus.NeedsGeometryCheck };
            var touchedIndices = new List<int>(); var bodyIndices = new List<int>(); var owners = new HashSet<string>(StringComparer.Ordinal);
            var reasons = new List<string>();
            if (!candidate.OccupancyComplete) reasons.Add("Часть геометрии кандидата не имеет актуального следа; нужна точная проверка.");
            foreach (var cell in cells)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= snapshot.Width || cell.y >= snapshot.Depth)
                { result.status = MapGrowthFilterStatus.Impossible; reasons.Add($"След геометрии вне сетки: ({cell.x}, {cell.y})."); continue; }
                int index = cell.y * snapshot.Width + cell.x; touchedIndices.Add(index);
                if (snapshot.IsForbidden(index))
                { result.status = MapGrowthFilterStatus.Impossible; reasons.Add($"Геометрия задевает явно запрещённую клетку ({cell.x}, {cell.y})."); }
                else if (snapshot.IsReserved(index) || snapshot.HasFixedTouch(index) || candidate.OwnersAt(index).Length > 0)
                {
                    if (result.status != MapGrowthFilterStatus.Impossible) result.status = MapGrowthFilterStatus.NeedsGeometryCheck;
                    foreach (string owner in candidate.OwnersAt(index)) owners.Add(owner);
                    foreach (string owner in snapshot.FixedOwnersAt(index)) owners.Add(owner);
                    reasons.Add($"Общая клетка/резерв ({cell.x}, {cell.y}) требует точной проверки; это не доказанная коллизия.");
                }
            }
            foreach (var cell in body)
                if (cell.x >= 0 && cell.y >= 0 && cell.x < snapshot.Width && cell.y < snapshot.Depth) bodyIndices.Add(cell.y * snapshot.Width + cell.x);
            if (result.status != MapGrowthFilterStatus.Impossible && candidate.OccupancyComplete)
            {
                var grid = candidate.CreateMovementGrid(); foreach (int index in bodyIndices) grid.Blocked[index] = true;
                var clear = MapAnalyzer.Clearance(grid); var layout = snapshot.CopyLayout();
                if (layout.positions.SelectMany(p => p.states).Any(s => {
                    int i = grid.IndexAt(s.center); return i < 0 || grid.Blocked[i] || clear[i] < layout.radius;
                }))
                { result.status = MapGrowthFilterStatus.NeedsGeometryCheck; reasons.Add("Клеточная маска не гарантирует габарит позы; проверить настоящее тело/геометрию."); }
            }
            result.touchedCells = touchedIndices.ToArray(); result.bodyBlockedCells = bodyIndices.ToArray();
            result.owners = owners.OrderBy(id => id, StringComparer.Ordinal).ToArray(); result.reasons = reasons.ToArray(); return result;
        }
    }
}

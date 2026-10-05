using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VrBattlegrounds.Editor.LevelDesign
{
    internal delegate Task<ImpactRoute> MapRouteBuilder(MapGrid grid, Vector2 from, Vector2 to, Vector2[] via,
        float radius, float speed, bool[] allowed, float[] clearance, bool direct);
    public static class PositionRouteAnalyzer
    {
        public static ImpactRoute Build(MapGrid grid, Vector2 from, Vector2 to, Vector2[] via, float radius, float speed)
            => Build(grid, from, to, via, radius, speed, null, null);

        public static ImpactRoute Build(MapGrid grid, Vector2 from, Vector2 to, Vector2[] via, float radius, float speed,
            bool[] allowedCells, float[] clearance, bool requireDirect = false)
        {
            if (grid == null || !PositionImpactValidation.Finite(from) || !PositionImpactValidation.Finite(to)
                || !PositionImpactValidation.Finite(radius) || radius <= 0
                || !PositionImpactValidation.Finite(speed) || speed <= 0)
                throw new ArgumentException("Неверные параметры маршрута.");
            if (allowedCells != null && allowedCells.Length != grid.Count || clearance != null && clearance.Length != grid.Count)
                throw new ArgumentException("Маска/clearance принадлежат другой сетке.");
            float[] clear = clearance ?? MapAnalyzer.Clearance(grid);
            if (allowedCells != null)
            {
                var mask = new MapGrid(grid.Width, grid.Depth, grid.Cell, grid.Origin);
                for (int i = 0; i < grid.Count; i++) mask.Blocked[i] = !allowedCells[i];
                float[] boundary = MapAnalyzer.Clearance(mask);
                var combined = new float[grid.Count];
                for (int i = 0; i < grid.Count; i++) combined[i] = Math.Min(clear[i], boundary[i]);
                clear = combined;
            }
            var result = new ImpactRoute();
            var anchors = new List<Vector2> { from };
            if (via != null) anchors.AddRange(via);
            anchors.Add(to);
            if (requireDirect)
            {
                Vector2 direction = to - from; float last = 0;
                foreach (Vector2 point in anchors)
                {
                    float t = direction.sqrMagnitude < 1e-12f ? 0 : Vector2.Dot(point - from, direction) / direction.sqrMagnitude;
                    if (!PositionImpactValidation.Finite(point) || t < last - 1e-5f || t > 1 + 1e-5f || (point - (from + direction * t)).sqrMagnitude > 1e-8f)
                    { result.problem = "Direct: портал вне прямого прохода между концами."; return result; }
                    last = t;
                }
                if (!PositionRouteGeometry.Fits(grid, from, to, radius, allowedCells))
                { result.problem = "Direct, сегмент 1: габарит тела пересекает занятые клетки, границу коридора или край сетки."; return result; }
                int intervals = Math.Max(1, Mathf.CeilToInt(direction.magnitude / grid.Cell));
                result.points = new Vector2[intervals + 1]; result.minimumClearance = float.PositiveInfinity;
                for (int n = 0; n <= intervals; n++)
                    result.points[n] = Vector2.Lerp(from, to, (float)n / intervals);
                result.minimumClearance = PositionRouteGeometry.ClearanceAlong(grid, from, to, allowedCells, float.PositiveInfinity);
                result.length = direction.magnitude; result.duration = result.length / speed; result.reachable = true; return result;
            }
            var path = new List<int>();
            var authoredPath = allowedCells == null ? null : new List<Vector2>();
            for (int n = 0; n + 1 < anchors.Count; n++)
            {
                if (!PositionImpactValidation.Finite(anchors[n]) || !PositionImpactValidation.Finite(anchors[n + 1]))
                { result.problem = "Нечисловой портал."; return result; }
                int start = grid.IndexAt(anchors[n]), end = grid.IndexAt(anchors[n + 1]);
                List<int> segment = FindPath(grid, clear, start, end, radius, allowedCells);
                if (segment == null)
                { result.problem = $"Сегмент {n + 1}: нет пути для заданного радиуса" + (allowedCells == null ? "." : " внутри допустимого коридора; проверьте концы, ширину и занятые клетки."); return result; }
                if (authoredPath != null)
                {
                    Vector2 first = grid.Center(segment[0]), last = grid.Center(segment[segment.Count - 1]);
                    if (!PositionRouteGeometry.Fits(grid, anchors[n], first, radius, allowedCells)
                        || !PositionRouteGeometry.Fits(grid, last, anchors[n + 1], radius, allowedCells))
                    { result.problem = $"Сегмент {n + 1}: габарит тела не помещается у фактического конца/портала или на присоединении к клеточному пути."; return result; }
                    AddPoint(authoredPath, anchors[n]);
                    foreach (int cell in segment) AddPoint(authoredPath, grid.Center(cell));
                    AddPoint(authoredPath, anchors[n + 1]);
                }
                if (path.Count > 0) segment.RemoveAt(0);
                path.AddRange(segment);
            }
            result.points = authoredPath == null ? new Vector2[path.Count] : authoredPath.ToArray();
            result.minimumClearance = float.PositiveInfinity;
            for (int n = 0; n < result.points.Length; n++)
            {
                if (authoredPath == null) result.points[n] = grid.Center(path[n]);
                result.minimumClearance = Math.Min(result.minimumClearance, clear[grid.IndexAt(result.points[n])]);
                if (n > 0) result.length += (result.points[n] - result.points[n - 1]).magnitude;
            }
            if (allowedCells != null)
            {
                if (result.points.Length == 1) result.minimumClearance = PositionRouteGeometry.ClearanceAlong(grid, result.points[0], result.points[0], allowedCells, result.minimumClearance);
                for (int n = 1; n < result.points.Length; n++)
                    result.minimumClearance = PositionRouteGeometry.ClearanceAlong(grid, result.points[n - 1], result.points[n], allowedCells, result.minimumClearance);
            }
            result.duration = result.length / speed;
            result.reachable = true;
            return result;
        }

        private static void AddPoint(List<Vector2> path, Vector2 point)
        { if (path.Count == 0 || (path[path.Count - 1] - point).sqrMagnitude > 1e-12f) path.Add(point); }

        private static List<int> FindPath(MapGrid grid, float[] clear, int start, int end, float radius, bool[] mask)
        {
            var checkedCells = mask == null ? null : new bool[grid.Count];
            var fit = mask == null ? null : new bool[grid.Count];
            bool Allowed(int i)
            {
                if (i < 0 || i >= grid.Count || grid.Blocked[i] || clear[i] + 1e-5f < radius || mask != null && !mask[i]) return false;
                if (mask == null) return true;
                if (!checkedCells[i]) { fit[i] = PositionRouteGeometry.Fits(grid, grid.Center(i), grid.Center(i), radius, mask); checkedCells[i] = true; }
                return fit[i];
            }
            if (!Allowed(start) || !Allowed(end)) return null;
            var dist = new float[grid.Count];
            var parent = new int[grid.Count];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.PositiveInfinity; parent[i] = -1; }
            // Сначала стоимость, затем индекс: воспроизводимый выбор при равных путях.
            var pending = new SortedSet<(float cost, int cell)>();
            dist[start] = 0; pending.Add((0, start));
            while (pending.Count > 0)
            {
                var current = pending.Min; pending.Remove(current);
                int i = current.cell;
                if (i == end) break;
                int x = grid.X(i), z = grid.Z(i);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0 || !grid.InBounds(x + dx, z + dz)) continue;
                    int j = grid.Index(x + dx, z + dz);
                    if (!Allowed(j)) continue;
                    if (dx != 0 && dz != 0 && (!Allowed(grid.Index(x + dx, z)) || !Allowed(grid.Index(x, z + dz)))) continue;
                    if (mask != null && !PositionRouteGeometry.Fits(grid, grid.Center(i), grid.Center(j), radius, mask)) continue;
                    float next = dist[i] + grid.Cell * (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                    if (next >= dist[j]) continue;
                    pending.Remove((dist[j], j));
                    dist[j] = next; parent[j] = i; pending.Add((next, j));
                }
            }
            if (float.IsPositiveInfinity(dist[end])) return null;
            var path = new List<int>();
            for (int i = end; i >= 0; i = parent[i]) { path.Add(i); if (i == start) break; }
            path.Reverse();
            return path;
        }

        /// <summary>Стрелок остаётся в одном состоянии. Образцы движущегося тела поворачиваются по направлению ребра.</summary>
        public static ImpactRouteInfluence Evaluate(MapGrid grid, ImpactState source, ImpactRoute route, ImpactState bodyTemplate, float speed)
        {
            var result = new ImpactRouteInfluence();
            foreach (var work in EvaluateSteps(grid, source, route, bodyTemplate, speed, result)) work.Execute();
            return result;
        }
        internal static IEnumerable<MapEvaluationWork> EvaluateSteps(MapGrid grid, ImpactState source, ImpactRoute route,
            ImpactState bodyTemplate, float speed, ImpactRouteInfluence result)
        {
            if (route == null || !route.reachable || route.points == null || bodyTemplate == null
                || !PositionImpactValidation.Finite(speed) || speed <= 0)
                throw new ArgumentException("Для оценки требуется доступный маршрут и шаблон тела.");
            if (source == null || source.body == null || source.body.Length == 0 || bodyTemplate.body == null || bodyTemplate.body.Length == 0)
                throw new ArgumentException("Нет состояния или образцов тела.");
            result.state = source.id; result.targetTemplate = bodyTemplate.id;
            var intervals = new List<ImpactRouteInterval>();
            float distance = 0, continuous = 0;
            for (int n = 1; n < route.points.Length; n++)
            {
                Vector2 a = route.points[n - 1], b = route.points[n];
                float length = (b - a).magnitude;
                if (length <= 0) continue;
                var target = new ImpactState
                {
                    id = bodyTemplate.id, center = (a + b) * 0.5f,
                    yaw = (float)(Math.Atan2(b.x - a.x, b.y - a.y) * 180.0 / Math.PI),
                    eyeOffset = bodyTemplate.eyeOffset, muzzleOffset = bodyTemplate.muzzleOffset, body = bodyTemplate.body
                };
                yield return new MapEvaluationWork(3 * (source.body.Length + target.body.Length), () => {
                ImpactPairState row = PositionImpactAnalyzer.Evaluate(grid, source, target);
                intervals.Add(new ImpactRouteInterval
                {
                    from = a, to = b, startDistance = distance, length = length,
                    visibleShare = row.visibleShare, shotShare = row.shotShare,
                    visibleShotShare = row.visibleShotShare, hiddenShotShare = row.hiddenShotShare
                });
                // Длина воздействия: хоть один образец поражаем. Доли тела сохраняются отдельно, не подменяют время.
                if (row.visibleShare > 0)
                {
                    result.visibleLength += length;
                    if (result.firstVisibleDistance < 0) result.firstVisibleDistance = distance;
                }
                if (row.shotShare > 0)
                {
                    result.shotLength += length; continuous += length;
                    result.longestShotLength = Math.Max(result.longestShotLength, continuous);
                    if (result.firstShotDistance < 0) result.firstShotDistance = distance;
                }
                else continuous = 0;
                if (row.hiddenShotShare > 0) result.hiddenShotLength += length;
                distance += length;
                });
            }
            result.shotDuration = result.shotLength / speed;
            result.intervals = intervals.ToArray();
        }
    }
}

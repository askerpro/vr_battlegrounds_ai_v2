using UnityEngine;
using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public static class PositionRouteAnalyzer
    {
        public static ImpactRoute Build(MapGrid grid, Vector2 from, Vector2 to, Vector2[] via, float radius, float speed)
        {
            if (grid == null || !PositionImpactValidation.Finite(from) || !PositionImpactValidation.Finite(to)
                || !PositionImpactValidation.Finite(radius) || radius <= 0
                || !PositionImpactValidation.Finite(speed) || speed <= 0)
                throw new ArgumentException("Неверные параметры маршрута.");
            float[] clear = MapAnalyzer.Clearance(grid);
            var result = new ImpactRoute();
            var anchors = new List<Vector2> { from };
            if (via != null) anchors.AddRange(via);
            anchors.Add(to);
            var path = new List<int>();
            for (int n = 0; n + 1 < anchors.Count; n++)
            {
                if (!PositionImpactValidation.Finite(anchors[n]) || !PositionImpactValidation.Finite(anchors[n + 1]))
                { result.problem = "Нечисловой портал."; return result; }
                int start = grid.IndexAt(anchors[n]), end = grid.IndexAt(anchors[n + 1]);
                List<int> segment = FindPath(grid, clear, start, end, radius);
                if (segment == null)
                { result.problem = $"Сегмент {n + 1}: нет пути для заданного радиуса."; return result; }
                if (path.Count > 0) segment.RemoveAt(0);
                path.AddRange(segment);
            }
            result.points = new Vector2[path.Count];
            result.minimumClearance = float.PositiveInfinity;
            for (int n = 0; n < path.Count; n++)
            {
                result.points[n] = grid.Center(path[n]);
                result.minimumClearance = Math.Min(result.minimumClearance, clear[path[n]]);
                if (n > 0) result.length += (result.points[n] - result.points[n - 1]).magnitude;
            }
            result.duration = result.length / speed;
            result.reachable = true;
            return result;
        }

        private static List<int> FindPath(MapGrid grid, float[] clear, int start, int end, float radius)
        {
            bool Allowed(int i) => i >= 0 && i < grid.Count && !grid.Blocked[i] && clear[i] + 1e-5f >= radius;
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
            if (route == null || !route.reachable || route.points == null || bodyTemplate == null
                || !PositionImpactValidation.Finite(speed) || speed <= 0)
                throw new ArgumentException("Для оценки требуется доступный маршрут и шаблон тела.");
            var result = new ImpactRouteInfluence { state = source.id, targetTemplate = bodyTemplate.id };
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
            }
            result.shotDuration = result.shotLength / speed;
            result.intervals = intervals.ToArray();
            return result;
        }
    }
}

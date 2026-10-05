using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Подтверждает прямоугольную верхнюю поверхность по фактическим треугольникам; дырка не считается полом.</summary>
    public static class MapGrowthFloorSupport
    {
        public static bool TryRectangle(Vector3[] vertices, int[] triangles, float y, out Rect rectangle)
        {
            rectangle = default;
            if (vertices == null || triangles == null || triangles.Length % 3 != 0 || !PositionImpactValidation.Finite(y)) return false;
            var weld = new Dictionary<(long, long), int>(); var points = new List<Vector2>();
            var edges = new Dictionary<(int, int), (int count, int winding)>();
            double area = 0; int orientation = 0;
            foreach (var v in vertices) if (!PositionImpactValidation.Finite(v) || Math.Abs(v.x) > 10000 || Math.Abs(v.z) > 10000) return false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) return false;
                if (Mathf.Abs(vertices[a].y - y) > .00001f || Mathf.Abs(vertices[b].y - y) > .00001f || Mathf.Abs(vertices[c].y - y) > .00001f) continue;
                a = Weld(vertices[a]); b = Weld(vertices[b]); c = Weld(vertices[c]);
                double cross = (double)(points[b].x - points[a].x) * (points[c].y - points[a].y) - (double)(points[b].y - points[a].y) * (points[c].x - points[a].x);
                if (Math.Abs(cross) < 1e-12) continue;
                int winding = Math.Sign(cross); if (orientation != 0 && winding != orientation) return false;
                orientation = winding; area += Math.Abs(cross) * .5;
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            if (points.Count < 4 || area <= 1e-9) return false;
            var min = points[0]; var max = min;
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            double expected = (double)(max.x - min.x) * (max.y - min.y);
            if (Math.Abs(area - expected) > Math.Max(1e-7, expected * 1e-6)) return false;
            foreach (var pair in edges)
            {
                var edge = pair.Value;
                if (edge.count == 2 && edge.winding == 0) continue;
                if (edge.count != 1) return false;
                var a = points[pair.Key.Item1]; var b = points[pair.Key.Item2];
                bool Same(float x, float z, float boundary) => Math.Abs(x - boundary) < 1e-5 && Math.Abs(z - boundary) < 1e-5;
                if (!Same(a.x, b.x, min.x) && !Same(a.x, b.x, max.x) && !Same(a.y, b.y, min.y) && !Same(a.y, b.y, max.y)) return false;
            }
            rectangle = Rect.MinMaxRect(min.x, min.y, max.x, max.y); return true;
            int Weld(Vector3 v)
            {
                var key = ((long)Math.Round(v.x / 1e-5), (long)Math.Round(v.z / 1e-5));
                if (!weld.TryGetValue(key, out int id)) { id = points.Count; points.Add(new Vector2(v.x, v.z)); weld.Add(key, id); }
                return id;
            }
            void Edge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a); edges.TryGetValue(key, out var v);
                edges[key] = (v.count + 1, v.winding + (a < b ? 1 : -1));
            }
        }
    }
}

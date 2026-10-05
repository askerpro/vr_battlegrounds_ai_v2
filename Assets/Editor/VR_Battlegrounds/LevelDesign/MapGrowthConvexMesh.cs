using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Проверка замкнутого выпуклого меша перед созданием отдельного native-прокси пересечений.</summary>
    public static class MapGrowthConvexMesh
    {
        public static bool IsClosedConvex(Vector3[] vertices, int[] triangles)
        {
            if (vertices == null || triangles == null || vertices.Length < 4 || triangles.Length < 12
                || triangles.Length % 3 != 0) return false;
            const double quantum = 1e-5;
            var weld = new Dictionary<(long x, long y, long z), int>();
            var unique = new List<Vector3>(); var ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                if (!PositionImpactValidation.Finite(v) || Math.Abs(v.x) > 10000 || Math.Abs(v.y) > 10000 || Math.Abs(v.z) > 10000) return false;
                var key = ((long)Math.Round(v.x / quantum), (long)Math.Round(v.y / quantum), (long)Math.Round(v.z / quantum));
                if (!weld.TryGetValue(key, out int id)) { id = unique.Count; unique.Add(v); weld.Add(key, id); }
                ids[i] = id;
            }
            if (unique.Count < 4) return false;
            var edges = new Dictionary<(int, int), (int count, int winding)>(); double volume = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                for (int n = 0; n < 3; n++) if (triangles[i + n] < 0 || triangles[i + n] >= ids.Length) return false;
                int a = ids[triangles[i]], b = ids[triangles[i + 1]], c = ids[triangles[i + 2]];
                if (a == b || b == c || c == a) return false;
                var normal = Vector3.Cross(unique[b] - unique[a], unique[c] - unique[a]);
                float length = normal.magnitude; if (length < 1e-10f) return false;
                normal /= length;
                foreach (var v in unique) if (Vector3.Dot(normal, v - unique[a]) > 1e-5f) return false;
                volume += Vector3.Dot(unique[a], Vector3.Cross(unique[b], unique[c])) / 6.0;
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            if (volume <= 1e-9) return false;
            foreach (var edge in edges.Values) if (edge.count != 2 || edge.winding != 0) return false;
            return true;

            void Edge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a); edges.TryGetValue(key, out var value);
                edges[key] = (value.count + 1, value.winding + (a < b ? 1 : -1));
            }
        }

        /// <summary>Убирает только избыточную триангуляцию плоских граней доказанного выпуклого объёма.</summary>
        public static bool TryCookableGeometry(Vector3[] vertices, int[] triangles, out Vector3[] resultVertices, out int[] resultTriangles)
        {
            resultVertices = null; resultTriangles = null;
            if (!IsClosedConvex(vertices, triangles)) return false;
            if (triangles.Length / 3 <= 255)
            { resultVertices = (Vector3[])vertices.Clone(); resultTriangles = (int[])triangles.Clone(); return true; }
            var planes = new List<(Vector3 normal, Vector3 point)>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var n = Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a).normalized;
                if (!planes.Any(p => (p.normal - n).sqrMagnitude < 1e-8f && Math.Abs(Vector3.Dot(p.normal, a - p.point)) <= 1e-5f))
                    planes.Add((n, a));
            }
            var reduced = new List<Vector3>(); var faces = new List<int>();
            foreach (var plane in planes)
            {
                var u = Vector3.Cross(plane.normal, Math.Abs(plane.normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(plane.normal, u);
                var points = vertices.Where(p => Math.Abs(Vector3.Dot(plane.normal, p - plane.point)) <= 1e-5f)
                    .Select(p => (point: p, x: Vector3.Dot(p - plane.point, u), y: Vector3.Dot(p - plane.point, v)))
                    .OrderBy(p => p.x).ThenBy(p => p.y).ToList();
                var unique = new List<(Vector3 point, float x, float y)>();
                foreach (var p in points) if (unique.Count == 0 || (unique[unique.Count - 1].point - p.point).sqrMagnitude > 1e-10f) unique.Add(p);
                if (unique.Count < 3) return false;
                var hull = new List<(Vector3 point, float x, float y)>();
                foreach (var p in unique)
                { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-7) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
                int lower = hull.Count;
                for (int i = unique.Count - 2; i >= 0; i--)
                { var p = unique[i]; while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-7) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
                hull.RemoveAt(hull.Count - 1);
                int start = reduced.Count; reduced.AddRange(hull.Select(p => p.point));
                for (int i = 1; i < hull.Count - 1; i++) { faces.Add(start); faces.Add(start + i); faces.Add(start + i + 1); }
            }
            var result = reduced.ToArray(); var indices = faces.ToArray();
            if (indices.Length / 3 > 255 || !IsClosedConvex(result, indices)) return false;
            // Подмножество исходных точек + включение всех исходных точек в новый объём доказывают равенство.
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = result[indices[i]];
                var normal = Vector3.Cross(result[indices[i + 1]] - a, result[indices[i + 2]] - a).normalized;
                if (vertices.Any(p => Vector3.Dot(normal, p - a) > 1e-5f)) return false;
            }
            resultVertices = result; resultTriangles = indices; return true;

            double Cross((Vector3 point, float x, float y) a, (Vector3 point, float x, float y) b, (Vector3 point, float x, float y) c)
                => ((double)b.x - a.x) * (c.y - a.y) - ((double)b.y - a.y) * (c.x - a.x);
        }
    }
}

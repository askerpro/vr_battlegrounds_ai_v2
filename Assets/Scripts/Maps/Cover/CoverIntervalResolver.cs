using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Maps
{
    /// <summary>Проход через один секционный блок по настоящим треугольникам его коллайдеров. Общий для пули и оценщика.</summary>
    public static class CoverIntervalResolver
    {
        private const float Epsilon = .00001f;
        private const float EntryBackstep = .0001f;
        public struct Result
        {
            public bool blocked;
            public float thickness, penetrationModifier;
            public Vector3 exitPoint, resumePoint;
            public Collider exitCollider;
        }
        private struct Crossing { public float distance; public bool entering; }
        private struct Interval { public float start, end; public CoverSurface surface; public Collider collider; }
        private static class Buffers<T>
        {
            [ThreadStatic] private static Stack<List<T>> pool;
            public static List<T> Rent() => pool != null && pool.Count > 0 ? pool.Pop() : new List<T>();
            public static void Return(List<T> list) { list.Clear(); if (pool == null) pool = new Stack<List<T>>(); pool.Push(list); }
        }

        /// <summary>false означает обычное укрытие, для которого действует прежний поиск выхода.</summary>
        public static bool TryResolve(Collider entryCollider, Vector3 entryPoint, Vector3 direction, float exitOffset, out Result result)
        {
            result = default;
            var part = entryCollider != null ? entryCollider.GetComponent<BlockoutSectionPart>() : null;
            if (part == null || part.owner == null || !part.owner.Initialized) return false;
            direction = direction.normalized;
            var origin = entryPoint - direction * EntryBackstep;
            var intervals = Buffers<Interval>.Rent();
            try
            {
            foreach (var section in part.owner.parts)
            {
                if (section == null || !section.gameObject.activeInHierarchy) continue;
                var collider = section.GetComponent<MeshCollider>();
                if (!collider.enabled || collider.isTrigger || collider.sharedMesh == null) continue;
                Collect(collider, origin, direction, intervals);
            }
            intervals.Sort((a, b) => a.start.CompareTo(b.start));
            int first = intervals.FindIndex(i => i.start <= EntryBackstep + Epsilon && i.end >= EntryBackstep - Epsilon);
            if (first < 0) { result.blocked = true; return true; }
            float end = intervals[first].end, modifier = float.PositiveInfinity;
            Collider exitCollider = intervals[first].collider;
            for (int i = first; i < intervals.Count && intervals[i].start <= end + Epsilon; i++)
            {
                var interval = intervals[i];
                if (interval.end < EntryBackstep - Epsilon) continue;
                if (interval.surface == null || interval.surface.Class != CoverClass.Soft)
                { result.blocked = true; return true; }
                modifier = Mathf.Min(modifier, interval.surface.PenetrationModifier);
                if (interval.end > end) { end = interval.end; exitCollider = interval.collider; }
            }
            float offset = Mathf.Max(0, exitOffset);
            foreach (var interval in intervals)
                if (interval.start > end + Epsilon) { offset = Mathf.Min(offset, (interval.start - end) / 2); break; }
            result.thickness = Mathf.Max(0, end - EntryBackstep);
            result.penetrationModifier = modifier;
            result.exitPoint = origin + direction * end;
            result.resumePoint = result.exitPoint + direction * offset;
            result.exitCollider = exitCollider;
            return true;
            }
            finally { Buffers<Interval>.Return(intervals); }
        }

        private static void Collect(MeshCollider collider, Vector3 origin, Vector3 direction, List<Interval> intervals)
        {
            var mesh = collider.sharedMesh;
            var part = collider.GetComponent<BlockoutSectionPart>();
            if (part == null || !part.TryMeshData(mesh, out var vertices, out var triangles)) return;
            var matrix = collider.transform.worldToLocalMatrix;
            var localOrigin = matrix.MultiplyPoint3x4(origin); var localDirection = matrix.MultiplyVector(direction);
            var crossings = Buffers<Crossing>.Rent();
            try
            {
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 edge1 = b - a, edge2 = c - a, cross = Vector3.Cross(localDirection, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < .00000001f) continue;
                float inverse = 1 / determinant; Vector3 delta = localOrigin - a;
                float u = Vector3.Dot(delta, cross) * inverse;
                if (u < -Epsilon || u > 1 + Epsilon) continue;
                Vector3 q = Vector3.Cross(delta, edge1); float v = Vector3.Dot(localDirection, q) * inverse;
                if (v < -Epsilon || u + v > 1 + Epsilon) continue;
                float distance = Vector3.Dot(edge2, q) * inverse;
                crossings.Add(new Crossing { distance = distance, entering = determinant > 0 });
            }
            crossings.Sort((a, b) => a.distance.CompareTo(b.distance));
            bool inside = false; float start = 0;
            for (int i = 0; i < crossings.Count;)
            {
                float distance = crossings[i].distance; bool entering = false, exiting = false;
                do { entering |= crossings[i].entering; exiting |= !crossings[i].entering; i++; }
                while (i < crossings.Count && Mathf.Abs(crossings[i].distance - distance) <= Epsilon);
                // Две стороны касания не создают ни пустоты, ни дополнительного входа.
                if (entering == exiting) continue;
                if (entering && !inside) { inside = true; start = distance; }
                else if (exiting && inside)
                {
                    inside = false;
                    if (distance > start + Epsilon && distance >= 0) intervals.Add(new Interval { start = start, end = distance, surface = CoverSurface.Of(collider), collider = collider });
                }
            }
            }
            finally { Buffers<Crossing>.Return(crossings); }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    [Serializable]
    public sealed class HandPhalanxReferenceSegment
    {
        public string Id, Finger, Segment;
        // Все координаты — метры в общей рамке эталонной кожи и костей.
        public Vector3 Start, End, PalmarDirection;
    }

    [Serializable]
    public sealed class HandPhalanxProbe
    {
        public string SegmentId, Finger, Segment;
        public string Source;
        public int SourceSubMesh, SourceTriangle;
        public Vector3 Barycentric, ReferencePosition;
        public string SelectionMode, Error;
        public double? SelectionDistanceMm;
        public double MaximumSelectionDistanceMm;
    }

    [Serializable]
    public sealed class HandPhalanxProbeSet
    {
        public int SchemaVersion = 1;
        public string BindingFingerprint, Side;
        public string Method = "fixed_material_point; phalanx_midpoint_palmar_ray; no_weapon_input; no_nearest_fallback";
        public int ExpectedCount;
        public bool Valid, Inferred;
        public List<HandPhalanxProbe> Probes = new List<HandPhalanxProbe>();
        public List<string> Errors = new List<string>();
    }

    [Serializable]
    public sealed class HandPhalanxProbeMeasurement
    {
        public string SegmentId, Finger, Segment, Error;
        public Vector3 Position, NearestObjectPosition;
        public double? DistanceMm, SignedDistanceMm;
        public bool? WithinBand;
    }

    [Serializable]
    public sealed class HandPhalanxProbeReport
    {
        public bool Valid, AllWithinBand;
        public string BindingFingerprint;
        public string Method = "fixed_barycentric_material_points; unsigned_nearest_surface_distance; signed_unknown";
        public string Meaning = "AllWithinBand относится только к фиксированным опорным точкам; не подтверждает отсутствие столкновений всей кисти или качество хвата.";
        public double ContactBandMm;
        public int ExpectedCount, MeasuredCount, MissingCount, ExcludedObjectDegenerateTriangles;
        public List<HandPhalanxProbeMeasurement> Points = new List<HandPhalanxProbeMeasurement>();
        public List<string> Errors = new List<string>();
    }

    /// <summary>Однократный выбор по эталонной анатомии и измерение тех же материальных точек после BakeMesh.</summary>
    public static class HandPhalanxContactProbes
    {
        const float MinimumCrossMagnitude = 1e-12f;
        const float MinimumSegmentLength = 1e-5f;

        public static HandPhalanxProbeSet Select(FitTriangle[] hand, string binding, string side,
            HandPhalanxReferenceSegment[] segments)
        {
            var result = new HandPhalanxProbeSet
            {
                BindingFingerprint = binding, Side = side,
                ExpectedCount = segments == null ? 0 : segments.Length
            };
            if (string.IsNullOrWhiteSpace(binding)) result.Errors.Add("Отсутствует fingerprint привязки кожи.");
            if (!SideValid(side)) result.Errors.Add("Нужна явная сторона Left или Right.");
            if (segments == null || segments.Length == 0) result.Errors.Add("Нет ожидаемых сегментов фаланг.");
            Dictionary<TriangleKey, FitTriangle> unused;
            string handError;
            if (!IndexHand(hand, out unused, out handError)) result.Errors.Add(handError);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var materialKeys = new HashSet<TriangleKey>();
            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    var probe = new HandPhalanxProbe
                    {
                        SegmentId = segment == null ? null : segment.Id,
                        Finger = segment == null ? null : segment.Finger,
                        Segment = segment == null ? null : segment.Segment,
                        SelectionMode = "phalanx_midpoint_palmar_ray"
                    };
                    result.Probes.Add(probe);
                    string error = ValidateSegment(segment);
                    if (error == null && !ids.Add(segment.Id)) error = "Повторный идентификатор сегмента.";
                    if (error == null && handError != null) error = handError;
                    if (error != null) { probe.Error = error; continue; }
                    Vector3 axis = segment.End - segment.Start;
                    Vector3 origin = segment.Start + axis * 0.5f;
                    Vector3 direction = segment.PalmarDirection / segment.PalmarDirection.magnitude;
                    // Дальняя поверхность другого участка не подменяет отсутствующую кожу сегмента.
                    float limit = 0.03f;
                    probe.MaximumSelectionDistanceMm = limit * 1000.0;
                    float best = float.PositiveInfinity;
                    FitTriangle chosen = default(FitTriangle);
                    Vector3 barycentric = default(Vector3);
                    bool found = false;
                    foreach (var triangle in hand)
                    {
                        if (!Matches(triangle, segment.Finger, segment.Segment) || !Usable(triangle)) continue;
                        // Изнутри фаланги луч должен выходить через ладонную наружную грань.
                        if (Vector3.Dot(triangle.Normal, direction) <= 1e-4f) continue;
                        float distance;
                        Vector3 bary;
                        if (!RayTriangle(origin, direction, triangle, out distance, out bary) || distance > limit) continue;
                        if (!found || distance < best || (distance == best && Key(triangle).CompareTo(Key(chosen)) < 0))
                        {
                            found = true; best = distance; chosen = triangle; barycentric = bary;
                        }
                    }
                    if (!found)
                    {
                        probe.Error = "Нет ладонной грани своего сегмента на ограниченном положительном луче; fallback не выполнялся.";
                        continue;
                    }
                    if (!materialKeys.Add(Key(chosen)))
                    {
                        probe.Error = "Один материальный треугольник выбран для нескольких опорных точек.";
                        continue;
                    }
                    probe.Source = chosen.Source;
                    probe.SourceSubMesh = chosen.SourceSubMesh;
                    probe.SourceTriangle = chosen.SourceTriangle;
                    probe.Barycentric = barycentric;
                    probe.ReferencePosition = MaterialPoint(chosen, barycentric);
                    probe.SelectionDistanceMm = best * 1000.0;
                }
            }
            foreach (var probe in result.Probes)
                if (probe.Error != null) result.Errors.Add((probe.SegmentId ?? "<null>") + ": " + probe.Error);
            result.Valid = result.ExpectedCount > 0 && result.Errors.Count == 0;
            return result;
        }

        public static HandPhalanxProbeReport Evaluate(FitTriangle[] hand, FitSurface objectSurface, string binding,
            HandPhalanxProbeSet probes, float bandMm = 2)
        {
            var report = new HandPhalanxProbeReport
            {
                BindingFingerprint = binding, ContactBandMm = bandMm,
                ExpectedCount = probes == null ? 0 : probes.ExpectedCount
            };
            if (!Finite(bandMm) || bandMm < 0) report.Errors.Add("Некорректный допуск контакта.");
            if (probes == null) report.Errors.Add("Нет фиксированного набора опорных точек.");
            else
            {
                if (probes.SchemaVersion != 1) report.Errors.Add("Неизвестная схема опорных точек.");
                if (!probes.Valid || probes.Errors == null || probes.Errors.Count != 0) report.Errors.Add("Исходный выбор опорных точек неполон или недостоверен.");
                if (!SideValid(probes.Side)) report.Errors.Add("Не задана сторона набора.");
                if (string.IsNullOrWhiteSpace(binding) || !string.Equals(binding, probes.BindingFingerprint, StringComparison.Ordinal))
                    report.Errors.Add("Привязка кожи устарела или отсутствует.");
                if (probes.ExpectedCount <= 0 || probes.Probes == null || probes.Probes.Count != probes.ExpectedCount)
                    report.Errors.Add("Набор не содержит все ожидаемые точки.");
            }
            Dictionary<TriangleKey, FitTriangle> byKey;
            string handError;
            if (!IndexHand(hand, out byKey, out handError)) report.Errors.Add(handError);
            FitSurface surface = SanitizedObject(objectSurface, report);
            bool prerequisitesValid = report.Errors.Count == 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var materialKeys = new HashSet<TriangleKey>();
            if (probes != null && probes.Probes != null)
            {
                foreach (var probe in probes.Probes)
                {
                    var point = new HandPhalanxProbeMeasurement
                    {
                        SegmentId = probe == null ? null : probe.SegmentId,
                        Finger = probe == null ? null : probe.Finger,
                        Segment = probe == null ? null : probe.Segment
                    };
                    report.Points.Add(point);
                    if (probe == null) point.Error = "Пустая запись точки.";
                    else if (!string.IsNullOrEmpty(probe.Error)) point.Error = probe.Error;
                    else if (string.IsNullOrWhiteSpace(probe.SegmentId) || !ids.Add(probe.SegmentId)) point.Error = "Пустой или повторный идентификатор точки.";
                    else if (!materialKeys.Add(new TriangleKey(probe.Source, probe.SourceSubMesh, probe.SourceTriangle))) point.Error = "Повторный материальный треугольник опорной точки.";
                    else if (!ValidBarycentric(probe.Barycentric)) point.Error = "Некорректные барицентрические координаты.";
                    else
                    {
                        FitTriangle triangle;
                        if (byKey == null || !byKey.TryGetValue(new TriangleKey(probe.Source, probe.SourceSubMesh, probe.SourceTriangle), out triangle))
                            point.Error = "Исходный материальный треугольник отсутствует.";
                        else if (!Matches(triangle, probe.Finger, probe.Segment)) point.Error = "Материальный треугольник больше не соответствует сегменту.";
                        else if (!Usable(triangle)) point.Error = "Материальный треугольник вырожден.";
                        else if (!prerequisitesValid) point.Error = "Измерение недоступно из-за ошибок набора или геометрии.";
                        else
                        {
                            point.Position = MaterialPoint(triangle, probe.Barycentric);
                            var nearest = surface.NearestHit(point.Position);
                            double distance = Distance(point.Position, nearest.Position) * 1000;
                            if (!Finite(nearest.Position) || double.IsNaN(distance) || double.IsInfinity(distance)) point.Error = "Запрос расстояния вернул недостоверные координаты.";
                            else
                            {
                                point.NearestObjectPosition = nearest.Position;
                                point.DistanceMm = distance;
                                // Открытая геометрия не даёт достоверного знака; winding здесь не используется.
                                point.SignedDistanceMm = null;
                                point.WithinBand = distance <= bandMm;
                                report.MeasuredCount++;
                            }
                        }
                    }
                    if (point.Error != null) report.Errors.Add((point.SegmentId ?? "<null>") + ": " + point.Error);
                }
            }
            report.MissingCount = Math.Max(0, report.ExpectedCount - report.MeasuredCount);
            report.Valid = report.ExpectedCount > 0 && report.Errors.Count == 0 && report.MeasuredCount == report.ExpectedCount;
            report.AllWithinBand = report.Valid;
            foreach (var point in report.Points) if (point.WithinBand != true) report.AllWithinBand = false;
            return report;
        }

        static FitSurface SanitizedObject(FitSurface source, HandPhalanxProbeReport report)
        {
            if (source == null || source.Triangles == null || source.Triangles.Length == 0)
            { report.Errors.Add("Нет поверхности предмета."); return null; }
            var triangles = new List<FitTriangle>();
            foreach (var triangle in source.Triangles)
            {
                if (!Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C))
                { report.Errors.Add("Поверхность предмета содержит non-finite координаты."); return null; }
                if (!FiniteCross(triangle))
                { report.Errors.Add("Геометрия поверхности предмета переполняет числовой диапазон."); return null; }
                if (!Usable(triangle)) { report.ExcludedObjectDegenerateTriangles++; continue; }
                triangles.Add(triangle);
            }
            if (triangles.Count == 0) { report.Errors.Add("Поверхность предмета полностью вырождена."); return null; }
            // Исключение касается самого query, а не только счётчика диагностики.
            return report.ExcludedObjectDegenerateTriangles == 0 ? source : new FitSurface(triangles.ToArray());
        }

        static bool IndexHand(FitTriangle[] hand, out Dictionary<TriangleKey, FitTriangle> index, out string error)
        {
            index = new Dictionary<TriangleKey, FitTriangle>(); error = null;
            if (hand == null || hand.Length == 0) { error = "Нет геометрии кожи кисти."; return false; }
            foreach (var triangle in hand)
            {
                if (!Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C)) { error = "Кожа содержит non-finite координаты."; return false; }
                if (!FiniteCross(triangle)) { error = "Геометрия кожи переполняет числовой диапазон."; return false; }
                if (string.IsNullOrWhiteSpace(triangle.Source) || triangle.SourceSubMesh < 0 || triangle.SourceTriangle < 0)
                { error = "Треугольник кожи не имеет полной материальной идентичности."; return false; }
                var key = Key(triangle);
                if (index.ContainsKey(key)) { error = "Материальная идентичность треугольника кожи неоднозначна."; return false; }
                index.Add(key, triangle);
            }
            return true;
        }

        static string ValidateSegment(HandPhalanxReferenceSegment segment)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Id)) return "Нет идентификатора сегмента.";
            if (!FingerValid(segment.Finger) || !SegmentValid(segment.Finger, segment.Segment)) return "Неизвестные палец или фаланга.";
            if (!Finite(segment.Start) || !Finite(segment.End) || !Finite(segment.PalmarDirection)) return "Non-finite рамка сегмента.";
            if (!Finite(segment.End - segment.Start) || !Finite((segment.End - segment.Start).magnitude) || !Finite(segment.PalmarDirection.magnitude)) return "Рамка сегмента переполняет числовой диапазон.";
            if ((segment.End - segment.Start).magnitude < MinimumSegmentLength) return "Сегмент кости имеет нулевую длину.";
            if (segment.PalmarDirection.magnitude < 1e-6f) return "Не задано направление к ладонной коже.";
            if (Mathf.Abs(Vector3.Dot((segment.End - segment.Start).normalized, segment.PalmarDirection.normalized)) > 0.8f)
                return "Ладонное направление почти параллельно оси фаланги.";
            return null;
        }

        static bool Matches(FitTriangle triangle, string finger, string segment)
        {
            return FingerValid(finger) && SegmentValid(finger, segment) && triangle.Zone == finger &&
                triangle.FingerSegment == FullSegment(finger, segment);
        }
        static bool SideValid(string side) { return string.Equals(side, "left", StringComparison.OrdinalIgnoreCase) || string.Equals(side, "right", StringComparison.OrdinalIgnoreCase); }
        static bool FingerValid(string finger) { return finger == "thumb" || finger == "index" || finger == "middle" || finger == "ring" || finger == "little"; }
        static string FullSegment(string finger, string segment) { return segment != null && segment.IndexOf('/') >= 0 ? segment : finger + "/" + segment; }
        static bool SegmentValid(string finger, string segment)
        {
            string full = FullSegment(finger, segment);
            return full == finger + "/proximal" || full == finger + "/intermediate" || full == finger + "/distal";
        }
        static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        static bool Finite(Vector3 p) { return Finite(p.x) && Finite(p.y) && Finite(p.z); }
        static bool FiniteCross(FitTriangle t)
        {
            Vector3 cross = Vector3.Cross(t.B - t.A, t.C - t.A);
            return Finite(cross) && Finite(cross.magnitude);
        }
        static bool Usable(FitTriangle t)
        {
            Vector3 cross = Vector3.Cross(t.B - t.A, t.C - t.A);
            return Finite(cross) && cross.magnitude > MinimumCrossMagnitude;
        }
        static bool ValidBarycentric(Vector3 bary)
        {
            return Finite(bary) && bary.x >= 0 && bary.y >= 0 && bary.z >= 0 &&
                bary.x <= 1 && bary.y <= 1 && bary.z <= 1 && Mathf.Abs(bary.x + bary.y + bary.z - 1) <= 1e-5f;
        }
        static Vector3 MaterialPoint(FitTriangle t, Vector3 bary) { return t.A * bary.x + t.B * bary.y + t.C * bary.z; }
        static double Distance(Vector3 a, Vector3 b)
        {
            double x = (double)a.x - b.x, y = (double)a.y - b.y, z = (double)a.z - b.z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        static bool RayTriangle(Vector3 origin, Vector3 direction, FitTriangle triangle, out float distance, out Vector3 barycentric)
        {
            distance = 0; barycentric = default(Vector3);
            Vector3 edge1 = triangle.B - triangle.A, edge2 = triangle.C - triangle.A;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (!Finite(determinant) || Math.Abs(determinant) < 1e-12f) return false;
            float inverse = 1 / determinant;
            Vector3 translated = origin - triangle.A;
            float u = Vector3.Dot(translated, p) * inverse;
            Vector3 q = Vector3.Cross(translated, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            float t = Vector3.Dot(edge2, q) * inverse;
            if (!Finite(u) || !Finite(v) || !Finite(t) || u < 0 || v < 0 || u + v > 1 || t <= 1e-7f) return false;
            barycentric = new Vector3(1 - u - v, u, v); distance = t; return true;
        }

        struct TriangleKey : IEquatable<TriangleKey>, IComparable<TriangleKey>
        {
            readonly string source;
            readonly int subMesh, triangle;
            public TriangleKey(string source, int subMesh, int triangle) { this.source = source; this.subMesh = subMesh; this.triangle = triangle; }
            public bool Equals(TriangleKey other) { return source == other.source && subMesh == other.subMesh && triangle == other.triangle; }
            public override bool Equals(object other) { return other is TriangleKey && Equals((TriangleKey)other); }
            public override int GetHashCode() { unchecked { return ((source == null ? 0 : source.GetHashCode()) * 397 ^ subMesh) * 397 ^ triangle; } }
            public int CompareTo(TriangleKey other)
            {
                int value = string.CompareOrdinal(source, other.source);
                if (value != 0) return value;
                value = subMesh.CompareTo(other.subMesh);
                return value != 0 ? value : triangle.CompareTo(other.triangle);
            }
        }
        static TriangleKey Key(FitTriangle t) { return new TriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle); }
    }
}

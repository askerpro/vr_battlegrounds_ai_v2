using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    [Serializable]
    public sealed class HandPosePotentialContactCoverageSettings
    {
        public double ContactBandMm = 2;
        // Каждый треугольник разбивается на N² равновеликих ячеек; выборки — их центры.
        public int Subdivisions = 8;
        public bool CheckConvergence = true;
        public long MaxSampleQueries = 2000000;
        public double MinimumNormalOpposition = 0;
    }

    public struct HandPosePotentialContactSign
    {
        // Наличие Inside означает явное решение вызывающего кода о достоверности знака.
        // Confidence сам по себе не превращает предположение winding в достоверный знак.
        public bool? Inside;
        public double? Confidence;
        public string Reason;
    }

    [Serializable]
    public sealed class HandPosePotentialContactCoverageRegion
    {
        public string Region, Status;
        public int SelectedTriangles;
        public long Samples;
        public double CurrentPotentialAreaMm2;
        public double? ReferencePotentialAreaMm2;
        public double ContactBandAreaMm2, KnownGapAreaMm2, KnownDeepPenetrationAreaMm2;
        public double AmbiguousSignAreaMm2, AmbiguousSignOutsideBandAreaMm2;
        public double AreaOpposingNormalsMm2, QualifiedContactAreaMm2;
        public double? CurrentCoveragePercent, ReferenceWeightedContactPercent;
        // Это известные части площади. При неоднозначном знаке полные Gap/Deep остаются null.
        public double? GapAreaMm2, GapPercent, DeepPenetrationAreaMm2, DeepPenetrationPercent;
        public double? OpposingNormalsPercent, QualifiedCoveragePercent;
        public double? KnownMaxDepthMm, MaxDepthMm, MeanSignConfidence;
        public double SignConfidenceAreaMm2;
        public double MaxSampleCellDiameterMm;
        public double ContactBoundaryUnresolvedAreaMm2;
        public double? ContactPercentLowerBound, ContactPercentUpperBound;
        public double? CoarseCoveragePercent, FineMinusCoarsePercentagePoints;
        public List<string> SignReasons = new List<string>();
    }

    [Serializable]
    public sealed class HandPosePotentialContactCoverageReport
    {
        public bool ValidNumericCoverage, MaskReviewed, ReadyForComparison;
        public string BindingFingerprint, MaskProvenance;
        public string Status;
        public string Method = "fixed_material_triangle_mask; equal_area_barycentric_cell_centroids; current_posed_area_weighted";
        public string PrecisionMeaning = "Границы contact учитывают 1-Lipschitz расстояние и диаметр ячейки; это границы дискретизации, без сертификации float/BVH. Coarse/fine delta не является доверительным интервалом. Глубина — максимум среди выборок, не максимум всей поверхности.";
        public double ContactBandMm, MinimumNormalOpposition;
        public int Subdivisions, CoarseSubdivisions;
        public long RequiredSampleQueries, ExecutedSampleQueries, MaxSampleQueries;
        public List<string> Errors = new List<string>();
        public List<HandPosePotentialContactCoverageRegion> Regions = new List<HandPosePotentialContactCoverageRegion>();
        public HandPosePotentialContactCoverageRegion Total;
    }

    /// <summary>Измерение фиксированной материальной маски. Не создаёт маску и не выставляет качество хвата.</summary>
    public static class HandPosePotentialContactCoverage
    {
        static readonly string[] RegionNames = { "palm", "thumb", "index", "middle", "ring", "little" };

        sealed class Selected
        {
            public FitTriangle Triangle;
            public HandPosePotentialContactMaskTriangle Entry;
        }

        sealed class Accumulator
        {
            public readonly HandPosePotentialContactCoverageRegion Value;
            public double ReferenceArea, ReferenceContact, CoarseContact, CoarseArea;
            public double ContactBoundaryInside, ContactBoundaryOutside, ConfidenceSum;
            public bool HasReference = true;
            public bool HasKnownSign;
            public Accumulator(string region) { Value = new HandPosePotentialContactCoverageRegion { Region = region }; }

            public void Begin(Selected selected, double cellDiameterMm)
            {
                Value.SelectedTriangles++;
                Value.MaxSampleCellDiameterMm = Math.Max(Value.MaxSampleCellDiameterMm, cellDiameterMm);
                if (selected.Entry.ReferenceAreaMm2.HasValue)
                    ReferenceArea += selected.Entry.ReferenceAreaMm2.Value * selected.Entry.Weight;
                else HasReference = false;
            }

            public void Sample(double area, double referenceArea, double distanceMm, double diameterMm,
                bool opposing, HandPosePotentialContactSign sign, HandPosePotentialContactCoverageSettings settings)
            {
                var v = Value;
                v.Samples++;
                v.CurrentPotentialAreaMm2 += area;
                bool contact = distanceMm <= settings.ContactBandMm;
                if (contact) { v.ContactBandAreaMm2 += area; ReferenceContact += referenceArea; }
                if (opposing) v.AreaOpposingNormalsMm2 += area;
                if (contact && opposing) v.QualifiedContactAreaMm2 += area;
                // Расстояние меняется не быстрее перемещения по ячейке: неоднозначную границу не скрываем.
                if (Math.Abs(distanceMm - settings.ContactBandMm) <= diameterMm)
                {
                    v.ContactBoundaryUnresolvedAreaMm2 += area;
                    if (contact) ContactBoundaryInside += area; else ContactBoundaryOutside += area;
                }
                if (!sign.Inside.HasValue)
                {
                    v.AmbiguousSignAreaMm2 += area;
                    if (!contact) v.AmbiguousSignOutsideBandAreaMm2 += area;
                }
                else
                {
                    HasKnownSign = true;
                    if (sign.Inside.Value)
                    {
                        v.KnownMaxDepthMm = Math.Max(v.KnownMaxDepthMm ?? 0, distanceMm);
                        if (!contact) v.KnownDeepPenetrationAreaMm2 += area;
                    }
                    else if (!contact) v.KnownGapAreaMm2 += area;
                }
                if (sign.Confidence.HasValue)
                {
                    ConfidenceSum += sign.Confidence.Value * area;
                    v.SignConfidenceAreaMm2 += area;
                }
                if (!string.IsNullOrWhiteSpace(sign.Reason) && !v.SignReasons.Contains(sign.Reason)) v.SignReasons.Add(sign.Reason);
            }

            public HandPosePotentialContactCoverageRegion Finish(bool convergence)
            {
                var v = Value;
                v.Status = v.CurrentPotentialAreaMm2 > 0 ? "measured" : "empty";
                if (v.CurrentPotentialAreaMm2 <= 0) return v;
                v.CurrentCoveragePercent = v.ContactBandAreaMm2 / v.CurrentPotentialAreaMm2 * 100;
                v.OpposingNormalsPercent = v.AreaOpposingNormalsMm2 / v.CurrentPotentialAreaMm2 * 100;
                v.QualifiedCoveragePercent = v.QualifiedContactAreaMm2 / v.CurrentPotentialAreaMm2 * 100;
                v.ContactPercentLowerBound = Math.Max(0, v.ContactBandAreaMm2 - ContactBoundaryInside) / v.CurrentPotentialAreaMm2 * 100;
                v.ContactPercentUpperBound = Math.Min(v.CurrentPotentialAreaMm2, v.ContactBandAreaMm2 + ContactBoundaryOutside) / v.CurrentPotentialAreaMm2 * 100;
                if (HasReference)
                {
                    v.ReferencePotentialAreaMm2 = ReferenceArea;
                    v.ReferenceWeightedContactPercent = ReferenceContact / ReferenceArea * 100;
                }
                // Неизвестный знак внутри band не мешает полной площади gap/deep, но мешает max depth.
                if (v.AmbiguousSignOutsideBandAreaMm2 == 0)
                {
                    v.GapAreaMm2 = v.KnownGapAreaMm2;
                    v.GapPercent = v.KnownGapAreaMm2 / v.CurrentPotentialAreaMm2 * 100;
                    v.DeepPenetrationAreaMm2 = v.KnownDeepPenetrationAreaMm2;
                    v.DeepPenetrationPercent = v.KnownDeepPenetrationAreaMm2 / v.CurrentPotentialAreaMm2 * 100;
                }
                if (HasKnownSign && !v.KnownMaxDepthMm.HasValue) v.KnownMaxDepthMm = 0;
                if (v.AmbiguousSignAreaMm2 == 0) v.MaxDepthMm = v.KnownMaxDepthMm;
                if (v.SignConfidenceAreaMm2 > 0) v.MeanSignConfidence = ConfidenceSum / v.SignConfidenceAreaMm2;
                if (convergence && CoarseArea > 0)
                {
                    v.CoarseCoveragePercent = CoarseContact / CoarseArea * 100;
                    v.FineMinusCoarsePercentagePoints = v.CurrentCoveragePercent - v.CoarseCoveragePercent;
                }
                v.SignReasons.Sort(StringComparer.Ordinal);
                return v;
            }
        }

        public static HandPosePotentialContactCoverageReport Evaluate(IReadOnlyList<FitTriangle> currentBakedHand,
            FitSurface objectSurface, HandPosePotentialContactMask mask, string currentBindingFingerprint,
            HandPosePotentialContactCoverageSettings settings = null,
            Func<Vector3, FitNearestHit, HandPosePotentialContactSign> signClassifier = null)
        {
            var supplied = settings ?? new HandPosePotentialContactCoverageSettings();
            // Callback не может незаметно изменить полосу или плотность уже начатого измерения.
            settings = new HandPosePotentialContactCoverageSettings
            {
                ContactBandMm = supplied.ContactBandMm, Subdivisions = supplied.Subdivisions,
                CheckConvergence = supplied.CheckConvergence, MaxSampleQueries = supplied.MaxSampleQueries,
                MinimumNormalOpposition = supplied.MinimumNormalOpposition
            };
            var report = new HandPosePotentialContactCoverageReport
            {
                Status = "invalid", BindingFingerprint = currentBindingFingerprint,
                MaskReviewed = mask?.Metadata?.Reviewed ?? false, MaskProvenance = mask?.Metadata?.Provenance,
                ContactBandMm = settings.ContactBandMm, MinimumNormalOpposition = settings.MinimumNormalOpposition,
                Subdivisions = settings.Subdivisions, CoarseSubdivisions = settings.CheckConvergence ? Math.Max(1, settings.Subdivisions / 2) : 0,
                MaxSampleQueries = settings.MaxSampleQueries
            };
            List<Selected> selected = Validate(currentBakedHand, objectSurface, mask, currentBindingFingerprint, settings, report.Errors);
            if (report.Errors.Count > 0) return report;
            long fineCount = (long)settings.Subdivisions * settings.Subdivisions;
            long coarseCount = (long)report.CoarseSubdivisions * report.CoarseSubdivisions;
            report.RequiredSampleQueries = selected.Count * (fineCount + coarseCount);
            if (report.RequiredSampleQueries > settings.MaxSampleQueries)
            {
                report.Status = "budget_exceeded";
                report.Errors.Add("Число запросов " + report.RequiredSampleQueries + " превышает бюджет " + settings.MaxSampleQueries + ". Выборка не уменьшалась.");
                return report;
            }
            var accumulators = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            foreach (string region in RegionNames) accumulators.Add(region, new Accumulator(region));
            var total = new Accumulator("total");
            try
            {
                foreach (Selected selection in selected)
                {
                    FitTriangle t = selection.Triangle;
                    double area = AreaMm2(t) * selection.Entry.Weight;
                    double reference = selection.Entry.ReferenceAreaMm2.GetValueOrDefault() * selection.Entry.Weight;
                    double diameter = Math.Max((t.A - t.B).magnitude, Math.Max((t.A - t.C).magnitude, (t.B - t.C).magnitude)) * 1000 / settings.Subdivisions;
                    Accumulator region = accumulators[selection.Entry.Region];
                    region.Begin(selection, diameter); total.Begin(selection, diameter);
                    foreach (Vector3 point in Points(t, settings.Subdivisions))
                    {
                        FitNearestHit hit = objectSurface.NearestHit(point);
                        double distance = (point - hit.Position).magnitude * 1000;
                        report.ExecutedSampleQueries++;
                        if (!Finite(distance) || !Finite(hit.Normal)) throw new ArgumentException("Нечисловой результат NearestHit.");
                        HandPosePotentialContactSign sign = signClassifier == null
                            ? new HandPosePotentialContactSign { Reason = "sign_classifier_not_supplied" }
                            : signClassifier(point, hit);
                        if (sign.Confidence.HasValue && (!Finite(sign.Confidence.Value) || sign.Confidence.Value < 0 || sign.Confidence.Value > 1))
                            throw new ArgumentException("Sign.Confidence должен быть null либо конечным числом в [0,1].");
                        double opposition = -Vector3.Dot(t.Normal, hit.Normal);
                        bool opposing = t.Normal.sqrMagnitude > 0 && hit.Normal.sqrMagnitude > 0 && opposition >= settings.MinimumNormalOpposition;
                        region.Sample(area / fineCount, reference / fineCount, distance, diameter, opposing, sign, settings);
                        total.Sample(area / fineCount, reference / fineCount, distance, diameter, opposing, sign, settings);
                    }
                    if (report.CoarseSubdivisions > 0)
                    {
                        foreach (Vector3 point in Points(t, report.CoarseSubdivisions))
                        {
                            double distance = objectSurface.Distance(point) * 1000;
                            report.ExecutedSampleQueries++;
                            if (!Finite(distance)) throw new ArgumentException("Нечисловой результат coarse Distance.");
                            region.CoarseArea += area / coarseCount; total.CoarseArea += area / coarseCount;
                            if (distance <= settings.ContactBandMm)
                            { region.CoarseContact += area / coarseCount; total.CoarseContact += area / coarseCount; }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // При ошибке классификатора не выдаём частичную площадь за измеренную маску.
                report.Errors.Add(error.GetType().Name + ": " + error.Message);
                return report;
            }
            var regions = new List<HandPosePotentialContactCoverageRegion>();
            foreach (string region in RegionNames) regions.Add(accumulators[region].Finish(settings.CheckConvergence));
            var overall = total.Finish(settings.CheckConvergence);
            foreach (var region in regions)
                if (!FiniteResult(region)) { report.Errors.Add("Числовое переполнение региона " + region.Region + "."); return report; }
            if (!FiniteResult(overall)) { report.Errors.Add("Числовое переполнение суммарного результата."); return report; }
            report.Regions = regions;
            report.Total = overall;
            report.ValidNumericCoverage = true;
            report.ReadyForComparison = selected.Count > 0 && report.Total.CurrentPotentialAreaMm2 > 0 &&
                report.MaskReviewed && !string.IsNullOrWhiteSpace(report.MaskProvenance);
            report.Status = selected.Count > 0 ? "measured" : "empty";
            return report;
        }

        static List<Selected> Validate(IReadOnlyList<FitTriangle> hand, FitSurface surface,
            HandPosePotentialContactMask mask, string fingerprint, HandPosePotentialContactCoverageSettings settings, List<string> errors)
        {
            var selected = new List<Selected>();
            if (!Finite(settings.ContactBandMm) || settings.ContactBandMm <= 0) errors.Add("ContactBandMm должен быть конечным и >0.");
            if (settings.Subdivisions < 1 || settings.Subdivisions > 128) errors.Add("Subdivisions: 1..128.");
            if (settings.MaxSampleQueries < 1) errors.Add("MaxSampleQueries должен быть >0.");
            if (!Finite(settings.MinimumNormalOpposition) || settings.MinimumNormalOpposition < -1 || settings.MinimumNormalOpposition > 1) errors.Add("MinimumNormalOpposition: [-1,1].");
            if (string.IsNullOrWhiteSpace(fingerprint)) errors.Add("Не задан текущий binding fingerprint.");
            if (mask == null || mask.Metadata == null) { errors.Add("Не задана маска или её metadata."); return selected; }
            if (mask.Version != HandPosePotentialContactMask.CurrentVersion) errors.Add("Неизвестная версия маски.");
            if (string.IsNullOrWhiteSpace(mask.Metadata.BindingFingerprint) || !StringComparer.Ordinal.Equals(mask.Metadata.BindingFingerprint, fingerprint)) errors.Add("Маска устарела или binding fingerprint не задан.");
            var regions = new HashSet<string>(StringComparer.Ordinal);
            if (mask.Regions == null) errors.Add("Нет списка регионов маски.");
            else foreach (var region in mask.Regions)
            {
                if (region == null || !HandPosePotentialContactMask.IsRegion(region.Region)) errors.Add("Неизвестный регион маски.");
                else if (!regions.Add(region.Region)) errors.Add("Повтор региона: " + region.Region);
            }
            foreach (string region in RegionNames) if (!regions.Contains(region)) errors.Add("Нет явного региона: " + region);
            var handByKey = new Dictionary<HandPosePotentialContactTriangleKey, FitTriangle>();
            if (hand == null) errors.Add("Нет baked-геометрии кисти.");
            else foreach (FitTriangle t in hand)
            {
                var key = Key(t.Source, t.SourceSubMesh, t.SourceTriangle);
                if (!ValidKey(key)) errors.Add("Неверный ключ baked-грани: " + key);
                if (!Finite(t.A) || !Finite(t.B) || !Finite(t.C) || !Finite(AreaMm2(t))) errors.Add("Нечисловая baked-геометрия: " + key);
                if (handByKey.ContainsKey(key)) errors.Add("Повтор ключа baked-грани: " + key);
                else handByKey.Add(key, t);
            }
            if (surface == null || surface.Triangles == null || surface.Triangles.Length == 0) errors.Add("Нет поверхности предмета.");
            else foreach (FitTriangle t in surface.Triangles)
                if (!Finite(t.A) || !Finite(t.B) || !Finite(t.C) || !Finite(AreaMm2(t)) || AreaMm2(t) <= 0)
                { errors.Add("Нечисловая или вырожденная поверхность предмета."); break; }
            var keys = new HashSet<HandPosePotentialContactTriangleKey>();
            double referenceSum = 0;
            if (mask.Triangles == null) errors.Add("Нет списка граней маски.");
            else foreach (var entry in mask.Triangles)
            {
                if (entry == null) { errors.Add("Пустая запись грани маски."); continue; }
                var key = Key(entry.Source, entry.SourceSubMesh, entry.SourceTriangle);
                if (!ValidKey(key)) errors.Add("Неверный ключ маски: " + key);
                if (!keys.Add(key)) errors.Add("Повтор ключа маски: " + key);
                if (!HandPosePotentialContactMask.IsRegion(entry.Region) || !regions.Contains(entry.Region)) errors.Add("Неизвестный регион грани: " + key);
                if (!Finite(entry.Weight) || entry.Weight < 0 || entry.Weight > 1) errors.Add("Вес вне [0,1]: " + key);
                if (entry.ReferenceAreaMm2.HasValue && (!Finite(entry.ReferenceAreaMm2.Value) || entry.ReferenceAreaMm2.Value <= 0)) errors.Add("ReferenceAreaMm2 должна быть null или конечной и >0: " + key);
                if (!handByKey.TryGetValue(key, out FitTriangle t)) errors.Add("Baked-грань маски отсутствует: " + key);
                else if (entry.Weight > 0)
                {
                    if (AreaMm2(t) <= 0) errors.Add("Выбранная baked-грань вырождена: " + key);
                    double sampleCount = (double)settings.Subdivisions * settings.Subdivisions;
                    double weightedArea = AreaMm2(t) * entry.Weight;
                    if (!Finite(weightedArea) || weightedArea / sampleCount <= 0)
                        errors.Add("Взвешенная площадь не представима при заданной выборке: " + key);
                    if (entry.ReferenceAreaMm2.HasValue)
                    {
                        double weightedReference = entry.ReferenceAreaMm2.Value * entry.Weight;
                        referenceSum += weightedReference;
                        if (!Finite(referenceSum) || weightedReference / sampleCount <= 0)
                            errors.Add("Эталонная взвешенная площадь не представима: " + key);
                    }
                    selected.Add(new Selected { Triangle = t, Entry = new HandPosePotentialContactMaskTriangle
                    {
                        Source = entry.Source, SourceSubMesh = entry.SourceSubMesh, SourceTriangle = entry.SourceTriangle,
                        Region = entry.Region, Weight = entry.Weight, ReferenceAreaMm2 = entry.ReferenceAreaMm2
                    } });
                }
            }
            // Порядок сохранения маски и baked-меша не влияет на суммирование и callback.
            selected.Sort((a, b) =>
            {
                int source = StringComparer.Ordinal.Compare(a.Entry.Source, b.Entry.Source);
                if (source != 0) return source;
                int sub = a.Entry.SourceSubMesh.CompareTo(b.Entry.SourceSubMesh);
                return sub != 0 ? sub : a.Entry.SourceTriangle.CompareTo(b.Entry.SourceTriangle);
            });
            return selected;
        }

        static HandPosePotentialContactTriangleKey Key(string source, int sub, int triangle) => new HandPosePotentialContactTriangleKey(source, sub, triangle);
        static bool ValidKey(HandPosePotentialContactTriangleKey key) => !string.IsNullOrWhiteSpace(key.Source) && key.SourceSubMesh >= 0 && key.SourceTriangle >= 0;
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static double AreaMm2(FitTriangle t) => (double)Vector3.Cross(t.B - t.A, t.C - t.A).magnitude * 500000;

        static bool FiniteResult(HandPosePotentialContactCoverageRegion v)
        {
            foreach (double value in new[] { v.CurrentPotentialAreaMm2, v.ContactBandAreaMm2, v.KnownGapAreaMm2,
                v.KnownDeepPenetrationAreaMm2, v.AmbiguousSignAreaMm2, v.AmbiguousSignOutsideBandAreaMm2,
                v.AreaOpposingNormalsMm2, v.QualifiedContactAreaMm2, v.SignConfidenceAreaMm2,
                v.MaxSampleCellDiameterMm, v.ContactBoundaryUnresolvedAreaMm2 }) if (!Finite(value)) return false;
            foreach (double? value in new[] { v.ReferencePotentialAreaMm2, v.CurrentCoveragePercent,
                v.ReferenceWeightedContactPercent, v.GapAreaMm2, v.GapPercent, v.DeepPenetrationAreaMm2,
                v.DeepPenetrationPercent, v.OpposingNormalsPercent, v.QualifiedCoveragePercent,
                v.KnownMaxDepthMm, v.MaxDepthMm, v.MeanSignConfidence, v.ContactPercentLowerBound,
                v.ContactPercentUpperBound, v.CoarseCoveragePercent, v.FineMinusCoarsePercentagePoints })
                if (value.HasValue && !Finite(value.Value)) return false;
            return true;
        }

        static IEnumerable<Vector3> Points(FitTriangle t, int n)
        {
            Vector3 ab = t.B - t.A, ac = t.C - t.A;
            for (int i = 0; i < n; i++) for (int j = 0; j < n - i; j++)
            {
                yield return t.A + ab * ((i + 1f / 3) / n) + ac * ((j + 1f / 3) / n);
                if (i + j < n - 1) yield return t.A + ab * ((i + 2f / 3) / n) + ac * ((j + 2f / 3) / n);
            }
        }
    }
}

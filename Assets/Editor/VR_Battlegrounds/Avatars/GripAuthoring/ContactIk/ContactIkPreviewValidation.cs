using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>Явные пороги вызывающего кода, без расширения до исходной позы.</summary>
    public sealed class ContactIkPreviewPolicy
    {
        public readonly ContactIkCriterion Criterion;
        public readonly double MaximumPenetrationMm, MaximumPenetrationAreaMm2;
        public readonly long MaximumIntersectionPairs, IntersectionPairBudget;
        public readonly int CollisionSamples, CollisionSeed;
        public readonly IReadOnlyDictionary<string, double> MinimumCoverage;
        public readonly HandPosePotentialContactCoverageSettings Coverage;
        public readonly double CoveragePenaltyWeight, CollisionPenaltyWeight;

        public ContactIkPreviewPolicy(double maximumPenetrationMm, double maximumPenetrationAreaMm2, long maximumIntersectionPairs,
            IDictionary<string, double> minimumCoverage, HandPosePotentialContactCoverageSettings coverage,
            int collisionSamples, int collisionSeed, long intersectionPairBudget, double coveragePenaltyWeight = 1, double collisionPenaltyWeight = 1)
        {
            Criterion = ContactIkCriterion.LegacyAreaContact;
            if (!ContactIkSurfaceAnchor.Finite(maximumPenetrationMm) || maximumPenetrationMm < 0 ||
                !ContactIkSurfaceAnchor.Finite(maximumPenetrationAreaMm2) || maximumPenetrationAreaMm2 < 0 || maximumIntersectionPairs < 0 ||
                collisionSamples < 1 || intersectionPairBudget < 1 || coverage == null || minimumCoverage == null ||
                !ContactIkSurfaceAnchor.Finite(coveragePenaltyWeight) || coveragePenaltyWeight < 0 ||
                !ContactIkSurfaceAnchor.Finite(collisionPenaltyWeight) || collisionPenaltyWeight < 0)
                throw new ArgumentException("Нужны явные конечные пороги проникновения, coverage и бюджеты измерений.");
            var copied = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string name in ContactIkPreviewSession.RegionNames)
            {
                if (!minimumCoverage.TryGetValue(name, out double value) || !ContactIkSurfaceAnchor.Finite(value) || value < 0 || value > 1)
                    throw new ArgumentException("Для каждой области нужен явный minimum coverage [0;1]: " + name);
                copied.Add(name, value);
            }
            if (minimumCoverage.Count != copied.Count) throw new ArgumentException("В политике указана неизвестная область coverage.");
            MinimumCoverage = new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(copied);
            Coverage = CopyCoverage(coverage);
            MaximumPenetrationMm = maximumPenetrationMm; MaximumPenetrationAreaMm2 = maximumPenetrationAreaMm2;
            MaximumIntersectionPairs = maximumIntersectionPairs; CollisionSamples = collisionSamples; CollisionSeed = collisionSeed;
            IntersectionPairBudget = intersectionPairBudget; CoveragePenaltyWeight = coveragePenaltyWeight; CollisionPenaltyWeight = collisionPenaltyWeight;
        }

        /// <summary>Полоса ±2 мм вокруг поверхности для фиксированных кожных точек; площадь не измеряется и не ограничивает результат.</summary>
        public static ContactIkPreviewPolicy ForFixedPointBand(double maximumPenetrationMm, long maximumIntersectionPairs,
            int collisionSamples, int collisionSeed, long intersectionPairBudget, double collisionPenaltyWeight = 1) =>
            new ContactIkPreviewPolicy(maximumPenetrationMm, maximumIntersectionPairs, collisionSamples, collisionSeed, intersectionPairBudget, collisionPenaltyWeight);

        /// <summary>v2: бюджеты полной collision-диагностики; её числа не ограничивают и не оценивают q.</summary>
        public static ContactIkPreviewPolicy ForDistalPalmTargets(int collisionSamples, int collisionSeed, long intersectionPairBudget) =>
            new ContactIkPreviewPolicy(0, 0, collisionSamples, collisionSeed, intersectionPairBudget, 0, ContactIkCriterion.DistalPalmTargets);

        /// <summary>Обхват: только point constraints; fullskin остаётся диагностикой независимо от выбранного компромисса.</summary>
        public static ContactIkPreviewPolicy ForForwardSurfaceWrap(int collisionSamples, int collisionSeed, long intersectionPairBudget) =>
            new ContactIkPreviewPolicy(0, 0, collisionSamples, collisionSeed, intersectionPairBudget, 0, ContactIkCriterion.ForwardSurfaceWrap);

        private ContactIkPreviewPolicy(double maximumPenetrationMm, long maximumIntersectionPairs,
            int collisionSamples, int collisionSeed, long intersectionPairBudget, double collisionPenaltyWeight,
            ContactIkCriterion criterion = ContactIkCriterion.FixedPointBand)
        {
            if (!ContactIkSurfaceAnchor.Finite(maximumPenetrationMm) || maximumPenetrationMm < 0 || maximumIntersectionPairs < 0 ||
                collisionSamples < 1 || intersectionPairBudget < 1 || !ContactIkSurfaceAnchor.Finite(collisionPenaltyWeight) || collisionPenaltyWeight < 0)
                throw new ArgumentException("Нужны явные max depth/intersections и бюджеты collision.");
            Criterion = criterion;
            MaximumPenetrationMm = maximumPenetrationMm; MaximumIntersectionPairs = maximumIntersectionPairs;
            CollisionSamples = collisionSamples; CollisionSeed = collisionSeed; IntersectionPairBudget = intersectionPairBudget;
            CollisionPenaltyWeight = collisionPenaltyWeight;
            MinimumCoverage = new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(new Dictionary<string, double>());
            Coverage = null; CoveragePenaltyWeight = 0;
            // Legacy-поле MaximumPenetrationAreaMm2 в этом режиме не используется.
        }

        internal static HandPosePotentialContactCoverageSettings CopyCoverage(HandPosePotentialContactCoverageSettings s) => new HandPosePotentialContactCoverageSettings
        {
            ContactBandMm = s.ContactBandMm, Subdivisions = s.Subdivisions, CheckConvergence = s.CheckConvergence,
            MaxSampleQueries = s.MaxSampleQueries, MinimumNormalOpposition = s.MinimumNormalOpposition
        };
    }

    public sealed class ContactIkObjectEvidence
    {
        public bool GeometryReviewed, FullAssembly, OutwardOrientationReviewed;
        public string Evidence;
    }

    /// <summary>Полная кожа без contact-маски и полный предмет для независимого валидатора.</summary>
    public sealed class ContactIkCollisionInput
    {
        public IReadOnlyList<FitTriangle> Hand, Object;
        public ContactIkPreviewCollisionDiagnostics Sampled;
        public ContactIkPreviewPolicy Policy;
    }

    /// <summary>Явное доказательство полноты/надёжности. Числа — консервативные границы, а не максимум редких выборок.</summary>
    public sealed class ContactIkCollisionValidation
    {
        public bool Complete, Reliable;
        public double? MaximumPenetrationMm, PenetrationAreaMm2;
        public long? IntersectionPairs;
        public string Evidence;
    }

    public sealed class ContactIkPreviewCollisionDiagnostics
    {
        public bool PenetrationAreaMeasured;
        public int HandTriangles, DegenerateHandTriangles, Samples, KnownSignSamples, UnknownSignSamples;
        public long IntersectionPairs, IntersectionPairTests, IntersectionPairBudget;
        public bool IntersectionPassComplete;
        public double KnownSampledPenetrationAreaMm2, KnownSampledMaxDepthMm;
        public double? SampledMaximumDepthMm;
        public double WindingMin = double.PositiveInfinity, WindingMax = double.NegativeInfinity;
        public string Meaning = "Полная кожа участвует в intersection pass в пределах бюджета; глубина/площадь sampled. Нулевые пересечения и sampled depth не доказывают отсутствие полного containment или глубокого участка между выборками.";
    }

    public sealed class ContactIkPreviewObservation
    {
        public ContactIkCriterion Criterion;
        public bool AreaCriterionEvaluated;
        public bool FullSkinDiagnosticOnly, RequiresVisualReview;
        public float DistalPriority;
        public Vector3 PalmTranslationObjectMeters;
        public bool PalmTranslationAtLimit;
        public ContactIkEvaluation Evaluation;
        public HandPosePotentialContactCoverageReport Coverage;
        public ContactIkPreviewCollisionDiagnostics Collision;
        public ContactIkCollisionValidation ExternalValidation;
        public FitTopology ObjectTopology;
        public int CapturedRenderers, HandTriangles;
        public bool AllQueriedSignsKnown, RendererSelectionReviewed, CorrespondencesReviewed;
        public double CurrentPotentialAreaMm2;
        public double? ReferencePotentialAreaMm2;
        public ContactIkPreviewAnchorObservation[] Anchors;
    }

    public sealed class ContactIkPreviewAnchorObservation
    {
        public string PatchId;
        public ContactIkTargetRole Role;
        public string FingerId;
        public bool Ignored;
        public double EffectiveWeight;
        public Vector3? ObjectTargetPositionMeters;
        public Vector3 PositionMeters, Normal;
        public double UnsignedDistanceMm;
        public double? SignedDistanceMm;
        public HandPosePotentialContactSign Sign;
    }

    /// <summary>Честная предварительная collision-диагностика. Самостоятельно не выдаёт сертификат полной безопасности.</summary>
    public static class ContactIkPreviewCollision
    {
        public static ContactIkPreviewCollisionDiagnostics Measure(FitTriangle[] hand, FitSurface fullObject, ContactIkPreviewPolicy policy,
            Func<Vector3, FitNearestHit, HandPosePotentialContactSign> signClassifier)
        {
            if (hand == null || hand.Length == 0 || fullObject == null || policy == null) throw new ArgumentException("Нет полной геометрии или политики collision.");
            var d = new ContactIkPreviewCollisionDiagnostics
            { HandTriangles = hand.Length, IntersectionPairBudget = policy.IntersectionPairBudget, IntersectionPassComplete = true, PenetrationAreaMeasured = policy.Criterion == ContactIkCriterion.LegacyAreaContact };
            foreach (FitTriangle t in hand) if (t.Area <= 1e-12f) d.DegenerateHandTriangles++;
            foreach (FitTriangle t in hand)
            {
                foreach (int index in fullObject.Overlaps(t.Bounds))
                {
                    if (d.IntersectionPairTests >= policy.IntersectionPairBudget) { d.IntersectionPassComplete = false; break; }
                    d.IntersectionPairTests++;
                    if (HandPoseFitGeometry.Intersects(t, fullObject.Triangles[index])) d.IntersectionPairs++;
                }
                if (!d.IntersectionPassComplete) break;
            }
            foreach (FitSample sample in HandPoseFitGeometry.Sample(hand, policy.CollisionSamples, policy.CollisionSeed))
            {
                FitNearestHit hit = fullObject.NearestHit(sample.Position);
                double distance = (hit.Position - sample.Position).magnitude * 1000;
                double winding = fullObject.WindingNumber(sample.Position);
                if (!ContactIkSurfaceAnchor.Finite(distance) || !ContactIkSurfaceAnchor.Finite(winding)) throw new ArgumentException("Нечисловая collision-диагностика.");
                d.Samples++; d.WindingMin = Math.Min(d.WindingMin, winding); d.WindingMax = Math.Max(d.WindingMax, winding);
                HandPosePotentialContactSign sign = signClassifier == null ? new HandPosePotentialContactSign { Reason = "sign_classifier_not_supplied" } : signClassifier(sample.Position, hit);
                if (!sign.Inside.HasValue) { d.UnknownSignSamples++; continue; }
                d.KnownSignSamples++;
                if (sign.Inside.Value)
                {
                    d.KnownSampledMaxDepthMm = Math.Max(d.KnownSampledMaxDepthMm, distance);
                    if (d.PenetrationAreaMeasured) d.KnownSampledPenetrationAreaMm2 += sample.Area * 1e6;
                }
            }
            if (d.UnknownSignSamples == 0) d.SampledMaximumDepthMm = d.KnownSampledMaxDepthMm;
            return d;
        }
    }
}

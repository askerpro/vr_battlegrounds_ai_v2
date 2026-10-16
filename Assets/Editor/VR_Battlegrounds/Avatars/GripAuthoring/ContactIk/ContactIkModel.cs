using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>Независимая степень свободы: угол относительно точно перенесённой исходной позы, градусы.</summary>
    public sealed class ContactIkDof
    {
        public string Id;
        public Transform Bone;
        public Vector3 LocalAxis;
        public float MinDegrees, MaxDegrees;
        public float PriorScaleDegrees = 30f;
        public float PriorWeight = 1f;
    }

    public enum ContactIkCriterion { LegacyAreaContact, FixedPointBand, DistalPalmTargets, ForwardSurfaceWrap }

    public enum ContactIkTargetRole { Unspecified, Palm, Distal, Intermediate }

    public enum ContactIkTargetMode { Contact, NoContact, PreservePrior, FixedPointBand }

    /// <summary>Стабильный материальный участок; точка и нормаль заданы в системе предмета.</summary>
    public sealed class ContactIkTarget
    {
        public const float FixedPointBandMeters = .002f;
        public string PatchId;
        public ContactIkTargetRole Role;
        public string FingerId;
        public ContactIkTargetMode Mode;
        public Vector3 PositionMeters, Normal;
        public bool MatchNormal;
        public float PositionToleranceMeters = .002f;
        public float NormalToleranceDegrees = 20f;
        public float MinimumClearanceMeters = .002f;
        public float Weight = 1f;
    }

    /// <summary>Наблюдение того же участка на реально запечённой коже, не на жёсткой модели фаланг.</summary>
    public sealed class ContactIkPatchObservation
    {
        public string PatchId;
        public Vector3 PositionMeters, Normal;
        public float? SignedClearanceMeters;
        public bool ClearanceReliable;
        /// <summary>Расстояние текущей фиксированной кожной точки до ближайшей поверхности; знак для этого измерения не нужен.</summary>
        public float? UnsignedDistanceMeters;
        public bool UnsignedDistanceReliable;
    }

    /// <summary>Площадь заранее размеченной области. Набор материальных образцов и знаменатель неизменны.</summary>
    public sealed class ContactIkRegionObservation
    {
        public string RegionId, SampleSetId;
        public double FixedAreaMm2, NearAreaMm2;
        /// <summary>Проникновение с фиксированными эталонными весами площади; null, если не измерено или знак неизвестен.</summary>
        public double? PenetrationAreaMm2;
        /// <summary>Подтверждённая глубина; null, если полного надёжного измерения нет.</summary>
        public double? MaxPenetrationMm;
        public double Coverage => FixedAreaMm2 > 0 ? NearAreaMm2 / FixedAreaMm2 : 0;
    }

    /// <summary>
    /// Результат фактического BakeMesh/collision validator. В legacy жёсткие запреты не компенсируются стоимостью IK.
    /// В DistalPalmTargets fullskin-поля исключительно диагностические и не участвуют в выборе q.
    /// </summary>
    public sealed class ContactIkEvaluation
    {
        public ContactIkCriterion Criterion;
        /// <summary>Пользователь проверил материальные точки; численное попадание в полосу само по себе этого не означает.</summary>
        public bool FixedPointsReviewed;
        public float DistalPriority;
        public Vector3 PalmTranslationObjectMeters;
        public ContactIkPatchObservation[] Patches = Array.Empty<ContactIkPatchObservation>();
        public ContactIkRegionObservation[] Regions = Array.Empty<ContactIkRegionObservation>();
        /// <summary>Проверена вся кожа ладони/пястных/фаланг против полного предмета; маска контакта не ограничивает collision.</summary>
        public bool FullSurfaceChecked;
        /// <summary>Надёжность знака/расстояний/нормалей; неполная или открытая геометрия не даёт автоматической приёмки.</summary>
        public bool Reliable;
        /// <summary>Legacy: соблюдены явные collision ограничения. V2: только отдельная raw диагностика, не критерий численного поиска.</summary>
        public bool Feasible;
        /// <summary>Выполнены пороги coverage заданных областей ладони и пальцев; минимум задаёт вызывающий код.</summary>
        public bool ContactCoverageSatisfied;
        public double ConstraintViolation, SurfaceCost, ContactCoveragePenalty;
        public string Diagnostic;
    }

    public sealed class ContactIkSettings
    {
        public ContactIkCriterion Criterion;
        /// <summary>Только v2: множитель intermediate равен 1-priority. Полная кожа остаётся диагностикой.</summary>
        public float DistalPriority;
        public int MaxEvaluations = 512;
        public float InitialStepDegrees = 8f, MinimumStepDegrees = .125f;
        public double PriorWeight = .01, ImprovementEpsilon = 1e-9;
    }

    public sealed class ContactIkTargetError
    {
        public string PatchId;
        public bool Satisfied;
        public bool Ignored;
        public ContactIkTargetRole Role;
        public double EffectiveWeight;
        public float PositionErrorMeters, NormalErrorDegrees, ClearanceDeficitMeters;
        public float? UnsignedDistanceMeters, BandExcessMeters;
    }

    /// <summary>Диагностика поиска, а не приёмка игрового хвата. Итоговая поза остаётся только на копии.</summary>
    public sealed class ContactIkResult
    {
        public string Status;
        public bool Converged, HasReliableFeasiblePose, TargetsReached, RequiresReview;
        /// <summary>Численное достижение активных точек; не приёмка хвата и не доказательство collision safety.</summary>
        public bool NumericalTargetsReached;
        public Vector3 PalmTranslationObjectMeters;
        public bool Solved => Converged;
        public int Evaluations, Sweeps;
        public float[] Degrees;
        public string[] AtLimits;
        public double Cost;
        public ContactIkTargetError[] TargetErrors;
        public ContactIkEvaluation FinalEvaluation;
    }
}

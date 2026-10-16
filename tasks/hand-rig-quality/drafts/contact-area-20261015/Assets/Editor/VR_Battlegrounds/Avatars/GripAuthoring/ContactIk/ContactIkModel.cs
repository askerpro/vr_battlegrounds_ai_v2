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

    public enum ContactIkTargetMode { Contact, NoContact, PreservePrior }

    /// <summary>Стабильный материальный участок; точка и нормаль заданы в системе предмета.</summary>
    public sealed class ContactIkTarget
    {
        public string PatchId;
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
    /// Результат внешнего полного BakeMesh/collision validator. Жёсткие запреты не компенсируются стоимостью IK.
    /// Допустимые проникновения и пороги площади задаёт вызывающий код явно, без послабления до исходной позы.
    /// </summary>
    public sealed class ContactIkEvaluation
    {
        public ContactIkPatchObservation[] Patches = Array.Empty<ContactIkPatchObservation>();
        public ContactIkRegionObservation[] Regions = Array.Empty<ContactIkRegionObservation>();
        /// <summary>Проверена вся кожа ладони/пястных/фаланг против полного предмета; маска контакта не ограничивает collision.</summary>
        public bool FullSurfaceChecked;
        /// <summary>Надёжность знака/расстояний/нормалей; неполная или открытая геометрия не даёт автоматической приёмки.</summary>
        public bool Reliable;
        /// <summary>Соблюдены явно переданные вызывающим кодом жёсткие ограничения проникновений.</summary>
        public bool Feasible;
        /// <summary>Выполнены пороги coverage заданных областей ладони и пальцев; минимум задаёт вызывающий код.</summary>
        public bool ContactCoverageSatisfied;
        public double ConstraintViolation, SurfaceCost, ContactCoveragePenalty;
        public string Diagnostic;
    }

    public sealed class ContactIkSettings
    {
        public int MaxEvaluations = 512;
        public float InitialStepDegrees = 8f, MinimumStepDegrees = .125f;
        public double PriorWeight = .01, ImprovementEpsilon = 1e-9;
    }

    public sealed class ContactIkTargetError
    {
        public string PatchId;
        public bool Satisfied;
        public float PositionErrorMeters, NormalErrorDegrees, ClearanceDeficitMeters;
    }

    /// <summary>Диагностика поиска, а не приёмка игрового хвата. Итоговая поза остаётся только на копии.</summary>
    public sealed class ContactIkResult
    {
        public string Status;
        public bool Converged, HasReliableFeasiblePose, TargetsReached, RequiresReview;
        public bool Solved => Converged;
        public int Evaluations, Sweeps;
        public float[] Degrees;
        public string[] AtLimits;
        public double Cost;
        public ContactIkTargetError[] TargetErrors;
        public ContactIkEvaluation FinalEvaluation;
    }
}

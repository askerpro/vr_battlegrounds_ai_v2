using System;
using UltimateXR.Core.Serialization;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Scripting;

namespace UltimateXR.Mechanics.Weapons
{
    public enum UxrFirearmNotReadyReason { NoMagazine, EmptyMagazine, ChamberingRequired }
    public enum UxrFirearmTriggerDecisionKind { FireAllowed, NotReady, PrepareOnlyConsumed, OtherDenied }
    public enum UxrFirearmShotEmissionOutcome { NotEmitted, Emitted, Indeterminate }
    public enum UxrFirearmReadinessOperation { Initialize, MagazineChanged, BeginAction, Extract, Complete, Cancel, Shot, Reconcile, Automation, CloseOnly, EmptyRestAcknowledged, AmmoAdmission }

    public readonly struct UxrFirearmTriggerDecision
    {
        public UxrFirearmTriggerDecisionKind Kind { get; }
        public UxrFirearmNotReadyReason Reason { get; }
        public UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind kind, UxrFirearmNotReadyReason reason = default)
        { Kind = kind; Reason = reason; }
    }

    /// <summary>Семантика патронника. Magazine — identity, единственный store магазина остаётся Rounds.</summary>
    [Serializable, Preserve]
    public sealed class UxrFirearmReadinessState : IUxrSerializable, ICloneable
    {
        public bool ReadinessInitialized, ChamberRound, ChamberCyclePending, ActionOpen;
        // Последнее committed consumption, не доказательство Source Emitted/FX и не Ready.
        public bool PostShotEmptyAction;
        public UxrGrabbableObject CurrentMagazine;
        public uint CycleSequence, ExtractedCycleSequence, Revision, ShotSequence;
        public int SerializationVersion => 2;
        public object Clone() => MemberwiseClone();
        public void Serialize(IUxrSerializer serializer, int version)
        {
            serializer.Serialize(ref ReadinessInitialized);
            serializer.SerializeUniqueComponent(ref CurrentMagazine);
            serializer.Serialize(ref ChamberRound); serializer.Serialize(ref ChamberCyclePending); serializer.Serialize(ref ActionOpen);
            serializer.Serialize(ref CycleSequence); serializer.Serialize(ref ExtractedCycleSequence);
            serializer.Serialize(ref Revision); serializer.Serialize(ref ShotSequence);
            if (version >= 2) serializer.Serialize(ref PostShotEmptyAction);
            else if (serializer.IsReading) PostShotEmptyAction = false;
        }
        public override bool Equals(object obj) => obj is UxrFirearmReadinessState other &&
            ReadinessInitialized == other.ReadinessInitialized && CurrentMagazine == other.CurrentMagazine &&
            ChamberRound == other.ChamberRound && ChamberCyclePending == other.ChamberCyclePending && ActionOpen == other.ActionOpen &&
            CycleSequence == other.CycleSequence && ExtractedCycleSequence == other.ExtractedCycleSequence && Revision == other.Revision && ShotSequence == other.ShotSequence && PostShotEmptyAction == other.PostShotEmptyAction;
        public override int GetHashCode() => (ReadinessInitialized ? 1 : 0) ^ (ChamberRound ? 2 : 0) ^
            (ChamberCyclePending ? 4 : 0) ^ (ActionOpen ? 8 : 0) ^ (PostShotEmptyAction ? 16 : 0) ^ (CurrentMagazine != null ? CurrentMagazine.GetHashCode() : 0) ^
            CycleSequence.GetHashCode() ^ ExtractedCycleSequence.GetHashCode() ^ Revision.GetHashCode() ^ ShotSequence.GetHashCode();
    }

    /// <summary>Неизменяемый дополнительный shot, подготовленный автором до расхода.</summary>
    public readonly struct UxrAdditionalShotPlan
    {
        public int ShotIndex { get; }
        public Vector3 Position { get; }
        public Quaternion Orientation { get; }
        public UxrAdditionalShotPlan(int shotIndex, Vector3 position, Quaternion orientation)
        { ShotIndex = shotIndex; Position = position; Orientation = orientation; }
    }

    [Serializable, Preserve]
    public sealed class UxrAdditionalShotCommit : IUxrSerializable
    {
        public int ShotIndex;
        public Vector3 Position;
        public Quaternion Orientation;
        public UxrFirearmShotEmissionOutcome Outcome;
        public int SerializationVersion => 1;
        public void Serialize(IUxrSerializer serializer, int version)
        {
            serializer.Serialize(ref ShotIndex); serializer.Serialize(ref Position);
            serializer.Serialize(ref Orientation); serializer.SerializeEnum(ref Outcome);
        }
    }

    /// <summary>After-values конкретного магазина и патронника; DTO не является вторым живым store.</summary>
    [Serializable, Preserve]
    public sealed class UxrFirearmReadinessCommit : IUxrSerializable
    {
        public int TriggerIndex, MagazineRoundsAfter;
        public uint ExpectedRevision, NextRevision;
        public Guid ReferencedMagazineIdentity;
        public UxrGrabbableObject ReferencedMagazine;
        public UxrFirearmReadinessState StateAfter;
        public UxrFirearmReadinessOperation Operation;
        public UxrFirearmShotEmissionOutcome EmissionOutcome;
        public Vector3 SourcePosition;
        public Quaternion SourceOrientation;
        public UxrAdditionalShotCommit[] AdditionalShots;
        public int SerializationVersion => 2;
        public void Serialize(IUxrSerializer serializer, int version)
        {
            serializer.Serialize(ref TriggerIndex); serializer.Serialize(ref ExpectedRevision); serializer.Serialize(ref NextRevision);
            serializer.SerializeUniqueComponent(ref ReferencedMagazine); serializer.Serialize(ref ReferencedMagazineIdentity); serializer.Serialize(ref MagazineRoundsAfter);
            serializer.SerializeAnyVar(ref StateAfter); serializer.SerializeEnum(ref Operation); serializer.SerializeEnum(ref EmissionOutcome);
            serializer.Serialize(ref SourcePosition); serializer.Serialize(ref SourceOrientation);
            if (version >= 2) serializer.SerializeAnyVar(ref AdditionalShots);
            else if (serializer.IsReading) AdditionalShots = null;
        }
    }
}

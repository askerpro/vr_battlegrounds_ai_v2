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
    public enum UxrFirearmReadinessOperation
    {
        Initialize,
        // VR Battlegrounds patch 53: магазин учёт не фиксирует — его знает гнездо. Значения не перенумерованы
        // ради формата сети; ValidateReadinessCommit отклоняет обе операции.
        [Obsolete("Patch 53: магазин выводится из гнезда, фиксации нет.")] MagazineChanged,
        BeginAction, Extract, Complete, Cancel, Shot,
        [Obsolete("Patch 53: сверки с гнездом нет — копии магазина в учёте нет.")] Reconcile,
        Automation, CloseOnly, EmptyRestAcknowledged, AmmoAdmission
    }

    /// <summary>
    /// VR Battlegrounds patch 53: что replay учёта увидел у получателя (п. 3a weapon-ledger-single-source).
    /// DeltaMismatch — ревизия совпала, M получателя не сходится с дельтой операции: применено значение автора.
    /// RevisionFork — ревизия не совпала: фиксация не применена, сервер публикует поправку.
    /// CorrectionApplied — получатель применил поправку сервера.
    /// </summary>
    public enum UxrFirearmReplayDivergenceKind { DeltaMismatch, RevisionFork, CorrectionApplied }

    /// <summary>Данные инцидента расхождения учёта. Лог пишет игровой владелец портов (SDK не знает GameLog).</summary>
    public sealed class UxrFirearmReplayDivergence
    {
        public UxrFirearmReplayDivergenceKind Kind;
        public int TriggerIndex;
        public UxrFirearmReadinessOperation Operation;
        public UxrGrabbableObject Magazine;
        public Guid MagazineIdentity;
        public uint ExpectedRevision, NextRevision, LocalRevision;
        /// <summary>M получателя до replay, ожидаемое после по дельте операции, после-значение автора. -1 — неприменимо.</summary>
        public int ReceiverRoundsBefore = -1, ReceiverExpectedRoundsAfter = -1, AuthorRoundsAfter = -1;
        public bool Replay;
    }

    public readonly struct UxrFirearmTriggerDecision
    {
        public UxrFirearmTriggerDecisionKind Kind { get; }
        public UxrFirearmNotReadyReason Reason { get; }
        public UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind kind, UxrFirearmNotReadyReason reason = default)
        { Kind = kind; Reason = reason; }
    }

    /// <summary>
    /// Семантика патронника. VR Battlegrounds patch 53: учёт хранит только то, чего не знают гнездо и магазин.
    /// Какой магазин вставлен — всегда гнездо (<c>GetCurrentReadinessMagazine</c>); копии магазина здесь нет.
    /// <see cref="CycleMagazineIdentity"/> — с каким магазином начат цикл: Pending действителен, только пока
    /// в гнезде тот же магазин (<see cref="UxrFirearmWeapon.IsCyclePendingFor"/>).
    /// </summary>
    [Serializable, Preserve]
    public sealed class UxrFirearmReadinessState : IUxrSerializable, ICloneable
    {
        public bool ReadinessInitialized, ChamberRound, ChamberCyclePending, ActionOpen;
        // Последнее committed consumption, не доказательство Source Emitted/FX и не Ready.
        public bool PostShotEmptyAction;
        public Guid CycleMagazineIdentity;
        public uint CycleSequence, ExtractedCycleSequence, Revision, ShotSequence;
        public int SerializationVersion => 3;
        public object Clone() => MemberwiseClone();
        public void Serialize(IUxrSerializer serializer, int version)
        {
            serializer.Serialize(ref ReadinessInitialized);
            if (version >= 3) serializer.Serialize(ref CycleMagazineIdentity);
            else
            {
                // v2: копия магазина читается и отбрасывается; снимок не наследует жест (T32s).
                UxrGrabbableObject legacyMagazine = null;
                serializer.SerializeUniqueComponent(ref legacyMagazine);
            }
            serializer.Serialize(ref ChamberRound); serializer.Serialize(ref ChamberCyclePending); serializer.Serialize(ref ActionOpen);
            serializer.Serialize(ref CycleSequence); serializer.Serialize(ref ExtractedCycleSequence);
            serializer.Serialize(ref Revision); serializer.Serialize(ref ShotSequence);
            if (version >= 2) serializer.Serialize(ref PostShotEmptyAction);
            else if (serializer.IsReading) PostShotEmptyAction = false;
            if (version < 3 && serializer.IsReading) { ChamberCyclePending = false; CycleMagazineIdentity = Guid.Empty; }
        }
        public override bool Equals(object obj) => obj is UxrFirearmReadinessState other &&
            ReadinessInitialized == other.ReadinessInitialized && CycleMagazineIdentity == other.CycleMagazineIdentity &&
            ChamberRound == other.ChamberRound && ChamberCyclePending == other.ChamberCyclePending && ActionOpen == other.ActionOpen &&
            CycleSequence == other.CycleSequence && ExtractedCycleSequence == other.ExtractedCycleSequence && Revision == other.Revision && ShotSequence == other.ShotSequence && PostShotEmptyAction == other.PostShotEmptyAction;
        public override int GetHashCode() => (ReadinessInitialized ? 1 : 0) ^ (ChamberRound ? 2 : 0) ^
            (ChamberCyclePending ? 4 : 0) ^ (ActionOpen ? 8 : 0) ^ (PostShotEmptyAction ? 16 : 0) ^ CycleMagazineIdentity.GetHashCode() ^
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

    /// <summary>M одного магазина в поправке учёта (VR Battlegrounds patch 53).</summary>
    [Serializable, Preserve]
    public sealed class UxrFirearmMagazineRounds : IUxrSerializable
    {
        public UxrGrabbableObject Magazine;
        public Guid Identity;
        public int Rounds;
        public int SerializationVersion => 1;
        public void Serialize(IUxrSerializer serializer, int version)
        { serializer.SerializeUniqueComponent(ref Magazine); serializer.Serialize(ref Identity); serializer.Serialize(ref Rounds); }
    }

    /// <summary>
    /// VR Battlegrounds patch 53: поправка учёта, которую публикует сервер при форке ревизии. Содержание —
    /// только уже принятые сервером фиксации авторов: учёт спуска и M упомянутых магазинов.
    /// </summary>
    [Serializable, Preserve]
    public sealed class UxrFirearmLedgerCorrection : IUxrSerializable
    {
        public int TriggerIndex;
        public UxrFirearmReadinessState State;
        public UxrFirearmMagazineRounds[] Magazines;
        public int SerializationVersion => 1;
        public void Serialize(IUxrSerializer serializer, int version)
        { serializer.Serialize(ref TriggerIndex); serializer.SerializeAnyVar(ref State); serializer.SerializeAnyVar(ref Magazines); }
    }
}

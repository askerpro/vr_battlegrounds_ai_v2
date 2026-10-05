using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Scripting;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrFirearmWeapon
    {
        // VR Battlegrounds patch: opt-in ledger. Политика/physical evidence остаются вне SDK.
        private readonly HashSet<int> _readinessEnabled = new HashSet<int>();
        public Func<int, bool> CanAuthorReadinessAction { get; set; }
        public Action<int> RefreshPhysicalActionState { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidateChamberCompletion { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidatePhysicalActionClosed { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidatePostShotEmptyActionRest { get; set; }
        public Func<int, UxrGrabber, UxrFirearmTriggerDecision> EvaluateLocalTriggerAttempt { get; set; }
        public Func<int, bool> CanPrepareReadinessForAutomation { get; set; }
        public event Action<int, uint> ChamberRoundExtracted;
        public event Action<int, UxrFirearmShotEmissionOutcome, Exception> ReadinessFaulted;
        private readonly HashSet<int> _readinessFaulted = new HashSet<int>();
        private readonly HashSet<int> _readinessCommitting = new HashSet<int>();
        private bool IsReadinessReplay => UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync;

        public bool UsesReadinessLedger(int triggerIndex) => _readinessEnabled.Contains(triggerIndex);
        public bool TryEnableReadiness(int triggerIndex)
        {
            if (triggerIndex < 0 || triggerIndex >= TriggerCount || !_runtimeTriggers.ContainsKey(triggerIndex) ||
                CanAuthorReadinessAction == null || RefreshPhysicalActionState == null ||
                ValidateChamberCompletion == null || EvaluateLocalTriggerAttempt == null ||
                _triggers[triggerIndex].AmmunitionMagAnchor == null) return false;
            _readinessEnabled.Add(triggerIndex);
            return true;
        }
        public UxrFirearmReadinessState GetReadinessState(int triggerIndex) =>
            _runtimeTriggers.TryGetValue(triggerIndex, out RuntimeTriggerInfo runtime) && runtime.Readiness != null
                ? (UxrFirearmReadinessState)runtime.Readiness.Clone() : null;
        public int GetMagazineRounds(int triggerIndex) => GetCurrentReadinessMagazine(triggerIndex)?.GetComponent<UxrFirearmMag>().Rounds ?? 0;
        public bool HasChamberRound(int triggerIndex) => UsesReadinessLedger(triggerIndex) && GetReadinessState(triggerIndex)?.ChamberRound == true;
        public int GetTotalAmmoLeft(int triggerIndex) => UsesReadinessLedger(triggerIndex)
            ? GetMagazineRounds(triggerIndex) + (HasChamberRound(triggerIndex) ? 1 : 0) : GetAmmoLeft(triggerIndex);
        public bool IsReadyToFire(int triggerIndex)
        {
            if (!UsesReadinessLedger(triggerIndex)) return IsLoaded(triggerIndex);
            var state = GetReadinessState(triggerIndex);
            return state != null && state.ReadinessInitialized && state.ChamberRound &&
                   !state.ChamberCyclePending && !state.ActionOpen && !_readinessFaulted.Contains(triggerIndex);
        }
        public UxrFirearmTriggerDecision QueryReadinessDecision(int triggerIndex)
        {
            var state = GetReadinessState(triggerIndex);
            if (!UsesReadinessLedger(triggerIndex) || state == null || !state.ReadinessInitialized ||
                !CanUse || state.ActionOpen || state.ChamberCyclePending || _readinessFaulted.Contains(triggerIndex))
                return new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.OtherDenied);
            if (state.ChamberRound) return new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.FireAllowed);
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            return new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.NotReady, mag == null
                ? UxrFirearmNotReadyReason.NoMagazine : mag.GetComponent<UxrFirearmMag>().Rounds == 0
                    ? UxrFirearmNotReadyReason.EmptyMagazine : UxrFirearmNotReadyReason.ChamberingRequired);
        }
        private UxrGrabbableObject GetCurrentReadinessMagazine(int triggerIndex)
        {
            if (!TryGetTriggerMagazineAnchor(triggerIndex, out var anchor) || !anchor.isActiveAndEnabled) return null;
            var mag = anchor.CurrentPlacedObject;
            return mag != null && mag.CurrentAnchor == anchor && anchor.IsCompatibleObject(mag) &&
                   mag.GetComponent<UxrFirearmMag>() != null ? mag : null;
        }
        private bool CanOwnReadiness(int triggerIndex) => UsesReadinessLedger(triggerIndex) && !IsReadinessReplay &&
            !_readinessCommitting.Contains(triggerIndex) && CanAuthorReadinessAction?.Invoke(triggerIndex) == true;
        private bool CanWriteReadiness(int triggerIndex) => CanOwnReadiness(triggerIndex) &&
            isActiveAndEnabled && CanUse && TryGetTriggerMagazineAnchor(triggerIndex, out var anchor) && anchor.isActiveAndEnabled &&
            !_readinessFaulted.Contains(triggerIndex);
        private bool TryGetWritableState(int triggerIndex, uint revision, out UxrFirearmReadinessState state)
        {
            state = GetReadinessState(triggerIndex);
            return CanWriteReadiness(triggerIndex) && state != null && state.ReadinessInitialized && state.Revision == revision;
        }
        private UxrFirearmReadinessCommit CreateReadinessCommit(int triggerIndex, UxrFirearmReadinessState after,
            UxrGrabbableObject magazine, int rounds, UxrFirearmReadinessOperation operation)
        {
            uint previous = _runtimeTriggers[triggerIndex].Readiness?.Revision ?? 0;
            if (previous == uint.MaxValue) throw new InvalidOperationException("Readiness revision exhausted");
            after.CurrentMagazine = magazine; after.Revision = previous + 1;
            return new UxrFirearmReadinessCommit { TriggerIndex = triggerIndex, ExpectedRevision = previous, NextRevision = previous + 1,
                ReferencedMagazine = magazine, ReferencedMagazineIdentity = magazine != null ? magazine.UniqueId : Guid.Empty,
                MagazineRoundsAfter = rounds, StateAfter = after, Operation = operation };
        }

        // Одноразовый локальный snapshot границы readonly physical validator, не живой ammo store.
        private sealed class PhysicalValidationCapture
        {
            public readonly RuntimeTriggerInfo Runtime;
            public readonly UxrFirearmReadinessState State;
            public readonly bool HasReloaded;
            public readonly UxrGrabbableObject Magazine;
            public readonly UxrFirearmMag Ammo;
            public readonly Guid MagazineIdentity;
            public readonly UxrGrabbableObjectAnchor Anchor;
            public readonly UxrGrabbableObject PlacedObject;
            public readonly int Rounds, Capacity;
            public PhysicalValidationCapture(UxrFirearmWeapon weapon, int triggerIndex)
            {
                Runtime = weapon._runtimeTriggers[triggerIndex];
                State = Runtime.Readiness != null ? (UxrFirearmReadinessState)Runtime.Readiness.Clone() : null;
                HasReloaded = Runtime.HasReloaded;
                Magazine = weapon.GetCurrentReadinessMagazine(triggerIndex);
                Ammo = Magazine != null ? Magazine.GetComponent<UxrFirearmMag>() : null;
                MagazineIdentity = Magazine != null ? Magazine.UniqueId : Guid.Empty;
                weapon.TryGetTriggerMagazineAnchor(triggerIndex, out Anchor);
                PlacedObject = Anchor != null ? Anchor.CurrentPlacedObject : null;
                Rounds = Ammo != null ? Ammo.Rounds : 0;
                Capacity = Ammo != null ? Ammo.Capacity : 0;
            }
        }
        private bool IsPhysicalValidationCurrent(int triggerIndex, PhysicalValidationCapture capture)
        {
            if (!CanWriteReadiness(triggerIndex) || !_runtimeTriggers.TryGetValue(triggerIndex, out var runtime) ||
                !ReferenceEquals(runtime, capture.Runtime) || runtime.HasReloaded != capture.HasReloaded ||
                !(runtime.Readiness == null ? capture.State == null : runtime.Readiness.Equals(capture.State)) ||
                !TryGetTriggerMagazineAnchor(triggerIndex, out var anchor) || anchor != capture.Anchor ||
                anchor.CurrentPlacedObject != capture.PlacedObject || GetCurrentReadinessMagazine(triggerIndex) != capture.Magazine) return false;
            if (capture.Ammo == null) return capture.MagazineIdentity == Guid.Empty && GetMagazineRounds(triggerIndex) == 0;
            return capture.Magazine != null && capture.Magazine.UniqueId == capture.MagazineIdentity &&
                   capture.Magazine.CurrentAnchor == anchor && anchor.IsCompatibleObject(capture.Magazine) &&
                   capture.Magazine.GetComponent<UxrFirearmMag>() == capture.Ammo &&
                   capture.Ammo.Rounds == capture.Rounds && capture.Ammo.Capacity == capture.Capacity;
        }

        public bool TryInitializeReadiness(int triggerIndex)
        {
            if (!CanWriteReadiness(triggerIndex)) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (capture.State?.ReadinessInitialized == true) return false;
            bool prepared = capture.Rounds > 0 && capture.HasReloaded &&
                            ValidateChamberCompletion(triggerIndex, capture.State?.Revision ?? 0, capture.Magazine);
            if (!IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            var state = new UxrFirearmReadinessState { ReadinessInitialized = true, ChamberRound = prepared };
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, capture.Magazine,
                capture.Rounds - (prepared ? 1 : 0), UxrFirearmReadinessOperation.Initialize));
        }
        public bool TryBeginManualAction(int triggerIndex, uint expectedRevision, uint cycleSequence)
        {
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || cycleSequence <= state.CycleSequence) return false;
            state.ActionOpen = true; state.ChamberCyclePending = true; state.CycleSequence = cycleSequence;
            state.PostShotEmptyAction = false;
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.BeginAction));
        }
        public bool TryExtractChamberRound(int triggerIndex, uint expectedRevision, uint cycleSequence)
        {
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || !state.ActionOpen || !state.ChamberCyclePending ||
                state.CycleSequence != cycleSequence || state.ExtractedCycleSequence == cycleSequence) return false;
            state.ChamberRound = false; state.ExtractedCycleSequence = cycleSequence;
            state.PostShotEmptyAction = false;
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.Extract));
        }

        public bool TryCompleteChamber(int triggerIndex, uint expectedRevision, UxrGrabbableObject expectedMagazine)
        {
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || !state.ChamberCyclePending ||
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine || state.CurrentMagazine != expectedMagazine) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (!ValidateChamberCompletion(triggerIndex, expectedRevision, expectedMagazine) ||
                !IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            int rounds = capture.Rounds;
            if (!state.ChamberRound && rounds > 0) { rounds--; state.ChamberRound = true; }
            state.ChamberCyclePending = false; state.ActionOpen = false; state.PostShotEmptyAction = false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, expectedMagazine, rounds, UxrFirearmReadinessOperation.Complete));
        }
        public bool TryConfirmPhysicalActionClosed(int triggerIndex, uint expectedRevision, UxrGrabbableObject expectedMagazine)
        {
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || !state.ActionOpen || state.ChamberCyclePending ||
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine || state.CurrentMagazine != expectedMagazine ||
                ValidatePhysicalActionClosed == null) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (!ValidatePhysicalActionClosed(triggerIndex, expectedRevision, expectedMagazine) ||
                !IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            state.ActionOpen = false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, expectedMagazine, capture.Rounds, UxrFirearmReadinessOperation.CloseOnly));
        }
        // Обслуженный post-shot rest не создаёт chamber cycle и не переносит M→C.
        public bool TryAcknowledgePostShotEmptyActionRest(int triggerIndex, uint expectedRevision, UxrGrabbableObject expectedMagazine)
        {
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || !state.PostShotEmptyAction ||
                state.ChamberRound || state.ActionOpen || state.ChamberCyclePending ||
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine || state.CurrentMagazine != expectedMagazine ||
                ValidatePostShotEmptyActionRest == null) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (!ValidatePostShotEmptyActionRest(triggerIndex, expectedRevision, expectedMagazine) ||
                !IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            state.PostShotEmptyAction = false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, expectedMagazine, capture.Rounds,
                UxrFirearmReadinessOperation.EmptyRestAcknowledged));
        }
        public bool TryCancelReadinessCycle(int triggerIndex, uint expectedRevision)
        {
            var state = GetReadinessState(triggerIndex);
            if (!CanOwnReadiness(triggerIndex) || state?.ReadinessInitialized != true || state.Revision != expectedRevision) return false;
            state.ChamberCyclePending = false; // ActionOpen не лжёт о физическом закрытии.
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.Cancel));
        }
        private void ReadinessMagazineChanged(int triggerIndex)
        {
            if (!CanOwnReadiness(triggerIndex)) return;
            var state = GetReadinessState(triggerIndex);
            if (state?.ReadinessInitialized != true) return;
            state.ChamberCyclePending = false;
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.MagazineChanged));
        }
        public bool TryReconcileReadiness(int triggerIndex, uint expectedRevision)
        {
            var state = GetReadinessState(triggerIndex);
            if (!CanOwnReadiness(triggerIndex) || state?.ReadinessInitialized != true || state.Revision != expectedRevision) return false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, GetCurrentReadinessMagazine(triggerIndex),
                GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.Reconcile));
        }

        public bool TryRefillAndPrepareForAutomation(int triggerIndex)
        {
            if (!CanWriteReadiness(triggerIndex) || CanPrepareReadinessForAutomation?.Invoke(triggerIndex) != true) return false;
            var state = GetReadinessState(triggerIndex);
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            if (state?.ReadinessInitialized != true || mag == null || state.ActionOpen || state.ChamberCyclePending) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (!ValidateChamberCompletion(triggerIndex, state.Revision, mag) ||
                !IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            int rounds = capture.Rounds;
            if (state.ChamberRound) return false;
            if (rounds == 0) rounds = capture.Capacity;
            if (rounds == 0) return false;
            state.ChamberRound = true; state.PostShotEmptyAction = false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, rounds - 1, UxrFirearmReadinessOperation.Automation));
        }
        // Единственный mutation sink. Replay разрешён только SDK state-sync scope, direct чужой вызов запрещён.
        [Preserve]
        private bool ApplyReadinessCommit(UxrFirearmReadinessCommit commit)
        {
            if (commit == null || commit.Operation == UxrFirearmReadinessOperation.Shot || !ValidateReadinessCommit(commit)) return false;
            bool endAttempted = false;
            bool extractedRound = commit.Operation == UxrFirearmReadinessOperation.Extract && _runtimeTriggers[commit.TriggerIndex].Readiness?.ChamberRound == true;
            Exception failure = null;
            _readinessCommitting.Add(commit.TriggerIndex);
            BeginSync();
            try
            {
                failure = WriteReadinessCommit(commit);
                endAttempted = true; EndSyncMethod(new object[] { commit });
                if (extractedRound) ChamberRoundExtracted?.Invoke(commit.TriggerIndex, commit.StateAfter.CycleSequence);
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(commit.TriggerIndex); }
            if (failure != null)
            {
                _readinessFaulted.Add(commit.TriggerIndex);
                ReadinessFaulted?.Invoke(commit.TriggerIndex, UxrFirearmShotEmissionOutcome.NotEmitted, failure);
                return false;
            }
            return true;
        }
        private bool ValidateReadinessCommit(UxrFirearmReadinessCommit commit)
        {
            if (commit == null || commit.StateAfter == null ||
                (int)commit.Operation < (int)UxrFirearmReadinessOperation.Initialize ||
                (int)commit.Operation > (int)UxrFirearmReadinessOperation.EmptyRestAcknowledged || !UsesReadinessLedger(commit.TriggerIndex) ||
                !_runtimeTriggers.TryGetValue(commit.TriggerIndex, out var runtime) || _readinessCommitting.Contains(commit.TriggerIndex) ||
                (!IsReadinessReplay && !(commit.Operation == UxrFirearmReadinessOperation.Cancel ||
                    commit.Operation == UxrFirearmReadinessOperation.MagazineChanged || commit.Operation == UxrFirearmReadinessOperation.Reconcile
                        ? CanOwnReadiness(commit.TriggerIndex) : CanWriteReadiness(commit.TriggerIndex)))) return false;
            uint revision = runtime.Readiness?.Revision ?? 0;
            if (revision == uint.MaxValue || commit.ExpectedRevision != revision || commit.NextRevision != revision + 1 ||
                commit.StateAfter.Revision != commit.NextRevision || !commit.StateAfter.ReadinessInitialized || commit.MagazineRoundsAfter < 0) return false;
            if (!ValidatePostShotEmptyActionCommit(commit, runtime.Readiness)) return false;
            if (commit.Operation == UxrFirearmReadinessOperation.EmptyRestAcknowledged && !ValidateEmptyRestCommit(commit, runtime.Readiness)) return false;
            if (commit.Operation == UxrFirearmReadinessOperation.CloseOnly && !ValidateCloseOnlyCommit(commit, runtime.Readiness)) return false;
            var mag = commit.ReferencedMagazine;
            if (commit.ReferencedMagazineIdentity != Guid.Empty && (mag == null || mag.UniqueId != commit.ReferencedMagazineIdentity)) return false;
            if (mag == null) return commit.ReferencedMagazineIdentity == Guid.Empty && commit.MagazineRoundsAfter == 0 && commit.StateAfter.CurrentMagazine == null;
            var ammo = mag.GetComponent<UxrFirearmMag>();
            return mag.UniqueId != Guid.Empty && commit.ReferencedMagazineIdentity == mag.UniqueId && ammo != null &&
                   commit.MagazineRoundsAfter <= ammo.Capacity && commit.StateAfter.CurrentMagazine == mag;
        }

        private bool ValidatePostShotEmptyActionCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            var after = commit.StateAfter;
            if (after.PostShotEmptyAction && (!after.ReadinessInitialized || after.ChamberRound ||
                after.ActionOpen || after.ChamberCyclePending || after.ShotSequence == 0)) return false;
            switch (commit.Operation)
            {
                case UxrFirearmReadinessOperation.Shot:
                    return after.PostShotEmptyAction == (!after.ChamberRound && commit.MagazineRoundsAfter == 0);
                case UxrFirearmReadinessOperation.Cancel:
                case UxrFirearmReadinessOperation.MagazineChanged:
                case UxrFirearmReadinessOperation.Reconcile:
                case UxrFirearmReadinessOperation.CloseOnly:
                    return after.PostShotEmptyAction == (before?.PostShotEmptyAction == true);
                default:
                    return !after.PostShotEmptyAction;
            }
        }
        private bool ValidateEmptyRestCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            if (before?.ReadinessInitialized != true || !before.PostShotEmptyAction || before.ChamberRound ||
                before.ActionOpen || before.ChamberCyclePending || commit.ReferencedMagazine != before.CurrentMagazine) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            expected.PostShotEmptyAction = false; expected.Revision = commit.NextRevision;
            var ammo = commit.ReferencedMagazine != null ? commit.ReferencedMagazine.GetComponent<UxrFirearmMag>() : null;
            return commit.StateAfter.Equals(expected) && commit.MagazineRoundsAfter == (ammo != null ? ammo.Rounds : 0);
        }
        private bool ValidateShotCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            if (before?.ReadinessInitialized != true || !before.ChamberRound || before.ActionOpen ||
                before.ChamberCyclePending || before.ShotSequence == uint.MaxValue ||
                commit.ReferencedMagazine != before.CurrentMagazine ||
                (int)commit.EmissionOutcome < (int)UxrFirearmShotEmissionOutcome.NotEmitted ||
                (int)commit.EmissionOutcome > (int)UxrFirearmShotEmissionOutcome.Indeterminate) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            var ammo = commit.ReferencedMagazine != null ? commit.ReferencedMagazine.GetComponent<UxrFirearmMag>() : null;
            int rounds = ammo != null ? ammo.Rounds : 0;
            expected.ChamberRound = false; expected.ShotSequence++; expected.Revision = commit.NextRevision;
            if (_triggers[commit.TriggerIndex].CycleType != UxrShotCycle.ManualReload && rounds > 0)
            { rounds--; expected.ChamberRound = true; }
            expected.PostShotEmptyAction = !expected.ChamberRound && rounds == 0;
            return commit.MagazineRoundsAfter == rounds && commit.StateAfter.Equals(expected);
        }
        private bool ValidateCloseOnlyCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            if (before?.ReadinessInitialized != true || !before.ActionOpen || before.ChamberCyclePending ||
                commit.ReferencedMagazine != before.CurrentMagazine) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            expected.ActionOpen = false; expected.Revision = commit.NextRevision;
            if (!commit.StateAfter.Equals(expected)) return false;
            // CloseOnly не является ammo reconciliation, включая non-author replay.
            var ammo = commit.ReferencedMagazine != null ? commit.ReferencedMagazine.GetComponent<UxrFirearmMag>() : null;
            return commit.MagazineRoundsAfter == (ammo != null ? ammo.Rounds : 0);
        }
        private Exception WriteReadinessCommit(UxrFirearmReadinessCommit commit)
        {
            var runtime = _runtimeTriggers[commit.TriggerIndex];
            runtime.Readiness = (UxrFirearmReadinessState)commit.StateAfter.Clone();
            runtime.HasReloaded = runtime.Readiness.ChamberRound && !runtime.Readiness.ChamberCyclePending && !runtime.Readiness.ActionOpen;
            // Rounds setter записывает значение ДО RoundsChanged: подписчик видит согласованные M/C.
            // Отказ уведомления не откатывает расход/revision и не инициирует повтор.
            try { if (commit.ReferencedMagazine != null) commit.ReferencedMagazine.GetComponent<UxrFirearmMag>().Rounds = commit.MagazineRoundsAfter; }
            catch (Exception exception) { return exception; }
            return null;
        }
        private bool TryShootReadinessRound(int triggerIndex)
        {
            if (!CanWriteReadiness(triggerIndex)) return false;
            RefreshPhysicalActionState(triggerIndex);
            if (!IsReadyToFire(triggerIndex)) return false;
            var runtime = _runtimeTriggers[triggerIndex];
            if (runtime.LastShotTimer > 0f || _weaponSource == null) return false;
            var trigger = _triggers[triggerIndex]; int shotIndex = trigger.ProjectileShotIndex;
            if (shotIndex < 0 || shotIndex >= _weaponSource.ShotTypes.Count || _weaponSource.ShotTypes[shotIndex].ShotSource == null ||
                _weaponSource.ShotTypes[shotIndex].ProjectilePrefab == null) return false;
            var state = GetReadinessState(triggerIndex);
            if (state.ShotSequence == uint.MaxValue) return false;
            state.ShotSequence++; state.ChamberRound = false;
            int rounds = GetMagazineRounds(triggerIndex);
            if (trigger.CycleType != UxrShotCycle.ManualReload && rounds > 0) { rounds--; state.ChamberRound = true; }
            // Атомарный marker consumption: Source outcome не меняет его после публикации.
            state.PostShotEmptyAction = !state.ChamberRound && rounds == 0;
            var commit = CreateReadinessCommit(triggerIndex, state, GetCurrentReadinessMagazine(triggerIndex), rounds, UxrFirearmReadinessOperation.Shot);
            var source = _weaponSource.ShotTypes[shotIndex].ShotSource;
            commit.SourcePosition = source.position;
            commit.SourceOrientation = ShotOrientationModifier != null ? ShotOrientationModifier(triggerIndex, source.rotation) : source.rotation;
            commit.EmissionOutcome = UxrFirearmShotEmissionOutcome.Emitted;
            return CommitShotSynced(commit);
        }
        [Preserve]
        private bool CommitShotSynced(UxrFirearmReadinessCommit commit)
        {
            if (commit?.Operation != UxrFirearmReadinessOperation.Shot || !ValidateReadinessCommit(commit)) return false;
            var runtime = _runtimeTriggers[commit.TriggerIndex];
            if (!ValidateShotCommit(commit, runtime.Readiness)) return false;
            bool replay = IsReadinessReplay, endAttempted = false;
            Exception failure = null; bool success = false;
            _readinessCommitting.Add(commit.TriggerIndex);
            BeginSync();
            try
            {
                failure = WriteReadinessCommit(commit);
                if (failure != null && !replay) commit.EmissionOutcome = UxrFirearmShotEmissionOutcome.NotEmitted;
                var trigger = _triggers[commit.TriggerIndex];
                runtime.LastShotTimer = trigger.MaxShotFrequency > 0 ? 1f / trigger.MaxShotFrequency : -1f;
                if (commit.EmissionOutcome == UxrFirearmShotEmissionOutcome.Emitted)
                {
                    _shootingLocally = !replay;
                    try
                    {
                        UxrFirearmShotEmissionOutcome outcome;
                        Exception sourceFailure;
                        success = _weaponSource.TryShootWithOutcome(trigger.ProjectileShotIndex, commit.SourcePosition, commit.SourceOrientation, out outcome, out sourceFailure);
                        // Ledger notification failure уже означает committed failure. Source outcome
                        // на receiving Emitted replay не может стереть его или инициировать retry.
                        if (sourceFailure != null)
                            failure = failure == null ? sourceFailure : new AggregateException(failure, sourceFailure);
                        if (!replay) commit.EmissionOutcome = outcome;
                    }
                    finally { _shootingLocally = false; }
                }
                if (!replay && success)
                {
                    runtime.RecoilTimer = trigger.RecoilDurationSeconds;
                    try { trigger.ShotAudio?.Play(commit.SourcePosition); OnProjectileShot(commit.TriggerIndex); }
                    catch (Exception exception) { failure = exception; success = false; }
                }
                endAttempted = true; EndSyncMethod(new object[] { commit });
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(commit.TriggerIndex); }
            if (failure != null || commit.EmissionOutcome != UxrFirearmShotEmissionOutcome.Emitted)
            {
                _readinessFaulted.Add(commit.TriggerIndex);
                ReadinessFaulted?.Invoke(commit.TriggerIndex, commit.EmissionOutcome, failure);
                return false;
            }
            return success;
        }
    }
}

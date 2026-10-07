using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Core.Settings;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Scripting;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrFirearmWeapon
    {
        // VR Battlegrounds patch: opt-in ledger. Политика/physical evidence остаются вне SDK.
        //
        // VR Battlegrounds patch 53 (этап C2, Docs/tasks/weapon-ledger-single-source.md):
        // - учёт не хранит магазин: магазин — всегда гнездо (GetCurrentReadinessMagazine);
        // - Pending действителен, только пока в гнезде магазин, с которым начат цикл (IsCyclePendingFor);
        // - фиксации нет ни на событиях гнезда, ни в SyncAmmoLeft: Reconcile и MagazineChanged удалены;
        // - каждая операция объявляет точную дельту M; replay сверяет её и при расхождении берёт значение
        //   автора (ReadinessReplayDiverged), при форке ревизии фиксацию не применяет — сервер публикует поправку;
        // - команда учёта внутри чужого BeginSync отклоняется (иначе не ушла бы в сеть — класс Б);
        // - сбой уведомлений после записи не блокирует стрельбу (В-Л5): только ReadinessFaulted для лога.
        private readonly HashSet<int> _readinessEnabled = new HashSet<int>();
        public Func<int, bool> CanAuthorReadinessAction { get; set; }
        public Action<int> RefreshPhysicalActionState { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidateChamberCompletion { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidatePhysicalActionClosed { get; set; }
        public Func<int, uint, UxrGrabbableObject, bool> ValidatePostShotEmptyActionRest { get; set; }
        public Func<int, UxrGrabber, UxrFirearmTriggerDecision> EvaluateLocalTriggerAttempt { get; set; }
        public Func<int, bool> CanPrepareReadinessForAutomation { get; set; }
        /// <summary>Patch 53: принимает ли эта машина поправку учёта из сети. Порт игры: сервер — нет, клиент — да.</summary>
        public Func<int, bool> CanAcceptLedgerCorrection { get; set; }
        public event Action<int, uint> ChamberRoundExtracted;
        /// <summary>
        /// Patch 53 (В-Л5): сбой уведомления после записи (RoundsChanged, Source). Учёт уже записан и разослан,
        /// стрельба не блокируется; подписчик пишет ошибку в лог.
        /// </summary>
        public event Action<int, UxrFirearmShotEmissionOutcome, Exception> ReadinessFaulted;
        /// <summary>Patch 53: replay расошёлся с учётом получателя или применена поправка (п. 3a).</summary>
        public event Action<UxrFirearmReplayDivergence> ReadinessReplayDiverged;
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
            MarkLedgerStore(_triggers[triggerIndex].AmmunitionMagAnchor.CurrentPlacedObject);
            return true;
        }
        public UxrFirearmReadinessState GetReadinessState(int triggerIndex) =>
            _runtimeTriggers.TryGetValue(triggerIndex, out RuntimeTriggerInfo runtime) && runtime.Readiness != null
                ? (UxrFirearmReadinessState)runtime.Readiness.Clone() : null;
        public int GetMagazineRounds(int triggerIndex) => GetCurrentReadinessMagazine(triggerIndex)?.GetComponent<UxrFirearmMag>().Rounds ?? 0;
        public bool HasChamberRound(int triggerIndex) => UsesReadinessLedger(triggerIndex) && GetReadinessState(triggerIndex)?.ChamberRound == true;
        public int GetTotalAmmoLeft(int triggerIndex) => UsesReadinessLedger(triggerIndex)
            ? GetMagazineRounds(triggerIndex) + (HasChamberRound(triggerIndex) ? 1 : 0) : GetAmmoLeft(triggerIndex);

        /// <summary>Patch 53: цикл действителен, только пока в гнезде тот магазин, с которым он начат. Единое правило.</summary>
        public static bool IsCyclePendingFor(UxrFirearmReadinessState state, Guid magazineIdentity) =>
            state != null && state.ChamberCyclePending && state.CycleMagazineIdentity == magazineIdentity;
        /// <summary>Patch 53: действительный Pending спуска (магазин — гнездо).</summary>
        public bool IsReadinessCyclePending(int triggerIndex) =>
            _runtimeTriggers.TryGetValue(triggerIndex, out RuntimeTriggerInfo runtime) &&
            IsCyclePendingFor(runtime.Readiness, CurrentReadinessMagazineIdentity(triggerIndex));
        private Guid CurrentReadinessMagazineIdentity(int triggerIndex)
        {
            var magazine = GetCurrentReadinessMagazine(triggerIndex);
            return magazine != null ? magazine.UniqueId : Guid.Empty;
        }
        // Недействительный Pending (магазин сменился) — для команд его нет: следующая фиксация его снимает.
        private static UxrFirearmReadinessState WithValidPending(UxrFirearmReadinessState state, Guid magazineIdentity)
        {
            if (state == null) return null;
            var copy = (UxrFirearmReadinessState)state.Clone();
            if (!IsCyclePendingFor(copy, magazineIdentity)) { copy.ChamberCyclePending = false; copy.CycleMagazineIdentity = Guid.Empty; }
            return copy;
        }
        private static void MarkLedgerStore(UxrGrabbableObject magazine)
        {
            var ammo = magazine != null ? magazine.GetComponent<UxrFirearmMag>() : null;
            if (ammo != null) ammo.MarkLedgerStore();
        }

        public bool IsReadyToFire(int triggerIndex)
        {
            if (!UsesReadinessLedger(triggerIndex)) return IsLoaded(triggerIndex);
            if (IsAmmoAdmissionPending(triggerIndex) || _fixedAmmoSnapshotReading) return false;
            if (HasFixedAmmoBinding(triggerIndex) && GetFixedAmmoStore(triggerIndex) == null) return false;
            var state = GetReadinessState(triggerIndex);
            return state != null && state.ReadinessInitialized && state.ChamberRound &&
                   !IsReadinessCyclePending(triggerIndex) && !state.ActionOpen;
        }
        public UxrFirearmTriggerDecision QueryReadinessDecision(int triggerIndex)
        {
            var state = GetReadinessState(triggerIndex);
            if (!UsesReadinessLedger(triggerIndex) || state == null || !state.ReadinessInitialized ||
                (HasFixedAmmoBinding(triggerIndex) && GetFixedAmmoStore(triggerIndex) == null) ||
                !CanUse || _fixedAmmoSnapshotReading || IsAmmoAdmissionPending(triggerIndex) || state.ActionOpen || IsReadinessCyclePending(triggerIndex))
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
            !_fixedAmmoSnapshotReading && !IsAmmoAdmissionPending(triggerIndex) && !_readinessCommitting.Contains(triggerIndex) && CanAuthorReadinessAction?.Invoke(triggerIndex) == true;
        private bool CanWriteReadiness(int triggerIndex) => CanOwnReadiness(triggerIndex) &&
            (!HasFixedAmmoBinding(triggerIndex) || GetFixedAmmoStore(triggerIndex) != null) &&
            isActiveAndEnabled && CanUse && TryGetTriggerMagazineAnchor(triggerIndex, out var anchor) && anchor.isActiveAndEnabled;
        // Состояние для команды автора: действительный Pending относительно гнезда.
        private UxrFirearmReadinessState GetAuthorState(int triggerIndex) =>
            WithValidPending(_runtimeTriggers.TryGetValue(triggerIndex, out RuntimeTriggerInfo runtime) ? runtime.Readiness : null,
                CurrentReadinessMagazineIdentity(triggerIndex));
        private bool TryGetWritableState(int triggerIndex, uint revision, out UxrFirearmReadinessState state)
        {
            state = GetAuthorState(triggerIndex);
            return CanWriteReadiness(triggerIndex) && state != null && state.ReadinessInitialized && state.Revision == revision;
        }
        private UxrFirearmReadinessCommit CreateReadinessCommit(int triggerIndex, UxrFirearmReadinessState after,
            UxrGrabbableObject magazine, int rounds, UxrFirearmReadinessOperation operation)
        {
            uint previous = _runtimeTriggers[triggerIndex].Readiness?.Revision ?? 0;
            if (previous == uint.MaxValue) throw new InvalidOperationException("Readiness revision exhausted");
            after.Revision = previous + 1;
            if (!after.ChamberCyclePending) after.CycleMagazineIdentity = Guid.Empty;
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
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            state.ActionOpen = true; state.ChamberCyclePending = true; state.CycleSequence = cycleSequence;
            state.CycleMagazineIdentity = mag != null ? mag.UniqueId : Guid.Empty;
            state.PostShotEmptyAction = false;
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
            // Действительный Pending уже означает «цикл начат с магазином, который сейчас в гнезде».
            if (!TryGetWritableState(triggerIndex, expectedRevision, out var state) || !state.ChamberCyclePending ||
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine) return false;
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
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine || ValidatePhysicalActionClosed == null) return false;
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
                GetCurrentReadinessMagazine(triggerIndex) != expectedMagazine || ValidatePostShotEmptyActionRest == null) return false;
            var capture = new PhysicalValidationCapture(this, triggerIndex);
            if (!ValidatePostShotEmptyActionRest(triggerIndex, expectedRevision, expectedMagazine) ||
                !IsPhysicalValidationCurrent(triggerIndex, capture)) return false;
            state.PostShotEmptyAction = false;
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, expectedMagazine, capture.Rounds,
                UxrFirearmReadinessOperation.EmptyRestAcknowledged));
        }
        public bool TryCancelReadinessCycle(int triggerIndex, uint expectedRevision)
        {
            var state = GetAuthorState(triggerIndex);
            if (!CanOwnReadiness(triggerIndex) || state?.ReadinessInitialized != true || state.Revision != expectedRevision) return false;
            state.ChamberCyclePending = false; // ActionOpen не лжёт о физическом закрытии.
            var mag = GetCurrentReadinessMagazine(triggerIndex);
            return ApplyReadinessCommit(CreateReadinessCommit(triggerIndex, state, mag, GetMagazineRounds(triggerIndex), UxrFirearmReadinessOperation.Cancel));
        }

        public bool TryRefillAndPrepareForAutomation(int triggerIndex)
        {
            if (!CanWriteReadiness(triggerIndex) || CanPrepareReadinessForAutomation?.Invoke(triggerIndex) != true) return false;
            var state = GetAuthorState(triggerIndex);
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

        // Класс Б: команда внутри чужого BeginSync не поднимет ComponentStateChanged (только верхний уровень)
        // и не уйдёт в сеть, а replay корня её не пересчитает (producer'ы защищены от replay). Отказ — в одной точке.
        // IgnoreNestingCheck не подходит: вложенная фиксация ушла бы в сеть раньше своего корня.
        private bool IsNestedLedgerCommand(string sink)
        {
            if (IsReadinessReplay || UxrStateSyncImplementer.SyncCallDepth == 0) return false;
            if (UxrGlobalSettings.Instance.LogLevelWeapons >= UxrLogLevel.Warnings)
                Debug.LogWarning($"{UxrConstants.WeaponsModule} {name}.{sink}: команда учёта внутри чужой синхронизации " +
                                 $"(SyncCallDepth={UxrStateSyncImplementer.SyncCallDepth}) отклонена — она не ушла бы в сеть.");
            return true;
        }

        // Единственный mutation sink. Replay разрешён только SDK state-sync scope, direct чужой вызов запрещён.
        [Preserve]
        private bool ApplyReadinessCommit(UxrFirearmReadinessCommit commit)
        {
            if (commit == null || commit.Operation == UxrFirearmReadinessOperation.Shot ||
                commit.Operation == UxrFirearmReadinessOperation.AmmoAdmission || IsNestedLedgerCommand(nameof(ApplyReadinessCommit))) return false;
            if (!ValidateReadinessCommit(commit, out var divergence)) { RaiseReplayDivergence(divergence); return false; }
            bool endAttempted = false;
            bool extractedRound = commit.Operation == UxrFirearmReadinessOperation.Extract && _runtimeTriggers[commit.TriggerIndex].Readiness?.ChamberRound == true;
            bool written = false;
            Exception failure = null;
            _readinessCommitting.Add(commit.TriggerIndex);
            BeginSync();
            try
            {
                failure = WriteReadinessCommit(commit);
                written = true;
                endAttempted = true; EndSyncMethod(new object[] { commit });
                if (extractedRound) ChamberRoundExtracted?.Invoke(commit.TriggerIndex, commit.StateAfter.CycleSequence);
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(commit.TriggerIndex); }
            RaiseReplayDivergence(divergence);
            if (failure != null) ReportReadinessFault(commit.TriggerIndex, UxrFirearmShotEmissionOutcome.NotEmitted, failure);
            return written;
        }

        /// <summary>
        /// Проверка фиксации (автор и replay). Магазин автора — магазин в гнезде; на replay — identity, ёмкость и
        /// дельта M без членства в гнезде (replay не зависит от порядка событий grab-менеджера и учёта).
        /// <paramref name="divergence"/>: RevisionFork при отказе replay из-за ревизии; DeltaMismatch при принятой
        /// фиксации, у которой M получателя не сходится с дельтой (применяется значение автора).
        /// </summary>
        private bool ValidateReadinessCommit(UxrFirearmReadinessCommit commit, out UxrFirearmReplayDivergence divergence)
        {
            divergence = null;
            bool replay = IsReadinessReplay;
#pragma warning disable 0618
            if (commit == null || commit.StateAfter == null ||
                (int)commit.Operation < (int)UxrFirearmReadinessOperation.Initialize ||
                (int)commit.Operation > (int)UxrFirearmReadinessOperation.AmmoAdmission ||
                commit.Operation == UxrFirearmReadinessOperation.MagazineChanged || commit.Operation == UxrFirearmReadinessOperation.Reconcile ||
                !UsesReadinessLedger(commit.TriggerIndex) ||
                !_runtimeTriggers.TryGetValue(commit.TriggerIndex, out var runtime) || _readinessCommitting.Contains(commit.TriggerIndex) ||
                (!replay && !(commit.Operation == UxrFirearmReadinessOperation.AmmoAdmission ? CanCommitAmmoAdmission(commit.TriggerIndex) :
                    commit.Operation == UxrFirearmReadinessOperation.Cancel ? CanOwnReadiness(commit.TriggerIndex) : CanWriteReadiness(commit.TriggerIndex)))) return false;
#pragma warning restore 0618
            var after = commit.StateAfter;
            if (commit.ExpectedRevision == uint.MaxValue || commit.NextRevision != commit.ExpectedRevision + 1 ||
                after.Revision != commit.NextRevision || !after.ReadinessInitialized || commit.MagazineRoundsAfter < 0) return false;
            // Pending после фиксации принадлежит магазину фиксации; без Pending identity цикла пуста.
            if (after.ChamberCyclePending ? after.CycleMagazineIdentity != commit.ReferencedMagazineIdentity : after.CycleMagazineIdentity != Guid.Empty) return false;
            var mag = commit.ReferencedMagazine;
            if (commit.ReferencedMagazineIdentity != Guid.Empty && (mag == null || mag.UniqueId != commit.ReferencedMagazineIdentity)) return false;
            UxrFirearmMag ammo = null;
            if (mag == null)
            {
                if (commit.ReferencedMagazineIdentity != Guid.Empty || commit.MagazineRoundsAfter != 0) return false;
            }
            else
            {
                ammo = mag.GetComponent<UxrFirearmMag>();
                if (mag.UniqueId == Guid.Empty || ammo == null || !ammo.IsFixedStoreBindingValid(this, commit.TriggerIndex) ||
                    commit.MagazineRoundsAfter > ammo.Capacity ||
                    (ammo.IsFixedAmmoStore && commit.MagazineRoundsAfter + (after.ChamberRound ? 1 : 0) > ammo.Capacity)) return false;
            }
            if (!replay && mag != GetCurrentReadinessMagazine(commit.TriggerIndex)) return false;
            uint revision = runtime.Readiness?.Revision ?? 0;
            int receiverRounds = ammo != null ? ammo.Rounds : 0;
            if (commit.ExpectedRevision != revision)
            {
                if (replay) divergence = CreateDivergence(UxrFirearmReplayDivergenceKind.RevisionFork, commit, revision, receiverRounds, -1);
                return false;
            }
            var before = WithValidPending(runtime.Readiness, commit.ReferencedMagazineIdentity);
            if (!ValidatePostShotEmptyActionCommit(commit, before)) return false;
            switch (commit.Operation)
            {
                case UxrFirearmReadinessOperation.EmptyRestAcknowledged: if (!ValidateEmptyRestCommit(commit, before)) return false; break;
                case UxrFirearmReadinessOperation.CloseOnly: if (!ValidateCloseOnlyCommit(commit, before)) return false; break;
                case UxrFirearmReadinessOperation.Shot:
                    // Автор — фактический M; replay — M до выстрела, объявленный автором (расхождение ловит дельта ниже).
                    if (!ValidateShotCommit(commit, before, replay ? commit.MagazineRoundsAfter + (after.ChamberRound ? 1 : 0) : receiverRounds)) return false;
                    break;
                case UxrFirearmReadinessOperation.AmmoAdmission:
                    if (before?.ReadinessInitialized != true || ammo == null || !ammo.IsFixedAmmoStore) return false;
                    var expected = (UxrFirearmReadinessState)before.Clone(); expected.Revision = commit.NextRevision;
                    if (!expected.Equals(after)) return false;
                    break;
            }
            int expectedRounds = ExpectedRoundsAfter(commit, before, receiverRounds, ammo != null ? ammo.Capacity : 0);
            if (expectedRounds != commit.MagazineRoundsAfter)
            {
                if (!replay) return false; // у автора дельта точна по построению; иначе — ошибка самой команды.
                divergence = CreateDivergence(UxrFirearmReplayDivergenceKind.DeltaMismatch, commit, revision, receiverRounds, expectedRounds);
            }
            return true;
        }

        /// <summary>Точная дельта M каждой операции: M после по M до и объявленному состоянию после.</summary>
        private static int ExpectedRoundsAfter(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before, int roundsBefore, int capacity)
        {
            bool chamberAfter = commit.StateAfter.ChamberRound;
            switch (commit.Operation)
            {
                case UxrFirearmReadinessOperation.Initialize: return roundsBefore - (chamberAfter ? 1 : 0);
                case UxrFirearmReadinessOperation.Complete: return roundsBefore - (chamberAfter && before?.ChamberRound != true ? 1 : 0);
                case UxrFirearmReadinessOperation.Shot: return roundsBefore - (chamberAfter ? 1 : 0);
                case UxrFirearmReadinessOperation.Automation: return (roundsBefore == 0 ? capacity : roundsBefore) - 1;
                case UxrFirearmReadinessOperation.AmmoAdmission: return roundsBefore + 1;
                default: return roundsBefore; // BeginAction, Extract, Cancel, CloseOnly, EmptyRestAcknowledged
            }
        }

        private UxrFirearmReplayDivergence CreateDivergence(UxrFirearmReplayDivergenceKind kind, UxrFirearmReadinessCommit commit,
            uint localRevision, int receiverRounds, int expectedRounds) => new UxrFirearmReplayDivergence
        {
            Kind = kind, TriggerIndex = commit.TriggerIndex, Operation = commit.Operation,
            Magazine = commit.ReferencedMagazine, MagazineIdentity = commit.ReferencedMagazineIdentity,
            ExpectedRevision = commit.ExpectedRevision, NextRevision = commit.NextRevision, LocalRevision = localRevision,
            ReceiverRoundsBefore = receiverRounds, ReceiverExpectedRoundsAfter = expectedRounds,
            AuthorRoundsAfter = commit.MagazineRoundsAfter, Replay = IsReadinessReplay
        };

        private void RaiseReplayDivergence(UxrFirearmReplayDivergence divergence)
        {
            if (divergence == null) return;
            try { ReadinessReplayDiverged?.Invoke(divergence); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void ReportReadinessFault(int triggerIndex, UxrFirearmShotEmissionOutcome outcome, Exception failure)
        {
            try { ReadinessFaulted?.Invoke(triggerIndex, outcome, failure); }
            catch (Exception exception) { Debug.LogException(exception, this); }
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
                case UxrFirearmReadinessOperation.CloseOnly:
                case UxrFirearmReadinessOperation.AmmoAdmission:
                    return after.PostShotEmptyAction == (before?.PostShotEmptyAction == true);
                default:
                    return !after.PostShotEmptyAction;
            }
        }
        private bool ValidateEmptyRestCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            if (before?.ReadinessInitialized != true || !before.PostShotEmptyAction || before.ChamberRound ||
                before.ActionOpen || before.ChamberCyclePending) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            expected.PostShotEmptyAction = false; expected.Revision = commit.NextRevision;
            return commit.StateAfter.Equals(expected);
        }
        private bool ValidateShotCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before, int roundsBefore)
        {
            if (!ValidateShotDescriptor(_triggers[commit.TriggerIndex].ProjectileShotIndex, commit.SourcePosition, commit.SourceOrientation) ||
                commit.AdditionalShots != null && commit.AdditionalShots.Length > MaximumAdditionalShots) return false;
            if (commit.AdditionalShots != null)
                foreach (var shot in commit.AdditionalShots)
                    if (shot == null || !ValidateShotDescriptor(shot.ShotIndex, shot.Position, shot.Orientation) ||
                        (int)shot.Outcome < 0 || (int)shot.Outcome > (int)UxrFirearmShotEmissionOutcome.Indeterminate) return false;
            if (before?.ReadinessInitialized != true || !before.ChamberRound || before.ActionOpen ||
                before.ChamberCyclePending || before.ShotSequence == uint.MaxValue ||
                (int)commit.EmissionOutcome < (int)UxrFirearmShotEmissionOutcome.NotEmitted ||
                (int)commit.EmissionOutcome > (int)UxrFirearmShotEmissionOutcome.Indeterminate) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            int rounds = roundsBefore;
            expected.ChamberRound = false; expected.ShotSequence++; expected.Revision = commit.NextRevision;
            if (_triggers[commit.TriggerIndex].CycleType != UxrShotCycle.ManualReload && rounds > 0)
            { rounds--; expected.ChamberRound = true; }
            expected.PostShotEmptyAction = !expected.ChamberRound && rounds == 0;
            return commit.StateAfter.Equals(expected);
        }
        private bool ValidateCloseOnlyCommit(UxrFirearmReadinessCommit commit, UxrFirearmReadinessState before)
        {
            if (before?.ReadinessInitialized != true || !before.ActionOpen || before.ChamberCyclePending) return false;
            var expected = (UxrFirearmReadinessState)before.Clone();
            expected.ActionOpen = false; expected.Revision = commit.NextRevision;
            return commit.StateAfter.Equals(expected);
        }
        // О7: всё проверено до записи; учёт и M пишутся, бросать может только уведомление после записи.
        private Exception WriteReadinessCommit(UxrFirearmReadinessCommit commit)
        {
            var runtime = _runtimeTriggers[commit.TriggerIndex];
            var ammo = commit.ReferencedMagazine != null ? commit.ReferencedMagazine.GetComponent<UxrFirearmMag>() : null;
            runtime.Readiness = (UxrFirearmReadinessState)commit.StateAfter.Clone();
            runtime.HasReloaded = runtime.Readiness.ChamberRound && !runtime.Readiness.ChamberCyclePending && !runtime.Readiness.ActionOpen;
            if (ammo != null) ammo.WriteLedgerRounds(this, commit.TriggerIndex, commit.MagazineRoundsAfter, false);
            // Уведомление после записи: подписчик видит согласованные M/C. Отказ не откатывает и не блокирует.
            try { if (ammo != null) ammo.NotifyRoundsChanged(); }
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
            var state = GetAuthorState(triggerIndex);
            if (state.ShotSequence == uint.MaxValue) return false;
            state.ShotSequence++; state.ChamberRound = false;
            int rounds = GetMagazineRounds(triggerIndex);
            if (trigger.CycleType != UxrShotCycle.ManualReload && rounds > 0) { rounds--; state.ChamberRound = true; }
            // Атомарный marker consumption: Source outcome не меняет его после публикации.
            state.PostShotEmptyAction = !state.ChamberRound && rounds == 0;
            var commit = CreateReadinessCommit(triggerIndex, state, GetCurrentReadinessMagazine(triggerIndex), rounds, UxrFirearmReadinessOperation.Shot);
            var source = _weaponSource.ShotTypes[shotIndex].ShotSource;
            commit.SourcePosition = source.position;
            commit.EmissionOutcome = UxrFirearmShotEmissionOutcome.Emitted;
            // Producer — только автор. Guard запрещает синхронную повторную запись из delegate.
            _readinessCommitting.Add(triggerIndex);
            try
            {
                commit.SourceOrientation = ShotOrientationModifier != null ? ShotOrientationModifier(triggerIndex, source.rotation) : source.rotation;
                if (!ValidateShotDescriptor(shotIndex, commit.SourcePosition, commit.SourceOrientation)) return false;
                var plan = AdditionalShotPlan != null ? AdditionalShotPlan(triggerIndex) : null;
                if (plan != null)
                {
                    if (plan.Length > MaximumAdditionalShots) return false;
                    commit.AdditionalShots = new UxrAdditionalShotCommit[plan.Length];
                    for (int i = 0; i < plan.Length; i++)
                    {
                        var shot = plan[i];
                        if (!ValidateShotDescriptor(shot.ShotIndex, shot.Position, shot.Orientation)) return false;
                        commit.AdditionalShots[i] = new UxrAdditionalShotCommit { ShotIndex = shot.ShotIndex,
                            Position = shot.Position, Orientation = shot.Orientation, Outcome = UxrFirearmShotEmissionOutcome.NotEmitted };
                    }
                }
            }
            catch { return false; }
            finally { _readinessCommitting.Remove(triggerIndex); }
            if (!CanWriteReadiness(triggerIndex) || !IsReadyToFire(triggerIndex)) return false;
            return CommitShotSynced(commit);
        }
        public const int MaximumAdditionalShots = 31;
        public Func<int, UxrAdditionalShotPlan[]> AdditionalShotPlan { get; set; }
        private bool ValidateShotDescriptor(int index, Vector3 position, Quaternion orientation)
        {
            if (_weaponSource == null || index < 0 || index >= _weaponSource.ShotTypes.Count ||
                _weaponSource.ShotTypes[index].ShotSource == null || _weaponSource.ShotTypes[index].ProjectilePrefab == null) return false;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) ||
                !Finite(orientation.x) || !Finite(orientation.y) || !Finite(orientation.z) || !Finite(orientation.w)) return false;
            float norm = Quaternion.Dot(orientation, orientation);
            return Mathf.Abs(norm - 1f) <= 0.001f;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        [Preserve]
        private bool CommitShotSynced(UxrFirearmReadinessCommit commit)
        {
            if (commit?.Operation != UxrFirearmReadinessOperation.Shot || IsNestedLedgerCommand(nameof(CommitShotSynced))) return false;
            if (!ValidateReadinessCommit(commit, out var divergence)) { RaiseReplayDivergence(divergence); return false; }
            var runtime = _runtimeTriggers[commit.TriggerIndex];
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
                        // Сбой уведомления учёта уже committed. Source outcome на replay не стирает его и не даёт retry.
                        if (sourceFailure != null)
                            failure = failure == null ? sourceFailure : new AggregateException(failure, sourceFailure);
                        if (!replay) commit.EmissionOutcome = outcome;
                    }
                    finally { _shootingLocally = false; }
                }
                if (commit.AdditionalShots != null)
                {
                    foreach (var shot in commit.AdditionalShots)
                    {
                        // Автор прекращает batch после первого отказа. Replay выпускает только доказанные Emitted.
                        if (!replay && (!success || failure != null)) break;
                        if (replay && shot.Outcome != UxrFirearmShotEmissionOutcome.Emitted) continue;
                        UxrFirearmShotEmissionOutcome outcome;
                        Exception sourceFailure;
                        _shootingLocally = !replay;
                        try { success = _weaponSource.TryShootWithOutcome(shot.ShotIndex, shot.Position, shot.Orientation, out outcome, out sourceFailure); }
                        finally { _shootingLocally = false; }
                        if (!replay) shot.Outcome = outcome;
                        if (sourceFailure != null) failure = failure == null ? sourceFailure : new AggregateException(failure, sourceFailure);
                    }
                }
                if (!replay && success && failure == null)
                {
                    runtime.RecoilTimer = trigger.RecoilDurationSeconds;
                    try { trigger.ShotAudio?.Play(commit.SourcePosition); OnProjectileShot(commit.TriggerIndex); }
                    catch (Exception exception) { failure = exception; success = false; }
                }
                endAttempted = true; EndSyncMethod(new object[] { commit });
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(commit.TriggerIndex); }
            RaiseReplayDivergence(divergence);
            var aggregateOutcome = commit.EmissionOutcome;
            if (commit.AdditionalShots != null)
                foreach (var shot in commit.AdditionalShots)
                    if (shot.Outcome == UxrFirearmShotEmissionOutcome.Indeterminate) aggregateOutcome = UxrFirearmShotEmissionOutcome.Indeterminate;
                    else if (shot.Outcome == UxrFirearmShotEmissionOutcome.NotEmitted && aggregateOutcome == UxrFirearmShotEmissionOutcome.Emitted)
                        aggregateOutcome = UxrFirearmShotEmissionOutcome.NotEmitted;
            if (failure != null || aggregateOutcome != UxrFirearmShotEmissionOutcome.Emitted)
            {
                // В-Л5: расход уже зафиксирован и разослан; следующий выстрел не блокируется.
                ReportReadinessFault(commit.TriggerIndex, aggregateOutcome, failure);
                return false;
            }
            return success;
        }

        /// <summary>
        /// Patch 53 (В-Л6): публикация поправки учёта. Вызывает только серверный обработчик форка (игра), вне
        /// replay и вне чужого BeginSync. Содержание — уже принятое состояние этой машины: учёт спуска и M
        /// перечисленных магазинов; у публикующего ничего не меняется.
        /// </summary>
        public bool TryPublishLedgerCorrection(int triggerIndex, IEnumerable<UxrGrabbableObject> magazines)
        {
            if (!UsesReadinessLedger(triggerIndex) || IsReadinessReplay || _readinessCommitting.Contains(triggerIndex) ||
                IsNestedLedgerCommand(nameof(TryPublishLedgerCorrection))) return false;
            var state = GetReadinessState(triggerIndex);
            if (state?.ReadinessInitialized != true) return false;
            var entries = new List<UxrFirearmMagazineRounds>();
            var seen = new HashSet<UxrGrabbableObject>();
            if (magazines != null)
                foreach (var magazine in magazines)
                {
                    var ammo = magazine != null ? magazine.GetComponent<UxrFirearmMag>() : null;
                    if (ammo == null || !seen.Add(magazine) || magazine.UniqueId == Guid.Empty || !ammo.IsFixedStoreBindingValid(this, triggerIndex)) continue;
                    entries.Add(new UxrFirearmMagazineRounds { Magazine = magazine, Identity = magazine.UniqueId, Rounds = ammo.Rounds });
                }
            return ApplyLedgerCorrection(new UxrFirearmLedgerCorrection { TriggerIndex = triggerIndex, State = state, Magazines = entries.ToArray() });
        }

        [Preserve]
        private bool ApplyLedgerCorrection(UxrFirearmLedgerCorrection correction)
        {
            if (correction?.State == null || !correction.State.ReadinessInitialized || !UsesReadinessLedger(correction.TriggerIndex) ||
                !_runtimeTriggers.TryGetValue(correction.TriggerIndex, out var runtime) || _readinessCommitting.Contains(correction.TriggerIndex)) return false;
            int trigger = correction.TriggerIndex;
            var entries = correction.Magazines ?? Array.Empty<UxrFirearmMagazineRounds>();
            foreach (var entry in entries)
            {
                var ammo = entry?.Magazine != null ? entry.Magazine.GetComponent<UxrFirearmMag>() : null;
                if (ammo == null || entry.Identity == Guid.Empty || entry.Magazine.UniqueId != entry.Identity ||
                    !ammo.IsFixedStoreBindingValid(this, trigger) || entry.Rounds < 0 || entry.Rounds > ammo.Capacity ||
                    (ammo.IsFixedAmmoStore && entry.Rounds + (correction.State.ChamberRound ? 1 : 0) > ammo.Capacity)) return false;
            }
            if (!IsReadinessReplay)
            {
                // Публикация: только то, что уже есть у этой машины.
                if (!correction.State.Equals(runtime.Readiness)) return false;
                foreach (var entry in entries) if (entry.Magazine.GetComponent<UxrFirearmMag>().Rounds != entry.Rounds) return false;
                BeginSync();
                EndSyncMethod(new object[] { correction });
                return true;
            }
            if (CanAcceptLedgerCorrection?.Invoke(trigger) != true) return false;
            uint localRevision = runtime.Readiness?.Revision ?? 0;
            int roundsBefore = entries.Length > 0 ? entries[0].Magazine.GetComponent<UxrFirearmMag>().Rounds : -1;
            Exception failure = null;
            bool endAttempted = false;
            _readinessCommitting.Add(trigger);
            BeginSync();
            try
            {
                runtime.Readiness = (UxrFirearmReadinessState)correction.State.Clone();
                runtime.HasReloaded = runtime.Readiness.ChamberRound && !runtime.Readiness.ChamberCyclePending && !runtime.Readiness.ActionOpen;
                foreach (var entry in entries) entry.Magazine.GetComponent<UxrFirearmMag>().WriteLedgerRounds(this, trigger, entry.Rounds, false);
                foreach (var entry in entries)
                {
                    try { entry.Magazine.GetComponent<UxrFirearmMag>().NotifyRoundsChanged(); }
                    catch (Exception exception) { failure = failure == null ? exception : new AggregateException(failure, exception); }
                }
                endAttempted = true; EndSyncMethod(new object[] { correction });
            }
            catch (Exception exception) { failure = failure == null ? exception : new AggregateException(failure, exception); }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(trigger); }
            RaiseReplayDivergence(new UxrFirearmReplayDivergence
            {
                Kind = UxrFirearmReplayDivergenceKind.CorrectionApplied, TriggerIndex = trigger,
                Magazine = entries.Length > 0 ? entries[0].Magazine : null, MagazineIdentity = entries.Length > 0 ? entries[0].Identity : Guid.Empty,
                NextRevision = correction.State.Revision, LocalRevision = localRevision,
                ReceiverRoundsBefore = roundsBefore, AuthorRoundsAfter = entries.Length > 0 ? entries[0].Rounds : -1, Replay = true
            });
            if (failure != null) ReportReadinessFault(trigger, UxrFirearmShotEmissionOutcome.NotEmitted, failure);
            return true;
        }
    }
}

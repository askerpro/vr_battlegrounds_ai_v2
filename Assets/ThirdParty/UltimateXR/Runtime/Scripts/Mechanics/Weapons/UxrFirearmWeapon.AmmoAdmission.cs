using System;
using System.Collections.Generic;
using UltimateXR.Core.Serialization;
using UltimateXR.Manipulation;
using UnityEngine.Scripting;

namespace UltimateXR.Mechanics.Weapons
{
    public enum UxrAmmoAdmissionOutcome { Rejected, Committed, CommittedWithNotificationFailure }

    // DTO только одного after-value. Единственный живой M остаётся в UxrFirearmMag.
    [Serializable, Preserve]
    // ICloneable обязателен: SerializeStateValue кэширует значение через ObjectExt.DeepCopy, а без Clone()
    // тип с полем-компонентом уходит в BinaryFormatter и падает SerializationException на каждом сохранении.
    public sealed class UxrFixedAmmoSnapshot : IUxrSerializable, ICloneable
    {
        public object Clone() => MemberwiseClone();
        public int TriggerIndex, Rounds;
        public UxrFirearmMag Store;
        public Guid StoreIdentity;
        public int SerializationVersion => 1;
        public void Serialize(IUxrSerializer serializer, int version)
        { serializer.Serialize(ref TriggerIndex); serializer.SerializeUniqueComponent(ref Store); serializer.Serialize(ref StoreIdentity); serializer.Serialize(ref Rounds); }
    }

    [Serializable, Preserve]
    public sealed class UxrAmmoAdmissionCommit : IUxrSerializable
    {
        public UxrFirearmReadinessCommit Ledger;
        public UxrFirearmAmmoUnit Unit;
        public Guid UnitIdentity;
        public ulong RequestToken;
        public int SerializationVersion => 1;
        public void Serialize(IUxrSerializer serializer, int version)
        { serializer.SerializeAnyVar(ref Ledger); serializer.SerializeUniqueComponent(ref Unit); serializer.Serialize(ref UnitIdentity); serializer.Serialize(ref RequestToken); }
    }

    public partial class UxrFirearmWeapon
    {
        // Порты принадлежат единственному game receiver. Это разрешения, не второй ammo writer.
        public Func<int, bool> CanAuthorAmmoAdmission { get; set; }
        public Func<int, UxrFirearmAmmoUnit, bool> ValidateAmmoUnitAdmission { get; set; }
        public event Action<int, ulong, Exception> AmmoAdmissionFaulted;
        private readonly Dictionary<int, ulong> _ammoAdmissionPending = new Dictionary<int, ulong>();
        private bool _fixedAmmoSnapshotReading;
        public bool IsAmmoAdmissionPending(int triggerIndex) => _ammoAdmissionPending.ContainsKey(triggerIndex);

        public bool TryBeginAmmoAdmissionBarrier(int triggerIndex, ulong requestToken)
        {
            if (requestToken == 0 || !CanOwnReadiness(triggerIndex) || IsAmmoAdmissionPending(triggerIndex)) return false;
            _ammoAdmissionPending.Add(triggerIndex, requestToken);
            return true;
        }
        public bool TryEndAmmoAdmissionBarrier(int triggerIndex, ulong requestToken)
        {
            if (!_ammoAdmissionPending.TryGetValue(triggerIndex, out ulong current) || current != requestToken) return false;
            _ammoAdmissionPending.Remove(triggerIndex);
            return true;
        }
        public UxrFirearmMag GetFixedAmmoStore(int triggerIndex)
        {
            var magazine = GetCurrentReadinessMagazine(triggerIndex);
            var store = magazine != null ? magazine.GetComponent<UxrFirearmMag>() : null;
            return store != null && store.IsFixedAmmoStore && store.FixedStoreWeapon == this &&
                   store.FixedStoreTrigger == triggerIndex && GetBoundFixedAmmoStore(triggerIndex) == store ? store : null;
        }
        private bool HasFixedAmmoBinding(int triggerIndex)
        {
            foreach (var store in GetComponentsInChildren<UxrFirearmMag>(true))
                if (store.IsFixedAmmoStore && store.FixedStoreWeapon == this && store.FixedStoreTrigger == triggerIndex) return true;
            return false;
        }
        // Snapshot binding известен из authoring, даже если другие topology компоненты ещё читаются.
        private UxrFirearmMag GetBoundFixedAmmoStore(int triggerIndex)
        {
            UxrFirearmMag result = null;
            foreach (var store in GetComponentsInChildren<UxrFirearmMag>(true))
            {
                if (!store.IsFixedAmmoStore || store.FixedStoreWeapon != this || store.FixedStoreTrigger != triggerIndex) continue;
                if (result != null) return null;
                result = store;
            }
            return result;
        }
        private bool CanCommitAmmoAdmission(int triggerIndex) => UsesReadinessLedger(triggerIndex) &&
            !_fixedAmmoSnapshotReading && !IsReadinessReplay && !_readinessCommitting.Contains(triggerIndex) && isActiveAndEnabled && CanUse &&
            !_readinessFaulted.Contains(triggerIndex) && CanAuthorAmmoAdmission?.Invoke(triggerIndex) == true;

        public UxrAmmoAdmissionOutcome TryAcceptAmmoUnit(int triggerIndex, uint expectedRevision,
            UxrFirearmAmmoUnit unit, ulong requestToken)
        {
            var before = GetReadinessState(triggerIndex);
            var store = GetFixedAmmoStore(triggerIndex);
            if (!CanCommitAmmoAdmission(triggerIndex) || before?.ReadinessInitialized != true ||
                before.Revision != expectedRevision || store == null || unit == null || unit.UniqueId == Guid.Empty ||
                unit.HasBeenConsumed || requestToken == 0 || ValidateAmmoUnitAdmission == null) return UxrAmmoAdmissionOutcome.Rejected;
            int rounds = store.Rounds;
            Guid identity = unit.UniqueId;
            if (rounds + (before.ChamberRound ? 1 : 0) >= store.Capacity ||
                !ValidateAmmoUnitAdmission(triggerIndex, unit) || !CanCommitAmmoAdmission(triggerIndex) ||
                !before.Equals(GetReadinessState(triggerIndex)) || GetFixedAmmoStore(triggerIndex) != store ||
                rounds != store.Rounds || unit == null || unit.UniqueId != identity || unit.HasBeenConsumed)
                return UxrAmmoAdmissionOutcome.Rejected;
            var ledger = CreateReadinessCommit(triggerIndex, (UxrFirearmReadinessState)before.Clone(),
                GetCurrentReadinessMagazine(triggerIndex), rounds + 1, UxrFirearmReadinessOperation.AmmoAdmission);
            var commit = new UxrAmmoAdmissionCommit { Ledger = ledger, Unit = unit, UnitIdentity = identity, RequestToken = requestToken };
            return CommitAmmoAdmission(commit);
        }

        [Preserve]
        private UxrAmmoAdmissionOutcome CommitAmmoAdmission(UxrAmmoAdmissionCommit admission)
        {
            var commit = admission?.Ledger;
            if (commit == null || commit.Operation != UxrFirearmReadinessOperation.AmmoAdmission ||
                admission.Unit == null || admission.UnitIdentity == Guid.Empty || admission.RequestToken == 0 ||
                admission.Unit.UniqueId != admission.UnitIdentity || admission.Unit.HasBeenConsumed ||
                !ValidateReadinessCommit(commit)) return UxrAmmoAdmissionOutcome.Rejected;
            var before = GetReadinessState(commit.TriggerIndex);
            var store = GetFixedAmmoStore(commit.TriggerIndex);
            if (before?.ReadinessInitialized != true || store == null || before.Revision == uint.MaxValue ||
                before.CurrentMagazine != commit.ReferencedMagazine || commit.MagazineRoundsAfter != store.Rounds + 1)
                return UxrAmmoAdmissionOutcome.Rejected;
            var expected = (UxrFirearmReadinessState)before.Clone(); expected.Revision = commit.NextRevision;
            if (!expected.Equals(commit.StateAfter)) return UxrAmmoAdmissionOutcome.Rejected;

            bool endAttempted = false, committed = false;
            Exception failure = null;
            var runtimeAfter = (UxrFirearmReadinessState)commit.StateAfter.Clone();
            _readinessCommitting.Add(commit.TriggerIndex);
            BeginSync();
            try
            {
                // Все guards завершены, unit sink не вызывает callback. Между записями нет reentry.
                if (!admission.Unit.WriteConsumed(admission.UnitIdentity)) return UxrAmmoAdmissionOutcome.Rejected;
                _runtimeTriggers[commit.TriggerIndex].Readiness = runtimeAfter;
                _runtimeTriggers[commit.TriggerIndex].HasReloaded = commit.StateAfter.ChamberRound &&
                    !commit.StateAfter.ChamberCyclePending && !commit.StateAfter.ActionOpen;
                store.WriteLedgerRounds(this, commit.TriggerIndex, commit.MagazineRoundsAfter, false);
                committed = true;
                try { store.NotifyRoundsChanged(); } catch (Exception exception) { failure = exception; }
                try { admission.Unit.NotifyConsumed(); } catch (Exception exception) { failure = failure == null ? exception : new AggregateException(failure, exception); }
                // Publication тоже callback: failure не превращает committed admission в retry/refund.
                endAttempted = true; EndSyncMethod(new object[] { admission });
            }
            catch (Exception exception) { failure = failure == null ? exception : new AggregateException(failure, exception); }
            finally { if (!endAttempted) CancelSync(); _readinessCommitting.Remove(commit.TriggerIndex); }
            if (!committed) return UxrAmmoAdmissionOutcome.Rejected;
            if (failure != null)
            {
                _readinessFaulted.Add(commit.TriggerIndex);
                try { AmmoAdmissionFaulted?.Invoke(commit.TriggerIndex, admission.RequestToken, failure); } catch { }
                try { ReadinessFaulted?.Invoke(commit.TriggerIndex, UxrFirearmShotEmissionOutcome.NotEmitted, failure); } catch { }
                return UxrAmmoAdmissionOutcome.CommittedWithNotificationFailure;
            }
            return UxrAmmoAdmissionOutcome.Committed;
        }

        // ACK подтверждённой snapshot publication снимает только local fault metadata, не пишет ammo.
        // Порт CanAuthorAmmoAdmission здесь не годится: он описывает текущий запрос, а после фиксации
        // гильза уже consumed и запрос невалиден — fault не снимался бы никогда. Достаточно привязанного receiver.
        public bool TryAcknowledgeFixedAmmoResynchronization(int triggerIndex, uint revision)
        {
            var state = GetReadinessState(triggerIndex); var store = GetFixedAmmoStore(triggerIndex);
            if (IsReadinessReplay || CanAuthorAmmoAdmission == null || state?.Revision != revision ||
                store == null || store.Rounds + (state.ChamberRound ? 1 : 0) > store.Capacity) return false;
            return _readinessFaulted.Remove(triggerIndex);
        }

        private UxrFixedAmmoSnapshot[] CaptureFixedAmmoSnapshots()
        {
            var result = new List<UxrFixedAmmoSnapshot>();
            for (int trigger = 0; trigger < TriggerCount; trigger++)
            {
                var store = GetBoundFixedAmmoStore(trigger);
                if (store != null) result.Add(new UxrFixedAmmoSnapshot { TriggerIndex = trigger, Store = store,
                    StoreIdentity = store.UniqueId, Rounds = store.Rounds });
            }
            return result.ToArray();
        }
        private bool ValidateFixedAmmoSnapshots(Dictionary<int, RuntimeTriggerInfo> prospective, UxrFixedAmmoSnapshot[] snapshots)
        {
            if (prospective == null || snapshots == null) return false;
            var expected = CaptureFixedAmmoSnapshots();
            if (expected.Length != snapshots.Length) return false;
            for (int trigger = 0; trigger < TriggerCount; trigger++)
                if (HasFixedAmmoBinding(trigger) && GetBoundFixedAmmoStore(trigger) == null) return false;
            var seen = new HashSet<int>();
            foreach (var snapshot in snapshots)
            {
                if (snapshot == null || !seen.Add(snapshot.TriggerIndex) || snapshot.Store == null ||
                    snapshot.StoreIdentity == Guid.Empty || snapshot.Store.UniqueId != snapshot.StoreIdentity ||
                    !snapshot.Store.IsFixedStoreBindingValid(this, snapshot.TriggerIndex) || !snapshot.Store.IsFixedAmmoStore ||
                    GetBoundFixedAmmoStore(snapshot.TriggerIndex) != snapshot.Store ||
                    !prospective.TryGetValue(snapshot.TriggerIndex, out var runtime) || runtime == null ||
                    (runtime.Readiness != null && runtime.Readiness.CurrentMagazine != snapshot.Store.GetComponent<UxrGrabbableObject>()) ||
                    snapshot.Rounds < 0 || snapshot.Rounds + (runtime.Readiness?.ChamberRound == true ? 1 : 0) > snapshot.Store.Capacity) return false;
            }
            return true;
        }
    }
}

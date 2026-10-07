using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>Фиксация учёта, увиденная в <c>StateChanged</c> оружия (своя или replay).</summary>
    internal readonly struct LedgerCommitInfo
    {
        public readonly LedgerOp Op;
        public readonly bool ChamberBefore, ChamberAfter, Replay;
        public readonly uint ExpectedRevision, NextRevision;

        public LedgerCommitInfo(LedgerOp op, bool chamberBefore, bool chamberAfter, bool replay, uint expectedRevision, uint nextRevision)
        {
            Op = op; ChamberBefore = chamberBefore; ChamberAfter = chamberAfter; Replay = replay;
            ExpectedRevision = expectedRevision; NextRevision = nextRevision;
        }
    }

    /// <summary>
    /// Порт учёта только на чтение (этап C): снимок <see cref="LedgerView"/> из <c>UxrFirearmWeapon.Readiness</c>
    /// и разбор фиксаций из <c>StateChanged</c>. Команд не выдаёт. Запоминает последнее увиденное состояние,
    /// чтобы машина могла получить снимок «до фиксации» (досчёт решения старого кода в тот же момент).
    /// Магазины сравниваются по <c>GetInstanceID</c> (0 — нет магазина).
    /// </summary>
    internal sealed class WeaponLedgerReader
    {
        private readonly UxrFirearmWeapon _weapon;
        private readonly int _trigger;
        private UxrFirearmReadinessState _last;

        public WeaponLedgerReader(UxrFirearmWeapon weapon, int trigger)
        {
            _weapon = weapon; _trigger = trigger;
            Remember();
        }

        /// <summary>Учёт сейчас.</summary>
        public LedgerView Read() => Build(_weapon.GetReadinessState(_trigger));

        /// <summary>Учёт до последней фиксации (магазин и барьер — текущие).</summary>
        public LedgerView ReadBefore() => Build(_last);

        /// <summary>Последнее увиденное состояние учёта (его ревизия).</summary>
        public uint LastRevision => _last?.Revision ?? 0;

        public void Remember() => _last = _weapon.GetReadinessState(_trigger);

        private LedgerView Build(UxrFirearmReadinessState s)
        {
            UxrGrabbableObject anchorMagazine = CurrentAnchorMagazine();
            UxrFirearmMag ammo = anchorMagazine != null ? anchorMagazine.GetComponent<UxrFirearmMag>() : null;
            bool initialized = s != null && s.ReadinessInitialized;
            return new LedgerView(
                initialized: initialized,
                chamber: initialized && s.ChamberRound,
                actionOpen: initialized && s.ActionOpen,
                cyclePending: initialized && s.ChamberCyclePending,
                slideLocked: initialized && s.PostShotEmptyAction,
                faulted: FirearmIntrospection.IsReadinessFaulted(_weapon, _trigger),
                admissionPending: _weapon.IsAmmoAdmissionPending(_trigger),
                magazinePresent: ammo != null,
                magazineRounds: ammo != null ? ammo.Rounds : 0,
                capacity: ammo != null ? ammo.Capacity : 0,
                revision: s?.Revision ?? 0,
                cycleSequence: s?.CycleSequence ?? 0,
                extractedCycle: s?.ExtractedCycleSequence ?? 0,
                shotSequence: s?.ShotSequence ?? 0,
                magazineToken: s != null && s.CurrentMagazine != null ? s.CurrentMagazine.GetInstanceID() : 0,
                anchorMagazineToken: anchorMagazine != null ? anchorMagazine.GetInstanceID() : 0);
        }

        /// <summary>Магазин в гнезде спуска — та же проверка, что <c>GetCurrentReadinessMagazine</c> SDK.</summary>
        public UxrGrabbableObject CurrentAnchorMagazine()
        {
            if (!_weapon.TryGetTriggerMagazineAnchor(_trigger, out UxrGrabbableObjectAnchor anchor) || anchor == null || !anchor.isActiveAndEnabled) return null;
            UxrGrabbableObject magazine = anchor.CurrentPlacedObject;
            return magazine != null && magazine.CurrentAnchor == anchor && anchor.IsCompatibleObject(magazine) &&
                   magazine.GetComponent<UxrFirearmMag>() != null ? magazine : null;
        }

        /// <summary>
        /// Фиксация учёта этого спуска в событии <c>StateChanged</c>: <c>ApplyReadinessCommit</c>,
        /// <c>CommitShotSynced</c> (параметр <see cref="UxrFirearmReadinessCommit"/>) и <c>CommitAmmoAdmission</c>
        /// (<see cref="UxrAmmoAdmissionCommit"/>). Событие поднимается после записи учёта, поэтому текущий учёт —
        /// состояние после фиксации; «до» — последнее запомненное.
        /// </summary>
        public bool TryParseCommit(UxrSyncEventArgs args, out LedgerCommitInfo info)
        {
            info = default;
            if (!(args is UxrMethodInvokedSyncEventArgs method) || method.Parameters == null) return false;
            UxrFirearmReadinessCommit commit = null;
            foreach (object parameter in method.Parameters)
            {
                if (parameter is UxrFirearmReadinessCommit direct) commit = direct;
                else if (parameter is UxrAmmoAdmissionCommit admission) commit = admission.Ledger;
                if (commit != null) break;
            }
            if (commit == null || commit.TriggerIndex != _trigger || commit.StateAfter == null) return false;
            bool replay = UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync;
            bool before = _last != null && _last.ReadinessInitialized && _last.ChamberRound;
            info = new LedgerCommitInfo(MapOperation(commit.Operation), before, commit.StateAfter.ChamberRound, replay,
                commit.ExpectedRevision, commit.NextRevision);
            return true;
        }

        public static LedgerOp MapOperation(UxrFirearmReadinessOperation operation)
        {
            switch (operation)
            {
                case UxrFirearmReadinessOperation.Initialize: return LedgerOp.Initialize;
                case UxrFirearmReadinessOperation.MagazineChanged: return LedgerOp.MagazineChanged;
                case UxrFirearmReadinessOperation.BeginAction: return LedgerOp.BeginAction;
                case UxrFirearmReadinessOperation.Extract: return LedgerOp.Extract;
                case UxrFirearmReadinessOperation.Complete: return LedgerOp.Complete;
                case UxrFirearmReadinessOperation.Cancel: return LedgerOp.Cancel;
                case UxrFirearmReadinessOperation.Shot: return LedgerOp.Shot;
                case UxrFirearmReadinessOperation.Reconcile: return LedgerOp.Reconcile;
                case UxrFirearmReadinessOperation.Automation: return LedgerOp.Automation;
                case UxrFirearmReadinessOperation.CloseOnly: return LedgerOp.CloseOnly;
                case UxrFirearmReadinessOperation.EmptyRestAcknowledged: return LedgerOp.EmptyRestAcknowledged;
                case UxrFirearmReadinessOperation.AmmoAdmission: return LedgerOp.AmmoAdmission;
                default: return LedgerOp.None;
            }
        }
    }
}

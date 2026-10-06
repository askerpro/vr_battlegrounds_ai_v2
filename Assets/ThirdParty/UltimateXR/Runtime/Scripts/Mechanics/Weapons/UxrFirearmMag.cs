// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrFirearmMag.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using UltimateXR.Core.Components;
using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     A magazine that contains ammo for a <see cref="UxrFirearmWeapon" />. Magazines can be attached to firearms using
    ///     <see cref="UxrGrabbableObject" /> functionality.
    /// </summary>
    public partial class UxrFirearmMag : UxrComponent
    {
        #region Inspector Properties/Serialized Fields

        [SerializeField] private int _rounds;
        [SerializeField] private int _capacity;
        // VR Battlegrounds patch: постоянный reservoir, а не физическая единица патрона.
        [SerializeField] private UxrFirearmWeapon _fixedStoreWeapon;
        [SerializeField] private int _fixedStoreTrigger;

        #endregion

        #region Protected Overrides

        // VR Battlegrounds patch: карман выключает тот же магазин; единственный Rounds store
        // обязан оставаться в roots=null SaveRequiredComponents для late join.
        protected override bool SaveStateWhenDisabled => true;
        // Snapshot firearm/C читается раньше reservoir/M; fixed binding не меняет ordinary mags.
        protected override int SerializationOrder => base.SerializationOrder + (IsFixedAmmoStore ? 1 : 0);

        #endregion

        #region Public Types & Data

        /// <summary>
        ///     Total ammo capacity.
        /// </summary>
        public int Capacity => _capacity;
        public bool IsFixedAmmoStore => _fixedStoreWeapon != null;
        public UxrFirearmWeapon FixedStoreWeapon => _fixedStoreWeapon;
        public int FixedStoreTrigger => _fixedStoreTrigger;

        /// <summary>Authoring binding. Не ставит предмет в якорь и не изменяет боезапас.</summary>
        public bool ConfigureFixedStore(UxrFirearmWeapon weapon, int triggerIndex)
        {
            if (Application.isPlaying || weapon == null || triggerIndex < 0 || triggerIndex >= weapon.TriggerCount ||
                !transform.IsChildOf(weapon.transform) || _capacity <= 0 ||
                !weapon.TryGetTriggerMagazineAnchor(triggerIndex, out var anchor) || anchor == null ||
                GetComponent<UxrGrabbableObject>() == null) return false;
            _fixedStoreWeapon = weapon; _fixedStoreTrigger = triggerIndex;
            return true;
        }

        internal bool IsFixedStoreBindingValid(UxrFirearmWeapon weapon, int triggerIndex) =>
            !IsFixedAmmoStore || (_fixedStoreWeapon == weapon && _fixedStoreTrigger == triggerIndex);

        internal void WriteLedgerRounds(UxrFirearmWeapon weapon, int triggerIndex, int rounds, bool notify)
        {
            if (!IsFixedStoreBindingValid(weapon, triggerIndex) || rounds < 0 || rounds > _capacity ||
                (IsFixedAmmoStore && rounds + (weapon.GetReadinessState(triggerIndex)?.ChamberRound == true ? 1 : 0) > _capacity))
                throw new InvalidOperationException("Ledger ammo store binding/capacity mismatch.");
            _rounds = rounds;
            if (notify) NotifyRoundsChanged();
        }
        internal void NotifyRoundsChanged()
        {
            if (!IsFixedAmmoStore) { RoundsChanged?.Invoke(); return; }
            if (RoundsChanged == null) return;
            System.Collections.Generic.List<Exception> failures = null;
            foreach (Action handler in RoundsChanged.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception)
                { if (failures == null) failures = new System.Collections.Generic.List<Exception>(); failures.Add(exception); }
            }
            if (failures != null) throw new AggregateException(failures);
        }

        internal bool IsFixedSnapshotRoundsValid(int rounds) => !IsFixedAmmoStore ||
            (rounds >= 0 && rounds + (_fixedStoreWeapon.GetReadinessState(_fixedStoreTrigger)?.ChamberRound == true ? 1 : 0) <= _capacity);

        /// <summary>
        ///     Remaining ammo.
        /// </summary>
        public int Rounds
        {
            get => Mathf.Clamp(_rounds, 0, _capacity);
            set
            {
                // Fixed M пишет исключительно ledger. Прямой refill не может создать capacity+C.
                if (IsFixedAmmoStore) throw new InvalidOperationException("Fixed ammo store must be written through firearm ledger.");
                _rounds = Mathf.Clamp(value, 0, _capacity);
                RoundsChanged?.Invoke();
            }
        }

        /// <summary>
        ///     Event called whenever the number of rounds changed.
        /// </summary>
        public Action RoundsChanged;

        #endregion
    }
}

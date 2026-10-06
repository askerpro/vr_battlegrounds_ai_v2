using System;
using UltimateXR.Core.Components;
using UltimateXR.Core.StateSave;
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>VR Battlegrounds patch: одна физическая единица, не контейнер боезапаса.</summary>
    [DisallowMultipleComponent]
    public sealed class UxrFirearmAmmoUnit : UxrComponent
    {
        [SerializeField] private bool _consumed;
        public bool HasBeenConsumed => _consumed;
        public event Action<UxrFirearmAmmoUnit> ConsumptionChanged;
        protected override bool SaveStateWhenDisabled => true;

        // Только атомарный firearm admission вызывает этот sink после всех guards.
        internal bool WriteConsumed(Guid expectedIdentity)
        {
            if (_consumed || expectedIdentity == Guid.Empty || UniqueId != expectedIdentity) return false;
            _consumed = true;
            return true;
        }
        internal void NotifyConsumed()
        {
            if (ConsumptionChanged == null) return;
            System.Collections.Generic.List<Exception> failures = null;
            foreach (Action<UxrFirearmAmmoUnit> handler in ConsumptionChanged.GetInvocationList())
            {
                try { handler(this); }
                catch (Exception exception)
                { if (failures == null) failures = new System.Collections.Generic.List<Exception>(); failures.Add(exception); }
            }
            if (failures != null) throw new AggregateException(failures);
        }

        protected override void SerializeState(bool isReading, int stateSerializationVersion,
            UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            base.SerializeState(isReading, stateSerializationVersion, level, options);
            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                bool consumed = _consumed;
                SerializeStateValue(level, options | UxrStateSaveOptions.DontCheckCache, nameof(_consumed), ref consumed);
                if (isReading)
                {
                    if (_consumed && !consumed) throw new InvalidOperationException("Consumed ammo identity cannot be resurrected.");
                    _consumed = consumed;
                }
            }
        }
    }
}

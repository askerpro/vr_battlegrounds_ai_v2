// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrFirearmWeapon.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UltimateXR.Core.StateSave;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrFirearmWeapon
    {
        #region Protected Overrides UxrComponent

        protected override int StateSerializationVersion
        {
            get
            {
                for (int trigger = 0; trigger < TriggerCount; trigger++) if (HasFixedAmmoBinding(trigger)) return 1;
                return base.StateSerializationVersion;
            }
        }

        /// <inheritdoc />
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            bool ownsFixedRead = isReading && CaptureFixedAmmoSnapshots().Length != 0;
            if (ownsFixedRead && _fixedAmmoSnapshotReading) throw new System.InvalidOperationException("Reentrant fixed snapshot read.");
            if (ownsFixedRead) _fixedAmmoSnapshotReading = true;
            try
            {
            base.SerializeState(isReading, stateSerializationVersion, level, options);

            // Logic is already handled through events, we don't serialize these parameters in incremental changes

            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                // Opt-in initialized state нужен late join даже если initial cache уже снят после
                // стартового M→C. Другая копия не повторяет initialization из собственного prefab.
                bool hasLedger = false;
                foreach (var runtime in _runtimeTriggers.Values) if (runtime.Readiness?.ReadinessInitialized == true) { hasLedger = true; break; }
                // Prospective values читаются локально. Неполный fixed snapshot не публикует C без M.
                var prospective = _runtimeTriggers;
                SerializeStateValue(level, hasLedger ? options | UxrStateSaveOptions.DontCheckCache : options, nameof(_runtimeTriggers), ref prospective);
                var fixedSnapshots = CaptureFixedAmmoSnapshots();
                if (stateSerializationVersion >= 1)
                    SerializeStateValue(level, options | UxrStateSaveOptions.DontCheckCache, "fixedAmmoSnapshots", ref fixedSnapshots);
                else if (isReading && fixedSnapshots.Length != 0)
                    throw new System.InvalidOperationException("Fixed ammo store requires an atomic snapshot.");
                if (isReading)
                {
                    if (!ValidateFixedAmmoSnapshots(prospective, fixedSnapshots))
                        throw new System.InvalidOperationException("Invalid/partial fixed ammo snapshot.");
                    _runtimeTriggers = prospective;
                    foreach (var snapshot in fixedSnapshots)
                        snapshot.Store.WriteLedgerRounds(this, snapshot.TriggerIndex, snapshot.Rounds, false);
                }
            }
            }
            finally { if (ownsFixedRead) _fixedAmmoSnapshotReading = false; }
        }

        #endregion
    }
}

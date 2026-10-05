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

        /// <inheritdoc />
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            base.SerializeState(isReading, stateSerializationVersion, level, options);

            // Logic is already handled through events, we don't serialize these parameters in incremental changes

            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                // Opt-in initialized state нужен late join даже если initial cache уже снят после
                // стартового M→C. Другая копия не повторяет initialization из собственного prefab.
                bool hasLedger = false;
                foreach (var runtime in _runtimeTriggers.Values) if (runtime.Readiness?.ReadinessInitialized == true) { hasLedger = true; break; }
                SerializeStateValue(level, hasLedger ? options | UxrStateSaveOptions.DontCheckCache : options, nameof(_runtimeTriggers), ref _runtimeTriggers);
            }
        }

        #endregion
    }
}

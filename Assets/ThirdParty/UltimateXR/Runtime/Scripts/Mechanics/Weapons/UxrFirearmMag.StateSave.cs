// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrFirearmMag.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UltimateXR.Core.StateSave;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrFirearmMag
    {
        #region Protected Overrides

        /// <inheritdoc />
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            base.SerializeState(isReading, stateSerializationVersion, level, options);

            // Fixed-only snapshot пишет firearm атомарно с C; отдельной загрузки M нет.
            if (level > UxrStateSaveLevel.ChangesSincePreviousSave && !IsFixedAmmoStore)
            {
                // VR Battlegrounds patch: late join получает реальный единственный store,
                // даже если исходный cache захвачен после переноса патрона в патронник.
                int serializedRounds = _rounds;
                SerializeStateValue(level, options | UxrStateSaveOptions.DontCheckCache, nameof(_rounds), ref serializedRounds);
                if (isReading)
                {
                    if (!IsFixedSnapshotRoundsValid(serializedRounds))
                        throw new System.InvalidOperationException("Fixed store snapshot exceeds total capacity.");
                    _rounds = serializedRounds;
                }
            }
        }

        #endregion
    }
}

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

            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                // VR Battlegrounds patch: late join получает реальный единственный store,
                // даже если исходный cache захвачен после переноса патрона в патронник.
                SerializeStateValue(level, options | UxrStateSaveOptions.DontCheckCache, nameof(_rounds), ref _rounds);
            }
        }

        #endregion
    }
}

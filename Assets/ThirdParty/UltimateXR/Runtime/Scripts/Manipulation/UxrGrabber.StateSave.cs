// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrGrabber.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UltimateXR.Core.StateSave;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabber
    {
        #region Protected Overrides UxrComponent

        /// <inheritdoc />
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            bool restoreGrabState = isReading && level > UxrStateSaveLevel.ChangesSincePreviousSave;
            if (restoreGrabState)
            {
                BeginGrabStateMutation(); // VR Battlegrounds patch 71: VarSerializing/VarSerialized не получают usable stamp.
                BeginGrabLifetimeMutation();
            }
            try
            {
                base.SerializeState(isReading, stateSerializationVersion, level, options);

                // Manipulations are already handled through events, we don't serialize them in incremental changes

                if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
                {
                    SerializeStateValue(level, options, nameof(_grabbedObject), ref _grabbedObject);
                }
            }
            finally
            {
                if (restoreGrabState)
                {
                    try { EndGrabLifetimeMutation(); }
                    finally { EndGrabStateMutation(); }
                }
            }
        }

        #endregion
    }
}
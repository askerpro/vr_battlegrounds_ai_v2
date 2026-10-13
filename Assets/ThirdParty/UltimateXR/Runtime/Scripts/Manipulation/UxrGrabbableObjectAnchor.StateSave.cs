// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrGrabbableObjectAnchor.StateSave.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using UltimateXR.Core.StateSave;
using UltimateXR.Exceptions; // VR Battlegrounds patch 75: UxrComponentNotFoundException

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabbableObjectAnchor
    {
        protected override void SerializeState(bool isReading, int stateSerializationVersion, UxrStateSaveLevel level, UxrStateSaveOptions options)
        {
            base.SerializeState(isReading, stateSerializationVersion, level, options);

            // Manipulations are already handled through events, we don't serialize them in incremental changes

            if (level > UxrStateSaveLevel.ChangesSincePreviousSave)
            {
                // VR Battlegrounds patch 75: содержимое якоря пишется всегда (см. UxrGrabbableObject.StateSave.cs),
                // после чтения ссылки согласует менеджер.
                UxrGrabbableObject placedBeforeLoad   = _currentPlacedObject;
                Guid               unresolvedPlacedId = Guid.Empty;

                try
                {
                    SerializeStateValue(level, options | UxrStateSaveOptions.DontCheckCache, nameof(_currentPlacedObject), ref _currentPlacedObject);
                }
                catch (UxrComponentNotFoundException e) when (isReading)
                {
                    // Предмет ещё не зарегистрирован у клиента: ссылка прочитана целиком, согласование — при его появлении.
                    unresolvedPlacedId   = e.UniqueId;
                    _currentPlacedObject = null;
                }

                if (isReading)
                {
                    UxrGrabManager.Instance.ReconcileLoadedAnchor(this, placedBeforeLoad, unresolvedPlacedId);
                }
            }
        }
    }
}
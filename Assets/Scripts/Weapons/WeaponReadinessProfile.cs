using System;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    public enum WeaponChamberPolicy { ManualReturn, AutoOnMagazineInsert, TriggerAssistPrepareOnly }
    public enum WeaponEmptyPose { HoldOpen, ReturnToRest }
    public enum WeaponAmmoCapability { LegacyAmmo, DetachableMagazineChamber }
    public enum WeaponPhysicalCapability { NoAction, ActionTravel }

    /// <summary>Только данные готовности; FireMode, ёмкость и темп принадлежат SDK trigger.</summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Weapons/Readiness Profile")]
    public sealed class WeaponReadinessProfile : ScriptableObject
    {
        [SerializeField] private WeaponAmmoCapability _ammoCapability = WeaponAmmoCapability.DetachableMagazineChamber;
        [SerializeField] private WeaponPhysicalCapability _physicalCapability = WeaponPhysicalCapability.ActionTravel;
        [SerializeField] private WeaponChamberPolicy _chamberPolicy = WeaponChamberPolicy.ManualReturn;
        [SerializeField] private WeaponEmptyPose _emptyPose = WeaponEmptyPose.ReturnToRest;

        public WeaponAmmoCapability AmmoCapability => _ammoCapability;
        public WeaponPhysicalCapability PhysicalCapability => _physicalCapability;
        public WeaponChamberPolicy ChamberPolicy => _chamberPolicy;
        public WeaponEmptyPose EmptyPose => _emptyPose;

        public bool TryValidate(out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(WeaponAmmoCapability), _ammoCapability) ||
                !Enum.IsDefined(typeof(WeaponPhysicalCapability), _physicalCapability) ||
                !Enum.IsDefined(typeof(WeaponChamberPolicy), _chamberPolicy) ||
                !Enum.IsDefined(typeof(WeaponEmptyPose), _emptyPose))
                error = "Профиль содержит неизвестное значение enum.";
            else if (_ammoCapability == WeaponAmmoCapability.DetachableMagazineChamber &&
                     _physicalCapability == WeaponPhysicalCapability.NoAction &&
                     (_chamberPolicy == WeaponChamberPolicy.ManualReturn || _emptyPose == WeaponEmptyPose.HoldOpen))
                error = "NoAction не поддерживает ManualReturn или HoldOpen.";
            return error == null;
        }
    }
}

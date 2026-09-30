using System;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrWeaponManager
    {
        /// <summary>
        ///     Gets whether the weapon system is enabled. If disabled, all weapon-related calculations (projectiles, damage)
        ///     and usage are blocked.
        /// </summary>
        public bool WeaponSystemEnabled { get; private set; } = true;

        /// <summary>
        ///     VR Battlegrounds patch (Патч 32): решение о пробитии не-актора пулей. Не задано — пуля останавливается на
        ///     первом препятствии, как в SDK. Ставит игра (<c>WallPenetration</c>); одинаково на каждой машине.
        /// </summary>
        public static UxrProjectilePenetrationHandler ProjectilePenetration { get; set; }

        /// <summary>
        ///     Sets whether the weapon system is enabled.
        /// </summary>
        /// <param name="enabled">Whether to enable the weapon system</param>
        public void SetWeaponSystemEnabled(bool enabled)
        {
            WeaponSystemEnabled = enabled;
        }
    }
}

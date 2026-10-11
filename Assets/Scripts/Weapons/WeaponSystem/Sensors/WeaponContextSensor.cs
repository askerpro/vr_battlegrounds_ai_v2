using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Network;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик контекста шага: роль (<c>StateEventAuthority.IsAuthorOfItem</c>), replay state sync, «контекст автора»
    /// (VR-рука либо разрешённая экипировка из единого resolver), блокировка (<c>CanUse</c>, препятствие
    /// у ствола), остаток таймера темпа SDK и авторство мира (бот). Таймер темпа читается отражением, поэтому только
    /// по запросу — для событий спуска.
    /// </summary>
    internal sealed class WeaponContextSensor
    {
        private readonly UxrFirearmWeapon _weapon;
        private readonly WeaponSystem _host;
        private readonly BarrelObstruction _obstruction;
        private readonly int _trigger;

        public WeaponContextSensor(UxrFirearmWeapon weapon, WeaponSystem host, int trigger)
        {
            _weapon = weapon; _host = host; _trigger = trigger;
            _obstruction = weapon.GetComponent<BarrelObstruction>();
        }

        /// <param name="mainHand">Рука на основной рукояти в контексте автора (иначе null).</param>
        /// <param name="withRateOfFire">Прочитать таймер темпа (нужен только событиям спуска).</param>
        public WeaponContext Read(out UxrGrabber mainHand, bool withRateOfFire = false)
            => Read(WeaponUseContext.Resolve(_weapon, _trigger), out mainHand, withRateOfFire);

        public WeaponContext Read(in WeaponUseContext use, out UxrGrabber mainHand, bool withRateOfFire = false)
        {
            mainHand = null;
            bool insideReplay = UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync;
            WeaponRole role = StateEventAuthority.IsAuthorOfItem(_weapon) ? WeaponRole.Author : WeaponRole.Observer;
            bool canUse = _weapon.CanUse;
            bool eligible = role == WeaponRole.Author && !insideReplay && canUse && _weapon.isActiveAndEnabled &&
                            _host.isActiveAndEnabled && _host.IsConfigured && use.Kind != WeaponUseKind.None && use.AuthoredHere;
            bool mainGripLocal = eligible && use.Kind == WeaponUseKind.VrHand && UxrGrabManager.HasInstance &&
                                 UxrGrabManager.Instance.isActiveAndEnabled && use.Hand != null && use.Hand.isActiveAndEnabled &&
                                 use.Epoch > 0 && use.Hand.GrabLifetimeEpoch == use.Epoch;
            if (mainGripLocal) mainHand = use.Hand;
            bool automationAllowed = eligible && (use.Kind == WeaponUseKind.Equipment || mainGripLocal) &&
                                     StateEventAuthority.IsWorldAuthority && _host.HasAutomationGrant(use);
            bool hasAuthorContext = mainGripLocal || (eligible && use.Kind == WeaponUseKind.Equipment && automationAllowed);
            WeaponBlockReason blocked = canUse ? WeaponBlockReason.None
                : _obstruction != null && _obstruction.IsObstructed ? WeaponBlockReason.Obstructed : WeaponBlockReason.Other;
            float rof = withRateOfFire ? FirearmIntrospection.GetLastShotTimer(_weapon, _trigger) : 0f;
            return new WeaponContext(role, mainGripLocal, insideReplay, blocked, rof, StateEventAuthority.IsWorldAuthority,
                hasAuthorContext, automationAllowed);
        }

        /// <summary>Рука на основной рукояти любой (для отклика автора без контекста не нужна).</summary>
        public UxrGrabber MainHand() => WeaponUseContext.Resolve(_weapon, _trigger).Hand;
    }
}

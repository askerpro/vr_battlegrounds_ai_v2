using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Network;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик контекста шага: роль (<c>StateEventAuthority.IsAuthorOfItem</c>), replay state sync, «контекст автора»
    /// (основную рукоять держит рука аватара, авторство которого на этой машине), блокировка (<c>CanUse</c>, препятствие
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
        {
            mainHand = null;
            bool insideReplay = UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync;
            WeaponRole role = StateEventAuthority.IsAuthorOfItem(_weapon) ? WeaponRole.Author : WeaponRole.Observer;
            bool canUse = _weapon.CanUse;
            bool mainGripLocal = role == WeaponRole.Author && !insideReplay && canUse && _weapon.isActiveAndEnabled &&
                                 _host.isActiveAndEnabled && _host.IsConfigured && TryGetLocalMainHand(out mainHand);
            if (!mainGripLocal) mainHand = null;
            WeaponBlockReason blocked = canUse ? WeaponBlockReason.None
                : _obstruction != null && _obstruction.IsObstructed ? WeaponBlockReason.Obstructed : WeaponBlockReason.Other;
            float rof = withRateOfFire ? FirearmIntrospection.GetLastShotTimer(_weapon, _trigger) : 0f;
            return new WeaponContext(role, mainGripLocal, insideReplay, blocked, rof, StateEventAuthority.IsWorldAuthority);
        }

        /// <summary>Рука на основной рукояти любой (для отклика автора без контекста не нужна).</summary>
        public UxrGrabber MainHand() =>
            UxrGrabManager.HasInstance && _weapon.TryGetTriggerGrip(_trigger, out UxrGrabbableObject grip, out int point) &&
            UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber hand) ? hand : null;

        private bool TryGetLocalMainHand(out UxrGrabber hand)
        {
            hand = null;
            if (!UxrGrabManager.HasInstance || !UxrGrabManager.Instance.isActiveAndEnabled ||
                !_weapon.TryGetTriggerGrip(_trigger, out UxrGrabbableObject grip, out int point) ||
                !UxrGrabManager.Instance.GetGrabbingHand(grip, point, out hand) ||
                hand == null || hand.Avatar == null || !StateEventAuthority.IsAuthoredHere(hand.Avatar)) { hand = null; return false; }
            return true;
        }
    }
}

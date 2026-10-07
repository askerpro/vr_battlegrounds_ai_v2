using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Network;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик контекста шага: роль (<c>StateEventAuthority.IsAuthorOfItem</c>), replay state sync,
    /// «контекст автора» (основную рукоять держит рука аватара, авторство которого на этой машине — то же
    /// правило, что <c>WeaponReadinessController.HasContext</c>), блокировка (<c>CanUse</c>, препятствие у ствола),
    /// остаток таймера темпа SDK и авторство мира (бот).
    /// </summary>
    internal sealed class WeaponContextSensor
    {
        private readonly UxrFirearmWeapon _weapon;
        private readonly WeaponReadinessController _controller;
        private readonly BarrelObstruction _obstruction;
        private readonly int _trigger;

        public WeaponContextSensor(UxrFirearmWeapon weapon, WeaponReadinessController controller, int trigger)
        {
            _weapon = weapon; _controller = controller; _trigger = trigger;
            _obstruction = weapon.GetComponent<BarrelObstruction>();
        }

        public WeaponContext Read(out UxrAvatar mainAvatar)
        {
            mainAvatar = null;
            bool insideReplay = UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync;
            WeaponRole role = StateEventAuthority.IsAuthorOfItem(_weapon) ? WeaponRole.Author : WeaponRole.Observer;
            bool canUse = _weapon.CanUse;
            bool mainGripLocal = role == WeaponRole.Author && !insideReplay && canUse && _weapon.isActiveAndEnabled &&
                                 _controller.isActiveAndEnabled && _controller.IsConfigured && TryGetLocalMainHand(out mainAvatar);
            if (!mainGripLocal) mainAvatar = null;
            WeaponBlockReason blocked = canUse ? WeaponBlockReason.None
                : _obstruction != null && _obstruction.IsObstructed ? WeaponBlockReason.Obstructed : WeaponBlockReason.Other;
            return new WeaponContext(role, mainGripLocal, insideReplay, blocked,
                FirearmIntrospection.GetLastShotTimer(_weapon, _trigger), StateEventAuthority.IsWorldAuthority);
        }

        private bool TryGetLocalMainHand(out UxrAvatar avatar)
        {
            avatar = null;
            if (!UxrGrabManager.HasInstance || !UxrGrabManager.Instance.isActiveAndEnabled ||
                !_weapon.TryGetTriggerGrip(_trigger, out UxrGrabbableObject grip, out int point) ||
                !UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber hand) ||
                hand == null || hand.Avatar == null || !StateEventAuthority.IsAuthoredHere(hand.Avatar)) return false;
            avatar = hand.Avatar;
            return true;
        }
    }
}

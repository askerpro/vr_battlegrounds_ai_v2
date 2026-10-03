using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.WallPass;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный автор IsUseBlocked: объединяет упёртый ствол и нарушение стены
    /// текущим держателем. Работает на всех копиях оружия без синхронизации действий.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class WeaponUseBlocker : MonoBehaviour
    {
        private UxrWeapon _weapon;
        private UxrFirearmWeapon _firearm;
        private bool _barrelObstructed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            UxrWeapon.GlobalEnabled -= Attach;
            UxrWeapon.GlobalEnabled += Attach;
        }

        private static void Attach(UxrWeapon weapon)
        {
            if (weapon != null && weapon.GetComponent<WeaponUseBlocker>() == null)
                weapon.gameObject.AddComponent<WeaponUseBlocker>();
        }

        private void Awake()
        {
            _weapon = GetComponent<UxrWeapon>();
            _firearm = _weapon as UxrFirearmWeapon;
        }

        private void OnEnable()
        {
            UxrManager.AvatarsUpdating += Apply;
            Apply();
        }

        private void OnDisable()
        {
            UxrManager.AvatarsUpdating -= Apply;
            if (_weapon != null) _weapon.IsUseBlocked = _barrelObstructed;
        }

        private void Update() => Apply();

        /// <summary>Проверка ствола публикует свою причину, не снимая запрет T-40.</summary>
        public void SetBarrelObstructed(bool blocked)
        {
            _barrelObstructed = blocked;
            Apply();
        }

        private void Apply()
        {
            if (_weapon != null) _weapon.IsUseBlocked = _barrelObstructed || HolderViolatesWall();
        }

        private bool HolderViolatesWall()
        {
            if (!UxrGrabManager.HasInstance) return false;
            if (_firearm != null)
            {
                for (int trigger = 0; trigger < _firearm.TriggerCount; trigger++)
                {
                    if (!_firearm.TryGetTriggerGrip(trigger, out UxrGrabbableObject grip, out int point)) continue;
                    if (UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber hand) &&
                        hand != null && hand.Avatar != null && Violates(hand.Avatar.GetComponent<PlayerController>()))
                        return true;
                }
                return false;
            }

            return _weapon.Owner != null && Violates(_weapon.Owner.GetComponent<PlayerController>());
        }

        private static bool Violates(PlayerController player) =>
            player != null && player.Session != null &&
            player.Session.WallPassStatus.Stage == WallPassStage.Violating;
    }
}

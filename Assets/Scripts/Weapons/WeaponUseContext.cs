using System.Runtime.CompilerServices;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    public enum WeaponUseKind { None, VrHand, Equipment }

    /// <summary>Единственный вывод пользователя оружия; факты оборудования не копируются в учёт оружия.</summary>
    public readonly struct WeaponUseContext
    {
        public readonly WeaponUseKind Kind;
        public readonly UxrAvatar User;
        public readonly UxrGrabber Hand;
        public readonly bool AuthoredHere;
        public readonly int Epoch;

        private sealed class ConflictState { public bool Reported; }
        private static readonly ConditionalWeakTable<UxrFirearmWeapon, ConflictState> Conflicts =
            new ConditionalWeakTable<UxrFirearmWeapon, ConflictState>();
        private static readonly ConditionalWeakTable<UxrFirearmWeapon, ConflictState>.CreateValueCallback CreateConflict = CreateConflictState;

        private WeaponUseContext(WeaponUseKind kind, UxrAvatar user, UxrGrabber hand, bool authoredHere, int epoch)
        {
            Kind = kind; User = user; Hand = hand; AuthoredHere = authoredHere; Epoch = epoch;
        }

        /// <summary>Ключ разрешения; физическая рука и пригодность проверяются отдельно при каждом использовании.</summary>
        internal bool SameKey(in WeaponUseContext other) => Kind == other.Kind && User == other.User && Epoch == other.Epoch;

        public static WeaponUseContext Resolve(UxrFirearmWeapon weapon, int trigger)
        {
            if (weapon == null) return default;
            UxrGrabber hand = null;
            UxrGrabbableObject grip = null;
            if (UxrGrabManager.HasInstance && weapon.TryGetTriggerGrip(trigger, out grip, out int point))
                UxrGrabManager.Instance.GetGrabbingHand(grip, point, out hand);
            // Та же root-selection, что SDK RootGrabbable: trigger0 и его непосредственная grabbable dependency.
            UxrGrabbableObject root = null;
            if (weapon.TryGetTriggerGrip(0, out UxrGrabbableObject firstGrip, out _))
                root = firstGrip.UsesGrabbableParentDependency ? firstGrip.GrabbableParent : firstGrip;
            WeaponEquipmentBinding binding = root != null ? root.GetComponent<WeaponEquipmentBinding>() : null;
            bool claimed = binding != null && binding.IsActive;
            // Raw-конфликт проверяется до живости, локальности и CanUse. Weak table не удерживает уничтоженные стволы.
            ConflictState conflict = Conflicts.GetValue(weapon, CreateConflict);
            if (hand != null && claimed)
            {
                if (!conflict.Reported)
                    GameLog.WeaponSystem.Error("[WeaponUseContext] Одновременно активны SDK-хват и equipment claim.", weapon);
                conflict.Reported = true;
                return default;
            }
            conflict.Reported = false;
            if (claimed)
            {
                UxrAvatar owner = binding.Owner;
                if (owner == null || !binding.OwnerAlive) return default;
                return new WeaponUseContext(WeaponUseKind.Equipment, owner, null, StateEventAuthority.IsAuthoredHere(owner), binding.Epoch);
            }
            if (hand == null || hand.Avatar == null || hand.GrabbedObject != grip || !hand.isActiveAndEnabled ||
                !UxrGrabManager.Instance.isActiveAndEnabled) return default;
            int epoch = hand.GrabLifetimeEpoch;
            // SDK возвращает ноль во время canonical mutation/restore и при исчерпании глобального источника.
            if (epoch <= 0) return default;
            return new WeaponUseContext(WeaponUseKind.VrHand, hand.Avatar, hand, StateEventAuthority.IsAuthoredHere(hand.Avatar), epoch);
        }

        private static ConflictState CreateConflictState(UxrFirearmWeapon weapon) => new ConflictState();
    }
}

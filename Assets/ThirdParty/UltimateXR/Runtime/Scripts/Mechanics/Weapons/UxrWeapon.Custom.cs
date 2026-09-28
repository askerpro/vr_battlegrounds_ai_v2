using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    public abstract partial class UxrWeapon
    {
        /// <summary>
        ///     VR Battlegrounds patch 15: запрет использования извне — ствол упёрт в геометрию
        ///     (<c>BarrelObstruction</c>). Учитывается в <see cref="CanUse" />, а через него — в
        ///     <c>UxrFirearmWeapon.TryToShootRound</c>: выстрела нет.
        /// </summary>
        public bool IsUseBlocked { get; set; }

        /// <summary>
        ///     Gets whether the weapon can be used. By default it is true if the owner is not dead and the global check
        ///     in <see cref="UxrWeaponManager" /> allows it.
        /// </summary>
        public virtual bool CanUse
        {
            get
            {
                // VR Battlegrounds patch 15
                if (IsUseBlocked)
                {
                    return false;
                }

                if (Owner != null && Owner.IsDead)
                {
                    return false;
                }

                if (UxrWeaponManager.HasInstance)
                {
                    if (!UxrWeaponManager.Instance.WeaponSystemEnabled)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public partial class UxrFirearmWeapon
    {
        // VR Battlegrounds patch 15: сведения о спуске без рефлексии (UxrFirearmTrigger — internal).

        /// <summary>Число спусков оружия.</summary>
        public int TriggerCount => _triggers != null ? _triggers.Count : 0;

        /// <summary>За какой граббабл и какую точку хвата держат спуск.</summary>
        public bool TryGetTriggerGrip(int triggerIndex, out UxrGrabbableObject grabbable, out int grabPoint)
        {
            grabbable = null;
            grabPoint = 0;
            if (triggerIndex < 0 || triggerIndex >= TriggerCount) return false;

            grabbable = _triggers[triggerIndex].TriggerGrabbable;
            grabPoint = _triggers[triggerIndex].GrabbableGrabPointIndex;
            return grabbable != null;
        }

        /// <summary>Индекс типа выстрела <c>UxrProjectileSource</c>, которым стреляет спуск.</summary>
        public int GetTriggerShotIndex(int triggerIndex) =>
            triggerIndex >= 0 && triggerIndex < TriggerCount ? _triggers[triggerIndex].ProjectileShotIndex : 0;

        /// <summary>Звук пустого спуска («нет патронов») в точке.</summary>
        public void PlayTriggerNoAmmoSound(int triggerIndex, Vector3 position)
        {
            if (triggerIndex >= 0 && triggerIndex < TriggerCount)
            {
                _triggers[triggerIndex].ShotAudioNoAmmo?.Play(position);
            }
        }
    }
}

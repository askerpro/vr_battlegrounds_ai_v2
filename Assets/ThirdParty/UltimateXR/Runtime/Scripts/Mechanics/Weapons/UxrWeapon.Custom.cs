using System;
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

        // ── VR Battlegrounds patch 23: один источник выстрела ─────────────────────
        //
        // Выстрел решает только машина стрелка (TryToShootRound → синхронизируемый Shoot). Остальные
        // получают Shoot событием и по нему же (UxrProjectileSource.ShotFired) играют звук, отдачу и
        // тратят патрон копии — ProjectileShotReplayed. ProjectileShot поднимается только у стрелка:
        // подписчик, который сам стреляет (дробь), по построению не выстрелит на копии.

        /// <summary>
        ///     Выстрел другой машины повторён на этой копии оружия: звук и отдача уже сыграны.
        ///     Для косметики наблюдателя; <see cref="ProjectileShot" /> здесь не поднимается.
        /// </summary>
        public event Action<int> ProjectileShotReplayed;

        /// <summary>Идёт свой выстрел (<see cref="TryToShootRound" />) — его эффекты играются там, повтор не нужен.</summary>
        private bool _shootingLocally;

        private UxrProjectileSource _replaySource;

        private void SubscribeShotReplay(bool subscribe)
        {
            if (subscribe)
            {
                _replaySource = GetCachedComponent<UxrProjectileSource>();
                if (_replaySource != null) _replaySource.ShotFired += Source_ShotFired;
            }
            else if (_replaySource != null)
            {
                _replaySource.ShotFired -= Source_ShotFired;
                _replaySource = null;
            }
        }

        /// <summary>
        ///     Источник выстрелил. Свой выстрел пропускается; чужой (повтор события по сети) — эффекты
        ///     спуска, чей это тип выстрела. Дробинки — другой тип выстрела, на них ничего не играется.
        /// </summary>
        private void Source_ShotFired(int shotTypeIndex)
        {
            if (_shootingLocally)
            {
                return;
            }

            for (int i = 0; i < _triggers.Count; i++)
            {
                UxrFirearmTrigger trigger = _triggers[i];

                if (trigger.ProjectileShotIndex != shotTypeIndex || !_runtimeTriggers.TryGetValue(i, out RuntimeTriggerInfo runtimeTrigger))
                {
                    continue;
                }

                // Патрон копии: Rounds магазина не синхронизируется, пусть копия хотя бы не расходится с выстрелами.
                int ammo = GetAmmoLeft(i);
                if (ammo > 0 && ammo != int.MaxValue)
                {
                    SetAmmoLeft(i, ammo - 1);
                }

                runtimeTrigger.RecoilTimer = trigger.RecoilDurationSeconds;
                trigger.ShotAudio?.Play(_replaySource.GetShotOrigin(shotTypeIndex));
                ProjectileShotReplayed?.Invoke(i);
            }
        }

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

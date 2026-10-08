using UltimateXR.Audio;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный исполнитель звуков механизма (этап D, план п. 4.2): играет сигнал машины <see cref="WeaponCue"/> клипом
    /// из <see cref="WeaponAudioSet"/>. Сам ничего не решает: звуки фиксаций (ActionBack, ActionForward*, ChamberEjected)
    /// машина выдаёт по учёту одинаково у автора и наблюдателя (Н1, Н2), DryFire и Refusal — только у автора.
    ///
    /// <para>
    /// <b>До этапа E.</b> Щелчок при упёртом стволе (<c>DryFire(Obstructed)</c>) остаётся у <see cref="BarrelObstruction"/>:
    /// он проигрывает его сам по нажатию; второй щелчок здесь не играется. Запасной щелчок SDK подавлен хостом
    /// (подписка на <c>LocalTriggerAttemptDecided</c>), поэтому щелчок отказа по патронам звучит ровно один раз — отсюда.
    /// </para>
    /// </summary>
    internal sealed class WeaponAudioExecutor
    {
        private readonly UxrFirearmWeapon _weapon;
        private readonly int _trigger;
        private readonly WeaponAudioSet _set;
        private readonly WeaponMechanismRig _rig;

        public WeaponAudioExecutor(UxrFirearmWeapon weapon, int trigger, WeaponAudioSet set, WeaponMechanismRig rig)
        {
            _weapon = weapon; _trigger = trigger; _set = set; _rig = rig;
        }

        /// <summary>Сколько сигналов сыграно (для проб).</summary>
        public int Played { get; private set; }

        public void Play(WeaponCue cue, WeaponNotReadyReason reason, UxrGrabber mainHand)
        {
            if (_weapon == null || _set == null) return;
            if (cue == WeaponCue.DryFire && reason == WeaponNotReadyReason.Obstructed) return; // владелец — BarrelObstruction (до E)
            UxrAudioSample sample = _set.For(cue);
            if (cue == WeaponCue.DryFire || cue == WeaponCue.Refusal)
            {
                Vector3 trigger = _weapon.GetTriggerNoAmmoSoundPosition(_trigger, mainHand);
                if (WeaponAudioSet.Has(sample)) sample.Play(trigger);
                else if (cue == WeaponCue.DryFire) _weapon.PlayTriggerNoAmmoSound(_trigger, trigger);
                else return;
                Played++;
                return;
            }
            if (!WeaponAudioSet.Has(sample)) return;
            Transform at = _rig.Handle != null ? _rig.Handle.transform : _weapon.transform;
            sample.Play(at.position);
            Played++;
        }
    }
}

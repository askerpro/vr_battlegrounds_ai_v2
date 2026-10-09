using System.Collections.Generic;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный исполнитель вылета (этап ejection): по сигналу машины выпускает из окна выброса ствола
    /// (<see cref="WeaponEjectionPort"/>) живой патрон (<see cref="WeaponCue.ChamberEjected"/>) или стреляную гильзу
    /// (<see cref="WeaponCue.CasingEjected"/>). Префаб и импульс — запись ствола, а без неё — дефолт категории
    /// (<see cref="WeaponEjectionSet.Resolve"/> в момент вылета). Экземпляры — в общем пуле <see cref="WeaponEjectaPool"/>.
    ///
    /// <para>
    /// Сам ничего не решает: оба сигнала машина выдаёт по фиксации учёта SDK одинаково у автора и наблюдателя, поэтому вылет
    /// видят все игроки, а в сеть ничего не уходит. Скорость задаётся в системе окна; к ней добавляется скорость окна
    /// (ствол в руке движется вместе с игроком), измеренная по двум последним вызовам <see cref="Track"/>.
    /// </para>
    /// </summary>
    internal sealed class WeaponEjectionExecutor
    {
        private const float MaxInheritedSpeed = 6f;

        private readonly UxrFirearmWeapon _weapon;
        private readonly WeaponEjectionPort _port;
        private readonly WeaponEjectionSet _set;
        private readonly WeaponFeedbackDefaults _defaults;
        private readonly List<Collider> _colliders = new List<Collider>(32);
        private bool _warnedNoPort;
        private bool _tracked;
        private Vector3 _lastPosition, _portVelocity;
        private float _lastTime;

        public WeaponEjectionExecutor(UxrFirearmWeapon weapon, WeaponEjectionPort port, WeaponEjectionSet set, WeaponFeedbackDefaults defaults)
        {
            _weapon = weapon; _port = port; _set = set; _defaults = defaults;
        }

        /// <summary>Сколько вылетов выпущено (для проб).</summary>
        public int Spawned { get; private set; }

        /// <summary>
        /// Замер скорости окна раз в кадр, пока ствол не в покое (хост зовёт из <c>LateUpdate</c>). В покое окно не движется:
        /// замер сбрасывается, и первый вылет после покоя скорости не наследует.
        /// </summary>
        public void Track(bool idle)
        {
            if (idle || !_port.TryGetWorldPose(out Vector3 position, out _)) { _tracked = false; _portVelocity = Vector3.zero; return; }
            float now = Time.time;
            if (_tracked && now > _lastTime) _portVelocity = Vector3.ClampMagnitude((position - _lastPosition) / (now - _lastTime), MaxInheritedSpeed);
            _lastPosition = position; _lastTime = now; _tracked = true;
        }

        public void Play(WeaponCue cue)
        {
            if (!WeaponEjectionSet.IsEjectionCue(cue) || _weapon == null) return;
            WeaponEjectile ejectile = WeaponEjectionSet.Resolve(_set, _defaults != null ? _defaults.Ejection : null, cue, out _);
            if (ejectile == null) return;
            if (!_port.TryGetWorldPose(out Vector3 position, out Quaternion port))
            {
                if (!_warnedNoPort)
                    GameLog.WeaponSystem.Warning($"[WeaponSystem] {_weapon.name}: нет окна выброса — вылет {cue} пропущен (писатель WeaponSystemAuthoring).", _weapon);
                _warnedNoPort = true;
                return;
            }
            Vector3 local = ejectile.Velocity;
            float jitter = ejectile.VelocityJitter;
            if (jitter > 0f)
                local = new Vector3(local.x * (1f + Random.Range(-jitter, jitter)), local.y * (1f + Random.Range(-jitter, jitter)),
                    local.z * (1f + Random.Range(-jitter, jitter)));
            Vector3 velocity = port * local + _portVelocity;
            Vector3 spin = port * ejectile.Spin;
            // +Z корня префаба — к носику; в окне носик смотрит к дулу (+Z окна).
            _weapon.GetComponentsInChildren(false, _colliders);
            if (WeaponEjectaPool.Spawn(ejectile.Prefab, position, port, velocity, spin, ejectile.Lifetime, _colliders)) Spawned++;
            _colliders.Clear();
        }
    }
}

using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Ствол упёрт в геометрию — выстрела нет: осечка и вибрация контроллера.
    ///
    /// <para>
    /// <b>Зачем.</b> Снаряд UltimateXR рождается у дула (<c>ShotSource</c> на 1 см позади <c>Tip</c>),
    /// а луч попадания идёт от него вперёд. Ствол, просунутый сквозь стену, стрелял бы по ту
    /// сторону. Проверяется отрезок от казённой части (<see cref="_breech" />) до среза ствола
    /// капсулой <see cref="_radius" />: любой не-trigger коллайдер, кроме самого оружия и аватара,
    /// который его держит (с тем, что у него в руках), запрещает выстрел.
    /// </para>
    ///
    /// <para>
    /// <b>Как запрещается.</b> <c>UxrWeapon.IsUseBlocked</c> (патч SDK 15) → <c>CanUse</c> ложно →
    /// <c>TryToShootRound</c> не стреляет и патрон не тратит. Считается на каждой машине — ствол у
    /// всех в том же месте. Осечка и вибрация — только у держащего локального игрока, в момент
    /// нажатия спуска.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    public sealed class BarrelObstruction : MonoBehaviour
    {
        [Tooltip("Точка в ствольной коробке: от неё до среза ствола проверяется, свободен ли ствол.")]
        [SerializeField] private Transform _breech;

        [Tooltip("Радиус проверки ствола, метры.")]
        [SerializeField] private float _radius = 0.01f;

        [Tooltip("Индекс спуска UxrFirearmWeapon.")]
        [SerializeField] private int _triggerIndex;

        [Tooltip("Сила и длительность вибрации осечки.")]
        [SerializeField] private float _hapticAmplitude = 0.8f;
        [SerializeField] private float _hapticSeconds = 0.2f;

        private readonly Collider[] _overlaps = new Collider[16];
        private UxrFirearmWeapon _firearm;
        private UxrProjectileSource _source;
        private WeaponUseBlocker _blocking;

        public bool IsObstructed { get; private set; }
        public Transform Breech => _breech;

        private void Awake()
        {
            _firearm = GetComponent<UxrFirearmWeapon>();
            _source = GetComponent<UxrProjectileSource>();
            _blocking = GetComponent<WeaponUseBlocker>();
            if (_blocking == null) _blocking = gameObject.AddComponent<WeaponUseBlocker>();
        }

        private void OnDisable()
        {
            IsObstructed = false;
            if (_blocking != null) _blocking.SetBarrelObstructed(false);
        }

        private void Update()
        {
            if (!_firearm.TryGetTriggerGrip(_triggerIndex, out UxrGrabbableObject grip, out int gripPoint)) return;

            UxrGrabber grabber = null;
            bool held = UxrGrabManager.HasInstance && UxrGrabManager.Instance.GetGrabbingHand(grip, gripPoint, out grabber);

            if (!held)
            {
                IsObstructed = false;
                _blocking.SetBarrelObstructed(false);
                return;
            }

            IsObstructed = IsBarrelObstructed(grabber.Avatar != null ? grabber.Avatar.transform : null);
            _blocking.SetBarrelObstructed(IsObstructed);

            if (IsObstructed && grabber.Avatar != null && grabber.Avatar.AvatarMode == UxrAvatarMode.Local &&
                UxrAvatar.LocalAvatarInput.GetButtonsPressDown(grabber.Side, UxrInputButtons.Trigger))
            {
                _firearm.PlayTriggerNoAmmoSound(_triggerIndex, Muzzle.position);
                UxrAvatar.LocalAvatarInput.SendHapticFeedback(grabber.Side, UxrHapticClipType.RumbleFreqNormal, _hapticAmplitude, _hapticSeconds);
            }
        }

        /// <summary>
        /// Есть ли на стволе чужой коллайдер. <paramref name="owner" /> — корень аватара, который держит
        /// оружие: его тело и предметы в его руках не в счёт.
        /// </summary>
        public bool IsBarrelObstructed(Transform owner)
        {
            if (_breech == null) return false;

            Transform muzzle = Muzzle;
            Vector3 end = muzzle.position + muzzle.forward * _radius;
            int count = gameObject.scene.GetPhysicsScene().OverlapCapsule(_breech.position, end, _radius, _overlaps,
                                                                          Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = _overlaps[i].transform;
                if (hit.IsChildOf(transform)) continue;
                if (owner != null && hit.IsChildOf(owner)) continue;
                return true;
            }

            return false;
        }

        private Transform Muzzle
        {
            get
            {
                if (_firearm == null) _firearm = GetComponent<UxrFirearmWeapon>();
                if (_source == null) _source = GetComponent<UxrProjectileSource>();
                return _source.ShotTypes[_firearm.GetTriggerShotIndex(_triggerIndex)].Tip;
            }
        }
    }
}

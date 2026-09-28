using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Дробь: к каждому выстрелу добавляются дробинки с разбросом.
    ///
    /// <para>
    /// UltimateXR выпускает на выстрел один снаряд; «дробь» сэмплового дробовика — только широкий
    /// трассер и искры. Здесь на событие <see cref="UxrFirearmWeapon.ProjectileShot" /> выпускаются
    /// ещё <c>Pellets − 1</c> снарядов типа <see cref="_pelletShotIndex" /> в конусе
    /// <see cref="_spreadDegrees" /> от ствола.
    /// </para>
    ///
    /// <para>
    /// <b>Сеть.</b> Снаряды и дробь стрелка доезжают до остальных — и до сервера, который считает
    /// урон, — повтором синхронизированного <see cref="UxrProjectileSource.Shoot(int, Vector3, Quaternion)" />
    /// (замер T-24: сервер выстрелил 8 раз — у клиента 8 снарядов). Дробинки выпускаются тем же
    /// <c>Shoot</c> с уже посчитанным поворотом, поэтому случайный разброс у всех одинаков.
    /// <c>ProjectileShot</c> поднимается только у стрелка (патч SDK 23: копия в чужих руках выстрел
    /// не пересчитывает, эффекты у неё — <c>ProjectileShotReplayed</c>). До патча копия пересчитывала
    /// выстрел по синхронизированному спуску, поднимала <c>ProjectileShot</c>, и дробь множилась.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon), typeof(UxrProjectileSource))]
    public sealed class ShotgunPellets : MonoBehaviour
    {
        [Tooltip("Индекс спуска UxrFirearmWeapon, на выстрел которого добавляется дробь.")]
        [SerializeField] private int _triggerIndex;

        [Tooltip("Тип выстрела UxrProjectileSource для дробинок: без эффекта у дула, иначе вспышка на каждую.")]
        [SerializeField] private int _pelletShotIndex = 1;

        [Tooltip("Дробинок на выстрел вместе с основным снарядом.")]
        [SerializeField] [Min(1)] private int _pellets = 8;

        [Tooltip("Половина угла конуса разброса, градусы. 2.5° — около метра на 20 м.")]
        [SerializeField] [Range(0f, 15f)] private float _spreadDegrees = 2.5f;

        private UxrFirearmWeapon _firearm;
        private UxrProjectileSource _source;

        public int Pellets => _pellets;
        public int PelletShotIndex => _pelletShotIndex;
        public float SpreadDegrees => _spreadDegrees;

        private void Awake()
        {
            _firearm = GetComponent<UxrFirearmWeapon>();
            _source = GetComponent<UxrProjectileSource>();
        }

        private void OnEnable()
        {
            _firearm.ProjectileShot += Firearm_ProjectileShot;
        }

        private void OnDisable()
        {
            _firearm.ProjectileShot -= Firearm_ProjectileShot;
        }

        private void Firearm_ProjectileShot(int triggerIndex)
        {
            if (triggerIndex != _triggerIndex || _pelletShotIndex < 0 || _pelletShotIndex >= _source.ShotTypes.Count) return;

            Transform muzzle = _source.ShotTypes[_pelletShotIndex].ShotSource;
            for (int i = 1; i < _pellets; i++)
            {
                _source.Shoot(_pelletShotIndex, muzzle.position, muzzle.rotation * RandomSpread(_spreadDegrees));
            }
        }

        /// <summary>Случайное отклонение в конусе: равномерно по площади сечения, а не по углу.</summary>
        public static Quaternion RandomSpread(float spreadDegrees)
        {
            Vector2 offset = Random.insideUnitCircle * spreadDegrees;
            return Quaternion.Euler(offset.y, offset.x, 0f);
        }
    }
}

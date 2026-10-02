using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Разлёт дробин вокруг оси ствола (T-38). У пуль поправки направления нет:
    /// очередь уводит только реально повёрнутый отдачей ствол. Неточность CS2 не применяется.
    /// Патч SDK 34 нужен первой дробине, остальные выпускает ShotgunPellets с тем же конусом.
    /// Направления считает только стрелок (патч 23), по сети Shoot передаёт готовые повороты.
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    public sealed class WeaponSpread : MonoBehaviour
    {
        [SerializeField] private SpreadPattern _pattern = new SpreadPattern();

        private UxrFirearmWeapon _weapon;
        private ShotgunPellets _pellets;
        private WeaponAccuracy _accuracy;
        private uint _seed;
        private uint _shots;
        private uint _volleyShot;

        public SpreadPattern Pattern => _pattern;

        /// <summary>
        /// Старая модель оставлена для компиляции неизменённых тестов до проверки пользователем.
        /// В игровом выстреле не участвует; используются только SpreadOffset и ToRotation.
        /// </summary>
        public WeaponAccuracy Accuracy => _accuracy ??= new WeaponAccuracy(_pattern);

        private void Awake()
        {
            _weapon = GetComponent<UxrFirearmWeapon>();
            _pellets = GetComponent<ShotgunPellets>();
            _seed = (uint)GetInstanceID();
        }

        private void OnEnable()
        {
            // Защита старых префабов до Apply Weapon Balance: у пули хук не регистрируется.
            if (_pellets != null && _pellets.enabled && _pellets.Pellets > 1)
                _weapon.ShotOrientationModifier = ShotOrientation;
        }

        private void OnDisable()
        {
            if (_weapon.ShotOrientationModifier == (System.Func<int, Quaternion, Quaternion>)ShotOrientation)
                _weapon.ShotOrientationModifier = null;
        }

        /// <summary>Первая дробина — свой spread; пуля и другой спуск — строго по оси дула.</summary>
        public Quaternion ShotOrientation(int triggerIndex, Quaternion muzzle)
        {
            if (_pellets == null || !_pellets.isActiveAndEnabled || _pellets.Pellets <= 1 ||
                triggerIndex != _pellets.TriggerIndex)
                return muzzle;

            _volleyShot = _shots++;
            return muzzle * PelletDeviation(0);
        }

        /// <summary>Собственный разлёт дробины последнего залпа, без общего смещения неточности.</summary>
        public Quaternion PelletDeviation(int pellet) =>
            WeaponAccuracy.ToRotation(WeaponAccuracy.SpreadOffset(_pattern.Spread, _seed, _volleyShot,
                                                                 (uint)Mathf.Max(0, pellet)));
    }
}

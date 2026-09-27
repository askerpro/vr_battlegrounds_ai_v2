using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Component attached to instantiated weapons to easily identify their WeaponInfo
    /// for inventory and loadout management.
    /// </summary>
    public class WeaponComponent : MonoBehaviour
    {
        [SerializeField] private WeaponInfo _weaponInfo;

        public WeaponInfo WeaponData => _weaponInfo;

        /// <summary>
        /// Слот стены, который выдал это оружие, — его «дом». Ставится один раз при выдаче
        /// и дальше не меняется, какой бы путь ни прошёл ствол: рука, кобура, пол, чужой
        /// слот. Возврат оружия домой смотрит только сюда, а не на последний якорь
        /// (последним может быть кобура). Null — оружие выдано не стеной.
        /// </summary>
        public ArsenalSlotController HomeSlot { get; private set; }

        public void Init(WeaponInfo info)
        {
            _weaponInfo = info;
        }

        /// <summary>Запоминает слот-дом, если он ещё не задан.</summary>
        public void SetHomeIfUnset(ArsenalSlotController slot)
        {
            if (HomeSlot == null) HomeSlot = slot;
        }
    }
}

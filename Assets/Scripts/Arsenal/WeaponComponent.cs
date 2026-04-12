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

        public void Init(WeaponInfo info)
        {
            _weaponInfo = info;
        }
    }
}

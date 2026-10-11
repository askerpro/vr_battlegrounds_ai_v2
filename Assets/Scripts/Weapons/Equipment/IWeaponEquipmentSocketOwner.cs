using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Тело-владелец NPC-экипировки. Реализует клиповое AI-тело; без реализации binding не принимает claim.
    /// </summary>
    public interface IWeaponEquipmentSocketOwner
    {
        /// <summary>true только для тела под AI-управлением своей сессии.</summary>
        bool IsEquipmentOwner { get; }

        /// <summary>Сокет правой руки; null, пока тело не сконфигурировано.</summary>
        Transform EquipmentSocket { get; }
    }
}

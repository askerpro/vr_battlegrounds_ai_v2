using UnityEngine;
using VrBattlegrounds.Network;

using VrBattlegrounds.Core;
namespace VrBattlegrounds.Player.Avatars
{
    public abstract class AvatarSpawnStrategy : ScriptableObject
    {
        /// <summary>
        /// Возвращает префаб для спавна на основе параметров подключения игрока.
        /// </summary>
        /// <param name="session">Сессия с данными игрока.</param>
        /// <param name="globalFallback">Дефолтный префаб из AvatarManager на случай, если стратегия не может вернуть свой.</param>
        public abstract GameObject GetPrefab(PlayerSession session, GameObject globalFallback);
    }
}

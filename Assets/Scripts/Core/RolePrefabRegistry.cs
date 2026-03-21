using System;
using UnityEngine;

namespace VrBattlegrounds.Core
{
    [Serializable]
    public struct RolePrefabMapping
    {
        public ClientDeviceRole role;
        public GameObject prefab;
    }

    /// <summary>
    /// Реестр для хранения соответствий между ролями клиентов и их префабами.
    /// Позволяет не привязывать GameNetworkManager к конкретным ролям или префабам.
    /// </summary>
    [CreateAssetMenu(fileName = "RolePrefabRegistry", menuName = "VrBattlegrounds/Core/Role Prefab Registry")]
    public class RolePrefabRegistry : ScriptableObject
    {
        [Tooltip("Список маппингов ролей к префабам игроков (например, TabletAdmin -> AdminPrefab). Если роли нет в списке, используется стандартный playerPrefab.")]
        public RolePrefabMapping[] rolePrefabs;
    }
}

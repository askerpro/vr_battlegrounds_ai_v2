using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.UI.Menu
{
    public enum SceneMenuContext
    {
        Offline,
        Lobby,
        InGame
    }

    [System.Serializable]
    public struct GameModeMenuMapping
    {
        public GameModeData GameMode;
        public GameObject MenuPrefab;
    }

    [System.Serializable]
    public class RoleMenuConfig
    {
        public ClientDeviceRole Role;

        [Header("Scene Menus")]
        public GameObject OfflineMenuPrefab;
        public GameObject LobbyMenuPrefab;
        
        [Header("In-Game Menus")]
        [Tooltip("Fallback menu if a specific Game Mode menu isn't found")]
        public GameObject DefaultGameMenuPrefab; 
        public List<GameModeMenuMapping> SpecificGameModeMenus = new List<GameModeMenuMapping>();
    }

    [CreateAssetMenu(fileName = "MenuPrefabRegistry", menuName = "VrBattlegrounds/UI/Menu Prefab Registry")]
    public class MenuPrefabRegistry : ScriptableObject
    {
        [Tooltip("Configure menus per role. E.g. one for Player, one for VRAdmin.")]
        public List<RoleMenuConfig> RoleConfigurations = new List<RoleMenuConfig>();

        public GameObject GetMenuPrefab(ClientDeviceRole role, SceneMenuContext context, GameModeData currentGameMode = null)
        {
            var config = RoleConfigurations.Find(c => c.Role == role);
            if (config == null) return null;

            switch (context)
            {
                case SceneMenuContext.Offline:
                    return config.OfflineMenuPrefab;

                case SceneMenuContext.Lobby:
                    return config.LobbyMenuPrefab;

                case SceneMenuContext.InGame:
                    if (currentGameMode != null)
                    {
                        var specificMatch = config.SpecificGameModeMenus.Find(m => m.GameMode == currentGameMode);
                        if (specificMatch.MenuPrefab != null)
                            return specificMatch.MenuPrefab;
                    }
                    return config.DefaultGameMenuPrefab;

                default:
                    return null;
            }
        }
    }
}

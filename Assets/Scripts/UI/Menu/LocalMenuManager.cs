using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Network;
using VrBattlegrounds.Core;
using System.Collections;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Локальный оркестратор меню (живет в Offline сцене, DontDestroyOnLoad вместе с MANAGERS).
    /// Отвечает за инстанцирование правильного префаба меню (Root) в зависимости от контекста:
    /// Админ/Игрок и Лобби/Игра.
    /// </summary>
    public class LocalMenuManager : MonoBehaviour
    {
        [Header("Menu Registry")]
        [SerializeField] private MenuPrefabRegistry _menuRegistry;

        private GameObject _activeMenuInstance;

        private void Start()
        {
            GameNetworkManager.ServerSceneChanged += OnServerSceneChanged;
            SceneManager.sceneLoaded += OnUnitySceneLoaded;

            SpawnMenuForCurrentContext();
        }

        private void OnDestroy()
        {
            GameNetworkManager.ServerSceneChanged -= OnServerSceneChanged;
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        }

        private void OnServerSceneChanged(string sceneName)
        {
            StartCoroutine(SpawnMenuDelayed());
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(SpawnMenuDelayed());
        }

        private IEnumerator SpawnMenuDelayed()
        {
            yield return null;
            SpawnMenuForCurrentContext();
        }

        private void SpawnMenuForCurrentContext()
        {
            if (_activeMenuInstance != null)
            {
                Destroy(_activeMenuInstance);
                _activeMenuInstance = null;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            string scenePath = SceneManager.GetActiveScene().path;

            SceneMenuContext context = SceneMenuContext.InGame;

            if (GameNetworkManager.singleton != null)
            {
                string offlineSc = GameNetworkManager.singleton.offlineScene;
                string onlineSc = GameNetworkManager.singleton.onlineScene;

                if (!string.IsNullOrEmpty(offlineSc) && (sceneName == offlineSc || scenePath == offlineSc))
                    context = SceneMenuContext.Offline;
                else if (!string.IsNullOrEmpty(onlineSc) && (sceneName == onlineSc || scenePath == onlineSc))
                    context = SceneMenuContext.Lobby;
            }
            else
            {
                // Fallback in case NetworkManager isn't available
                if (sceneName == "Offline") context = SceneMenuContext.Offline;
                else if (sceneName == "Lobby") context = SceneMenuContext.Lobby;
            }

            ClientDeviceType deviceType = LocalClientProfile.LocalDeviceType;
            GameObject prefabToSpawn = null;

            if (_menuRegistry != null)
            {
                // In the future, pass currentGameMode instead of null to allow custom menus per Game Mode
                prefabToSpawn = _menuRegistry.GetMenuPrefab(deviceType, context, null);
                GameLog.UI.Info($"[LocalMenuManager] Spawn context menu (Device: {deviceType}, Context: {context}) -> {(prefabToSpawn != null ? prefabToSpawn.name : "NULL")}");
            }
            else
            {
                GameLog.Error("[LocalMenuManager] _menuRegistry is not assigned! Cannot spawn UI.");
            }

            if (prefabToSpawn != null)
            {
                _activeMenuInstance = Instantiate(prefabToSpawn, transform);
                _activeMenuInstance.SetActive(false); // <--- Скрываем весь префаб (включая планшет) сразу после спавна 

                GameLog.UI.Verbose($"[LocalMenuManager] Спавн контекст-меню: {prefabToSpawn.name}");

                MenuView view = _activeMenuInstance.GetComponent<MenuView>();
                if (view != null) 
                {
                    if (MenuController.Instance == null)
                    {
                        GameLog.UI.Info("[LocalMenuManager] MenuController missing on Managers. Auto-adding it.");
                        gameObject.AddComponent<MenuController>();
                    }
                    
                    MenuController.Instance.Initialize(view);
                }
                else
                {
                    GameLog.UI.Warning($"[LocalMenuManager] Prefab {prefabToSpawn.name} is missing a MenuView component! MVC injection skipped.");
                }
            }
            else
            {
                GameLog.UI.Warning("[LocalMenuManager] Нет префаба для текущего контекста. Меню не заспавнено.");
            }
        }
    }
}

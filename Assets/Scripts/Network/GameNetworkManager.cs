using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Единственная ответственность: управление сетевыми подключениями.
    /// Логика команд и старт матча — в подписчиках событий (GameplayManager, DebugOrchestrator).
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        [Header("Roles & Prefabs")]
        [Tooltip("Реестр маппингов ролей на префабы. Если роль не найдена, спавнится стандартный playerPrefab.")]
        [SerializeField] private RolePrefabRegistry _roleRegistry;

        /// <summary>Игрок подключился и спавнился на сервере.</summary>
        public static event Action<PlayerController> PlayerConnected;

        /// <summary>Игрок отключился от сервера.</summary>
        public static event Action<PlayerController> PlayerDisconnected;

        /// <summary>Сервер завершил загрузку сцены. Параметр — имя загруженной сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        [Header("Server Context")]
        [Tooltip("Префаб SessionManager, который будет спавниться при старте сервера.")]
        [SerializeField] private GameObject _sessionContextPrefab;

        public override void Awake()
        {
            base.Awake();
            // Отключаем автоматический спавн Mirror, так как будем сами отправлять RoleJoinMessage
            autoCreatePlayer = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_sessionContextPrefab != null)
            {
                GameObject sessionInstance = Instantiate(_sessionContextPrefab);
                NetworkServer.Spawn(sessionInstance);
                GameLog.Info(GameSettings.Instance.LogLevelNetwork, "[GameNetworkManager] SessionContext (SessionManager) успешно заспавнен сервером.");
            }
            else
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, "[GameNetworkManager] Префаб SessionContext не назначен, сессия не будет отслеживаться!");
            }
            
            NetworkServer.RegisterHandler<RoleJoinMessage>(OnRoleJoinMessage);
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            ServerSceneChanged?.Invoke(sceneName);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            PlayerController player = conn.identity.GetComponent<PlayerController>();
            if (player == null)
                return;

            PlayerConnected?.Invoke(player);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn.identity != null)
            {
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player != null)
                {
                    PlayerDisconnected?.Invoke(player);
                }
            }

            base.OnServerDisconnect(conn);
        }

        // ==============================================================================
        // CUSTOM SPAWN LOGIC (Role-based)
        // ==============================================================================

        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged();
            
            // Если сцена загрузилась и мы готовы, отправляем серверу запрос на спавн с нашей ролью
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
            {
                NetworkClient.Send(new RoleJoinMessage { role = AppRoleManager.LocalRole });
            }
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            
            // Если мы подключились и сцена уже загружена (NetworkManager базовый делает нас ready)
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
            {
                NetworkClient.Send(new RoleJoinMessage { role = AppRoleManager.LocalRole });
            }
        }

        private void OnRoleJoinMessage(NetworkConnectionToClient conn, RoleJoinMessage msg)
        {
            GameLog.Info(GameSettings.Instance.LogLevelNetwork, $"[GameNetworkManager] Получен RoleJoinMessage: {msg.role}");

            if (conn.identity != null)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, "[GameNetworkManager] Игрок уже заспавнен для этого соединения.");
                return;
            }

            GameObject prefabToSpawn = playerPrefab;

            if (_roleRegistry != null && _roleRegistry.rolePrefabs != null)
            {
                foreach (var mapping in _roleRegistry.rolePrefabs)
                {
                    if (mapping.role == msg.role && mapping.prefab != null)
                    {
                        prefabToSpawn = mapping.prefab;
                        break;
                    }
                }
            }

            if (prefabToSpawn == null)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, $"[GameNetworkManager] Не найден префаб для спавна роли {msg.role} (playerPrefab и RolePrefabMapping не настроены)!");
                return;
            }

            Transform startPos = GetStartPosition();
            GameObject playerInstance = startPos != null
                ? Instantiate(prefabToSpawn, startPos.position, startPos.rotation)
                : Instantiate(prefabToSpawn);

            playerInstance.name = $"{prefabToSpawn.name} [connId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, playerInstance);
        }    
    }
}

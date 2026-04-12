using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Единственная ответственность: управление сетевыми подключениями.
    /// Логика команд и старт матча — в подписчиках событий (GameplayManager, DebugOrchestrator).
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {

        /// <summary>Сервер завершил загрузку сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        [Header("Server Context")]
        [Tooltip("Префаб SessionManager, который будет спавниться при старте сервера.")]
        [SerializeField] private GameObject _sessionContextPrefab;

        public override void Awake()
        {
            base.Awake();
            autoCreatePlayer = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_sessionContextPrefab != null)
            {
                GameObject sessionInstance = Instantiate(_sessionContextPrefab);
                NetworkServer.Spawn(sessionInstance);
            }

            NetworkServer.RegisterHandler<GamePlayerConnectMessage>(OnGamePlayerConnect);
            NetworkServer.RegisterHandler<SpectatorConnectMessage>(OnSpectatorConnect);
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            ServerSceneChanged?.Invoke(sceneName);
        }

        // Вызывается когда клиент базово подключился, но мы ждем PlayerJoinMessage для спавна
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Мы не спавним ничего автоматически. Спавн идет в OnPlayerJoinMessage
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (PlayersManager.Instance != null)
            {
                PlayersManager.Instance.UnregisterSession(conn);
            }

            base.OnServerDisconnect(conn); // Mirror автоматически уничтожает Owned objects (сессию и аватар)
        }

        // ==============================================================================
        // CUSTOM SPAWN LOGIC (Role-based)
        // ==============================================================================

        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged();
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
                SendConnectMessage();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
                SendConnectMessage();
        }

        private void SendConnectMessage()
        {
            // Берем или генерируем deviceToken для сессии (сохраняется у клиента локально)
            string token = UnityEngine.PlayerPrefs.GetString("DeviceToken", "");
            if (string.IsNullOrEmpty(token))
            {
                token = System.Guid.NewGuid().ToString();
                UnityEngine.PlayerPrefs.SetString("DeviceToken", token);
                UnityEngine.PlayerPrefs.Save();
            }

#if UNITY_EDITOR
            // Чтобы редактор и билд на одном ПК (имеющие общие PlayerPrefs),
            // а также клоны редактора (ParrelSync) воспринимались сервером как разные устройства:
            token += "_editor_" + UnityEngine.Application.dataPath.GetHashCode();
#endif

            if (LocalClientProfile.LocalRole == GameRole.Player)
            {
                var msg = new GamePlayerConnectMessage(
                    token, 
                    LocalClientProfile.LocalDeviceType, 
                    1, 
                    0
                );

                NetworkClient.Send(msg);
            }
            else
            {
                var msg = new SpectatorConnectMessage
                {
                    deviceToken = token,
                    deviceType = LocalClientProfile.LocalDeviceType,
                    isAdmin = LocalClientProfile.IsLocalAdmin
                };
                NetworkClient.Send(msg);
            }
        }

        // Removed SetupPlayerSession as it's now internal to PlayersManager

        private void OnGamePlayerConnect(NetworkConnectionToClient conn, GamePlayerConnectMessage msg)
        {
            if (PlayersManager.Instance != null)
            {
                PlayersManager.Instance.HandlePlayerConnect(conn, msg);
            }
            else
            {
                GameLog.Error("[GameNetworkManager] PlayersManager is missing! Cannot handle player connection.");
            }
        }

        private void OnSpectatorConnect(NetworkConnectionToClient conn, SpectatorConnectMessage msg)
        {
            GameLog.Warning(GameSettings.Instance.LogLevelNetwork, $"[GameNetworkManager] Подключение Spectator (Device: {msg.deviceType}, Admin: {msg.isAdmin}) пока не реализовано.");
        }

    }
}

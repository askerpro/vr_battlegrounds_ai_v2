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
    /// Логика команд и старт матча — в подписчиках событий (MapReferee, DebugOrchestrator).
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.GameNetworkManager)]
    public class GameNetworkManager : NetworkManager
    {

        /// <summary>Сервер завершил загрузку сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        /// <summary>
        /// Клиент завершил загрузку сцены. Всё, что было в предыдущей, уничтожено,
        /// поэтому подписчикам нужно заново получить состояние сцены с сервера —
        /// см. <see cref="NetworkStateRelay"/>.
        /// </summary>
        public static event Action ClientSceneChanged;

        [Header("Server Context")]
        [Tooltip("Префаб SessionManager, который будет спавниться при старте сервера.")]
        [SerializeField] private GameObject _sessionContextPrefab;

        [Header("Карты")]
        [Tooltip("Центральный каталог служб и содержимого карт для MapBootstrap. Один на процесс, не на карту.")]
        [SerializeField] private Maps.Runtime.MapRuntimeCatalog _mapRuntimeCatalog;

        /// <summary>Установленный каталог запуска карт; null — менеджер не поднят или каталог не назначен.</summary>
        public static Maps.Runtime.MapRuntimeCatalog MapCatalog =>
            singleton is GameNetworkManager manager ? manager._mapRuntimeCatalog : null;

        public override void Awake()
        {
            base.Awake();
            autoCreatePlayer = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Ники живут, пока жив сервер: новый сервер — новая встреча.
            AdminNaming.ClearNicknames();

            if (_sessionContextPrefab != null)
            {
                GameObject sessionInstance = Instantiate(_sessionContextPrefab);
                NetworkServer.Spawn(sessionInstance);
            }

            NetworkServer.RegisterHandler<GamePlayerConnectMessage>(OnGamePlayerConnect);
            NetworkServer.RegisterHandler<SpectatorConnectMessage>(OnSpectatorConnect);
            VrBattlegrounds.DevTools.StressTest.StressTestNetwork.RegisterServerHandlers();
            VrBattlegrounds.DevTools.DebugModeNetwork.RegisterServerHandlers();
            VrBattlegrounds.Bots.BotNetwork.RegisterServerHandlers();
            VrBattlegrounds.Bots.BotMatchNetwork.RegisterServerHandlers();

            // У хоста первая сцена уже загружена (Mirror зовёт OnStartServer после неё): теперь есть Series,
            // и маршрут старта захватывает режим. У выделенного сервера сцена ещё впереди — захват в ServerChangeScene.
            ServerStartupRoute.OnServerStarted();
        }

        /// <summary>
        /// Единственная точка первой смены сцены сервера и для StartServer, и для StartHost: при запросе
        /// <see cref="ServerStartupRoute"/> сервер стартует сразу в цель вместо <c>onlineScene</c>. Поле
        /// <c>onlineScene</c> не меняется (NET-21); остальные смены сцены идут без изменений.
        /// </summary>
        public override void ServerChangeScene(string newSceneName)
        {
            base.ServerChangeScene(ServerStartupRoute.ResolveServerScene(newSceneName, onlineScene));
        }

        public override void OnStopServer()
        {
            ServerStartupRoute.OnServerStopped();
            base.OnStopServer();
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            ServerSceneChanged?.Invoke(sceneName);
        }

        /// <summary>
        /// Клиент поднялся. Сразу подменяем спавн сетевых префабов с компонентами
        /// UltimateXR: их <c>UniqueId</c> обязан совпасть с серверным, иначе ни один
        /// захват, выстрел или вставка магазина не будут применены на другой машине
        /// (находка NET-16). Подробности — <see cref="NetworkUxrIdentity"/>.
        ///
        /// Позже нельзя: спавн-сообщения приходят сразу после готовности клиента.
        /// Раньше тоже нельзя: штатную регистрацию префабов Mirror делает в
        /// <c>RegisterClientMessages</c>, то есть непосредственно перед этим вызовом.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkUxrIdentity.InstallClientSpawnHandlers();
            VrBattlegrounds.DevTools.StressTest.StressTestNetwork.RegisterClientHandlers();
            VrBattlegrounds.DevTools.DebugModeNetwork.RegisterClientHandlers();
        }

        public override void OnStopClient()
        {
            GameNetworkDiscovery.NotifyClientStopped(LocalClientProfile.HasTemporaryOverride);
            NetworkUxrIdentity.UninstallClientSpawnHandlers();
            base.OnStopClient();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Мы не спавним ничего автоматически. Спавн идет в OnPlayerJoinMessage
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);

            // Когда клиент полностью загрузил новую сцену, Mirror уничтожил старый аватар.
            // Нам нужно возродить его, если PlayerSession остался жив.
            if (PlayersManager.Instance != null && AvatarManager.Instance != null)
            {
                var session = PlayersManager.Instance.GetSession(conn);
                if (session != null && session.ActiveAvatar == null)
                {
                    // Спавним новый физический аватар на основе существующих данных (TeamIndex / AvatarIndex)
                    AvatarManager.Instance.ChangeAvatar(conn, session, session.TeamIndex, session.AvatarIndex);
                }
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (PlayersManager.Instance != null)
            {
                // Аватар уничтожит Mirror в base-вызове ниже — освобождаем его заранее, как
                // при смене скина: иначе у хоста и клиентов остаётся захват мёртвой руки.
                // Снимок сессии (UnregisterSession) снимается уже после: здоровье и место
                // от сброса снаряжения не зависят.
                PlayerSession session = PlayersManager.Instance.GetSession(conn);
                if (session != null)
                {
                    AvatarTeardown.ReleaseBeforeDestroy(session.ActiveAvatar, "отключение");
                }

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

            ClientSceneChanged?.Invoke();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            if (NetworkClient.ready && NetworkClient.connection.identity == null)
                SendConnectMessage();
        }

        private void SendConnectMessage()
        {
            string token = VrBattlegrounds.Core.ClientDeviceIdentity.ConnectionToken;

            if (LocalClientProfile.LocalRole == GameRole.Player)
            {
                var msg = new GamePlayerConnectMessage(
                    token, 
                    LocalClientProfile.LocalDeviceType, 
                    0, 
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
            GameLog.Network.Warning($"[GameNetworkManager] Подключение Spectator (Device: {msg.deviceType}, Admin: {msg.isAdmin}) пока не реализовано.");
        }

    }
}

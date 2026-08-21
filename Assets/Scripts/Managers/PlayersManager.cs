using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Хранит актуальный список подключённых игроков и предоставляет
    /// игровые запросы (живые, по команде).
    /// Подписывается на события <see cref="GameNetworkManager"/> —
    /// не зависит от деталей Mirror напрямую.
    /// Только сервер.
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.PlayersManager)]
    public class PlayersManager : MonoBehaviour
    {
        public static PlayersManager Instance { get; private set; }

        [Header("Отладка (только чтение)")]
        [Tooltip("Префаб PlayerSession (сетевой менеджер игрока).")]
        [SerializeField] private GameObject _playerSessionPrefab;

        [Tooltip("Список имён всех подключённых игроков (только для инспектора, обновляется автоматически)")]
        [SerializeField] private string[] _playerNames = new string[0];

        private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
        private readonly Dictionary<NetworkConnection, PlayerSession> _sessionsByConn = new Dictionary<NetworkConnection, PlayerSession>();

        public static event Action<PlayerSession> OnSessionConnected;
        public static event Action<PlayerSession> OnSessionDisconnected;

        /// <summary>
        /// Все подключённые сессии игроков (только для чтения).
        /// </summary>
        public IReadOnlyList<PlayerSession> Sessions => _sessions;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                GameLog.Network.Warning("[PlayersManager] Awake: Instance уже существует, уничтожаю дубликат.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            GameLog.Network.Info("[PlayersManager] Awake: Instance установлен.");
        }

        private PlayerSession CreatePlayerSession(NetworkConnectionToClient conn, GameRole role, string deviceToken, ClientDeviceType deviceType, bool isAdmin, SessionSnapshot snapshot = null, int initialTeamId = 0, int initialAvatarId = 0)
        {
            if (_playerSessionPrefab == null)
            {
                GameLog.Error("[PlayersManager] _playerSessionPrefab is missing! Cannot spawn session.");
                return null;
            }

            GameObject sessionGO = Instantiate(_playerSessionPrefab);
            PlayerSession session = sessionGO.GetComponent<PlayerSession>();
            session.DeviceToken = deviceToken;
            session.DeviceType = deviceType;
            session.IsAdmin = isAdmin;
            session.Role = role;

            if (snapshot != null)
            {
                session.PlayerName = snapshot.PlayerName;
                session.TeamIndex = snapshot.TeamIndex;
                session.AvatarIndex = snapshot.AvatarIndex;
                session.Kills = snapshot.Kills;
                session.Deaths = snapshot.Deaths;
                session.Score = snapshot.Score;
            }
            else
            {
                session.PlayerName = role + "_" + UnityEngine.Random.Range(1000, 9999);
                session.TeamIndex = initialTeamId;
                session.AvatarIndex = initialAvatarId;
            }

            NetworkServer.AddPlayerForConnection(conn, sessionGO);
            RegisterSession(conn, session);

            return session;
        }

        public void HandlePlayerConnect(NetworkConnectionToClient conn, GamePlayerConnectMessage msg)
        {
            GameLog.Network.Info($"[PlayersManager] Получен GamePlayerConnectMessage. Device: {msg.deviceType}");
            if (conn.identity != null) return;

            SessionSnapshot snapshot = null;
            if (SessionRecoveryManager.Instance != null)
            {
                snapshot = SessionRecoveryManager.Instance.GetAndRemoveSavedSession(msg.deviceToken);
            }

            if (snapshot != null)
            {
                msg.teamId = snapshot.TeamIndex;
                msg.avatarId = snapshot.AvatarIndex;
            }

            PlayerSession session = CreatePlayerSession(conn, GameRole.Player, msg.deviceToken, msg.deviceType, false, snapshot, msg.teamId, msg.avatarId);

            // Спавним физический аватар
            if (AvatarManager.Instance != null)
            {
                AvatarManager.Instance.SpawnAvatar(conn, msg, snapshot, session);
            }
            else
            {
                GameLog.Error("[PlayersManager] AvatarManager is missing! Cannot spawn pawn.");
            }
        }

        public void RegisterSession(NetworkConnection conn, PlayerSession session)
        {
            _sessionsByConn[conn] = session;
            if (!_sessions.Contains(session))
            {
                _sessions.Add(session);
            }
            UpdateDebugNames();
            OnSessionConnected?.Invoke(session);
        }

        public void UnregisterSession(NetworkConnection conn)
        {
            if (_sessionsByConn.TryGetValue(conn, out PlayerSession session))
            {
                // Сохраняем стейт в память перед отключением
                if (SessionRecoveryManager.Instance != null && !string.IsNullOrEmpty(session.DeviceToken))
                {
                    // conn.identity — это PlayerSession (она назначена объектом игрока в
                    // AddPlayerForConnection), а не аватар. Раньше здесь был
                    // conn.identity.GetComponent<PlayerController>(), который всегда возвращал null,
                    // из-за чего здоровье и позиция не сохранялись никогда.
                    SessionRecoveryManager.Instance.SaveDisconnectedSession(session.DeviceToken, session, session.ActiveAvatar);
                }

                _sessions.Remove(session);
                _sessionsByConn.Remove(conn);
                UpdateDebugNames();
                OnSessionDisconnected?.Invoke(session);
            }
        }

        public PlayerSession GetSession(NetworkConnection conn)
        {
            return _sessionsByConn.TryGetValue(conn, out PlayerSession session) ? session : null;
        }

        private void UpdateDebugNames()
        {
            _playerNames = _sessions.Select(s => s != null ? s.PlayerName : "null").ToArray();
        }

        /// <summary>Только живые сессии указанной команды (у которых заспавнен аватар и он жив).</summary>
        public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerSession>();

            return _sessions.Where(s =>
                s.TeamIndex == team.teamIndex &&
                s.ActiveAvatar != null &&
                s.ActiveAvatar.IsAlive);
        }

        /// <summary>Все сессии указанной команды.</summary>
        public IEnumerable<PlayerSession> GetPlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerSession>();

            return _sessions.Where(s => s.TeamIndex == team.teamIndex);
        }
    }
}

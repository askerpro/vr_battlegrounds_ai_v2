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

        public static event Action<PlayerSession> SessionConnected;
        public static event Action<PlayerSession> SessionDisconnected;

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
                // Ник, который админ дал этому устройству раньше (AdminNaming), — иначе случайный.
                session.PlayerName = AdminNaming.NicknameFor(deviceToken) ?? role + "_" + UnityEngine.Random.Range(1000, 9999);
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

            ApplyPhysicalPlace(session, msg, snapshot);

            // Спавним физический аватар
            if (AvatarManager.Instance != null)
            {
                AvatarManager.Instance.SpawnAvatar(conn, snapshot, session);
            }
            else
            {
                GameLog.Error("[PlayersManager] AvatarManager is missing! Cannot spawn pawn.");
            }
        }

        /// <summary>
        /// Раскладывает по местам то, что игрок принёс с собой о своём <b>физическом</b>
        /// положении: признак калибровки и место в системе координат якорей.
        ///
        /// <para>
        /// Зачем это здесь. Точку спавна сервер выбирает прямо сейчас, в обработке
        /// сообщения подключения, — то есть до того, как у клиента появится сессия,
        /// из которой можно было бы прислать <c>CmdSetCalibrated</c>. Признак поэтому
        /// едет в самом сообщении. Снимок отключённой сессии, если он есть, знает то же
        /// самое и заведомо не хуже: он снят сервером.
        /// </para>
        ///
        /// <para>
        /// Место кладётся в реестр <b>всегда</b>, а решает, применять его или нет,
        /// <c>SpawnPlaceRegistry.TryResolve</c> — по признаку калибровки. Гейт один
        /// на весь проект, и это тот же гейт, что при смене карты (T-30): второй,
        /// поставленный здесь, разъехался бы с первым при первой же правке. Находка,
        /// ради которой всё это, — <b>CAL-02</b>: раньше позиция из сообщения
        /// применялась дословно и всем подряд.
        /// </para>
        /// </summary>
        private static void ApplyPhysicalPlace(PlayerSession session, GamePlayerConnectMessage msg,
                                               SessionSnapshot snapshot)
        {
            if (session == null) return;

            session.IsCalibrated = msg.isCalibrated || (snapshot != null && snapshot.IsCalibrated);

            if (!msg.hasAnchorPlace) return;

            SpawnPlaceRegistry.Remember(session.netId, msg.anchorPlacePosition,
                                            msg.anchorPlaceRotation, msg.anchorPlaceMap);

            GameLog.PhysicalSpace.Info(
                $"[PlayersManager] {session.PlayerName} принёс своё место с карты '{msg.anchorPlaceMap}': " +
                $"относительно якорей {msg.anchorPlacePosition}, откалиброван={session.IsCalibrated} " +
                $"(сообщение={msg.isCalibrated}, снимок={(snapshot != null ? snapshot.IsCalibrated.ToString() : "нет")}).");
        }

        public void RegisterSession(NetworkConnection conn, PlayerSession session)
        {
            _sessionsByConn[conn] = session;
            if (!_sessions.Contains(session))
            {
                _sessions.Add(session);
            }
            UpdateDebugNames();
            SessionConnected?.Invoke(session);
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
                SessionDisconnected?.Invoke(session);
            }
        }

        /// <summary>
        /// Создаёт и регистрирует сессию бота: тот же префаб, что у игрока, но спавн без
        /// владельца (<c>NetworkServer.Spawn</c> вместо <c>AddPlayerForConnection</c>).
        /// Аватар создаёт вызывающий — <c>AvatarManager.SpawnAvatar(null, …)</c>.
        /// </summary>
        public PlayerSession CreateBotSession(string playerName, string deviceToken)
        {
            if (_playerSessionPrefab == null)
            {
                GameLog.Error("[PlayersManager] _playerSessionPrefab is missing! Cannot spawn bot session.");
                return null;
            }

            GameObject sessionGO = Instantiate(_playerSessionPrefab);
            sessionGO.name = $"PlayerSession [{playerName}]";
            PlayerSession session = sessionGO.GetComponent<PlayerSession>();
            session.DeviceToken = deviceToken;
            session.DeviceType = ClientDeviceType.PC;
            session.IsAdmin = false;
            session.Role = GameRole.Player;
            session.PlayerName = playerName;

            NetworkServer.Spawn(sessionGO);
            RegisterBot(session);
            return session;
        }

        /// <summary>
        /// Регистрирует сессию без соединения — бота (<c>DevTools.Bots.BotDirector</c>).
        /// Для игровой логики бот неотличим от игрока: тот же список <see cref="Sessions"/>
        /// и то же событие <see cref="SessionConnected"/> (режим раздаёт команды).
        /// В словарь соединений не попадает: соединения нет, ключ был бы <c>null</c>.
        /// </summary>
        public void RegisterBot(PlayerSession session)
        {
            if (session == null || _sessions.Contains(session)) return;

            _sessions.Add(session);
            UpdateDebugNames();
            SessionConnected?.Invoke(session);
        }

        /// <summary>
        /// Убирает бота из списка. Снимок для переподключения не пишется: переподключаться
        /// боту неоткуда. Аватар и сессию уничтожает вызывающий.
        /// </summary>
        public void UnregisterBot(PlayerSession session)
        {
            if (session == null || _sessionsByConn.ContainsValue(session)) return;
            if (!_sessions.Remove(session)) return;

            UpdateDebugNames();
            SessionDisconnected?.Invoke(session);
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

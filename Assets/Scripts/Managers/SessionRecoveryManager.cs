using UnityEngine;
using System.Collections.Generic;
using VrBattlegrounds.Player;
using VrBattlegrounds.Core;
using Mirror;

namespace VrBattlegrounds.Managers
{
    public class SessionSnapshot
    {
        public string DeviceToken;
        public string PlayerName;
        public int TeamIndex;
        public int AvatarIndex;

        // Stats
        public int Kills;
        public int Deaths;
        public int Score;

        // Physical state to restore
        public float Health;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool NeedsPhysicalRestore;
    }

    /// <summary>
    /// Серверный менеджер для сохранения состояния игроков при отключении и восстановления при переподключении.
    /// Работает поверх Mirror, идентифицируя игроков по deviceToken.
    /// </summary>
    public class SessionRecoveryManager : MonoBehaviour
    {
        public static SessionRecoveryManager Instance { get; private set; }

        private Dictionary<string, SessionSnapshot> _disconnectedSessions = new Dictionary<string, SessionSnapshot>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Server]
        public void SaveDisconnectedSession(string deviceToken, PlayerSession session, PlayerController avatar)
        {
            if (string.IsNullOrEmpty(deviceToken) || session == null) return;

            var snapshot = new SessionSnapshot
            {
                DeviceToken = deviceToken,
                PlayerName = session.PlayerName,
                TeamIndex = session.TeamIndex,
                AvatarIndex = session.AvatarIndex,
                Kills = session.Kills,
                Deaths = session.Deaths,
                Score = session.Score,
                NeedsPhysicalRestore = false
            };

            if (avatar != null)
            {
                snapshot.Health = avatar.Health;
                snapshot.Position = avatar.transform.position;
                snapshot.Rotation = avatar.transform.rotation;

                // Спавним на месте только если игрок был жив
                if (avatar.IsAlive)
                {
                    snapshot.NeedsPhysicalRestore = true;
                }
            }

            _disconnectedSessions[deviceToken] = snapshot;
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[SessionRecoveryManager] Saved session for {session.PlayerName} (Token: {deviceToken}).");
        }

        [Server]
        public SessionSnapshot GetAndRemoveSavedSession(string deviceToken)
        {
            if (string.IsNullOrEmpty(deviceToken)) return null;

            if (_disconnectedSessions.TryGetValue(deviceToken, out SessionSnapshot snapshot))
            {
                _disconnectedSessions.Remove(deviceToken);
                GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[SessionRecoveryManager] Restoring session for {snapshot.PlayerName} (Token: {deviceToken}).");
                return snapshot;
            }

            return null;
        }
    }
}

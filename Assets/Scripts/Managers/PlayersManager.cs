using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Хранит актуальный список подключённых игроков и предоставляет
    /// игровые запросы (живые, по команде).
    /// Подписывается на события <see cref="GameNetworkManager"/> —
    /// не зависит от деталей Mirror напрямую.
    /// Только сервер.
    /// </summary>
    public class PlayersManager : MonoBehaviour
    {
        public static PlayersManager Instance { get; private set; }

        [Header("Отладка (только чтение)")]
        [Tooltip("Список имён всех подключённых игроков (только для инспектора, обновляется автоматически)")]
        [SerializeField] private string[] _playerNames = new string[0];

        /// <summary>
        /// Все подключённые игроки (только для чтения, динамически из NetworkServer.connections).
        /// </summary>
        public IReadOnlyList<PlayerController> Players
        {
            get
            {
                if (!NetworkServer.active)
                    return System.Array.Empty<PlayerController>();
                var list = Mirror.NetworkServer.connections.Values
                    .Select(conn => conn != null && conn.identity != null ? conn.identity.GetComponent<PlayerController>() : null)
                    .Where(pc => pc != null)
                    .ToList();
                // Для инспектора
                _playerNames = list.Select(p => p != null ? p.name : "null").ToArray();
                return list;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, "[PlayersManager] Awake: Instance уже существует, уничтожаю дубликат.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            GameLog.Info(GameSettings.Instance.LogLevelNetwork, "[PlayersManager] Awake: Instance установлен.");
        }

        /// <summary>Только живые игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetAlivePlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerController>();
            return Players.Where(p => p.TeamIndex == team.teamIndex && p.IsAlive);
        }

        /// <summary>Все игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetPlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerController>();
            return Players.Where(p => p.TeamIndex == team.teamIndex);
        }
    }
}

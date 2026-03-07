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
    public class PlayersManager : NetworkBehaviour
    {
        public static PlayersManager Instance { get; private set; }

        private readonly List<PlayerController> _players = new List<PlayerController>();

        /// <summary>Все подключённые игроки (только для чтения).</summary>
        public IReadOnlyList<PlayerController> Players => _players;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            GameNetworkManager.PlayerConnected += OnPlayerConnected;
            GameNetworkManager.PlayerDisconnected += OnPlayerDisconnected;
        }

        private void OnDisable()
        {
            GameNetworkManager.PlayerConnected -= OnPlayerConnected;
            GameNetworkManager.PlayerDisconnected -= OnPlayerDisconnected;
        }

        private void OnPlayerConnected(PlayerController player)
        {
            if (!_players.Contains(player))
                _players.Add(player);
            GameLog.Verbose(GameSettings.Instance.LogLevelNetwork, $"[PlayersManager] Игрок добавлен: {player.name}");
        }

        private void OnPlayerDisconnected(PlayerController player)
        {
            _players.Remove(player);
            GameLog.Verbose(GameSettings.Instance.LogLevelNetwork, $"[PlayersManager] Игрок удалён: {player.name}");
        }

        /// <summary>Только живые игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetAlivePlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerController>();
            return _players.Where(p => p.TeamIndex == team.teamIndex && p.IsAlive);
        }

        /// <summary>Все игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetPlayers(TeamData team)
        {
            if (team == null)
                return Enumerable.Empty<PlayerController>();
            return _players.Where(p => p.TeamIndex == team.teamIndex);
        }
    }
}

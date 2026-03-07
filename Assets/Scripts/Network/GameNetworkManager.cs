using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Единственная ответственность: управление сетевыми подключениями.
    /// Логика команд и старт матча — в подписчиках событий (MatchManager, DebugOrchestrator).
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        /// <summary>Игрок подключился и спавнился на сервере.</summary>
        public static event Action<PlayerController> PlayerConnected;

        /// <summary>Игрок отключился от сервера.</summary>
        public static event Action<PlayerController> PlayerDisconnected;

        /// <summary>Сервер завершил загрузку сцены. Параметр — имя загруженной сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        /// <summary>Все подключённые игроки (только сервер).</summary>
        private readonly List<PlayerController> _players = new List<PlayerController>();

        /// <summary>Все подключённые игроки (только для чтения).</summary>
        public IReadOnlyList<PlayerController> Players => _players;

        /// <summary>Только живые игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetAlivePlayers(Team team) =>
            _players.Where(p => p.Team == team && p.IsAlive);

        /// <summary>Все игроки указанной команды.</summary>
        public IEnumerable<PlayerController> GetPlayers(Team team) =>
            _players.Where(p => p.Team == team);

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

            _players.Add(player);
            PlayerConnected?.Invoke(player);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn.identity != null)
            {
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player != null)
                {
                    _players.Remove(player);
                    PlayerDisconnected?.Invoke(player);
                }
            }

            base.OnServerDisconnect(conn);
        }
    }
}

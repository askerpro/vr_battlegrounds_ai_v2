using System.Collections.Generic;
using System.Linq;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Серверная логическая обёртка над командой во время матча.
    /// Не является сетевым компонентом (NetworkBehaviour), используется
    /// исключительно GameMode на сервере для удобной работы с командой.
    /// Источником правды об игроках выступает глобальный PlayersManager.
    /// </summary>
    public class TeamRuntimeData
    {
        public TeamData Team { get; private set; }
        
        private readonly GameMode _gameMode;

        public TeamRuntimeData(TeamData team, GameMode mode)
        {
            Team = team;
            _gameMode = mode;
        }

        /// <summary>Текущий счет команды (синхронизируется через GameMode).</summary>
        public int Score
        {
            get => _gameMode.GetScore(Team);
            set => _gameMode.SetScore(Team, value);
        }

        /// <summary>Все подключенные сессии игроков этой команды.</summary>
        public IEnumerable<PlayerSession> Sessions => PlayersManager.Instance.GetPlayers(Team);

        /// <summary>Только живые сессии игроков этой команды.</summary>
        public IEnumerable<PlayerSession> AliveSessions => PlayersManager.Instance.GetAlivePlayers(Team);

        /// <summary>Общее количество игроков в команде.</summary>
        public int PlayersCount => Sessions.Count();

        /// <summary>Есть ли в команде хотя бы один подключенный игрок?</summary>
        public bool HasPlayers() => Sessions.Any();

        /// <summary>Есть ли в команде хотя бы один живой игрок?</summary>
        public bool HasAlivePlayers() => AliveSessions.Any();

        /// <summary>Все ли подключённые игроки в этой команде живы (и заспавнены)?</summary>
        public bool AreAllPlayersAlive() => Sessions.All(s => s.ActiveAvatar != null && s.ActiveAvatar.IsAlive);

        /// <summary>Добавить очки команде.</summary>
        public void AddScore(int points = 1)
        {
            Score += points;
        }

        /// <summary>Сбросить счет команды.</summary>
        public void ResetScore()
        {
            Score = 0;
        }

        public override string ToString()
        {
            return $"Name: {Team?.displayName}, Index: {Team?.teamIndex}, Score: {Score}, Players: {PlayersCount}";
        }
    }
}

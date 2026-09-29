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
    ///
    /// Источник правды об игроках — <see cref="IPlayerRoster"/>, а не напрямую
    /// <see cref="PlayersManager"/>. Раньше обёртка дёргала синглтон без проверки,
    /// и <c>EliminationMode.PrepareNextRound</c> падал с NRE везде, где менеджера нет:
    /// в сцене, открытой без сети, и в любом тесте логики матча (находка NET-12).
    /// Реестр отвечает пустым списком — команда просто оказывается без игроков.
    /// </summary>
    public class TeamRuntimeData
    {
        public TeamData Team { get; private set; }

        private readonly GameMode _gameMode;
        private readonly IPlayerRoster _roster;

        /// <param name="roster">
        /// Откуда брать игроков. По умолчанию — боевой реестр поверх <see cref="PlayersManager"/>.
        /// </param>
        public TeamRuntimeData(TeamData team, GameMode mode, IPlayerRoster roster = null)
        {
            Team = team;
            _gameMode = mode;
            _roster = roster ?? new PlayersManagerRoster();
        }

        /// <summary>Текущий счет команды (синхронизируется через GameMode).</summary>
        public int Score
        {
            get => _gameMode.GetScore(Team);
            set => _gameMode.SetScore(Team, value);
        }

        /// <summary>Все подключенные сессии игроков этой команды.</summary>
        public IEnumerable<PlayerSession> Sessions => _roster.GetPlayers(Team);

        /// <summary>Только живые сессии игроков этой команды.</summary>
        public IEnumerable<PlayerSession> AliveSessions => _roster.GetAlivePlayers(Team);

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
            return $"Name: {Team?.Name}, Index: {Team?.teamIndex}, Score: {Score}, Players: {PlayersCount}";
        }
    }
}

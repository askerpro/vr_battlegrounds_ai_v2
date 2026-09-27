using System;
using System.Collections.Generic;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Как режим раздаёт свои команды игрокам, у которых команды этого режима нет:
    /// пришёл из лобби на карту, подключился посреди матча, впервые вошёл в лобби.
    ///
    /// <para>
    /// Точка замены. Сейчас единственная реализация — <see cref="AutoBalanceTeamPolicy"/>:
    /// временная мера этапа А, пока игрок не выбирает команду матча сам. Этап Б (выбор
    /// команды на карте, ожидание, выдача админом) подставит свою политику через
    /// <see cref="GameMode.TeamAssignmentPolicy"/> — например, «не назначать никого, ждать
    /// выбора», — и ни режимы, ни <c>GameplayManager</c> при этом не меняются.
    /// </para>
    /// </summary>
    public interface ITeamAssignmentPolicy
    {
        /// <summary>
        /// Кому какую команду выдать сейчас. Игроков, уже стоящих в одной из
        /// <paramref name="modeTeams"/>, план не содержит. Пустой план — никого не трогать.
        /// </summary>
        IReadOnlyList<KeyValuePair<PlayerSession, TeamData>> Plan(
            IReadOnlyList<TeamData> modeTeams, IReadOnlyList<PlayerSession> players);
    }

    /// <summary>Автобаланс: каждый игрок без команды режима — в самую малочисленную.</summary>
    public sealed class AutoBalanceTeamPolicy : ITeamAssignmentPolicy
    {
        public IReadOnlyList<KeyValuePair<PlayerSession, TeamData>> Plan(
            IReadOnlyList<TeamData> modeTeams, IReadOnlyList<PlayerSession> players)
        {
            return TeamAutoBalance.Plan(modeTeams, players, session => session.TeamIndex);
        }
    }

    /// <summary>
    /// Чистый расчёт автобаланса — без сети, синглтонов и сессий, поэтому целиком
    /// проверяется EditMode-тестом (<c>TeamAutoBalanceTests</c>).
    /// </summary>
    public static class TeamAutoBalance
    {
        /// <summary>
        /// Раскладывает по <paramref name="teams"/> игроков, чья команда не из этого списка.
        /// Уже стоящие в командах режима не двигаются, но учитываются в численности.
        /// Каждый следующий игрок уходит в самую малочисленную команду; при равенстве —
        /// в ту, что раньше в списке. Результат детерминирован: порядок игроков и команд
        /// задаёт его полностью.
        /// </summary>
        /// <param name="teamIndexOf">Текущая команда игрока (<c>teamIndex</c>, 0 — нет).</param>
        public static IReadOnlyList<KeyValuePair<TPlayer, TeamData>> Plan<TPlayer>(
            IReadOnlyList<TeamData> teams, IEnumerable<TPlayer> players, Func<TPlayer, int> teamIndexOf)
        {
            var plan = new List<KeyValuePair<TPlayer, TeamData>>();
            if (teams == null || players == null || teamIndexOf == null) return plan;

            var validTeams = new List<TeamData>();
            foreach (TeamData team in teams)
            {
                if (team != null) validTeams.Add(team);
            }
            if (validTeams.Count == 0) return plan;

            var counts = new int[validTeams.Count];
            var unassigned = new List<TPlayer>();

            foreach (TPlayer player in players)
            {
                int slot = validTeams.FindIndex(t => t.teamIndex == teamIndexOf(player));
                if (slot >= 0) counts[slot]++;
                else unassigned.Add(player);
            }

            foreach (TPlayer player in unassigned)
            {
                int smallest = 0;
                for (int i = 1; i < counts.Length; i++)
                {
                    if (counts[i] < counts[smallest]) smallest = i;
                }

                counts[smallest]++;
                plan.Add(new KeyValuePair<TPlayer, TeamData>(player, validTeams[smallest]));
            }

            return plan;
        }
    }
}

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Ручной выбор (этап Б): режим никого не назначает. Игрок выбирает команду сам
    /// в планшете (<c>PlayerSession.CmdRequestTeamChange</c>) или её выдаёт админ
    /// (<c>GameplayManager.ServerAdminAssignTeam</c>); матч ждёт, пока команда будет у всех
    /// (<see cref="GameMode.AllPlayersHaveModeTeam"/>).
    /// </summary>
    public sealed class PlayerChoiceTeamPolicy : ITeamAssignmentPolicy
    {
        private static readonly KeyValuePair<PlayerSession, TeamData>[] Nothing =
            new KeyValuePair<PlayerSession, TeamData>[0];

        public IReadOnlyList<KeyValuePair<PlayerSession, TeamData>> Plan(
            IReadOnlyList<TeamData> modeTeams, IReadOnlyList<PlayerSession> players) => Nothing;
    }
}

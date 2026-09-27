using System;
using System.Collections.Generic;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Чистый расчёт автобаланса — без сети, синглтонов и сессий, поэтому целиком
    /// проверяется EditMode-тестом (<c>TeamAutoBalanceTests</c>).
    ///
    /// <para>
    /// Раньше поверх него стоял интерфейс политики (<c>ITeamAssignmentPolicy</c>) с двумя
    /// реализациями, одна из которых ничего не делала. Политика режима — одно перечисление
    /// в данных (<see cref="GameModeData.teamAssignment"/>), и ветка по нему в
    /// <see cref="GameMode.ServerAssignTeams"/> проще, чем точка расширения без второго
    /// потребителя.
    /// </para>
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

using System.Collections.Generic;

namespace VrBattlegrounds.DevTools.Bots
{
    /// <summary>
    /// Какую команду режима выбирает бот. Чистый расчёт — проверяется EditMode-тестом
    /// (<c>BotTeamChoiceTests</c>).
    ///
    /// <para>
    /// Бот выравнивает команды по <b>общему</b> числу игроков (люди и боты вместе): один человек
    /// и один бот — противники, один человек и три бота — два на два, у человека есть союзник.
    /// Прежнее правило «туда, где меньше людей» уводило против человека всех ботов подряд,
    /// и союзник перебегал к противнику, едва человек выбирал его команду.
    /// </para>
    ///
    /// <para>
    /// Бот уходит из своей команды, только если переход строго выравнивает счёт: при равенстве
    /// остаётся — иначе он метался бы между командами и пересоздавал аватар. Люди выбирают
    /// команду позже бота (в планшете), поэтому выбор пересчитывается, пока режим его разрешает.
    /// </para>
    /// </summary>
    public static class BotTeamChoice
    {
        /// <param name="teams">Команды режима.</param>
        /// <param name="currentTeamIndex">Текущая команда бота (0 — нет).</param>
        /// <param name="humanTeams">Команды людей.</param>
        /// <param name="otherBotTeams">Команды остальных ботов (без этого).</param>
        /// <returns>Команда, в которой бот должен быть, или <c>null</c>, если команд нет.</returns>
        public static TeamData Choose(IReadOnlyList<TeamData> teams, int currentTeamIndex,
                                      IEnumerable<int> humanTeams, IEnumerable<int> otherBotTeams)
        {
            if (teams == null) return null;

            var counts = new Dictionary<int, int>();
            Count(counts, humanTeams);
            Count(counts, otherBotTeams);

            TeamData best = null;
            int bestCount = int.MaxValue;
            TeamData current = null;
            int currentCount = int.MaxValue;

            foreach (TeamData team in teams)
            {
                if (team == null) continue;

                int count = Get(counts, team.teamIndex);
                if (count < bestCount)
                {
                    best = team;
                    bestCount = count;
                }

                if (team.teamIndex == currentTeamIndex)
                {
                    current = team;
                    currentCount = count;
                }
            }

            return current != null && currentCount == bestCount ? current : best;
        }

        private static void Count(Dictionary<int, int> counts, IEnumerable<int> teamIndices)
        {
            if (teamIndices == null) return;

            foreach (int index in teamIndices)
            {
                counts[index] = Get(counts, index) + 1;
            }
        }

        private static int Get(Dictionary<int, int> counts, int teamIndex)
        {
            return counts.TryGetValue(teamIndex, out int value) ? value : 0;
        }
    }
}

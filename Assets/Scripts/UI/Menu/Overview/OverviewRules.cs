using System.Collections.Generic;
using System.Text;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Какой контекст показывать. Порядок проверок и есть правило: сеть → лобби → пауза →
    /// итог карты → матч → разминка.
    /// </summary>
    public static class OverviewContextResolver
    {
        public static OverviewContext Resolve(OverviewInput input)
        {
            if (input == null || !input.Online) return OverviewContext.Offline;
            if (input.IsLobby) return OverviewContext.Lobby;
            if (input.MapState == MapState.Paused) return OverviewContext.Paused;

            if (input.MapState == MapState.Live)
            {
                // Режим уже решил карту, а MapReferee ещё не вернул разминку.
                return input.Elimination != null && input.Elimination.State == EliminationState.Finished
                    ? OverviewContext.MapFinished
                    : OverviewContext.Live;
            }

            // Разминка с записанным итогом этой карты — ждём «Следующая карта» / «В лобби».
            return input.Series != null && input.Series.CurrentMapDecided
                ? OverviewContext.MapFinished
                : OverviewContext.Warmup;
        }
    }

    /// <summary>
    /// Кто чьё хп видит. Своя команда — всегда; наблюдатель и игрок без команды матча — всех;
    /// соперники — только если политика разрешает (<see cref="OverviewInput.ShowEnemyHealth"/>).
    /// </summary>
    public static class OverviewVisibility
    {
        public static bool CanSeeHealth(OverviewViewer viewer, OverviewPlayerInput player, bool showEnemyHealth)
        {
            if (player == null) return false;
            if (viewer == null || viewer.IsSpectatorRole || viewer.TeamIndex == OverviewPlayerInput.NoTeam) return true;
            if (viewer.TeamIndex == player.TeamIndex) return true;
            return showEnemyHealth;
        }
    }

    /// <summary>Тексты ячеек и шапки — в одном месте, чтобы их проверял тест, а не глаз.</summary>
    public static class OverviewFormat
    {
        public const string Yes = "да";
        public const string No = "нет";

        /// <summary>«м:сс»; отрицательное — ноль.</summary>
        public static string Clock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>Секунды для часов: вверх, чтобы «0:00» было только когда время вышло.</summary>
        public static int ClockSeconds(float seconds) => seconds <= 0f ? 0 : (int)System.Math.Ceiling(seconds);

        /// <summary>Хп целым вверх, как на часах руки: 0,3 хп — ещё жив, а не «0».</summary>
        public static string Health(float health) => (health <= 0f ? 0 : (int)System.Math.Ceiling(health)).ToString();

        public static string Kda(OverviewPlayerInput p) => p.Kills + "/" + p.Deaths + "/" + p.Assists;

        public static string PhaseName(RoundPhase phase)
        {
            switch (phase)
            {
                case RoundPhase.Setup: return "Подготовка";
                case RoundPhase.Equipment: return "Закупка";
                case RoundPhase.Countdown: return "Отсчёт";
                case RoundPhase.Combat: return "Бой";
                default: return "Итог раунда";
            }
        }

        public static string TeamName(OverviewInput input, int teamIndex)
        {
            OverviewTeamInput team = input.Teams.Find(t => t.Index == teamIndex);
            return team != null && !string.IsNullOrEmpty(team.Name) ? team.Name : "Команда " + teamIndex;
        }

        /// <summary>Две команды — «A 3 : 2 B», иначе — «A 3 · B 2 · C 1». Порядок — по индексу команды.</summary>
        public static string ScoreLine(OverviewInput input, System.Func<OverviewTeamInput, int> score)
        {
            var teams = new List<OverviewTeamInput>(input.Teams);
            teams.Sort((x, y) => x.Index.CompareTo(y.Index));
            if (teams.Count == 0) return "";
            if (teams.Count == 2)
                return TeamName(input, teams[0].Index) + " " + score(teams[0]) + " : " + score(teams[1]) + " " + TeamName(input, teams[1].Index);

            var sb = new StringBuilder();
            foreach (OverviewTeamInput t in teams)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(TeamName(input, t.Index)).Append(' ').Append(score(t));
            }
            return sb.ToString();
        }

        public static string MapScoreLine(OverviewInput input) => ScoreLine(input, t => t.MapScore);

        public static string SeriesScoreLine(OverviewInput input) =>
            "По картам: " + ScoreLine(input, t => input.Series != null && input.Series.MapWins.TryGetValue(t.Index, out int w) ? w : 0);
    }
}

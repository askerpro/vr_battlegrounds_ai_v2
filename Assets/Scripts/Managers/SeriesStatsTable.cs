using System.Collections.Generic;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Таблица статистики серии для экрана «Статистика»: секция на каждую карту серии
    /// и секция TOTAL. Чистая сборка из реплицированных строк <see cref="Series"/> —
    /// без UI и сети, проверяется EditMode-тестом.
    /// </summary>
    public static class SeriesStatsTable
    {
        public const string TotalTitle = "TOTAL";

        public sealed class TeamRow
        {
            public int Team;
            public int Rounds;
            /// <summary>Выиграно карт (в секции карты — 1 у победителя карты).</summary>
            public int MapsWon;
        }

        public sealed class PlayerRow
        {
            public string Key;
            public string Name;
            public int Team;
            public int Kills;
            public int Deaths;
            public int Assists;
        }

        public sealed class Section
        {
            public string Title;
            /// <summary>Индекс карты в серии; <see cref="Series.Total"/> — TOTAL.</summary>
            public int Map;
            public List<TeamRow> Teams = new List<TeamRow>();
            public List<PlayerRow> Players = new List<PlayerRow>();
        }

        /// <summary>
        /// Секции по картам в порядке серии и TOTAL последней. Игроки в секции — по
        /// убийствам (больше — выше), при равенстве — меньше смертей.
        /// </summary>
        /// <param name="results">Победитель каждой сыгранной карты (<c>teamIndex</c>, −1 — ничья) — выигранные карты.</param>
        public static List<Section> Build(IReadOnlyList<string> maps, IReadOnlyList<TeamMapStat> teams,
                                          IReadOnlyList<PlayerMapStat> players, IReadOnlyList<int> results = null)
        {
            var result = new List<Section>();
            int mapCount = maps != null ? maps.Count : 0;

            for (int m = 0; m < mapCount; m++)
                result.Add(Fill(new Section { Title = maps[m], Map = m }, teams, players, results));

            result.Add(Fill(new Section { Title = TotalTitle, Map = Series.Total }, teams, players, results));
            return result;
        }

        private static Section Fill(Section section, IReadOnlyList<TeamMapStat> teams, IReadOnlyList<PlayerMapStat> players,
                                    IReadOnlyList<int> results)
        {
            if (results != null)
            {
                for (int m = 0; m < results.Count; m++)
                {
                    if (results[m] < 0 || section.Map != Series.Total && m != section.Map) continue;
                    TeamRow row = section.Teams.Find(r => r.Team == results[m]);
                    if (row == null) section.Teams.Add(row = new TeamRow { Team = results[m] });
                    row.MapsWon++;
                }
            }

            if (teams != null)
            {
                foreach (TeamMapStat t in teams)
                {
                    if (section.Map != Series.Total && t.map != section.Map) continue;
                    TeamRow row = section.Teams.Find(r => r.Team == t.team);
                    if (row == null) section.Teams.Add(row = new TeamRow { Team = t.team });
                    row.Rounds += t.rounds;
                }
            }

            if (players != null)
            {
                foreach (PlayerMapStat p in players)
                {
                    if (section.Map != Series.Total && p.map != section.Map) continue;
                    PlayerRow row = section.Players.Find(r => r.Key == p.player);
                    if (row == null) section.Players.Add(row = new PlayerRow { Key = p.player });
                    row.Name = p.name;
                    row.Team = p.team;
                    row.Kills += p.kills;
                    row.Deaths += p.deaths;
                    row.Assists += p.assists;
                }
            }

            section.Teams.Sort((x, y) => x.Team.CompareTo(y.Team));
            section.Players.Sort((x, y) => x.Kills != y.Kills ? y.Kills.CompareTo(x.Kills) : x.Deaths.CompareTo(y.Deaths));
            return section;
        }

        /// <summary>
        /// Текст таблицы для экрана «Статистика» (TMP rich text): на каждую секцию — заголовок,
        /// строка команд (раунды, карты) и строки игроков «имя — У / С / А».
        /// </summary>
        public static string Format(List<Section> sections, System.Func<int, string> teamName)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Section section in sections)
            {
                bool total = section.Map == Series.Total;
                sb.AppendLine(total
                    ? "<b><size=120%>" + TotalTitle + "</size></b>"
                    : "<b>Карта " + (section.Map + 1) + ": " + section.Title + "</b>");

                if (section.Teams.Count == 0 && section.Players.Count == 0)
                {
                    sb.AppendLine("  <i>ещё не сыграна</i>");
                    continue;
                }

                foreach (TeamRow team in section.Teams)
                {
                    string line = "  " + (teamName != null ? teamName(team.Team) : team.Team.ToString()) + ": раундов " + team.Rounds;
                    if (team.MapsWon > 0) line += ", карт " + team.MapsWon;
                    sb.AppendLine(line);
                }

                if (section.Players.Count > 0)
                    sb.AppendLine("  <color=#AAAAAA>Игрок — убийства / смерти / ассисты</color>");

                foreach (PlayerRow p in section.Players)
                {
                    sb.AppendLine("    " + (string.IsNullOrEmpty(p.Name) ? p.Key : p.Name) +
                                  " — " + p.Kills + " / " + p.Deaths + " / " + p.Assists);
                }

                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}

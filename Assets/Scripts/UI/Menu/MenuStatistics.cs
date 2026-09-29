using System.Collections.Generic;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Статистика» (<see cref="MenuScreenType.Statistics"/>) — у всех игроков: сквозной
    /// счёт серии по картам и TOTAL — раунды и карты команд, убийства / смерти / ассисты игроков.
    ///
    /// <para>
    /// Данные — реплицированные строки <see cref="Series"/> (есть и у клиента), секции собирает
    /// <see cref="SeriesStatsTable"/>; экран рисует их таблицами набора (<see cref="MenuKit.TableRow"/>).
    /// Длинная серия прокручивается, а не сжимает шрифт. Перестраивается раз в 0,5 с и только
    /// когда что-то поменялось.
    /// </para>
    /// </summary>
    public class MenuStatistics : MenuScreen
    {
        private static readonly float[] TeamWeights = { 4f, 1f, 1f };
        private static readonly float[] PlayerWeights = { 4f, 1f, 1f, 1f };

        private string _last = "";

        public override void Show()
        {
            base.Show();
            _last = "";
            Rebuild();
        }

        private void Update()
        {
            if (RefreshDue()) Rebuild();
        }

        public override void BuildPreview()
        {
            base.Show();
            var sections = new List<SeriesStatsTable.Section>();
            string[] maps = { "TestMap1", "TestMap2", SeriesStatsTable.TotalTitle };
            string[] names = { "Асланбек", "Марат", "Игрок_07", "ОченьДлинныйНикИгрока", "Залим", "Рустам", "Тимур", "Кантемир" };
            for (int m = 0; m < maps.Length; m++)
            {
                var s = new SeriesStatsTable.Section { Title = maps[m], Map = m };
                s.Teams.Add(new SeriesStatsTable.TeamRow { Team = 1, Rounds = 7 + m, MapsWon = m == 0 ? 1 : 0 });
                s.Teams.Add(new SeriesStatsTable.TeamRow { Team = 2, Rounds = 5 + m, MapsWon = m == 1 ? 1 : 0 });
                for (int i = 0; i < names.Length; i++)
                    s.Players.Add(new SeriesStatsTable.PlayerRow { Name = names[i], Team = 1 + i % 2, Kills = 12 - i, Deaths = i + 2, Assists = i % 4 });
                sections.Add(s);
            }
            Render(sections, false);
        }

        private void Rebuild()
        {
            Series series = Series.Instance;
            List<SeriesStatsTable.Section> sections = series != null && series.Maps.Count > 0
                ? SeriesStatsTable.Build(series.Maps, series.TeamStats, series.PlayerStats, series.Results)
                : null;

            string key = sections == null ? "" : SeriesStatsTable.Format(sections, TeamName) + (series.IsAdHoc ? "|adhoc" : "");
            if (key == _last && Content.childCount > 0) return;
            _last = key;

            Render(sections, series != null && series.IsAdHoc);
        }

        private void Render(List<SeriesStatsTable.Section> sections, bool adHoc)
        {
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Статистика серии");

            if (sections == null)
            {
                MenuKit.EmptyState(Content, "Серия ещё не начиналась — статистики нет.");
                return;
            }

            // Карта загружена напрямую, без серии: та же таблица из одной карты.
            if (adHoc) MenuKit.Label(Content, "Карта без серии", MenuTextRole.Caption, MenuColorRole.TextSecondary);

            foreach (SeriesStatsTable.Section section in sections)
            {
                MenuKit.Section(Content, section.Title);
                MenuKit.TableRow(Content, new[] { "Команда", "Раунды", "Карты" }, TeamWeights, header: true);
                foreach (SeriesStatsTable.TeamRow team in section.Teams)
                    MenuKit.TableRow(Content, new[] { TeamName(team.Team), team.Rounds.ToString(), team.MapsWon.ToString() }, TeamWeights);

                if (section.Players.Count == 0) continue;
                MenuKit.TableRow(Content, new[] { "Игрок", "У", "С", "А" }, PlayerWeights, header: true);
                foreach (SeriesStatsTable.PlayerRow p in section.Players)
                    MenuKit.TableRow(Content, new[] { p.Name, p.Kills.ToString(), p.Deaths.ToString(), p.Assists.ToString() }, PlayerWeights);
            }
        }

        /// <summary>Текст таблицы для серии; без серии — пояснение. Для логов и тестов.</summary>
        public static string BuildText(Series series)
        {
            if (series == null || series.Maps.Count == 0)
                return "Серия ещё не начиналась — статистики нет.";

            var sections = SeriesStatsTable.Build(series.Maps, series.TeamStats, series.PlayerStats, series.Results);
            string table = SeriesStatsTable.Format(sections, TeamName);
            return series.IsAdHoc ? "<i>Карта без серии</i>\n\n" + table : table;
        }

        private static string TeamName(int teamIndex)
        {
            TeamData team = TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamIndex) : null;
            return team != null ? team.Name : "Команда " + teamIndex;
        }
    }
}

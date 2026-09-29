using System.Collections.Generic;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>Какая вторая колонка у таблицы команды.</summary>
    public enum OverviewTeamColumns
    {
        /// <summary>Игрок | Калибровка — разминка.</summary>
        Calibration,
        /// <summary>Игрок | HP | У/С/А — бой.</summary>
        Health,
        /// <summary>Игрок | Готов | У/С/А — закупка и отсчёт.</summary>
        Ready,
        /// <summary>Игрок | У/С/А — пауза, итог карты.</summary>
        Kda
    }

    /// <summary>Что писать в заголовке секции команды.</summary>
    public sealed class OverviewTeamTableStyle
    {
        public OverviewTeamColumns Columns = OverviewTeamColumns.Kda;

        /// <summary>«— 3» после названия команды.</summary>
        public bool ShowScore = true;

        /// <summary>«(живы 1/2)» после счёта.</summary>
        public bool ShowAlive;

        /// <summary>Текст ячейки хп у выбывшего: «выбыл» (Elimination), «респаун» (Respawn).</summary>
        public string DeadText = "выбыл";

        /// <summary>Команда-победитель — её секция с тоном <see cref="OverviewTone.Winner"/>.</summary>
        public int? WinnerTeam;
    }

    /// <summary>
    /// Таблицы команд — общая часть всех контекстов на карте: секция на команду (своя первой,
    /// остальные по индексу) и секция «Без команды». Игроки — по убийствам, при равенстве —
    /// меньше смертей, затем по имени. Наблюдатели (роль Spectator) в таблицы не попадают.
    /// </summary>
    public static class OverviewTeamTables
    {
        public const string NoTeamId = "no-team";

        public static string SectionId(int teamIndex) => "team:" + teamIndex;

        public static void Add(OverviewInput input, OverviewSnapshot snapshot, OverviewTeamTableStyle style)
        {
            var teams = new List<OverviewTeamInput>(input.Teams);
            int viewerTeam = input.Viewer != null ? input.Viewer.TeamIndex : OverviewPlayerInput.NoTeam;
            teams.Sort((x, y) =>
            {
                bool xm = x.Index == viewerTeam, ym = y.Index == viewerTeam;
                if (xm != ym) return xm ? -1 : 1;
                return x.Index.CompareTo(y.Index);
            });

            var noTeam = new List<OverviewPlayerInput>();
            foreach (OverviewPlayerInput p in input.Players)
            {
                if (p.IsSpectatorRole) continue;
                if (input.Teams.Find(t => t.Index == p.TeamIndex) == null) noTeam.Add(p);
            }

            foreach (OverviewTeamInput team in teams)
            {
                List<OverviewPlayerInput> players = input.Players.FindAll(p => !p.IsSpectatorRole && p.TeamIndex == team.Index);
                if (players.Count == 0) continue;
                players.Sort(Compare);

                var section = new OverviewSection
                {
                    Id = SectionId(team.Index),
                    Title = Title(input, team, players, style),
                    Tone = style.WinnerTeam == team.Index ? OverviewTone.Winner : OverviewTone.Normal,
                    Columns = Columns(style.Columns)
                };
                foreach (OverviewPlayerInput p in players)
                    section.Rows.Add(PlayerRow(input, p, style));
                snapshot.Sections.Add(section);
            }

            if (noTeam.Count == 0) return;

            noTeam.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            bool warmup = style.Columns == OverviewTeamColumns.Calibration;
            var none = new OverviewSection
            {
                Id = NoTeamId,
                Title = "Без команды",
                Columns = warmup ? Columns(OverviewTeamColumns.Calibration) : new[] { "Игрок" }
            };
            foreach (OverviewPlayerInput p in noTeam)
            {
                var row = new OverviewRow
                {
                    Key = p.Key,
                    Kind = OverviewRowKind.Player,
                    // В разминке без команды — надо выбрать; в матче — просто зритель.
                    Tone = warmup ? OverviewTone.Warning : IsViewer(input, p) ? OverviewTone.Local : OverviewTone.Dimmed,
                    Cells = warmup
                        ? new[] { p.Name, p.IsCalibrated ? OverviewFormat.Yes : OverviewFormat.No }
                        : new[] { p.Name }
                };
                none.Rows.Add(row);
            }
            snapshot.Sections.Add(none);
        }

        public static string[] Columns(OverviewTeamColumns columns)
        {
            switch (columns)
            {
                case OverviewTeamColumns.Calibration: return new[] { "Игрок", "Калибровка" };
                case OverviewTeamColumns.Health: return new[] { "Игрок", "HP", "У/С/А" };
                case OverviewTeamColumns.Ready: return new[] { "Игрок", "Готов", "У/С/А" };
                default: return new[] { "Игрок", "У/С/А" };
            }
        }

        public static bool IsViewer(OverviewInput input, OverviewPlayerInput p) =>
            input.Viewer != null && !string.IsNullOrEmpty(input.Viewer.PlayerKey) && input.Viewer.PlayerKey == p.Key;

        private static int Compare(OverviewPlayerInput x, OverviewPlayerInput y)
        {
            if (x.Kills != y.Kills) return y.Kills.CompareTo(x.Kills);
            if (x.Deaths != y.Deaths) return x.Deaths.CompareTo(y.Deaths);
            return string.CompareOrdinal(x.Name, y.Name);
        }

        private static string Title(OverviewInput input, OverviewTeamInput team, List<OverviewPlayerInput> players,
                                    OverviewTeamTableStyle style)
        {
            string title = OverviewFormat.TeamName(input, team.Index);
            if (style.ShowScore) title += " — " + team.MapScore;
            if (style.ShowAlive)
            {
                int alive = players.FindAll(p => p.HasAvatar && p.IsAlive).Count;
                title += " (живы " + alive + "/" + players.Count + ")";
            }
            return title;
        }

        private static OverviewRow PlayerRow(OverviewInput input, OverviewPlayerInput p, OverviewTeamTableStyle style)
        {
            bool viewer = IsViewer(input, p);
            var row = new OverviewRow { Key = p.Key, Kind = OverviewRowKind.Player };

            switch (style.Columns)
            {
                case OverviewTeamColumns.Calibration:
                    row.Cells = new[] { p.Name, p.IsCalibrated ? OverviewFormat.Yes : OverviewFormat.No };
                    row.Tone = !p.IsCalibrated ? OverviewTone.Warning : viewer ? OverviewTone.Local : OverviewTone.Normal;
                    return row;

                case OverviewTeamColumns.Health:
                    row.Cells = new[] { p.Name, HealthCell(input, p, style.DeadText), OverviewFormat.Kda(p) };
                    break;

                case OverviewTeamColumns.Ready:
                    row.Cells = new[] { p.Name, p.IsReady ? OverviewFormat.Yes : OverviewFormat.No, OverviewFormat.Kda(p) };
                    break;

                default:
                    row.Cells = new[] { p.Name, OverviewFormat.Kda(p) };
                    break;
            }

            bool out_ = style.Columns == OverviewTeamColumns.Health && (!p.HasAvatar || !p.IsAlive);
            row.Tone = viewer ? OverviewTone.Local : out_ ? OverviewTone.Dimmed : OverviewTone.Normal;
            return row;
        }

        /// <summary>Хп — числом, если смотрящему можно; иначе «жив». Выбывший — <see cref="OverviewTeamTableStyle.DeadText"/>.</summary>
        public static string HealthCell(OverviewInput input, OverviewPlayerInput p, string deadText)
        {
            if (!p.HasAvatar) return "—";
            if (!p.IsAlive) return deadText;
            return OverviewVisibility.CanSeeHealth(input.Viewer, p, input.ShowEnemyHealth)
                ? OverviewFormat.Health(p.Health)
                : "жив";
        }
    }
}

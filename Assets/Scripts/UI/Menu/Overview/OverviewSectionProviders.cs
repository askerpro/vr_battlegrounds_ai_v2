using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.UI.Menu.Overview
{
    // Стратегии экрана «Обзор». Каждая — один контекст (или один режим в контексте Live).
    // Порядок в OverviewBuilder.Default — порядок секций на экране.

    /// <summary>Нет сети: одно пояснение.</summary>
    public sealed class OfflineOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.Offline;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Title = "Нет подключения";
            s.Header.Status = "Поиск сервера…";
            var section = new OverviewSection { Id = "offline" };
            section.AddInfo("offline", "Обзор матча появится после подключения к серверу.");
            s.Sections.Add(section);
        }
    }

    /// <summary>
    /// Лобби (решение пользователя 2026-09-29): первым — блок «Вы» (команда, скин, калибровка),
    /// дальше выбор админа на следующую серию, коротко итог прошлой серии и весь зал.
    /// </summary>
    public sealed class LobbyOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.Lobby;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Title = "Лобби";
            bool planned = input.Plan != null && input.Plan.Maps.Count > 0;
            s.Header.Status = planned
                ? "Следующая серия: " + input.Plan.ModeTitle + ", карт: " + input.Plan.Maps.Count
                : "Серия не выбрана";

            AddYou(input, s);

            if (planned)
            {
                var plan = new OverviewSection { Id = "plan", Title = "Следующая серия" };
                for (int i = 0; i < input.Plan.Maps.Count; i++)
                    plan.AddInfo("plan:" + i, (i + 1) + ". " + input.Plan.Maps[i]);
                s.Sections.Add(plan);
            }

            AddLastSeries(input, s);
            AddHall(input, s);
        }

        private static void AddYou(OverviewInput input, OverviewSnapshot s)
        {
            OverviewPlayerInput me = input.Players.Find(p => OverviewTeamTables.IsViewer(input, p));
            if (me == null) return;

            var you = new OverviewSection { Id = "you", Title = "Вы" };
            if (me.IsSpectatorRole)
            {
                you.AddInfo("role", "Роль: наблюдатель");
            }
            else
            {
                bool noTeam = me.TeamIndex == OverviewPlayerInput.NoTeam;
                you.AddInfo("team", noTeam
                        ? "Команда: не выбрана — раздел «Команда»"
                        : "Команда: " + OverviewFormat.TeamName(input, me.TeamIndex),
                    noTeam ? OverviewTone.Warning : OverviewTone.Normal);
                if (!string.IsNullOrEmpty(me.SkinName))
                    you.AddInfo("skin", "Скин: " + me.SkinName);
                you.AddInfo("calibration", me.IsCalibrated
                        ? "Калибровка: пройдена"
                        : "Калибровка: не пройдена — раздел «Калибровка»",
                    me.IsCalibrated ? OverviewTone.Success : OverviewTone.Warning);
            }
            s.Sections.Add(you);
        }

        private static void AddHall(OverviewInput input, OverviewSnapshot s)
        {
            var hall = new OverviewSection { Id = "hall", Title = "Зал", Columns = new[] { "Игрок", "Команда", "Калибровка" } };
            var players = new System.Collections.Generic.List<OverviewPlayerInput>(input.Players);
            players.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

            foreach (OverviewPlayerInput p in players)
            {
                bool noTeam = !p.IsSpectatorRole && p.TeamIndex == OverviewPlayerInput.NoTeam;
                string team = p.IsSpectatorRole ? "наблюдатель"
                    : noTeam ? OverviewFormat.No
                    : OverviewFormat.TeamName(input, p.TeamIndex);
                bool needsCalibration = !p.IsSpectatorRole && !p.IsCalibrated;

                hall.Rows.Add(new OverviewRow
                {
                    Key = p.Key,
                    Kind = OverviewRowKind.Player,
                    Tone = noTeam || needsCalibration ? OverviewTone.Warning
                        : OverviewTeamTables.IsViewer(input, p) ? OverviewTone.Local
                        : OverviewTone.Normal,
                    Cells = new[] { p.Name, team, p.IsSpectatorRole ? "—" : p.IsCalibrated ? OverviewFormat.Yes : OverviewFormat.No }
                });
            }
            s.Sections.Add(hall);
        }

        private static void AddLastSeries(OverviewInput input, OverviewSnapshot s)
        {
            OverviewSeriesInput series = input.Series;
            if (series == null || series.Running || series.Results.Count == 0) return;

            int best = -1, bestWins = -1;
            bool tie = false;
            foreach (var pair in series.MapWins)
            {
                if (pair.Value > bestWins) { best = pair.Key; bestWins = pair.Value; tie = false; }
                else if (pair.Value == bestWins) tie = true;
            }

            var section = new OverviewSection { Id = "last-series", Title = "Прошлая серия" };
            if (best >= 0 && !tie)
                section.AddInfo("winner", "Победитель: " + OverviewFormat.TeamName(input, best), OverviewTone.Winner);
            else
                section.AddInfo("winner", "Ничья по картам");
            section.AddInfo("score", OverviewFormat.SeriesScoreLine(input));
            s.Sections.Add(section);
        }
    }

    /// <summary>Общая шапка карты: название и пометка про смотрящего.</summary>
    public sealed class MapHeaderOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) =>
            context != OverviewContext.Offline && context != OverviewContext.Lobby;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Title = input.MapTitle ?? "";

            if (input.Viewer == null) return;
            if (input.Viewer.IsSpectatorRole)
                s.Header.ViewerNote = "Вы наблюдатель";
            else if (context == OverviewContext.Live && input.Viewer.IsEliminated)
                s.Header.ViewerNote = input.Elimination != null
                    ? "Вы выбыли — наблюдение до конца раунда"
                    : "Вы выбыли — вернитесь в свою зону";
        }
    }

    /// <summary>Разминка на боевой карте: кто в какой команде, калибровка, хватает ли людей на старт.</summary>
    public sealed class WarmupOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.Warmup;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Status = "Разминка — ждём «Начать матч»";
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Calibration, ShowScore = false });

            if (input.MinPlayersToStart <= 0) return;

            int inTeams = input.Players.FindAll(p =>
                !p.IsSpectatorRole && input.Teams.Find(t => t.Index == p.TeamIndex) != null).Count;
            var start = new OverviewSection { Id = "start", Title = "Старт матча" };
            start.AddInfo("players", "Игроков в командах: " + inTeams + ", нужно минимум " + input.MinPlayersToStart,
                inTeams < input.MinPlayersToStart ? OverviewTone.Warning : OverviewTone.Normal);
            s.Sections.Add(start);
        }
    }

    /// <summary>Матч Elimination: раунд, фаза, часы фазы, счёт; в закупке — готовность, в бою — хп.</summary>
    public sealed class EliminationOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) =>
            context == OverviewContext.Live && input.Elimination != null;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            EliminationRoundInput round = input.Elimination;
            s.Header.Score = OverviewFormat.MapScoreLine(input);

            if (round.State == EliminationState.WaitingForPlayers)
            {
                s.Header.Status = "Ждём игроков";
                OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Ready });
                return;
            }

            string phase = OverviewFormat.PhaseName(round.Phase);
            s.Header.Status = round.RoundNumber > 0
                ? "Раунд " + round.RoundNumber + " из " + round.TotalRounds + " · " + phase
                : phase;
            s.Header.ClockLabel = phase;
            s.Header.ClockSeconds = OverviewFormat.ClockSeconds(round.SecondsRemaining);

            if (round.Phase == RoundPhase.Countdown && round.CountdownHeld)
            {
                var alert = new OverviewSection { Id = "alert", Tone = OverviewTone.Warning };
                alert.AddInfo("countdown-held", "Отсчёт стоит: кто-то из живых вне своей зоны", OverviewTone.Warning);
                s.Sections.Add(alert);
            }

            bool preparation = round.Phase == RoundPhase.Setup || round.Phase == RoundPhase.Equipment ||
                               round.Phase == RoundPhase.Countdown;
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle
            {
                Columns = preparation ? OverviewTeamColumns.Ready : OverviewTeamColumns.Health,
                ShowAlive = !preparation
            });
        }
    }

    /// <summary>Матч Respawn: остаток матча, счёт, хп; выбывший ждёт возрождения.</summary>
    public sealed class RespawnOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) =>
            context == OverviewContext.Live && input.Elimination == null && input.MatchTimeRemaining.HasValue;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Status = string.IsNullOrEmpty(input.ModeTitle) ? "Матч" : input.ModeTitle;
            s.Header.Score = OverviewFormat.MapScoreLine(input);
            s.Header.ClockLabel = "До конца";
            s.Header.ClockSeconds = OverviewFormat.ClockSeconds(input.MatchTimeRemaining.Value);
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Health, DeadText = "респаун" });
        }
    }

    /// <summary>
    /// Матч режима, для которого своей стратегии ещё нет: хотя бы счёт и таблица У/С/А.
    /// Режим с собственной стратегией сюда не попадает.
    /// </summary>
    public sealed class GenericLiveOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) =>
            context == OverviewContext.Live && input.Elimination == null && !input.MatchTimeRemaining.HasValue;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Status = string.IsNullOrEmpty(input.ModeTitle) ? "Матч" : input.ModeTitle;
            s.Header.Score = OverviewFormat.MapScoreLine(input);
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Kda });
        }
    }

    /// <summary>Пауза: счёт и У/С/А. Часов нет — время стоит.</summary>
    public sealed class PausedOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.Paused;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            s.Header.Status = "Пауза — ждём «Продолжить»";
            s.Header.Score = OverviewFormat.MapScoreLine(input);
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Kda });
        }
    }

    /// <summary>Итог карты: победитель, счёт, таблица У/С/А и что будет дальше.</summary>
    public sealed class MapFinishedOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.MapFinished;

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            int? winner = Winner(input);
            s.Header.Status = winner.HasValue ? "Победа: " + OverviewFormat.TeamName(input, winner.Value) : "Ничья";
            s.Header.Score = OverviewFormat.MapScoreLine(input);
            OverviewTeamTables.Add(input, s, new OverviewTeamTableStyle { Columns = OverviewTeamColumns.Kda, WinnerTeam = winner });

            var next = new OverviewSection { Id = "next", Title = "Дальше" };
            OverviewSeriesInput series = input.Series;
            string text;
            if (series != null && series.Running && !series.IsLastMap && series.CurrentIndex + 1 < series.Maps.Count)
                text = "Карта " + (series.CurrentIndex + 2) + ": " + series.Maps[series.CurrentIndex + 1] + " — по команде админа";
            else if (series != null && series.Running)
                text = "Серия сыграна — в лобби по команде админа";
            else
                text = "Решает админ";
            next.AddInfo("next", text);
            s.Sections.Add(next);
        }

        /// <summary>Победитель: записанный итог серии, иначе — единственный лидер по счёту карты; null — ничья.</summary>
        public static int? Winner(OverviewInput input)
        {
            OverviewSeriesInput series = input.Series;
            if (series != null && series.CurrentMapDecided)
            {
                int result = series.Results[series.CurrentIndex];
                return result >= 0 ? result : (int?)null;
            }

            int best = -1, bestScore = int.MinValue;
            bool tie = false;
            foreach (OverviewTeamInput t in input.Teams)
            {
                if (t.MapScore > bestScore) { best = t.Index; bestScore = t.MapScore; tie = false; }
                else if (t.MapScore == bestScore) tie = true;
            }
            return best >= 0 && !tie ? best : (int?)null;
        }
    }

    /// <summary>Серия — справка внизу: какая карта из скольких и счёт по картам.</summary>
    public sealed class SeriesOverview : IOverviewSectionProvider
    {
        public bool Applies(OverviewContext context, OverviewInput input) =>
            context != OverviewContext.Offline && context != OverviewContext.Lobby &&
            input.Series != null && (input.Series.Running || input.Series.AdHoc);

        public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot s)
        {
            OverviewSeriesInput series = input.Series;
            var section = new OverviewSection { Id = "series", Title = "Серия" };
            if (series.Running && series.CurrentIndex >= 0 && series.CurrentIndex < series.Maps.Count)
            {
                section.AddInfo("map", "Карта " + (series.CurrentIndex + 1) + " из " + series.Maps.Count + " · " +
                                       series.Maps[series.CurrentIndex]);
                section.AddInfo("score", OverviewFormat.SeriesScoreLine(input));
            }
            else
            {
                section.AddInfo("map", "Карта без серии");
            }
            s.Sections.Add(section);
        }
    }
}

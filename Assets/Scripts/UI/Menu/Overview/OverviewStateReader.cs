using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>Откуда читать ситуацию. <see cref="Current"/> — живые объекты этой машины; тест подставляет свои.</summary>
    public sealed class OverviewSources
    {
        public bool Online;
        public MapReferee Referee;
        public Series Series;
        public SessionManager Session;
        public PlayerSession Local;
        public IEnumerable<PlayerSession> Players;
        public TeamData[] AllTeams;

        /// <summary>Сцена карты — у <see cref="MapReferee.SceneName"/>, без него — активная сцена.</summary>
        public string SceneName = "";

        /// <summary>Сцена лобби — <c>MapRegistry.LobbyScene</c>.</summary>
        public string LobbyScene = "Lobby";

        /// <summary>Всё, что есть на этой машине сейчас. Сессии — все сетевые <see cref="PlayerSession"/>.</summary>
        public static OverviewSources Current()
        {
            MapReferee referee = MapReferee.Instance;
            SessionManager session = SessionManager.Instance;
            string lobby = session != null && session.MapRegistry != null ? session.MapRegistry.LobbyScene : "Lobby";

            return new OverviewSources
            {
                Online = NetworkClient.active || NetworkServer.active,
                Referee = referee,
                Series = Series.Instance,
                Session = session,
                Local = PlayerSession.LocalSession,
                Players = Object.FindObjectsByType<PlayerSession>(FindObjectsSortMode.None),
                AllTeams = TeamRegistry.Instance != null ? TeamRegistry.Instance.teams : null,
                SceneName = referee != null ? referee.SceneName : SceneManager.GetActiveScene().name,
                LobbyScene = lobby
            };
        }
    }

    /// <summary>
    /// Адаптер «реплицированное состояние → <see cref="OverviewInput"/>» для экрана «Обзор» (T-33).
    /// Только читает: ни команд, ни записи в сеть. Что откуда берётся и что реплицируется —
    /// таблица в <c>Docs/tasks/T-33-tablet-overview-screen.md</c>. Проверяет <c>OverviewStateReaderTests</c>.
    /// </summary>
    public static class OverviewStateReader
    {
        public static OverviewInput Read(OverviewSources src)
        {
            var input = new OverviewInput { Online = src != null && src.Online };
            if (src == null) return input;

            MapReferee referee = src.Referee;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;
            GameMode matchMode = mode != null && !mode.IsWarmup ? mode : null;

            input.IsLobby = !string.IsNullOrEmpty(src.SceneName) && src.SceneName == src.LobbyScene;
            input.MapState = referee != null ? referee.CurrentState : MapState.Warmup;
            input.MapTitle = MapTitle(referee, src.SceneName);
            input.ModeTitle = matchMode != null && matchMode.ModeData != null ? matchMode.ModeData.displayName : "";
            input.MinPlayersToStart = MinPlayers(matchMode, src.Session);

            ReadTeams(input, mode, src);
            ReadSeries(input, src.Series);
            ReadPlan(input, src.Session);
            ReadPlayers(input, src);
            ReadViewer(input, src.Local);
            ReadModeClock(input, matchMode);
            return input;
        }

        private static string MapTitle(MapReferee referee, string scene)
        {
            MapData map = referee != null ? referee.CurrentMap : null;
            return map != null && !string.IsNullOrEmpty(map.displayName) ? map.displayName : scene ?? "";
        }

        private static GameModeData SelectedMode(SessionManager session) =>
            session != null && !string.IsNullOrEmpty(session.SelectedModeId) ? session.SelectedGameModeData : null;

        private static int MinPlayers(GameMode matchMode, SessionManager session)
        {
            if (matchMode != null) return matchMode.MinPlayersToStart;
            GameModeData selected = SelectedMode(session);
            return selected != null ? selected.minPlayersToStart : 0;
        }

        /// <summary>Команды — тем же правилом, что предлагает экран выбора команды: активный режим, выбор серии, реестр.</summary>
        private static void ReadTeams(OverviewInput input, GameMode mode, OverviewSources src)
        {
            TeamData[] teams = MenuTeamSelection.ResolveAvailableTeams(mode, SelectedMode(src.Session), src.AllTeams);
            foreach (TeamData team in teams)
            {
                if (team == null || team.teamIndex == OverviewPlayerInput.NoTeam) continue;
                input.Teams.Add(new OverviewTeamInput
                {
                    Index = team.teamIndex,
                    Name = team.Name,
                    MapScore = mode != null ? mode.GetScore(team) : 0
                });
            }
        }

        private static void ReadSeries(OverviewInput input, Series series)
        {
            if (series == null) return;
            var s = new OverviewSeriesInput
            {
                Running = series.IsRunning,
                AdHoc = series.IsAdHoc,
                CurrentIndex = series.CurrentIndex
            };
            s.Maps.AddRange(series.Maps);
            s.Results.AddRange(series.Results);
            foreach (int winner in series.Results)
                if (winner >= 0) s.MapWins[winner] = (s.MapWins.TryGetValue(winner, out int w) ? w : 0) + 1;
            input.Series = s;
        }

        private static void ReadPlan(OverviewInput input, SessionManager session)
        {
            if (session == null || session.SelectedMaps.Count == 0) return;
            GameModeData mode = SelectedMode(session);
            var plan = new OverviewPlanInput { ModeTitle = mode != null ? mode.displayName : "" };
            plan.Maps.AddRange(session.SelectedMaps);
            input.Plan = plan;
        }

        private static void ReadPlayers(OverviewInput input, OverviewSources src)
        {
            if (src.Players == null) return;
            Series series = src.Series;
            int map = series != null && series.IsRecording ? series.CurrentIndex : -1;

            foreach (PlayerSession s in src.Players)
            {
                if (s == null) continue;
                PlayerController avatar = s.ActiveAvatar;
                TeamData team = s.Team;
                AvatarData skin = team != null ? team.GetAvatar(s.AvatarIndex) : null;

                input.Players.Add(new OverviewPlayerInput
                {
                    Key = Series.PlayerKey(s) ?? "",
                    Name = s.PlayerName,
                    TeamIndex = s.TeamIndex,
                    IsAdmin = s.IsAdmin,
                    IsSpectatorRole = s.Role == GameRole.Spectator,
                    HasAvatar = avatar != null && avatar._actor != null,
                    IsAlive = avatar != null && avatar._actor != null && avatar.IsAlive,
                    Health = avatar != null ? avatar.Health : 0f,
                    IsReady = s.ReadyState,
                    IsCalibrated = s.IsCalibrated,
                    SkinName = skin != null ? skin.displayName ?? "" : "",
                    Kills = map >= 0 ? series.GetKills(s, map) : 0,
                    Deaths = map >= 0 ? series.GetDeaths(s, map) : 0,
                    Assists = map >= 0 ? series.GetAssists(s, map) : 0
                });
            }
        }

        private static void ReadViewer(OverviewInput input, PlayerSession local)
        {
            if (local == null) return;
            PlayerController avatar = local.ActiveAvatar;
            input.Viewer = new OverviewViewer
            {
                PlayerKey = Series.PlayerKey(local) ?? "",
                TeamIndex = local.TeamIndex,
                IsAdmin = local.IsAdmin,
                IsSpectatorRole = local.Role == GameRole.Spectator,
                IsEliminated = avatar != null && avatar._actor != null && !avatar.IsAlive
            };
        }

        private static void ReadModeClock(OverviewInput input, GameMode matchMode)
        {
            if (matchMode is EliminationMode e)
            {
                RoundClock.TryGetTimeRemaining(e, out float seconds, out _);
                input.Elimination = new EliminationRoundInput
                {
                    State = e.CurrentState,
                    Phase = e.CurrentRoundPhase,
                    RoundNumber = e.CurrentRoundNumber,
                    TotalRounds = e.TotalRounds,
                    RoundsToWin = e.RoundsToWin,
                    SidesSwapped = e.SidesSwapped,
                    SecondsRemaining = seconds,
                    CountdownHeld = e.CountdownHeld
                };
            }
            else if (matchMode is RespawnMode r)
            {
                input.MatchTimeRemaining = r.TimeRemaining;
            }
        }
    }
}

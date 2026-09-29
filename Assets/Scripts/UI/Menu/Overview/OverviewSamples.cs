using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Образцовые входы «Обзора» без сети — для превью экрана в редакторе (<c>MenuOverview.BuildPreview</c>,
    /// снимки и проверка вмещаемости) и тестов. Самый нагруженный случай: бой Ликвидации, по 6 игроков
    /// в команде, длинный ник, выбывшие.
    /// </summary>
    public static class OverviewSamples
    {
        public const string LocalKey = "sample:me";

        public static OverviewInput Combat()
        {
            var input = new OverviewInput
            {
                Online = true,
                MapState = MapState.Live,
                MapTitle = "Склады",
                ModeTitle = "Ликвидация",
                MinPlayersToStart = 2,
                Viewer = new OverviewViewer { PlayerKey = LocalKey, TeamIndex = 1, IsAdmin = true },
                Elimination = new EliminationRoundInput
                {
                    State = EliminationState.Active,
                    Phase = RoundPhase.Combat,
                    RoundNumber = 5,
                    TotalRounds = 12,
                    RoundsToWin = 7,
                    SecondsRemaining = 84.4f
                },
                Series = new OverviewSeriesInput
                {
                    Running = true,
                    Maps = { "Порт", "Склады", "Порт" },
                    CurrentIndex = 1,
                    Results = { 1 },
                    MapWins = { [1] = 1 }
                }
            };

            input.Teams.Add(new OverviewTeamInput { Index = 1, Name = "Военные", MapScore = 3 });
            input.Teams.Add(new OverviewTeamInput { Index = 2, Name = "Повстанцы", MapScore = 2 });

            Add(input, LocalKey, "Админ", 1, 4, 2, 1, 76f, admin: true);
            Add(input, "a2", "Сокол", 1, 6, 1, 0, 100f);
            Add(input, "a3", "Ворон", 1, 3, 3, 2, 0f);
            Add(input, "a4", "ИгрокСОченьДлиннымНикомИзВоенных", 1, 2, 2, 1, 41f);
            Add(input, "a5", "Барс", 1, 1, 3, 0, 12f);
            Add(input, "a6", "Лис", 1, 0, 4, 3, 88f);

            Add(input, "b1", "Кобра", 2, 5, 3, 1, 64f);
            Add(input, "b2", "Шторм", 2, 4, 4, 0, 0f);
            Add(input, "b3", "Гром", 2, 3, 2, 2, 100f);
            Add(input, "b4", "Туман", 2, 2, 4, 1, 27f);
            Add(input, "b5", "Рысь", 2, 1, 5, 0, 55f);
            Add(input, "b6", "Дым", 2, 0, 3, 1, 93f);
            return input;
        }

        private static void Add(OverviewInput input, string key, string name, int team, int kills, int deaths, int assists,
                                float health, bool admin = false)
        {
            input.Players.Add(new OverviewPlayerInput
            {
                Key = key, Name = name, TeamIndex = team, IsAdmin = admin,
                HasAvatar = true, IsAlive = health > 0f, Health = health,
                IsReady = true, IsCalibrated = true,
                Kills = kills, Deaths = deaths, Assists = assists
            });
        }
    }
}

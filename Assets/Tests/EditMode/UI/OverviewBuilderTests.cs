using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Модель экрана «Обзор» планшета (T-33): контекст → секции, кто чьё хп видит, и три уровня
    /// сравнения снимков, от которых зависит, сбивается ли наведение луча при перестройке.
    /// Чистая логика — без Mirror, сцены и префабов.
    /// </summary>
    public class OverviewBuilderTests
    {
        private const int A = 1, B = 2;

        private static OverviewPlayerInput P(string key, int team, int kills = 0, int deaths = 0, int assists = 0,
                                             bool alive = true, float hp = 100f, bool ready = false, bool calibrated = true)
        {
            return new OverviewPlayerInput
            {
                Key = key, Name = key, TeamIndex = team, HasAvatar = true, IsAlive = alive, Health = alive ? hp : 0f,
                Kills = kills, Deaths = deaths, Assists = assists, IsReady = ready, IsCalibrated = calibrated
            };
        }

        /// <summary>Идёт бой Elimination на второй карте серии, смотрит игрок me из команды A.</summary>
        private static OverviewInput Combat()
        {
            var input = new OverviewInput
            {
                Online = true,
                MapState = MapState.Live,
                MapTitle = "TestMap2",
                ModeTitle = "Elimination",
                Viewer = new OverviewViewer { PlayerKey = "me", TeamIndex = A },
                Elimination = new EliminationRoundInput
                {
                    State = EliminationState.Active, Phase = RoundPhase.Combat,
                    RoundNumber = 5, TotalRounds = 12, RoundsToWin = 7, SecondsRemaining = 64.2f
                },
                Series = new OverviewSeriesInput
                {
                    Running = true, Maps = { "TestMap1", "TestMap2", "TestMap1" }, CurrentIndex = 1, Results = { A },
                    MapWins = { [A] = 1 }
                }
            };
            input.Teams.Add(new OverviewTeamInput { Index = A, Name = "Военные", MapScore = 3 });
            input.Teams.Add(new OverviewTeamInput { Index = B, Name = "Повстанцы", MapScore = 2 });
            input.Players.Add(P("me", A, kills: 1, deaths: 2, hp: 64.3f));
            input.Players.Add(P("ally", A, kills: 4, deaths: 0, alive: false));
            input.Players.Add(P("foe1", B, kills: 2, hp: 30f));
            input.Players.Add(P("foe2", B, kills: 2, deaths: 1, hp: 90f));
            return input;
        }

        private static OverviewSnapshot Build(OverviewInput input) => OverviewBuilder.Default.Build(input);

        private static string[] Keys(OverviewSection section) => section.Rows.Select(r => r.Key).ToArray();

        private static OverviewRow Row(OverviewSnapshot s, string key) =>
            s.Sections.SelectMany(x => x.Rows).First(r => r.Key == key);

        // ── Контекст ─────────────────────────────────────────────────────────

        [Test]
        public void Контекст_определяется_по_сети_сцене_состоянию_карты_и_итогу_серии()
        {
            Assert.AreEqual(OverviewContext.Offline, OverviewContextResolver.Resolve(new OverviewInput()));
            Assert.AreEqual(OverviewContext.Lobby, OverviewContextResolver.Resolve(new OverviewInput { Online = true, IsLobby = true }));
            Assert.AreEqual(OverviewContext.Warmup, OverviewContextResolver.Resolve(new OverviewInput { Online = true }));
            Assert.AreEqual(OverviewContext.Paused, OverviewContextResolver.Resolve(new OverviewInput { Online = true, MapState = MapState.Paused }));
            Assert.AreEqual(OverviewContext.Live, OverviewContextResolver.Resolve(Combat()));

            OverviewInput decided = Combat();
            decided.MapState = MapState.Warmup;
            decided.Series.Results.Add(B);
            Assert.AreEqual(OverviewContext.MapFinished, OverviewContextResolver.Resolve(decided),
                "Карта в разминке с записанным итогом — это «карта окончена», а не разминка.");

            OverviewInput finishedMode = Combat();
            finishedMode.Elimination.State = EliminationState.Finished;
            Assert.AreEqual(OverviewContext.MapFinished, OverviewContextResolver.Resolve(finishedMode),
                "Режим закончил карту, а MapReferee ещё не вернул разминку, — уже итог.");
        }

        // ── Бой ──────────────────────────────────────────────────────────────

        [Test]
        public void Бой_шапка_раунд_фаза_счёт_и_часы()
        {
            OverviewSnapshot s = Build(Combat());

            Assert.AreEqual(OverviewContext.Live, s.Context);
            Assert.AreEqual("TestMap2", s.Header.Title);
            Assert.AreEqual("Раунд 5 из 12 · Бой", s.Header.Status);
            Assert.AreEqual("Военные 3 : 2 Повстанцы", s.Header.Score);
            Assert.AreEqual("Бой", s.Header.ClockLabel);
            Assert.AreEqual(65, s.Header.ClockSeconds, "Остаток округляется вверх — 0:00 только когда время вышло.");
        }

        [Test]
        public void Бой_своя_команда_первой_игроки_по_убийствам_выбывшие_приглушены()
        {
            OverviewSnapshot s = Build(Combat());

            OverviewSection mine = s.Find("team:" + A);
            OverviewSection theirs = s.Find("team:" + B);
            Assert.NotNull(mine);
            Assert.NotNull(theirs);
            Assert.Less(s.Sections.IndexOf(mine), s.Sections.IndexOf(theirs), "Своя команда должна идти первой.");

            CollectionAssert.AreEqual(new[] { "Игрок", "HP", "У/С/А" }, mine.Columns);
            CollectionAssert.AreEqual(new[] { "ally", "me" }, Keys(mine), "Порядок — по убийствам.");
            CollectionAssert.AreEqual(new[] { "foe1", "foe2" }, Keys(theirs), "При равных убийствах — меньше смертей выше.");
            Assert.AreEqual("Военные — 3 (живы 1/2)", mine.Title);

            OverviewRow me = Row(s, "me");
            CollectionAssert.AreEqual(new[] { "me", "65", "1/2/0" }, me.Cells);
            Assert.AreEqual(OverviewTone.Local, me.Tone);

            OverviewRow ally = Row(s, "ally");
            Assert.AreEqual("выбыл", ally.Cells[1]);
            Assert.AreEqual(OverviewTone.Dimmed, ally.Tone);
        }

        [Test]
        public void Хп_соперника_видно_всем_по_умолчанию_политика_скрывает_от_игрока()
        {
            OverviewSnapshot player = Build(Combat());
            Assert.AreEqual("30", Row(player, "foe1").Cells[1], "Решение пользователя: хп соперников видно всем.");
            Assert.AreEqual("65", Row(player, "me").Cells[1]);

            OverviewInput closed = Combat();
            closed.ShowEnemyHealth = false;
            Assert.AreEqual("жив", Row(Build(closed), "foe1").Cells[1], "Политика выключена — у соперника только «жив».");
            Assert.AreEqual("65", Row(Build(closed), "me").Cells[1], "Своя команда видна всегда.");

            OverviewInput spectator = Combat();
            spectator.ShowEnemyHealth = false;
            spectator.Viewer = new OverviewViewer { PlayerKey = "admin", IsSpectatorRole = true };
            Assert.AreEqual("30", Row(Build(spectator), "foe1").Cells[1], "Наблюдатель видит хп всех и без политики.");

            OverviewInput noTeam = Combat();
            noTeam.ShowEnemyHealth = false;
            noTeam.Viewer = new OverviewViewer { PlayerKey = "x", TeamIndex = OverviewPlayerInput.NoTeam };
            Assert.AreEqual("30", Row(Build(noTeam), "foe1").Cells[1], "Игрок вне команд матча — зритель, видит всех.");
        }

        [Test]
        public void Выбывший_видит_пометку_в_шапке()
        {
            OverviewInput input = Combat();
            input.Viewer.IsEliminated = true;
            StringAssert.Contains("выбыли", Build(input).Header.ViewerNote);
            Assert.AreEqual("", Build(Combat()).Header.ViewerNote);
        }

        [Test]
        public void Закупка_колонка_готовности_вместо_хп_и_предупреждение_об_остановленном_отсчёте()
        {
            OverviewInput input = Combat();
            input.Elimination.Phase = RoundPhase.Countdown;
            input.Elimination.CountdownHeld = true;
            input.Players[0].IsReady = true;

            OverviewSnapshot s = Build(input);

            CollectionAssert.AreEqual(new[] { "Игрок", "Готов", "У/С/А" }, s.Find("team:" + A).Columns);
            Assert.AreEqual("да", Row(s, "me").Cells[1]);
            Assert.AreEqual("нет", Row(s, "foe1").Cells[1]);
            Assert.AreEqual("Отсчёт", s.Header.ClockLabel);
            OverviewSection alert = s.Find("alert");
            Assert.NotNull(alert, "Отсчёт стоит — нужно сказать почему.");
            Assert.AreEqual(OverviewTone.Warning, alert.Rows[0].Tone);
        }

        [Test]
        public void Серия_последней_секцией_номер_карты_и_счёт_по_картам()
        {
            OverviewSnapshot s = Build(Combat());
            OverviewSection series = s.Find("series");
            Assert.NotNull(series);
            Assert.AreEqual(s.Sections.Count - 1, s.Sections.IndexOf(series), "Серия — справка, внизу.");
            CollectionAssert.AreEqual(new[] { "Карта 2 из 3 · TestMap2", "По картам: Военные 1 : 0 Повстанцы" },
                series.Rows.Select(r => r.Cells[0]).ToArray());
        }

        // ── Сравнение снимков ────────────────────────────────────────────────

        [Test]
        public void Тик_часов_не_меняет_содержимое_хп_меняет_содержимое_но_не_раскладку()
        {
            OverviewSnapshot before = Build(Combat());

            OverviewInput tick = Combat();
            tick.Elimination.SecondsRemaining = 50f;
            OverviewSnapshot afterTick = Build(tick);
            Assert.AreNotEqual(before.Header.ClockSeconds, afterTick.Header.ClockSeconds);
            Assert.IsTrue(before.SameContent(afterTick), "Тик часов не должен перестраивать таблицу.");

            OverviewInput hit = Combat();
            hit.Players[0].Health = 20f;
            OverviewSnapshot afterHit = Build(hit);
            Assert.IsFalse(before.SameContent(afterHit), "Хп сменилось — ячейку нужно обновить.");
            Assert.IsTrue(before.SameLayout(afterHit), "Хп не меняет раскладку — строки не пересоздаются.");

            OverviewInput kill = Combat();
            kill.Players[0].Kills = 9;
            Assert.IsFalse(before.SameLayout(Build(kill)), "Порядок строк сменился — раскладка другая.");
        }

        // ── Разминка, пауза, итог карты ─────────────────────────────────────

        [Test]
        public void Разминка_без_команды_отдельно_и_сколько_игроков_до_старта()
        {
            var input = new OverviewInput
            {
                Online = true, MapTitle = "TestMap1", MinPlayersToStart = 2,
                Viewer = new OverviewViewer { PlayerKey = "me", TeamIndex = A }
            };
            input.Teams.Add(new OverviewTeamInput { Index = A, Name = "Военные" });
            input.Teams.Add(new OverviewTeamInput { Index = B, Name = "Повстанцы" });
            input.Players.Add(P("me", A));
            input.Players.Add(P("lost", OverviewPlayerInput.NoTeam, calibrated: false));

            OverviewSnapshot s = Build(input);

            Assert.AreEqual(OverviewContext.Warmup, s.Context);
            StringAssert.StartsWith("Разминка", s.Header.Status);
            Assert.AreEqual("", s.Header.Score, "В разминке счёта нет.");
            Assert.IsNull(s.Header.ClockSeconds);
            CollectionAssert.AreEqual(new[] { "Игрок", "Калибровка" }, s.Find("team:" + A).Columns);

            OverviewSection noTeam = s.Find("no-team");
            Assert.NotNull(noTeam);
            CollectionAssert.AreEqual(new[] { "lost" }, Keys(noTeam));
            Assert.AreEqual(OverviewTone.Warning, noTeam.Rows[0].Tone);

            OverviewSection start = s.Find("start");
            Assert.NotNull(start);
            Assert.AreEqual("Игроков в командах: 1, нужно минимум 2", start.Rows[0].Cells[0]);
            Assert.AreEqual(OverviewTone.Warning, start.Rows[0].Tone);
        }

        [Test]
        public void Пауза_счёт_без_хп()
        {
            OverviewInput input = Combat();
            input.MapState = MapState.Paused;

            OverviewSnapshot s = Build(input);

            Assert.AreEqual(OverviewContext.Paused, s.Context);
            StringAssert.StartsWith("Пауза", s.Header.Status);
            Assert.AreEqual("Военные 3 : 2 Повстанцы", s.Header.Score);
            Assert.IsNull(s.Header.ClockSeconds, "На паузе часы стоят — не показываем.");
            CollectionAssert.AreEqual(new[] { "Игрок", "У/С/А" }, s.Find("team:" + A).Columns);
        }

        [Test]
        public void Итог_карты_победитель_из_серии_и_что_дальше()
        {
            OverviewInput input = Combat();
            input.MapState = MapState.Warmup;
            input.Elimination = null;
            input.Series.Results.Add(B);

            OverviewSnapshot s = Build(input);

            Assert.AreEqual(OverviewContext.MapFinished, s.Context);
            Assert.AreEqual("Победа: Повстанцы", s.Header.Status);
            Assert.AreEqual(OverviewTone.Winner, s.Find("team:" + B).Tone);
            OverviewSection next = s.Find("next");
            Assert.NotNull(next);
            StringAssert.Contains("TestMap1", next.Rows[0].Cells[0], "Дальше — следующая карта серии.");

            input.Series.Results[1] = -1;
            Assert.AreEqual("Ничья", Build(input).Header.Status);

            input.Series.CurrentIndex = 2;
            input.Series.Results.Add(A);
            StringAssert.Contains("лобби", Build(input).Find("next").Rows[0].Cells[0], "После последней карты — лобби.");
        }

        // ── Лобби, без сети ──────────────────────────────────────────────────

        [Test]
        public void Лобби_первым_блок_Вы_команда_скин_калибровка()
        {
            var input = new OverviewInput { Online = true, IsLobby = true, Viewer = new OverviewViewer { PlayerKey = "me", TeamIndex = A } };
            input.Teams.Add(new OverviewTeamInput { Index = A, Name = "Военные" });
            OverviewPlayerInput me = P("me", A);
            me.SkinName = "Спецназ";
            input.Players.Add(me);
            input.Players.Add(P("other", A));

            OverviewSnapshot s = Build(input);

            OverviewSection you = s.Sections[0];
            Assert.AreEqual("you", you.Id, "В лобби первым — блок «Вы».");
            CollectionAssert.AreEqual(new[] { "Команда: Военные", "Скин: Спецназ", "Калибровка: пройдена" },
                you.Rows.Select(r => r.Cells[0]).ToArray());
            Assert.AreEqual(OverviewTone.Success, you.Rows[2].Tone);

            me.TeamIndex = OverviewPlayerInput.NoTeam;
            me.IsCalibrated = false;
            me.SkinName = "";
            input.Viewer.TeamIndex = OverviewPlayerInput.NoTeam;
            you = Build(input).Find("you");
            Assert.AreEqual(2, you.Rows.Count, "Скин неизвестен — строки нет.");
            Assert.IsTrue(you.Rows.All(r => r.Tone == OverviewTone.Warning), "Без команды и без калибровки — обе строки внимания.");
            StringAssert.Contains("не выбрана", you.Rows[0].Cells[0]);
            StringAssert.Contains("не пройдена", you.Rows[1].Cells[0]);
        }

        [Test]
        public void Лобби_зал_с_калибровкой_план_серии_и_итог_прошлой()
        {
            var input = new OverviewInput
            {
                Online = true, IsLobby = true,
                Viewer = new OverviewViewer { PlayerKey = "me" },
                Plan = new OverviewPlanInput { ModeTitle = "Elimination", Maps = { "TestMap1", "TestMap2" } }
            };
            input.Teams.Add(new OverviewTeamInput { Index = A, Name = "Военные" });
            input.Teams.Add(new OverviewTeamInput { Index = B, Name = "Повстанцы" });
            input.Players.Add(P("me", A));
            input.Players.Add(P("new", OverviewPlayerInput.NoTeam, calibrated: false));

            OverviewSnapshot s = Build(input);

            Assert.AreEqual(OverviewContext.Lobby, s.Context);
            Assert.AreEqual("Лобби", s.Header.Title);
            Assert.AreEqual("Следующая серия: Elimination, карт: 2", s.Header.Status);
            Assert.AreEqual("hall", s.Sections[s.Sections.Count - 1].Id, "Зал — последним, он длинный.");
            OverviewSection hall = s.Find("hall");
            Assert.NotNull(hall);
            CollectionAssert.AreEqual(new[] { "Игрок", "Команда", "Калибровка" }, hall.Columns);
            CollectionAssert.AreEqual(new[] { "me", "new" }, Keys(hall), "Зал — все игроки по имени.");
            Assert.AreEqual(OverviewTone.Warning, Row(s, "new").Tone, "Не откалиброван и без команды — внимание.");
            Assert.AreEqual("Военные", Row(s, "me").Cells[1]);
            Assert.IsNull(s.Find("last-series"), "Серий не было — секции итога нет.");
            Assert.NotNull(s.Find("plan"));

            input.Plan = null;
            input.Series = new OverviewSeriesInput { Running = false, Results = { A, B, A }, MapWins = { [A] = 2, [B] = 1 } };
            s = Build(input);
            Assert.AreEqual("Серия не выбрана", s.Header.Status);
            OverviewSection last = s.Find("last-series");
            Assert.NotNull(last, "После серии в лобби виден её итог.");
            CollectionAssert.AreEqual(new[] { "Победитель: Военные", "По картам: Военные 2 : 1 Повстанцы" },
                last.Rows.Select(r => r.Cells[0]).ToArray());
            Assert.AreEqual(OverviewTone.Winner, last.Rows[0].Tone);
        }

        [Test]
        public void Без_сети_одна_секция_с_пояснением()
        {
            OverviewSnapshot s = Build(new OverviewInput());
            Assert.AreEqual(OverviewContext.Offline, s.Context);
            Assert.AreEqual("Нет подключения", s.Header.Title);
            Assert.AreEqual(1, s.Sections.Count);
        }

        // ── Режим Respawn и расширяемость ───────────────────────────────────

        [Test]
        public void Respawn_часы_до_конца_матча_и_таблица_по_командам()
        {
            OverviewInput input = Combat();
            input.Elimination = null;
            input.MatchTimeRemaining = 125.5f;

            OverviewSnapshot s = Build(input);

            Assert.AreEqual("До конца", s.Header.ClockLabel);
            Assert.AreEqual(126, s.Header.ClockSeconds);
            Assert.AreEqual("Военные 3 : 2 Повстанцы", s.Header.Score);
            Assert.AreEqual("респаун", Row(s, "ally").Cells[1], "В Respawn выбывший ждёт возрождения, а не конца раунда.");
        }

        private sealed class CaptureFlagProvider : IOverviewSectionProvider
        {
            public bool Applies(OverviewContext context, OverviewInput input) => context == OverviewContext.Live;
            public void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot snapshot)
            {
                snapshot.Sections.Add(new OverviewSection { Id = "flags", Title = "Флаги" });
            }
        }

        [Test]
        public void Новый_режим_добавляет_свою_стратегию_а_не_ветку_в_экране()
        {
            var providers = OverviewBuilder.Default.Providers.ToList();
            providers.Add(new CaptureFlagProvider());
            var builder = new OverviewBuilder(providers);

            Assert.NotNull(builder.Build(Combat()).Find("flags"));
            Assert.IsNull(builder.Build(new OverviewInput { Online = true }).Find("flags"), "Стратегия спрашивается только в своём контексте.");
        }

        // ── Кнопки админа ────────────────────────────────────────────────────

        private static string[] Labels(OverviewAdminPlan plan) => plan.Others.Select(a => a.Label).ToArray();

        [Test]
        public void Админ_главное_действие_двигает_игру_стоп_последним_и_Danger()
        {
            System.Func<MapCommand, bool> warmup = c => AdminMapCommands.IsAvailable(c, MapState.Warmup, true, true, true);
            OverviewAdminPlan plan = OverviewAdminActions.Plan(OverviewContext.Warmup, false, warmup);
            Assert.AreEqual(MapCommand.GoLive, plan.Primary.Command);
            CollectionAssert.AreEqual(new[] { "Следующая карта", "Игроки и команды", "Стоп серии" }, Labels(plan));
            Assert.IsTrue(plan.Others.Last().Danger);

            System.Func<MapCommand, bool> live = c => AdminMapCommands.IsAvailable(c, MapState.Live, true, true, true);
            plan = OverviewAdminActions.Plan(OverviewContext.Live, false, live);
            Assert.IsNull(plan.Primary, "В бою главного действия нет — нечаянная «Пауза» обрывает раунд.");
            CollectionAssert.AreEqual(new[] { "Пауза", "Стоп серии" }, Labels(plan));

            System.Func<MapCommand, bool> paused = c => AdminMapCommands.IsAvailable(c, MapState.Paused, true, true, true);
            Assert.AreEqual(MapCommand.Resume, OverviewAdminActions.Plan(OverviewContext.Paused, false, paused).Primary.Command);

            plan = OverviewAdminActions.Plan(OverviewContext.MapFinished, true, warmup);
            Assert.AreEqual("В лобби", plan.Primary.Label, "На последней карте «Следующая карта» ведёт в лобби.");
            CollectionAssert.Contains(Labels(plan), "Начать матч", "Карту можно переиграть.");
        }

        [Test]
        public void Админ_в_лобби_выбор_серии_и_игроки_без_сети_ничего()
        {
            OverviewAdminPlan lobby = OverviewAdminActions.Plan(OverviewContext.Lobby, false, c => false);
            Assert.AreEqual(MenuScreenType.SessionSetup, lobby.Primary.Screen);
            Assert.IsNull(lobby.Primary.Command);
            CollectionAssert.AreEqual(new[] { "Игроки и команды" }, Labels(lobby));

            OverviewAdminPlan offline = OverviewAdminActions.Plan(OverviewContext.Offline, false, c => true);
            Assert.IsNull(offline.Primary);
            Assert.IsEmpty(offline.Others);

            OverviewAdminPlan unavailable = OverviewAdminActions.Plan(OverviewContext.Warmup, false, c => false);
            Assert.IsNull(unavailable.Primary, "Недоступная команда главным действием не становится.");
        }

        [Test]
        public void Образец_для_превью_бой_две_команды_по_шесть_смотрит_админ()
        {
            OverviewSnapshot s = Build(OverviewSamples.Combat());

            Assert.AreEqual(OverviewContext.Live, s.Context);
            Assert.AreEqual("Раунд 5 из 12 · Бой", s.Header.Status);
            Assert.AreEqual("Военные 3 : 2 Повстанцы", s.Header.Score);
            Assert.AreEqual(6, s.Find("team:1").Rows.Count);
            Assert.AreEqual(6, s.Find("team:2").Rows.Count);
            Assert.IsTrue(s.Sections.SelectMany(x => x.Rows).Count(r => r.Tone == OverviewTone.Dimmed) >= 2, "В образце есть выбывшие.");
            Assert.AreEqual(OverviewTone.Local, Row(s, OverviewSamples.LocalKey).Tone);
        }

        [Test]
        public void Часы_минуты_и_секунды()
        {
            Assert.AreEqual("0:00", OverviewFormat.Clock(0));
            Assert.AreEqual("1:05", OverviewFormat.Clock(65));
            Assert.AreEqual("12:30", OverviewFormat.Clock(750));
            Assert.AreEqual("0:00", OverviewFormat.Clock(-3));
        }
    }
}

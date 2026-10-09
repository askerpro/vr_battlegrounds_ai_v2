using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Сквозной смок сессии: серия из двух карт от старта до возврата в лобби одним прогоном.
    ///
    /// <para>
    /// Что доказывает. Звенья по отдельности закрыты своими тестами (фазы раунда, смена сторон, счёт карты, серия
    /// по картам); здесь проверяются стыки между ними: карта стартует в разминке → «Начать матч» → раунд проходит
    /// все фазы → следующий раунд стартует сам → после половины команды меняются сторонами и раунд после смены
    /// стартует → большинство раундов заканчивает карту → карта в разминке, итог в счёте серии, стороны на месте →
    /// «Следующая карта» → вторая карта в разминке с сохранённым счётом серии и командами → её матч с первой
    /// половины → конец серии → лобби, команды распущены.
    /// </para>
    ///
    /// <para>
    /// Как. Настоящие <see cref="Series"/>, <see cref="MapReferee"/> (запуск карты — как у <c>MapBootstrap</c>,
    /// <see cref="MirrorTestHarness.StartMapRun"/>) и <see cref="EliminationMode"/>; матч крутится его
    /// <c>ServerTick</c> (<see cref="RoundFlowDriver"/>), исход раунда задаёт тест. Сцены не грузятся: загрузчик серии
    /// подменён, смена карты — новый судья. Настройки режима — значения по умолчанию из кода (3 раунда в половине,
    /// 4 победы до конца карты). Живой прогон на сценах — этап series-smoke-e2e.
    /// </para>
    /// </summary>
    public class SeriesSmokeTests : MirrorTestHarness
    {
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<string> _loads = new List<string>();
        private TeamData _a, _b;
        private GameModeData _warmup, _elimination;
        private StubPlayerRoster _roster;
        private Series _series;
        private PlayerSession _pa, _pb;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            SpawnSides.Reset();
            _loads.Clear();
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);

            _warmup = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _warmup.modeId = "warmup";

            _elimination = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _elimination.modeId = "elimination";
            _elimination.minPlayersToStart = 2;
            _elimination.teams = new[] { _a, _b };

            var registry = Asset(ScriptableObject.CreateInstance<GameModeRegistry>());
            registry.warmup = _warmup;
            registry.modes = new[] { _elimination };

            var lobby = Map("Lobby");
            var maps = Asset(ScriptableObject.CreateInstance<MapRegistry>());
            maps.maps = new[] { lobby, Map("MapA", _elimination), Map("MapB", _elimination) };
            maps.lobby = lobby;

            SessionManager session = CreateNetworkComponent<SessionManager>("SessionManager");
            MatchFlowTests.InstallSessionManager(session, registry, maps);
            SpawnOnServer(session);

            _series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(_series, "Awake");
            _series.LoadMapOverride = scene => _loads.Add(scene);
            SpawnOnServer(_series);

            _roster = new StubPlayerRoster();
            _series.PlayerRoster = _roster;
            _pa = ReadySession("PA", _a);
            _pb = ReadySession("PB", _b);
            _roster.Add(_a, _pa);
            _roster.Add(_b, _pb);
        }

        [TearDown]
        public void Drop()
        {
            SpawnSides.Reset();
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        private T Asset<T>(T o) where T : Object { _assets.Add(o); return o; }

        private MapData Map(string scene, params GameModeData[] modes)
        {
            MapData map = Asset(ScriptableObject.CreateInstance<MapData>());
            map.sceneName = scene;
            map.supportedModes = modes;
            return map;
        }

        private PlayerSession ReadySession(string name, TeamData team)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        /// <summary>Судья карты, запущенный так, как его запускает <c>MapBootstrap</c>, — в разминке.</summary>
        private MapReferee StartMap(string scene, out Maps.TestMapRun run)
        {
            MapReferee referee = CreateNetworkComponent<MapReferee>("MapReferee " + scene);
            InvokeLifecycleMethod(referee, "Awake");
            referee.SceneNameOverride = scene;
            referee.ModeFactory = data =>
            {
                GameMode mode = data == _warmup
                    ? (GameMode)CreateNetworkComponent<WarmupMode>("WarmupMode")
                    : CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokeLifecycleMethod(mode, "Awake");
                mode.PlayerRoster = _roster;
                return mode.gameObject;
            };
            run = StartMapRun(referee, scene, _warmup.modeId, _series.CapturedModeId, _elimination.modeId);
            return referee;
        }

        /// <summary>
        /// Выгрузка карты, как в игре: загрузка следующей закрывает запуск (Closing, <c>MapLoader.MapLoadStarted</c>),
        /// выгрузка сцены уничтожает судью и снимает запуск (<c>Retire</c>).
        /// </summary>
        private static void UnloadMap(MapReferee referee, Maps.TestMapRun run)
        {
            Assert.IsTrue(run.Authority.Close(run.Scope, run.Authority.Current.Revision), "Запуск карты не закрылся.");
            Object.DestroyImmediate(referee.gameObject);
            Assert.IsTrue(run.Authority.Retire(run.Scope, run.Authority.Current.Revision), "Запуск карты не снят.");
        }

        private static EliminationMode Match(MapReferee referee) => referee.ActiveGameMode as EliminationMode;

        private RoundFlowDriver Driver(MapReferee referee) => new RoundFlowDriver(
            dt =>
            {
                _roster.DeclareAllReady();
                EliminationMode match = Match(referee);
                if (match != null) match.ServerTick(dt);
            },
            () => Match(referee) != null ? Match(referee).CurrentRoundPhase : RoundPhase.Setup);

        /// <summary>
        /// Матч карты до конца: раунды по <paramref name="winners"/>; после каждого — следующий раунд стартует сам,
        /// пока карта не решена. Возвращает след фаз матча.
        /// </summary>
        private RoundFlowDriver PlayMap(MapReferee referee, string map, params TeamData[] winners)
        {
            Assert.IsInstanceOf<WarmupMode>(referee.ActiveGameMode, $"{map}: карта стартовала не в разминке.");
            Assert.IsTrue(referee.GoLive(), $"{map}: «Начать матч» не сработал.");
            EliminationMode match = Match(referee);
            Assert.IsNotNull(match, $"{map}: после «Начать матч» режим не Elimination.");

            RoundFlowDriver driver = Driver(referee);
            for (int i = 0; i < winners.Length; i++)
            {
                int round = i + 1;
                driver.AdvanceUntil(() => Match(referee) == match && match.CurrentRoundNumber == round &&
                                          match.CurrentRoundPhase == RoundPhase.Combat,
                                    $"{map}: боя раунда {round}");
                Assert.AreEqual(round > 3, match.SidesSwapped,
                    $"{map}, раунд {round}: стороны {(match.SidesSwapped ? "поменяны" : "на месте")}, " +
                    "а должны меняться ровно после третьего раунда (первой половины).");

                match.RoundPhases.RequestRoundEnd(winners[i]);
                bool last = i == winners.Length - 1;
                if (last)
                {
                    driver.AdvanceUntil(() => !(referee.ActiveGameMode is EliminationMode),
                                        $"{map}: конца карты после раунда {round}");
                }
                else
                {
                    driver.AdvanceUntil(() => match.CurrentRoundNumber == round + 1,
                                        $"{map}: старта раунда {round + 1} после раунда {round}");
                }
            }
            return driver;
        }

        [Test]
        public void Серия_из_двух_карт_от_старта_до_лобби()
        {
            // Старт серии: первая карта.
            Assert.IsTrue(_series.ServerBegin(new[] { "MapA", "MapB" }, "elimination"));
            CollectionAssert.AreEqual(new[] { "MapA" }, _loads, "Серия не загрузила первую карту.");

            // Карта 1: A берёт первую половину 3:0 и четвёртый раунд после смены сторон — 4 из 6, карта решена.
            MapReferee mapA = StartMap("MapA", out Maps.TestMapRun runA);
            RoundFlowDriver driverA = PlayMap(mapA, "MapA", _a, _a, _a, _a);

            List<RoundPhase> phases = driverA.PhaseSequence();
            foreach (RoundPhase phase in new[] { RoundPhase.Setup, RoundPhase.Equipment, RoundPhase.Countdown,
                                                 RoundPhase.Combat, RoundPhase.Resolution, RoundPhase.Scoreboard })
                CollectionAssert.Contains(phases, phase, $"MapA: фаза {phase} не наблюдалась. След: {driverA.DumpSequence()}");

            Assert.IsInstanceOf<WarmupMode>(mapA.ActiveGameMode, "MapA: после конца карты нет разминки.");
            Assert.AreEqual(1, _series.GetMapWins(_a), "MapA: итог карты не попал в счёт серии.");
            Assert.AreEqual(0, _series.GetMapWins(_b));
            Assert.AreEqual(4, _series.GetRoundsWon(_a, Series.Total), "MapA: раунды не попали в общий счёт.");
            Assert.IsFalse(SpawnSides.Swapped, "MapA: после конца карты стороны остались поменянными.");
            CollectionAssert.AreEqual(new[] { "MapA" }, _loads, "Серия сама ушла на следующую карту — решать должен админ.");
            Assert.IsTrue(AdminMapCommands.IsAvailable(MapCommand.NextMap), "После конца карты нет «Следующей карты».");

            // «Следующая карта».
            Assert.IsTrue(AdminMapCommands.ServerExecute(AdminSession(), MapCommand.NextMap), "«Следующая карта» отклонена.");
            CollectionAssert.AreEqual(new[] { "MapA", "MapB" }, _loads, "«Следующая карта» загрузила не MapB.");
            Assert.AreEqual(_a.teamIndex, _pa.TeamIndex, "Переход на следующую карту сбросил команду.");
            Assert.AreEqual(_b.teamIndex, _pb.TeamIndex);

            // Смена сцены: судья первой карты уходит вместе с ней, вторую запускает новый.
            UnloadMap(mapA, runA);
            MapReferee mapB = StartMap("MapB", out _);
            Assert.AreEqual(1, _series.GetMapWins(_a), "MapB: счёт серии не пережил смену карты.");

            // Карта 2: B выигрывает 4 раунда подряд (3 в первой половине, 1 после смены сторон).
            PlayMap(mapB, "MapB", _b, _b, _b, _b);
            Assert.IsInstanceOf<WarmupMode>(mapB.ActiveGameMode, "MapB: после конца карты нет разминки.");
            Assert.AreEqual(1, _series.GetMapWins(_a));
            Assert.AreEqual(1, _series.GetMapWins(_b), "MapB: итог второй карты не попал в счёт серии.");
            Assert.IsTrue(_series.IsLastMap, "MapB — последняя карта серии.");

            // Конец серии: «Следующая карта» на последней ведёт в лобби.
            Assert.IsTrue(AdminMapCommands.ServerExecute(AdminSession(), MapCommand.NextMap), "Переход в лобби отклонён.");
            CollectionAssert.AreEqual(new[] { "MapA", "MapB", "Lobby" }, _loads, "После последней карты серия не ушла в лобби.");
            Assert.IsFalse(_series.IsRunning, "Серия после лобби всё ещё идёт.");
            Assert.AreEqual(0, _pa.TeamIndex, "Конец серии не распустил команды.");
            Assert.AreEqual(0, _pb.TeamIndex);
        }

        /// <summary>Админ для кнопок карты: команды исполняются на сервере только от админа.</summary>
        private PlayerSession AdminSession()
        {
            _pa.IsAdmin = true;
            return _pa;
        }
    }
}

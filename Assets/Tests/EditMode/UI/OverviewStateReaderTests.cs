using System.Linq;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;
using VrBattlegrounds.UI.Menu.Overview;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Читатель ситуации для «Обзора» (T-33): настоящие сетевые компоненты (ярус A — Mirror сервером
    /// без сокета) → <see cref="OverviewInput"/>. Проверяет, что до модели доезжает то, что она
    /// рисует: состояние карты, команды режима со счётом, игроки с хп и «жив», У/С/А текущей карты
    /// из <see cref="Series"/> по ключу сессии, свой ключ и «выбыл», серия, раунд Elimination.
    /// </summary>
    public class OverviewStateReaderTests : MirrorTestHarness
    {
        private TeamData _a, _b;

        [SetUp]
        public void Prepare()
        {
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);
        }

        private Series CreateSeries()
        {
            Series series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(series, "Awake");
            series.LoadMapOverride = _ => { };
            SpawnOnServer(series);
            series.ServerBegin(new[] { "MapA", "MapB" });
            return series;
        }

        private MapReferee CreateLiveMatch(out EliminationMode mode)
        {
            MapReferee referee = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(referee, "Awake");
            mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            mode.Initialize(new[] { _a, _b });
            InvokePrivateMethod(referee, "RegisterActiveGameMode", mode);
            SetPrivateField(referee, "_currentState", MapState.Live);
            return referee;
        }

        private PlayerController CreatePlayer(string name, TeamData team)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name + "_Session");
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;
            session.ServerAcceptCalibration(PlayerCalibration.None.WithCalibrated(true), PlayerSession.CalibrationOrigin.Connect);

            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            InvokePrivateMethod(player, "LinkSession", session);
            session.ActiveAvatar = player; // читатель идёт от сессии к аватару, как на клиенте
            actor.Life = 100f;
            return player;
        }

        private OverviewSources Sources(MapReferee referee, Series series, PlayerSession local, params PlayerController[] players) =>
            new OverviewSources
            {
                Online = true,
                Referee = referee,
                Series = series,
                Local = local,
                Players = players.Select(p => p.Session).ToArray(),
                AllTeams = TeamRegistry.Instance.teams,
                SceneName = "MapA",
                LobbyScene = "Lobby"
            };

        [Test]
        public void Матч_идёт_игроки_счёт_хп_и_статистика_текущей_карты_доезжают_до_модели()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            MapReferee referee = CreateLiveMatch(out EliminationMode mode);
            PlayerController killer = CreatePlayer("Killer", _a);
            PlayerController victim = CreatePlayer("Victim", _b);
            killer.GetComponent<UxrActor>().Life = 70f;
            mode.AssignScore(_a, 3);

            victim.GetComponent<UxrActor>().ReceiveImpact(killer.GetComponent<UxrActor>(), default(RaycastHit), 500f);
            Assert.IsFalse(victim.IsAlive, "Контроль: жертва погибла.");

            OverviewInput input = OverviewStateReader.Read(Sources(referee, series, killer.Session, killer, victim));

            Assert.IsTrue(input.Online);
            Assert.IsFalse(input.IsLobby);
            Assert.AreEqual(MapState.Live, input.MapState);
            Assert.AreEqual(OverviewContext.Live, OverviewContextResolver.Resolve(input));

            CollectionAssert.AreEquivalent(new[] { _a.teamIndex, _b.teamIndex }, input.Teams.Select(t => t.Index).ToArray(),
                "Команды — активного режима.");
            Assert.AreEqual(3, input.Teams.First(t => t.Index == _a.teamIndex).MapScore, "Счёт карты — GameMode.GetScore.");
            Assert.AreEqual(_a.Name, input.Teams.First(t => t.Index == _a.teamIndex).Name);

            OverviewPlayerInput k = input.Players.First(p => p.Name == "Killer");
            OverviewPlayerInput v = input.Players.First(p => p.Name == "Victim");
            Assert.AreEqual(Series.PlayerKey(killer.Session), k.Key);
            Assert.AreEqual(_a.teamIndex, k.TeamIndex);
            Assert.IsTrue(k.HasAvatar && k.IsAlive);
            Assert.AreEqual(70f, k.Health, 0.01f, "Хп — UxrActor.Life аватара сессии.");
            Assert.IsTrue(k.IsCalibrated);
            Assert.AreEqual(1, k.Kills, "Убийство текущей карты из Series.");
            Assert.AreEqual(1, v.Deaths);
            Assert.IsFalse(v.IsAlive, "Выбывший — не жив.");

            Assert.AreEqual(k.Key, input.Viewer.PlayerKey, "Смотрящий — локальная сессия.");
            Assert.AreEqual(_a.teamIndex, input.Viewer.TeamIndex);
            Assert.IsFalse(input.Viewer.IsEliminated);

            Assert.IsNotNull(input.Elimination, "Режим Elimination — данные раунда.");
            Assert.AreEqual(mode.CurrentRoundPhase, input.Elimination.Phase);
            Assert.AreEqual(mode.TotalRounds, input.Elimination.TotalRounds);
            Assert.IsNull(input.MatchTimeRemaining);

            Assert.IsNotNull(input.Series);
            Assert.IsTrue(input.Series.Running);
            CollectionAssert.AreEqual(new[] { "MapA", "MapB" }, input.Series.Maps);
            Assert.AreEqual(0, input.Series.CurrentIndex);

            OverviewInput asVictim = OverviewStateReader.Read(Sources(referee, series, victim.Session, killer, victim));
            Assert.IsTrue(asVictim.Viewer.IsEliminated, "Выбывший смотрящий — призрак.");
        }

        [Test]
        public void Лобби_по_имени_сцены_без_оркестратора_разминка()
        {
            SilenceMirrorNoise();
            PlayerController me = CreatePlayer("Me", _a);
            OverviewSources src = Sources(null, null, me.Session, me);
            src.SceneName = "Lobby";

            OverviewInput input = OverviewStateReader.Read(src);

            Assert.IsTrue(input.IsLobby);
            Assert.AreEqual(MapState.Warmup, input.MapState);
            Assert.AreEqual(OverviewContext.Lobby, OverviewContextResolver.Resolve(input));
            Assert.AreEqual(1, input.Players.Count);
            Assert.IsNotEmpty(input.Teams, "Без режима — команды реестра: нужны названия в блоке «Вы».");
        }
    }
}

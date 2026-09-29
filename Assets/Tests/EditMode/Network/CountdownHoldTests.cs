using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Обратный отсчёт к бою идёт, только пока все живые стоят в своих зонах. Вышел — отсчёт
    /// встаёт на полный и ждёт; вернулись все — идёт сначала, а не с места остановки. Выбывший
    /// отсчёт не держит.
    /// </summary>
    public class CountdownHoldTests : MirrorTestHarness
    {
        private const float Countdown = 3f;

        private EliminationMode _mode;
        private TeamData _teamA, _teamB;
        private StubPlayerRoster _roster;
        private RoundFlowDriver _driver;
        private PlayerSession _a, _b;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            SpawnSides.Reset();

            _teamA = TeamRegistry.Instance.GetByIndex(1);
            _teamB = TeamRegistry.Instance.GetByIndex(2);

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SetPrivateField(_mode, "_countdownDuration", Countdown);
            SpawnOnServer(_mode);

            _roster = new StubPlayerRoster();
            _a = CreatePlayer("A", _teamA);
            _b = CreatePlayer("B", _teamB);
            _mode.PlayerRoster = _roster;

            _driver = new RoundFlowDriver(dt => { _roster.DeclareAllReady(); _mode.ServerTick(dt); },
                                          () => _mode.CurrentRoundPhase);

            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Countdown, "обратного отсчёта");
        }

        [TearDown]
        public void ResetSides() => SpawnSides.Reset();

        private PlayerSession CreatePlayer(string name, TeamData team)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name + "_Session");
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;

            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            session.ActiveAvatar = player;
            actor.Life = 100f;

            session.ServerEnterSpawnZone(team.teamIndex);
            _roster.Add(team, session);
            return session;
        }

        private void Tick(float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.1f) _mode.ServerTick(0.1f);
        }

        [Test]
        public void Вышедший_из_зоны_держит_отсчёт()
        {
            SilenceMirrorNoise();
            _a.ServerExitSpawnZone(_teamA.teamIndex);

            Tick(Countdown * 3);

            Assert.AreEqual(RoundPhase.Countdown, _mode.CurrentRoundPhase, "Бой начался, хотя игрок вне своей зоны.");
            Assert.IsTrue(_mode.CountdownHeld, "Отсчёт не помечен остановленным — клиенты покажут идущий отсчёт.");
        }

        [Test]
        public void Вернувшийся_запускает_отсчёт_сначала()
        {
            SilenceMirrorNoise();
            Tick(Countdown - 0.6f); // почти истёк
            _a.ServerExitSpawnZone(_teamA.teamIndex);
            Tick(1f);
            _a.ServerEnterSpawnZone(_teamA.teamIndex);

            Tick(Countdown - 1f);
            Assert.AreEqual(RoundPhase.Countdown, _mode.CurrentRoundPhase,
                "Отсчёт продолжился с места остановки, а должен был начаться сначала.");
            Assert.IsFalse(_mode.CountdownHeld, "Все в зонах, а отсчёт всё ещё стоит.");

            Tick(1.5f);
            Assert.AreEqual(RoundPhase.Combat, _mode.CurrentRoundPhase, "Полный отсчёт прошёл — бой не начался.");
        }

        [Test]
        public void Выбывший_вне_зоны_отсчёт_не_держит()
        {
            SilenceMirrorNoise();
            _b.ActiveAvatar.ServerEliminateSilently("тест: опоздал к закупке");
            _b.ServerExitSpawnZone(_teamB.teamIndex);

            Tick(Countdown + 0.5f);

            Assert.AreEqual(RoundPhase.Combat, _mode.CurrentRoundPhase, "Выбывший вне зоны остановил отсчёт — бой не начнётся никогда.");
        }
    }
}

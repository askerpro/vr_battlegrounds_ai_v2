using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Живым игрок бывает только в закупке и бою. В конце боя выжившие выбывают — жизнь обнуляется
    /// мимо урона, поэтому <c>UxrActor.Died</c> не поднимается: ни статистики серии, ни ленты убийств.
    /// Оживают все одинаково — в своей зоне в подготовке следующего раунда.
    /// </summary>
    public class RoundEndDeathTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA, _teamB;
        private StubPlayerRoster _roster;
        private RoundFlowDriver _driver;
        private PlayerController _a, _b;
        private int _deathEvents;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            SpawnSides.Reset();
            _deathEvents = 0;

            _teamA = TeamRegistry.Instance.GetByIndex(1);
            _teamB = TeamRegistry.Instance.GetByIndex(2);

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            _roster = new StubPlayerRoster();
            _a = CreatePlayer("A", _teamA);
            _b = CreatePlayer("B", _teamB);
            _mode.PlayerRoster = _roster;

            _driver = new RoundFlowDriver(dt => { _roster.DeclareAllReady(); _mode.ServerTick(dt); },
                                          () => _mode.CurrentRoundPhase);
        }

        [TearDown]
        public void ResetSides() => SpawnSides.Reset();

        /// <summary>Игрок с телом, стоящий в своей зоне (сессия и физически — в зоне команды).</summary>
        private PlayerController CreatePlayer(string name, TeamData team)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name + "_Session");
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;

            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            go.AddComponent<SpectatorController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            session.ActiveAvatar = player;
            actor.Life = 100f;
            actor.Died += _ => _deathEvents++;

            session.ServerEnterSpawnZone(team.teamIndex);

            GameObject zoneGo = CreateObject("Zone_" + name);
            zoneGo.AddComponent<BoxCollider>();
            TeamSpawnZone zone = zoneGo.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);
            var inside = (HashSet<PlayerController>)typeof(TeamSpawnZone)
                .GetField("_playersInZone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(zone);
            inside.Add(player);

            _roster.Add(team, session);
            return player;
        }

        private void StartMatch()
        {
            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");
        }

        [Test]
        public void В_конце_боя_выжившие_выбывают_без_события_смерти()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "боя");
            Assert.IsTrue(_a.IsAlive && _b.IsAlive, "Контроль: в бою оба живы.");

            _mode.RoundPhases.RequestRoundEnd(_teamA);
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Resolution, "итогов");

            Assert.IsFalse(_a.IsAlive || _b.IsAlive, "Выжившие не выбыли в конце боя — живой вне закупки и боя.");
            Assert.IsTrue(_a.GetComponent<SpectatorController>().IsSpectating(), "Выбывший не в режиме наблюдателя.");
            Assert.AreEqual(0, _deathEvents, "Выбывание в конце боя подняло UxrActor.Died — пойдёт в статистику и ленту убийств.");
        }

        [Test]
        public void Выбывший_на_своей_базе_жив_к_закупке()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "боя");
            int round = _mode.CurrentRoundNumber;
            _mode.RoundPhases.RequestRoundEnd(_teamA);
            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber != round, "следующего раунда");
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Equipment, "закупки");

            Assert.IsTrue(_a.IsAlive && _b.IsAlive, "Стоящие в своей зоне не ожили к закупке.");
        }

        [Test]
        public void Остановка_матча_оживляет_всех()
        {
            SilenceMirrorNoise();
            StartMatch();

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "боя");
            _mode.RoundPhases.RequestRoundEnd(_teamA);
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Resolution, "итогов");
            Assert.IsFalse(_a.IsAlive, "Контроль: выбыл.");

            _mode.ForceStop();

            Assert.IsTrue(_a.IsAlive && _b.IsAlive, "В разминке мёртвых нет — остановка матча обязана оживить всех.");
        }
    }
}

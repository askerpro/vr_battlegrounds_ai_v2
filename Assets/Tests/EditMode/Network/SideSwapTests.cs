using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Смена сторон, как в CS: карта — две половины. Команда (Военные, Повстанцы) постоянна,
    /// после первой половины меняется только то, чьи зоны спавна чьи (<see cref="SpawnSides"/>).
    /// Подсчёт карты — <see cref="MapScoreTests"/>.
    /// </summary>
    public class SideSwapTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private RoundFlowDriver _driver;
        private StubPlayerRoster _roster;
        private bool _ended;

        [SetUp]
        public void PrepareMode()
        {
            SilenceMirrorNoise();
            SpawnSides.Reset();

            _teamA = TeamRegistry.Instance.GetByIndex(1);
            _teamB = TeamRegistry.Instance.GetByIndex(2);

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            _roster = new StubPlayerRoster();
            _roster.Add(_teamA, CreateSession("PlayerA"));
            _roster.Add(_teamB, CreateSession("PlayerB"));
            _mode.PlayerRoster = _roster;

            _ended = false;
            _mode.Finished += _ => _ended = true;

            _driver = new RoundFlowDriver(dt => { _roster.DeclareAllReady(); _mode.ServerTick(dt); },
                                          () => _mode.CurrentRoundPhase);
        }

        [TearDown]
        public void ResetSides() => SpawnSides.Reset();

        private PlayerSession CreateSession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            return session;
        }

        private void StartMatch()
        {
            SetPrivateField(_mode, "_roundsPerHalf", 3);
            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");
        }

        private void PlayRound(TeamData winner)
        {
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat");
            int roundBefore = _mode.CurrentRoundNumber;
            _mode.RoundPhases.RequestRoundEnd(winner);
            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber != roundBefore || _ended,
                                 "конца раунда " + roundBefore);
        }

        private TeamSpawnZone CreateZone(TeamData home)
        {
            GameObject go = CreateObject("Zone_" + home.displayName);
            go.AddComponent<BoxCollider>();
            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", home);
            return zone;
        }

        [Test]
        public void Первая_половина_стороны_на_месте()
        {
            SilenceMirrorNoise();
            StartMatch();

            Assert.IsFalse(_mode.SidesSwapped);
            Assert.AreSame(_teamA, SpawnSides.Resolve(_teamA));
        }

        [Test]
        public void После_первой_половины_команды_меняются_зонами()
        {
            SilenceMirrorNoise();
            TeamSpawnZone zoneOfA = CreateZone(_teamA);
            StartMatch();

            PlayRound(_teamA);
            PlayRound(_teamA);
            PlayRound(_teamB);   // три раунда первой половины сыграны — началась вторая

            Assert.IsTrue(_mode.SidesSwapped, "Вторая половина началась, а стороны не поменялись. Фазы: " + _driver.DumpSequence());
            Assert.AreSame(_teamB, zoneOfA.Team, "Зона первой половины команды A во второй обязана принадлежать B.");
            Assert.AreSame(_teamA, zoneOfA.HomeTeam, "Домашняя команда зоны — настройка сцены, она не меняется.");
            Assert.AreEqual(2, _mode.TeamStates[_teamA.teamIndex].Score, "Раунды засчитаны команде, а не стороне.");
        }

        [Test]
        public void Остановка_матча_возвращает_стороны()
        {
            SilenceMirrorNoise();
            StartMatch();
            PlayRound(_teamA);
            PlayRound(_teamA);
            PlayRound(_teamA);
            Assert.IsTrue(SpawnSides.Swapped, "Контроль: стороны поменялись.");

            _mode.ForceStop();

            Assert.IsFalse(SpawnSides.Swapped, "Разминка после матча обязана начинаться с первой половины.");
        }
    }
}

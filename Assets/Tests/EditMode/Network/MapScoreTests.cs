using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Счёт карты в Elimination, как в CS: раунды суммируются за обе половины, у половины
    /// своего победителя нет. После <c>_roundsPerHalf</c> раундов команды меняются сторонами;
    /// карта кончается, когда у команды больше половины всех раундов, или когда раунды
    /// сыграны все. Равный счёт — ничья.
    ///
    /// <para>
    /// Настройки режима — значения по умолчанию из кода (3 раунда в половине, 6 на карту,
    /// 4 победы до конца карты): тест проверяет правила, а не конкретный префаб.
    /// </para>
    /// </summary>
    public class MapScoreTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private RoundFlowDriver _driver;
        private StubPlayerRoster _roster;
        private bool _ended;
        private TeamData _mapWinner;
        private int _roundsWhenEnded;

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
            _mapWinner = null;
            _roundsWhenEnded = 0;
            _mode.Finished += w =>
            {
                _mapWinner = w;
                _ended = true;
                _roundsWhenEnded = _mode.CurrentRoundNumber;
            };

            _driver = new RoundFlowDriver(dt => { _roster.DeclareAllReady(); _mode.ServerTick(dt); },
                                          () => _mode.CurrentRoundPhase);

            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");
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

        /// <summary>Проигрывает раунды подряд; карта не должна кончиться раньше последнего.</summary>
        private void PlayRounds(params TeamData[] winners)
        {
            for (int i = 0; i < winners.Length; i++)
            {
                Assert.IsFalse(_ended,
                    $"Карта кончилась после раунда {_roundsWhenEnded}, а по счёту должен идти раунд {i + 1}. " +
                    $"Счёт {Score()}. Фазы: {_driver.DumpSequence()}");

                _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat");
                int roundBefore = _mode.CurrentRoundNumber;
                _mode.RoundPhases.RequestRoundEnd(winners[i]);
                _driver.AdvanceUntil(() => _mode.CurrentRoundNumber != roundBefore || _ended,
                                     "конца раунда " + roundBefore);
            }
        }

        private string Score() => $"A {_mode.GetScore(_teamA)} — B {_mode.GetScore(_teamB)}";

        [Test]
        public void Раунды_суммируются_за_обе_половины()
        {
            SilenceMirrorNoise();

            // Половина 1: A 2 — B 1. Половина 2: B 3 — A 0. Итог 2:4, карту берёт B в 6-м раунде.
            PlayRounds(_teamA, _teamA, _teamB, _teamB, _teamB, _teamB);

            Assert.IsTrue(_ended, "Карта не закончилась, хотя у B 4 раунда из 6. Счёт " + Score());
            Assert.AreEqual(6, _roundsWhenEnded, "Карта кончилась не на 6-м раунде.");
            Assert.AreSame(_teamB, _mapWinner, "Карту берёт команда с большим числом раундов за обе половины.");
            Assert.AreEqual(2, _mode.GetScore(_teamA), "Счёт карты — раунды, а не половины.");
            Assert.AreEqual(4, _mode.GetScore(_teamB));
        }

        [Test]
        public void Половина_не_кончается_досрочно()
        {
            SilenceMirrorNoise();

            PlayRounds(_teamA, _teamA);

            Assert.IsFalse(_ended, "При 2:0 в первой половине карта не решена.");
            Assert.AreEqual(3, _mode.CurrentRoundNumber, "После 2:0 третий раунд первой половины обязан играться.");
            Assert.IsFalse(_mode.SidesSwapped, "Стороны поменялись до конца половины.");
        }

        [Test]
        public void Стороны_меняются_после_последнего_раунда_половины()
        {
            SilenceMirrorNoise();

            PlayRounds(_teamA, _teamB, _teamA);

            Assert.AreEqual(4, _mode.CurrentRoundNumber);
            Assert.IsTrue(_mode.SidesSwapped, "Вторая половина началась, а стороны не поменялись.");
            Assert.AreEqual(2, _mode.GetScore(_teamA), "Смена сторон не обнуляет счёт.");
            Assert.AreEqual(1, _mode.GetScore(_teamB));
        }

        [Test]
        public void Большинство_раундов_заканчивает_карту_досрочно()
        {
            SilenceMirrorNoise();

            PlayRounds(_teamA, _teamA, _teamA, _teamA);

            Assert.IsTrue(_ended, "4 из 6 — большинство, карта обязана закончиться.");
            Assert.AreEqual(4, _roundsWhenEnded);
            Assert.AreSame(_teamA, _mapWinner);
        }

        [Test]
        public void Равный_счёт_после_всех_раундов_это_ничья()
        {
            SilenceMirrorNoise();

            PlayRounds(_teamA, _teamA, _teamA, _teamB, _teamB, _teamB);

            Assert.IsTrue(_ended, "Все раунды сыграны, карта обязана закончиться.");
            Assert.AreEqual(6, _roundsWhenEnded);
            Assert.IsNull(_mapWinner, "3:3 — ничья, а не победа по порядку команд.");
        }

        [Test]
        public void Ничейный_раунд_не_даёт_очков()
        {
            SilenceMirrorNoise();

            PlayRounds(_teamA, null);

            Assert.AreEqual(1, _mode.GetScore(_teamA));
            Assert.AreEqual(0, _mode.GetScore(_teamB));
            Assert.AreEqual(3, _mode.CurrentRoundNumber, "Ничейный раунд всё равно расходует раунд половины.");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Фазы раунда, прогнанные боевым путём: <c>EliminationMode.ServerTick</c> →
    /// <c>RoundPhases</c>. Находка MATCH-02 и корень 4, задача T-09.
    ///
    /// Почему через <c>Tick</c>, а не через прямой вызов «раунд закончился». Раньше
    /// цепочка «конец раунда → старт следующего раунда» была синхронной,
    /// поэтому фаза <c>Resolution</c> жила ровно до следующей строки, а ветки
    /// <c>Resolution</c> и <c>Scoreboard</c> в <c>Tick</c> были недостижимы. Тест, который
    /// дёргает конец раунда напрямую, этого не видит: он вообще не заходит в <c>Tick</c>.
    ///
    /// Почему ярус A. <c>ServerBeginRound</c>, <c>Initialize</c> и запись <c>SyncVar</c>
    /// помечены <c>[Server]</c> — вне активного сервера Mirror их молча заглушает,
    /// и тест зеленел бы, ничего не проверив.
    /// </summary>
    public class RoundPhaseFlowTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private RoundFlowDriver _driver;
        private StubPlayerRoster _roster;

        [SetUp]
        public void PrepareMatch()
        {
            SilenceMirrorNoise();

            TeamRegistry registry = TeamRegistry.Instance;
            Assert.IsNotNull(registry, "TeamRegistry.Instance не загрузился из Resources");

            _teamA = registry.GetByIndex(1);
            _teamB = registry.GetByIndex(2);
            Assert.IsNotNull(_teamA, "В реестре нет команды с teamIndex=1");
            Assert.IsNotNull(_teamB, "В реестре нет команды с teamIndex=2");

            // PlayersManager здесь намеренно не поднимается: после NET-12 и NET-18 весь
            // серверный путь режима спрашивает об игроках только IPlayerRoster.
            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            // Живых аватаров в EditMode не поднять, а без них фаза Equipment вечна.
            // Подменяем только источник данных об игроках — сама машина остаётся боевой.
            _roster = new StubPlayerRoster();
            _roster.Add(_teamA, CreateReadySession("PlayerA"));
            _roster.Add(_teamB, CreateReadySession("PlayerB"));
            _mode.PlayerRoster = _roster;

            // Готовность объявляется перед каждым тиком: с T-29 она живёт один раунд,
            // а эти тесты прогоняют их несколько подряд. Так заглушка играет роль
            // игроков, которые каждый раунд заново берут жетон.
            _driver = new RoundFlowDriver(
                dt =>
                {
                    _roster.DeclareAllReady();
                    _mode.ServerTick(dt);
                },
                () => _mode.CurrentRoundPhase);
        }

        /// <summary>
        /// Сессия игрока, который стоит в своей зоне спавна. Готовность здесь не
        /// объявляется: с T-29 она сбрасывается каждый раунд, поэтому её объявляет
        /// заново перед каждым тиком <c>StubPlayerRoster.DeclareAllReady</c>.
        /// </summary>
        private PlayerSession CreateReadySession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);

            // Игрок стоит в зоне спавна СВОЕЙ команды: с RDY-04 признак выводится из того,
            // чья это зона, поэтому проставляем его тем же входом, что и TeamSpawnZone.
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        /// <summary>Запускает матч, минуя ожидание подключения живых игроков.</summary>
        private void StartMatch(int roundsPerHalf = 3)
        {
            SetPrivateField(_mode, "_roundsPerHalf", roundsPerHalf);
            _mode.Initialize(new[] { _teamA, _teamB });

            // Штатный вход — ServerTick() → InitializeActiveGame(), но он ждёт подключённых
            // игроков, которых в EditMode нет. Зовём напрямую: это тот же серверный путь.
            InvokePrivateMethod(_mode, "InitializeActiveGame");

            Assert.IsNotNull(_mode.RoundPhases,
                "InitializeActiveGame не создал RoundPhases — значит [Server]-заглушка всё ещё срабатывает.");
        }

        /// <summary>
        /// Доводит текущий раунд до боя, объявляет победителя и прокручивает раунд
        /// до конца — до момента, когда сменится номер раунда.
        /// </summary>
        private void PlayRound(TeamData winner)
        {
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat");

            int roundBefore = _mode.CurrentRoundNumber;
            _mode.RoundPhases.RequestRoundEnd(winner);

            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber != roundBefore,
                "начала следующего раунда после раунда " + roundBefore);
        }

        // ── Сами фазы (MATCH-02) ────────────────────────────────────────────

        [Test]
        public void Раунд_проходит_все_шесть_фаз_по_порядку()
        {
            SilenceMirrorNoise();
            StartMatch();

            PlayRound(_teamA);

            List<RoundPhase> expected = new List<RoundPhase>
            {
                RoundPhase.Setup,
                RoundPhase.Equipment,
                RoundPhase.Countdown,
                RoundPhase.Combat,
                RoundPhase.Resolution,
                RoundPhase.Scoreboard,
                RoundPhase.Setup      // начался следующий раунд
            };

            CollectionAssert.AreEqual(expected, _driver.PhaseSequence(),
                "Раунд обязан прожить все шесть фаз по порядку.\n" +
                "Отсутствие Resolution и Scoreboard означает, что следующий раунд стартует\n" +
                "прямо внутри обработчика «раунд закончился» (MATCH-02).\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Resolution_длится_три_секунды()
        {
            SilenceMirrorNoise();
            StartMatch();

            PlayRound(_teamA);

            int expected = RoundFlowDriver.StepsFor(RoundPhases.ResolutionDuration);

            Assert.AreEqual(expected, _driver.FirstRunLength(RoundPhase.Resolution),
                "Пауза после победы обязана длиться " + RoundPhases.ResolutionDuration + " с, " +
                "то есть " + expected + " шагов по " + RoundFlowDriver.Step + " с.\n" +
                "Ноль означает, что фазы не было вовсе.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Scoreboard_длится_пять_секунд()
        {
            SilenceMirrorNoise();
            StartMatch();

            PlayRound(_teamA);

            int expected = RoundFlowDriver.StepsFor(RoundPhases.ScoreboardDuration);

            Assert.AreEqual(expected, _driver.FirstRunLength(RoundPhase.Scoreboard),
                "Экран итогов обязан длиться " + RoundPhases.ScoreboardDuration + " с, " +
                "то есть " + expected + " шагов по " + RoundFlowDriver.Step + " с.\n" +
                "Ноль означает, что фазы не было вовсе.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        // ── Владелец перехода «раунд → раунд» (MATCH-06) ─────────────────────

        [Test]
        public void Следующий_раунд_начинает_только_режим()
        {
            SilenceMirrorNoise();
            StartMatch();

            PlayRound(null);

            Assert.AreEqual(2, _mode.CurrentRoundNumber,
                "После полного цикла раунда номер обязан вырасти до 2.\n" +
                "Единица означает, что новый раунд запустил кто-то мимо EliminationMode.StartNextRound — " +
                "тогда счётчик раундов не растёт и RpcOnRoundStarted не уходит (MATCH-06).\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());
        }

        [Test]
        public void Карта_кончается_только_после_экрана_итогов()
        {
            SilenceMirrorNoise();

            // Одна половина по раунду: всего 2, до победы 2. Второй выигрыш A решает карту.
            StartMatch(roundsPerHalf: 1);
            bool ended = false;
            _mode.Finished += _ => ended = true;

            PlayRound(_teamA);

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "фазы Combat раунда 2");
            _mode.RoundPhases.RequestRoundEnd(_teamA);

            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Scoreboard,
                "экрана итогов победного раунда");

            Assert.AreEqual(2, _mode.TeamStates[_teamA.teamIndex].Score,
                "Очко за раунд начисляется на входе в Resolution — экран итогов показывает новый счёт.");
            Assert.IsFalse(ended,
                "Карта кончилась до того, как показали экран итогов.\n" +
                "Исход карты решается на выходе из Scoreboard, а не на конце раунда.\n" +
                "Фактически наблюдалось: " + _driver.DumpSequence());

            _driver.AdvanceUntil(() => ended, "конца карты");

            Assert.AreEqual(0, _mode.TeamStates[_teamB.teamIndex].Score,
                "Проигравшая команда очков не получает.");
        }
    }
}

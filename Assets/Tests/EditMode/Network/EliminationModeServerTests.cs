using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Ярус A: серверная логика <see cref="EliminationMode"/> под поднятым Mirror.
    ///
    /// Первый тест здесь — проверка самого харнесса. Без активного сервера
    /// <c>Initialize</c> (помечен <c>[Server]</c>) молча заглушается и оставляет
    /// <c>TeamStates.Count == 0</c>. Если он красный — остальные тесты на этом
    /// харнессе ничего не значат.
    /// </summary>
    public class EliminationModeServerTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA;
        private TeamData _teamB;
        private RoundFlowDriver _driver;

        [SetUp]
        public void PrepareMode()
        {
            SilenceMirrorNoise();

            // Команды берём из реального реестра: код ищет победителя через
            // TeamRegistry.GetByIndex(), синтетические TeamData он не найдёт.
            // Индексы команд — 1 и 2; 0 зарезервирован под «нет команды».
            TeamRegistry registry = TeamRegistry.Instance;
            Assert.IsNotNull(registry, "TeamRegistry.Instance не загрузился из Resources");

            _teamA = registry.GetByIndex(1);
            _teamB = registry.GetByIndex(2);
            Assert.IsNotNull(_teamA, "В реестре нет команды с teamIndex=1");
            Assert.IsNotNull(_teamB, "В реестре нет команды с teamIndex=2");

            // TeamRuntimeData.Sessions ходит в PlayersManager.Instance напрямую,
            // поэтому менеджер нужен даже там, где игроков нет.
            CreateManager<PlayersManager>("PlayersManager");

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            // Живых аватаров в EditMode не поднять, а без них раунд вечно стоит
            // в фазе Equipment и до конца сета дело не доходит.
            StubPlayerRoster roster = new StubPlayerRoster();
            roster.Add(_teamA, CreateReadySession("PlayerA"));
            roster.Add(_teamB, CreateReadySession("PlayerB"));
            _mode.PlayerRoster = roster;

            _driver = new RoundFlowDriver(dt => _mode.ServerTick(dt), () => _mode.CurrentRoundState);
        }

        /// <summary>Сессия игрока, который стоит в зоне спавна и взял жетон.</summary>
        private PlayerSession CreateReadySession(string name)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);

            session.IsInSpawnZone = true;
            session.HasGrabbedDogTag = true;
            return session;
        }

        /// <summary>
        /// Доводит текущий раунд до боя, объявляет победителя и прокручивает раунд
        /// целиком — до момента, когда сменится номер раунда.
        /// </summary>
        private void PlayRound(TeamData winner)
        {
            _driver.AdvanceUntil(() => _mode.CurrentRoundState == RoundState.Combat, "фазы Combat");

            int roundBefore = _mode.CurrentRoundNumber;
            _mode.RoundManager.RequestRoundEnd(winner);

            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber != roundBefore,
                "конца цикла раунда " + roundBefore);
        }

        /// <summary>
        /// Проверка харнесса. Вне активного сервера Mirror заглушает [Server]-методы,
        /// и этот же тест даёт Count == 0 — именно так он был красным до T-26.
        /// </summary>
        [Test, Order(0)]
        public void Initialize_заполняет_TeamStates()
        {
            SilenceMirrorNoise();

            Assert.AreEqual(0, _mode.TeamStates.Count,
                "До Initialize состояний команд быть не должно — тест проверяет не то, что думает.");

            _mode.Initialize(new[] { _teamA, _teamB });

            Assert.AreEqual(2, _mode.TeamStates.Count,
                "Initialize помечен [Server]. Count == 0 означает, что сервер Mirror не поднят " +
                "и харнесс не работает: все остальные сетевые тесты в этой сборке недостоверны.");
        }

        /// <summary>
        /// Находка T-02: двойная подписка на SetEnded удваивала счёт сетов. После T-09
        /// события SetEnded нет вовсе — наблюдатель передаётся конструктором SetManager,
        /// поэтому подписаться дважды не на что. Тест остаётся сторожем этой развязки.
        /// Недостижим без сервера — <c>InitializeActiveGame</c> помечен <c>[Server]</c>.
        /// </summary>
        [Test, Order(1)]
        public void Победа_в_сете_даёт_одно_очко()
        {
            SilenceMirrorNoise();

            _mode.Initialize(new[] { _teamA, _teamB });

            // Штатный вход — Update() → InitializeActiveGame(), но Update ждёт подключённых
            // игроков, а StartGameplayWhenReady крутит корутину, которой в EditMode нет.
            // Зовём напрямую: это тот же серверный путь, включая связывание с OnSetEnded.
            InvokePrivateMethod(_mode, "InitializeActiveGame");
            Assert.IsNotNull(_mode.RoundManager,
                "InitializeActiveGame не создал RoundManager — значит [Server]-заглушка всё ещё срабатывает.");

            // roundsPerSet = 3 → порог 2 победы, сет заканчивается досрочно после двух раундов.
            // Раунды проигрываются прокруткой ServerTick: очко за сет начисляется только
            // после того, как экран итогов прожил свои пять секунд.
            PlayRound(_teamA);
            PlayRound(_teamA);

            Assert.AreEqual(1, _mode.TeamStates[_teamA.teamIndex].Score,
                "За один выигранный сет команда должна получить ровно одно очко. " +
                "Двойка здесь = вернулось двойное оповещение об исходе сета (T-02). " +
                "Пройденные фазы: " + _driver.DumpSequence());

            Assert.AreEqual(0, _mode.TeamStates[_teamB.teamIndex].Score,
                "Проигравшая команда не должна получить очков за сет.");
        }
    }
}

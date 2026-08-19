using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests
{
    /// <summary>
    /// Подсчёт победителя сета в <see cref="SetManager"/> (находка MATCH-04).
    ///
    /// SetManager — обычный C#-класс, поэтому гоняется без запуска игры.
    /// EliminationMode нужен ему как параметр: его методы помечены [Server]
    /// и вне сервера Mirror их заглушает.
    ///
    /// Раунды проигрываются прокруткой <see cref="SetManager.Tick"/>, а не прямым
    /// «раунд закончился»: после T-09 очко за раунд начисляется при входе в фазу
    /// Resolution, а исход сета решается при выходе из Scoreboard. Тест, дёргающий
    /// конец раунда напрямую, не заходит в Tick и обеих точек не касается.
    ///
    /// Побочный эффект: SetManager дёргает [ClientRpc] RpcOnRoundStarted,
    /// на который Mirror пишет в консоль Error «called without an active server».
    /// Это шум харнесса, а не ошибка логики, поэтому логи глушатся.
    /// </summary>
    public class SetManagerScoringTests
    {
        private GameObject _modeObject;
        private EliminationMode _mode;
        private RoundManager _roundManager;
        private SetManager _setManager;
        private RoundFlowDriver _driver;

        /// <summary>Объекты сессий-заглушек: удаляются в TearDown, в сцене ничего не остаётся.</summary>
        private readonly List<GameObject> _sessionObjects = new List<GameObject>();

        /// <summary>Синтетические TeamData: это не ассеты проекта, их обязательно уничтожать.</summary>
        private readonly List<TeamData> _createdTeams = new List<TeamData>();

        private TeamData _teamA;
        private TeamData _teamB;

        /// <summary>Победители сетов в порядке срабатывания SetEnded.</summary>
        private readonly List<TeamData> _setEndedWinners = new List<TeamData>();

        /// <summary>Счёт раундов на момент завершения сета — для внятной диагностики.</summary>
        private readonly List<string> _scoresAtSetEnd = new List<string>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Команды синтетические, с индексами, которых в TeamRegistry заведомо нет.
            // Это и есть проверка T-08: победитель сета обязан определяться по составу,
            // переданному в StartSet, а не по глобальному реестру. Пока SetManager искал
            // команду через TeamRegistry.GetByIndex(), на этих индексах он получал null —
            // то есть «ничья» вместо победы, — и тест приходилось кормить командами
            // из реестра, чтобы он вообще что-то проверял.
            _teamA = CreateTeam("Синтетическая A", 901);
            _teamB = CreateTeam("Синтетическая B", 902);

            _modeObject = new GameObject("TestEliminationMode");
            _mode = _modeObject.AddComponent<EliminationMode>();

            // Живого аватара в EditMode нет, а без него реестр игроков пуст и раунд
            // навсегда стоит в фазе Equipment. Подменяем только источник данных.
            StubPlayerRoster roster = new StubPlayerRoster();
            roster.Add(_teamA, CreateReadySession("PlayerA"));
            roster.Add(_teamB, CreateReadySession("PlayerB"));

            _setEndedWinners.Clear();
            _scoresAtSetEnd.Clear();

            // Наблюдатель исхода сета передаётся конструктором: события SetEnded больше нет,
            // поэтому подписаться дважды (MATCH-01) не на что даже в тесте.
            _roundManager = new RoundManager(roster);
            _setManager = new SetManager(_roundManager, w =>
            {
                _setEndedWinners.Add(w);
                _scoresAtSetEnd.Add(DumpScores());
            });

            _driver = new RoundFlowDriver(dt => _setManager.Tick(dt), () => _roundManager.State);
        }

        /// <summary>
        /// Команда, созданная на лету. В <c>TeamRegistry</c> её нет и быть не должно:
        /// именно этим тест отличает счёт по переданному составу от счёта по реестру.
        /// </summary>
        private TeamData CreateTeam(string displayName, int teamIndex)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.name = displayName;
            team.displayName = displayName;
            team.teamIndex = teamIndex;

            _createdTeams.Add(team);
            return team;
        }

        /// <summary>Сессия игрока, который стоит в зоне спавна и взял жетон.</summary>
        private PlayerSession CreateReadySession(string name)
        {
            GameObject go = new GameObject(name);
            _sessionObjects.Add(go);

            // Сначала NetworkIdentity: без неё NetworkBehaviour.OnValidate пишет Error.
            go.AddComponent<Mirror.NetworkIdentity>();
            PlayerSession session = go.AddComponent<PlayerSession>();

            session.IsInSpawnZone = true;
            session.HasGrabbedDogTag = true;
            return session;
        }

        [TearDown]
        public void TearDown()
        {
            if (_modeObject != null) Object.DestroyImmediate(_modeObject);

            foreach (GameObject go in _sessionObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _sessionObjects.Clear();

            // Команды созданы тестом, а не загружены из Resources — утекут, если их не убрать.
            foreach (TeamData team in _createdTeams)
            {
                if (team != null) Object.DestroyImmediate(team);
            }
            _createdTeams.Clear();
        }

        private string DumpScores()
        {
            return string.Join(", ", _setManager.TeamRoundScores
                .Select(kv => "team" + kv.Key + "=" + kv.Value).ToArray());
        }

        /// <summary>Начинает сет и проигрывает раунды с заданными победителями.</summary>
        private void PlaySet(int roundsPerSet, params TeamData[] roundWinners)
        {
            // Ставим здесь, а не только в SetUp: тестовый фреймворк сбрасывает флаг
            // после SetUp, и Error от Mirror про ClientRpc вне сервера валит тест
            // ещё до проверки утверждений.
            LogAssert.ignoreFailingMessages = true;

            _setManager.StartSet(new[] { _teamA, _teamB }, _mode, roundsPerSet, 3f, 90f);

            foreach (TeamData winner in roundWinners)
            {
                _driver.AdvanceUntil(() => _roundManager.State == RoundState.Combat, "фазы Combat");
                _roundManager.RequestRoundEnd(winner);

                // Раунд считается прожитым, когда машина вернулась в Setup следующего
                // раунда либо сет закончился и новый раунд уже не начнётся.
                _driver.AdvanceUntil(
                    () => _roundManager.State == RoundState.Setup || _setEndedWinners.Count > 0,
                    "конца цикла раунда");
            }
        }

        /// <summary>Единая проверка исхода сета с диагностикой в сообщении.</summary>
        private void AssertSetEnded(TeamData expectedWinner, string because)
        {
            Assert.AreEqual(1, _setEndedWinners.Count,
                because + "\nИсход сета должен сообщаться ровно один раз. Счёт: " + DumpScores() +
                "\nПройденные фазы: " + _driver.DumpSequence());

            TeamData actual = _setEndedWinners[0];
            string actualName = actual != null ? actual.displayName : "ничья";
            string expectedName = expectedWinner != null ? expectedWinner.displayName : "ничья";

            Assert.AreSame(expectedWinner, actual,
                because + "\nОжидали: " + expectedName + ", получили: " + actualName +
                ". Счёт на момент завершения: " + _scoresAtSetEnd[0]);
        }

        // ── Счёт раундов ────────────────────────────────────────────────────

        [Test]
        public void Победа_в_раунде_даёт_одно_очко()
        {
            PlaySet(3, _teamA);

            Assert.AreEqual(1, _setManager.TeamRoundScores[_teamA.teamIndex],
                "Победитель раунда должен получить ровно одно очко. Счёт: " + DumpScores());
            Assert.AreEqual(0, _setManager.TeamRoundScores[_teamB.teamIndex]);
        }

        // ── Определение победителя сета (MATCH-04) ──────────────────────────

        [Test]
        public void Счёт_2_1_отдаёт_победу_а_не_ничью()
        {
            PlaySet(3, _teamA, _teamB, _teamA);
            AssertSetEnded(_teamA, "При счёте 2:1 побеждает команда A.");
        }

        [Test]
        public void Досрочная_победа_при_достижении_порога()
        {
            // 3 раунда в сете → порог 2 победы. После двух подряд сет заканчивается досрочно.
            PlaySet(3, _teamA, _teamA);
            AssertSetEnded(_teamA, "Две победы подряд при пороге 2 завершают сет досрочно.");
        }

        [Test]
        public void Счёт_1_1_это_ничья()
        {
            PlaySet(2, _teamA, _teamB);
            AssertSetEnded(null, "При счёте 1:1 и исчерпании раундов — ничья.");
        }

        [Test]
        public void Счёт_1_0_при_исчерпании_раундов_отдаёт_победу()
        {
            // 2 раунда в сете, порог 2. Порога никто не достиг, но раунды кончились.
            PlaySet(2, _teamA, null);
            AssertSetEnded(_teamA, "При счёте 1:0 и исчерпании раундов побеждает A.");
        }

        [Test]
        public void Ничейные_раунды_дают_ничью_в_сете()
        {
            PlaySet(2, null, null);
            AssertSetEnded(null, "Если никто не выиграл ни одного раунда — ничья.");
        }
    }
}

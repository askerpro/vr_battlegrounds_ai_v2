using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Tests
{
    /// <summary>
    /// Подсчёт победителя сета в <see cref="SetManager"/> (находка MATCH-04).
    ///
    /// SetManager — обычный C#-класс, поэтому гоняется без запуска игры.
    /// EliminationMode нужен ему как параметр: его методы помечены [Server]
    /// и вне сервера Mirror их заглушает.
    ///
    /// Побочный эффект: SetManager дёргает [ClientRpc] RpcOnRoundStarted,
    /// на который Mirror пишет в консоль Error «called without an active server».
    /// Это шум харнесса, а не ошибка логики, поэтому логи глушатся.
    /// Сама необходимость это глушить — признак того, что подсчёт очков сцеплен
    /// с сетевым слоем; развязывает это T-09.
    /// </summary>
    public class SetManagerScoringTests
    {
        private GameObject _modeObject;
        private EliminationMode _mode;
        private RoundManager _roundManager;
        private SetManager _setManager;

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

            // Команды берём из реального реестра, а не создаём на лету:
            // SetManager определяет победителя через TeamRegistry.GetByIndex(),
            // а не через переданный массив teams. С синтетическими TeamData
            // этот поиск возвращает null, и тест проверял бы не то.
            // Сама эта скрытая зависимость от глобального реестра — замечание к коду,
            // см. Docs/tasks/T-08.
            TeamRegistry registry = TeamRegistry.Instance;
            Assert.IsNotNull(registry, "TeamRegistry.Instance не загрузился из Resources");

            _teamA = registry.GetByIndex(1);
            _teamB = registry.GetByIndex(2);

            Assert.IsNotNull(_teamA, "В реестре нет команды с teamIndex=1");
            Assert.IsNotNull(_teamB, "В реестре нет команды с teamIndex=2");

            _modeObject = new GameObject("TestEliminationMode");
            _mode = _modeObject.AddComponent<EliminationMode>();

            _roundManager = new RoundManager();
            _setManager = new SetManager(_roundManager);

            _setEndedWinners.Clear();
            _scoresAtSetEnd.Clear();

            _setManager.SetEnded += w =>
            {
                _setEndedWinners.Add(w);
                _scoresAtSetEnd.Add(DumpScores());
            };
        }

        [TearDown]
        public void TearDown()
        {
            // _teamA / _teamB — ассеты из реестра, уничтожать их нельзя.
            if (_modeObject != null) Object.DestroyImmediate(_modeObject);
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
                _roundManager.EndRound(winner);
            }
        }

        /// <summary>Единая проверка исхода сета с диагностикой в сообщении.</summary>
        private void AssertSetEnded(TeamData expectedWinner, string because)
        {
            Assert.AreEqual(1, _setEndedWinners.Count,
                because + "\nSetEnded должен сработать ровно один раз. Счёт: " + DumpScores());

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

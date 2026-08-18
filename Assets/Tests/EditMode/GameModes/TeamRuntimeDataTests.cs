using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Команда матча без <c>PlayersManager</c> — находка NET-12.
    ///
    /// Что доказывают тесты. <c>TeamRuntimeData.Sessions</c> разыменовывала
    /// <c>PlayersManager.Instance</c> без проверки, и <c>EliminationMode.PrepareNextRound</c>
    /// падал с NullReferenceException везде, где менеджера нет: в сцене, открытой без сети,
    /// и в любом тесте логики матча. Из-за этого режим нельзя было проверить в изоляции —
    /// тестам приходилось поднимать менеджер только ради того, чтобы обёртка не падала.
    ///
    /// Лечится не проверкой на null, а тем же <see cref="IPlayerRoster"/>, который T-09
    /// завела для машины раунда: у обёртки появляется подставляемый источник игроков,
    /// а боевая реализация сама отвечает пустым списком, когда менеджера нет.
    ///
    /// <see cref="MirrorTestHarness"/> здесь нужен ровно за одним: он обнуляет
    /// <c>PlayersManager.Instance</c> между тестами, и «менеджера нет» — это факт,
    /// а не надежда на порядок запуска.
    /// </summary>
    public class TeamRuntimeDataTests : MirrorTestHarness
    {
        private TeamData _team;

        [SetUp]
        public void PrepareTeam()
        {
            SilenceMirrorNoise();

            _team = ScriptableObject.CreateInstance<TeamData>();
            _team.teamIndex = 1;
            _team.displayName = "Alpha";
        }

        // ── Сама находка ────────────────────────────────────────────────────

        [Test]
        public void Команда_без_менеджера_игроков_не_падает()
        {
            SilenceMirrorNoise();

            Assert.IsNull(PlayersManager.Instance,
                "Контроль: менеджера быть не должно, иначе тест проверяет не тот путь.");

            TeamRuntimeData team = new TeamRuntimeData(_team, null);

            Assert.DoesNotThrow(() => { int unused = team.PlayersCount; },
                "Обход игроков команды падает без PlayersManager. Это NET-12: тот же путь " +
                "проходит EliminationMode.PrepareNextRound в начале каждого раунда.");

            Assert.AreEqual(0, team.PlayersCount, "Без менеджера в команде не может быть игроков.");
            Assert.IsFalse(team.HasPlayers(), "Команда без менеджера объявила себя населённой.");
            Assert.IsFalse(team.HasAlivePlayers(), "Команда без менеджера объявила, что в ней есть живые.");
        }

        // ── Подставленный реестр ────────────────────────────────────────────

        [Test]
        public void Команда_берёт_игроков_из_подставленного_реестра()
        {
            SilenceMirrorNoise();

            StubPlayerRoster roster = new StubPlayerRoster();
            roster.Add(_team, CreateSession("PlayerA"));
            roster.Add(_team, CreateSession("PlayerB"));

            TeamRuntimeData team = new TeamRuntimeData(_team, null, roster);

            Assert.AreEqual(2, team.PlayersCount, "Реестр отдал двоих, а команда их не увидела.");
            Assert.IsTrue(team.HasPlayers(), "Команда с двумя игроками считает себя пустой.");
            Assert.AreEqual(2, team.AliveSessions.Count(), "Живые игроки тоже идут через реестр.");
        }

        [Test]
        public void Чужая_команда_остаётся_пустой()
        {
            SilenceMirrorNoise();

            TeamData other = ScriptableObject.CreateInstance<TeamData>();
            other.teamIndex = 2;
            other.displayName = "Bravo";

            StubPlayerRoster roster = new StubPlayerRoster();
            roster.Add(_team, CreateSession("PlayerA"));

            TeamRuntimeData bravo = new TeamRuntimeData(other, null, roster);

            Assert.AreEqual(0, bravo.PlayersCount,
                "Команда набрала игроков чужой команды — реестр опрашивается без учёта TeamData.");
        }

        private PlayerSession CreateSession(string name)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            session.TeamIndex = _team.teamIndex;
            return session;
        }
    }
}

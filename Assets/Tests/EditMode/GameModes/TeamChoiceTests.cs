using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Выбор команды матча на карте — этап Б.
    ///
    /// <para>
    /// Что доказывает. Игрок приходит на карту из лобби с командой «Лобби», то есть без
    /// команды режима. Режим с ручной политикой (<see cref="TeamAssignmentKind.PlayerChoice"/>)
    /// никого не назначает; игрок выбирает сам до старта матча, после старта — только админ;
    /// матч ждёт, пока команда режима будет у всех. Лобби-режим по-прежнему раздаёт свою
    /// единственную команду сам.
    /// </para>
    /// </summary>
    public class TeamChoiceTests : MirrorTestHarness
    {
        private const int LobbyTeamIndex = 3;

        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void DropAssets()
        {
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        private TeamData CreateTeam(string name, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.name = name;
            team.displayName = name;
            team.teamIndex = index;
            _assets.Add(team);
            return team;
        }

        private GameModeData CreateModeData(string id, TeamAssignmentKind kind, int minPlayers, params TeamData[] teams)
        {
            GameModeData data = ScriptableObject.CreateInstance<GameModeData>();
            data.modeId = id;
            data.teams = teams;
            data.minPlayersToStart = minPlayers;
            data.teamAssignment = kind;
            _assets.Add(data);
            return data;
        }

        private PlayerSession CreateSession(string name, int teamIndex, bool isAdmin = false)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = teamIndex;
            session.IsAdmin = isAdmin;
            return session;
        }

        private sealed class ListRoster : IPlayerRoster
        {
            public readonly List<PlayerSession> Players = new List<PlayerSession>();
            public IEnumerable<PlayerSession> GetPlayers(TeamData team) => Players.Where(p => team != null && p.TeamIndex == team.teamIndex);
            public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team) => GetPlayers(team);
            public IEnumerable<PlayerSession> GetAllPlayers() => Players;
        }

        private EliminationMode CreateElimination(GameModeData data, IPlayerRoster roster)
        {
            EliminationMode mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(mode);
            mode.PlayerRoster = roster;
            mode.Initialize(data);
            return mode;
        }

        private GameplayManager CreateGameplayManager(GameMode active)
        {
            GameplayManager manager = CreateNetworkComponent<GameplayManager>("GameplayManager");
            InvokeLifecycleMethod(manager, "Awake");
            InvokePrivateMethod(manager, "RegisterActiveGameMode", active);
            return manager;
        }

        // ── Политика и ожидание ──────────────────────────────────────────────

        [Test]
        public void Ручная_политика_никого_не_назначает()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            roster.Players.Add(CreateSession("p1", LobbyTeamIndex));
            roster.Players.Add(CreateSession("p2", LobbyTeamIndex));

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), roster);
            mode.ServerAssignTeams();

            Assert.IsTrue(roster.Players.All(p => p.TeamIndex == LobbyTeamIndex),
                "Режим с выбором команды игроком сам раздал команды: " +
                string.Join(", ", roster.Players.Select(p => p.PlayerName + "=" + p.TeamIndex)));
        }

        [Test]
        public void Автополитика_из_данных_режима_раздаёт_команды()
        {
            SilenceMirrorNoise();

            TeamData lobby = CreateTeam("Лобби", LobbyTeamIndex);
            var roster = new ListRoster();
            roster.Players.Add(CreateSession("p1", 1));

            LobbyMode mode = CreateNetworkComponent<LobbyMode>("LobbyMode");
            SpawnOnServer(mode);
            mode.PlayerRoster = roster;
            mode.Initialize(CreateModeData("lobby", TeamAssignmentKind.AutoBalance, 1, lobby));
            mode.ServerAssignTeams();

            Assert.AreEqual(LobbyTeamIndex, roster.Players[0].TeamIndex, "Лобби не выдало свою команду.");
        }

        [Test]
        public void Матч_ждёт_пока_команда_режима_будет_у_всех()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            roster.Players.Add(CreateSession("pa", 1));
            roster.Players.Add(CreateSession("pb", 2));
            PlayerSession late = CreateSession("pc", LobbyTeamIndex);
            roster.Players.Add(late);

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), roster);

            mode.ServerTick(0.1f);
            Assert.AreEqual(EliminationMatchState.WaitingForPlayers, mode.CurrentMatchState,
                "Один игрок ещё без команды режима, а матч начался — он остался бы вне игры.");

            late.TeamIndex = 1;
            mode.ServerTick(0.1f);
            Assert.AreEqual(EliminationMatchState.Active, mode.CurrentMatchState,
                "Команда есть у всех — матч обязан начаться.");
        }

        [Test]
        public void Минимум_игроков_берётся_из_данных_своего_режима()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            roster.Players.Add(CreateSession("pa", 1));
            roster.Players.Add(CreateSession("pb", 2));

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 3, a, b), roster);
            mode.ServerTick(0.1f);

            Assert.AreEqual(EliminationMatchState.WaitingForPlayers, mode.CurrentMatchState,
                "Режим требует трёх игроков, а начал с двумя: минимум взят не из данных режима.");
        }

        // ── Выбор игроком и выдача админом ───────────────────────────────────

        [Test]
        public void Игрок_выбирает_команду_до_старта_и_не_может_после()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            PlayerSession player = CreateSession("player", LobbyTeamIndex);
            roster.Players.Add(player);

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), roster);
            GameplayManager manager = CreateGameplayManager(mode);

            Assert.IsFalse(mode.TeamChoiceLocked, "Контроль: матч ещё не начался.");
            manager.ProcessTeamChangeRequest(player, a.teamIndex, 0);
            Assert.AreEqual(a.teamIndex, player.TeamIndex, "До старта матча игрок не смог выбрать команду сам.");

            SetPrivateField(mode, "_matchState", EliminationMatchState.Active);
            Assert.IsTrue(mode.TeamChoiceLocked, "Матч начался, а выбор команды открыт.");

            manager.ProcessTeamChangeRequest(player, b.teamIndex, 0);
            Assert.AreEqual(a.teamIndex, player.TeamIndex, "После старта матча игрок сменил команду сам.");
        }

        [Test]
        public void Админ_выдаёт_команду_и_после_старта()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            PlayerSession player = CreateSession("player", LobbyTeamIndex);
            PlayerSession admin = CreateSession("admin", a.teamIndex, isAdmin: true);
            PlayerSession stranger = CreateSession("stranger", a.teamIndex);
            roster.Players.Add(player);

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), roster);
            GameplayManager manager = CreateGameplayManager(mode);
            SetPrivateField(mode, "_matchState", EliminationMatchState.Active);

            Assert.IsFalse(manager.ServerAdminAssignTeam(stranger, player, b.teamIndex), "Не-админ выдал команду.");
            Assert.AreEqual(LobbyTeamIndex, player.TeamIndex);

            Assert.IsTrue(manager.ServerAdminAssignTeam(admin, player, b.teamIndex), "Админ не смог выдать команду.");
            Assert.AreEqual(b.teamIndex, player.TeamIndex, "Команда, выданная админом после старта, не применилась.");
        }

        [Test]
        public void Админ_раскладывает_автобалансом_разово()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            for (int i = 0; i < 4; i++) roster.Players.Add(CreateSession("p" + i, LobbyTeamIndex));
            PlayerSession admin = CreateSession("admin", 0, isAdmin: true);

            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), roster);
            GameplayManager manager = CreateGameplayManager(mode);

            Assert.AreEqual(4, manager.ServerAdminAutoBalance(admin));
            Assert.AreEqual(2, roster.Players.Count(p => p.TeamIndex == 1));
            Assert.AreEqual(2, roster.Players.Count(p => p.TeamIndex == 2));
            Assert.IsInstanceOf<PlayerChoiceTeamPolicy>(mode.TeamAssignmentPolicy,
                "Разовый автобаланс админа не должен менять политику режима.");
        }

        // ── Планшет и данные ─────────────────────────────────────────────────

        [Test]
        public void После_старта_планшет_предлагает_только_свою_команду()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            EliminationMode mode = CreateElimination(CreateModeData("m", TeamAssignmentKind.PlayerChoice, 2, a, b), new ListRoster());

            CollectionAssert.AreEqual(new[] { a, b }, MenuTeamSelection.ResolveAvailableTeams(mode, null, null, LobbyTeamIndex),
                "До старта игрок без команды выбирает из команд режима.");

            SetPrivateField(mode, "_matchState", EliminationMatchState.Active);
            CollectionAssert.AreEqual(new[] { a }, MenuTeamSelection.ResolveAvailableTeams(mode, null, null, a.teamIndex),
                "После старта планшет предлагает сменить команду.");
            CollectionAssert.IsEmpty(MenuTeamSelection.ResolveAvailableTeams(mode, null, null, LobbyTeamIndex),
                "После старта игроку без команды выбирать нечего — команду выдаёт админ.");
        }

        [Test]
        public void Данные_режима_известны_клиенту_по_modeId()
        {
            SilenceMirrorNoise();

            GameModeData lobby = AssetDatabase.LoadAssetAtPath<GameModeData>(GameModeWiringTests.LobbyModeDataPath);
            GameplayManager manager = CreateNetworkComponent<GameplayManager>("GameplayManager");
            InvokeLifecycleMethod(manager, "Awake");
            SetPrivateField(manager, "_sceneGameMode", lobby);

            Assert.AreSame(lobby, GameModeCatalog.Find("lobby"),
                "Лобби-режима нет в реестре матча — клиент обязан найти его у режима сцены.");

            LobbyMode mode = CreateNetworkComponent<LobbyMode>("LobbyMode");
            SpawnOnServer(mode);
            mode.Initialize(lobby);
            Assert.AreSame(lobby, mode.ModeData, "Режим не знает своих данных.");
        }
    }
}

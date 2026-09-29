using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// До старта раунда игрок либо выбывший, либо в своей зоне. Аватар без прошлого (смена
    /// карты, первый вход) входит в матч Elimination выбывшим и оживает на своей базе, как все.
    /// Раньше после смены карты игрок появлялся живым там, куда его поставила точка спавна, —
    /// хоть посреди карты. Аватар взамен прежнего (смена скина, команды) несёт его жизнь.
    /// </summary>
    public class NewAvatarAdmissionTests : MirrorTestHarness
    {
        private EliminationMode _mode;
        private TeamData _teamA, _teamB;
        private StubPlayerRoster _roster;
        private RoundFlowDriver _driver;
        private readonly Dictionary<TeamData, TeamSpawnZone> _zones = new Dictionary<TeamData, TeamSpawnZone>();

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            SpawnSides.Reset();
            _zones.Clear();

            _teamA = TeamRegistry.Instance.GetByIndex(1);
            _teamB = TeamRegistry.Instance.GetByIndex(2);

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);

            _roster = new StubPlayerRoster();
            _mode.PlayerRoster = _roster;
            _zones[_teamA] = CreateZone(_teamA);
            _zones[_teamB] = CreateZone(_teamB);

            _driver = new RoundFlowDriver(dt => { _roster.DeclareAllReady(); _mode.ServerTick(dt); },
                                          () => _mode.CurrentRoundPhase);
        }

        [TearDown]
        public void ResetSides() => SpawnSides.Reset();

        private TeamSpawnZone CreateZone(TeamData team)
        {
            GameObject zoneGo = CreateObject("Zone_" + team.Name);
            zoneGo.AddComponent<BoxCollider>();
            TeamSpawnZone zone = zoneGo.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);
            return zone;
        }

        /// <summary>Сессия с аватаром. Аватар создан живым — как его создаёт AvatarManager.</summary>
        private PlayerController CreatePlayer(string name, TeamData team, bool inZone)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name + "_Session");
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;

            PlayerController player = CreateAvatar(name, session);

            if (inZone) session.ServerEnterSpawnZone(team.teamIndex);
            if (inZone) ZonePlayers(_zones[team]).Add(player);

            _roster.Add(team, session);
            return player;
        }

        /// <summary>Новый аватар той же сессии — живой актор, как его создаёт AvatarManager.</summary>
        private PlayerController CreateAvatar(string name, PlayerSession session)
        {
            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            session.ActiveAvatar = player;
            actor.Life = 100f;
            return player;
        }

        private static HashSet<PlayerController> ZonePlayers(TeamSpawnZone zone) =>
            (HashSet<PlayerController>)typeof(TeamSpawnZone)
                .GetField("_playersInZone", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(zone);

        /// <summary>Игрок дошёл до своей зоны — то же, что делает триггер зоны.</summary>
        private void Enter(PlayerController player, TeamData team)
        {
            TeamSpawnZone zone = _zones[team];
            ZonePlayers(zone).Add(player);
            var handler = (MulticastDelegate)typeof(TeamSpawnZone)
                .GetField("PlayerEntered", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(zone);
            handler?.DynamicInvoke(zone, player);
        }

        private void StartMatchToEquipment(PlayerController a, PlayerController b)
        {
            _mode.Initialize(new[] { _teamA, _teamB });
            InvokePrivateMethod(_mode, "InitializeActiveGame");
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Equipment, "закупки");
        }

        [Test]
        public void До_старта_матча_новый_аватар_выбывший()
        {
            SilenceMirrorNoise();
            PlayerController player = CreatePlayer("A", _teamA, inZone: false);

            _mode.ServerAdmitAvatar(player, continuesPrevious: false);

            Assert.IsFalse(player.IsAlive, "Новый аватар до старта раунда жив вне своей зоны — появился живым посреди карты.");
            Assert.IsTrue(player.Session.IsEliminated, "Выбывание нового аватара не записано в сессию — тело не станет призраком.");
        }

        [Test]
        public void В_закупке_новый_аватар_в_своей_зоне_жив()
        {
            SilenceMirrorNoise();
            PlayerController a = CreatePlayer("A", _teamA, inZone: true);
            PlayerController b = CreatePlayer("B", _teamB, inZone: true);
            StartMatchToEquipment(a, b);

            // Пересоздан после смены карты — уже стоит в своей зоне.
            PlayerController fresh = CreatePlayer("A2", _teamA, inZone: true);
            _mode.ServerAdmitAvatar(fresh, continuesPrevious: false);

            Assert.IsTrue(fresh.IsAlive, "Новый аватар в своей зоне в закупке не ожил.");
        }

        [Test]
        public void В_закупке_новый_аватар_вне_зоны_оживает_дойдя_до_неё()
        {
            SilenceMirrorNoise();
            PlayerController a = CreatePlayer("A", _teamA, inZone: true);
            PlayerController b = CreatePlayer("B", _teamB, inZone: true);
            StartMatchToEquipment(a, b);

            PlayerController fresh = CreatePlayer("A2", _teamA, inZone: false);
            _mode.ServerAdmitAvatar(fresh, continuesPrevious: false);
            Assert.IsFalse(fresh.IsAlive, "Новый аватар вне своей зоны жив.");

            Enter(fresh, _teamA);
            Assert.IsTrue(fresh.IsAlive, "Дошёл до своей зоны в закупке — не ожил.");
        }

        [Test]
        public void Аватар_взамен_живого_остаётся_живым()
        {
            SilenceMirrorNoise();
            PlayerController a = CreatePlayer("A", _teamA, inZone: false);
            PlayerController b = CreatePlayer("B", _teamB, inZone: false);
            StartMatchToEquipment(a, b);

            // Смена скина посреди карты: AvatarManager перенёс жизнь, режим не трогает.
            PlayerController swapped = CreatePlayer("A2", _teamA, inZone: false);
            _mode.ServerAdmitAvatar(swapped, continuesPrevious: true);

            Assert.IsTrue(swapped.IsAlive, "Смена скина убила живого игрока.");
        }

        [Test]
        public void Старт_режима_делает_выбывшими_уже_стоящие_аватары()
        {
            SilenceMirrorNoise();
            PlayerController a = CreatePlayer("A", _teamA, inZone: false);

            InvokePrivateMethod(_mode, "Begin");

            Assert.IsFalse(a.IsAlive, "Аватар, созданный до режима матча, остался живым вне зоны.");
        }

        /// <summary>
        /// Выбывание — состояние игрока, а не тела (T-35): аватар сменный, и новый аватар
        /// выбывшего не оживает, даже если его никто не «переносил». Раньше жизнь жила только
        /// в <c>UxrActor.Life</c> аватара, и каждый путь пересоздания обязан был не забыть
        /// <c>CarryLifeState</c>.
        /// </summary>
        [Test]
        public void Выбывание_принадлежит_сессии_а_не_аватару()
        {
            SilenceMirrorNoise();
            PlayerController old = CreatePlayer("A", _teamA, inZone: false);
            PlayerSession session = old.Session;
            Assert.IsNotNull(session, "Контроль харнесса: аватар связан с сессией.");

            old.ServerEliminateSilently("тест");
            Assert.IsTrue(session.IsEliminated, "Выбывание не записано в сессию.");

            PlayerController fresh = CreateAvatar("A_new", session);
            Assert.IsFalse(fresh.IsAlive, "Новый аватар выбывшего жив — выбывание жило в старом теле.");

            fresh.Respawn();
            Assert.IsFalse(session.IsEliminated, "Возрождение не сняло выбывание с сессии.");
            Assert.IsTrue(fresh.IsAlive, "Возрождённый игрок не жив.");
        }

        [Test]
        public void Смена_аватара_переносит_выбывание_и_здоровье()
        {
            SilenceMirrorNoise();
            PlayerController deadOld = CreatePlayer("Old", _teamA, inZone: false);
            deadOld.ServerEliminateSilently("тест");
            PlayerController fromDead = CreateAvatar("New", deadOld.Session);

            AvatarManager.CarryLifeState(deadOld, fromDead);
            Assert.IsFalse(fromDead.IsAlive, "Смена тела оживила выбывшего.");
            Assert.AreEqual(0f, fromDead.Health, 0.01f, "Тело выбывшего (призрак) с полным здоровьем.");

            PlayerController woundedOld = CreatePlayer("Old2", _teamA, inZone: false);
            woundedOld.RestoreHealth(35f);
            PlayerController fromWounded = CreateAvatar("New2", woundedOld.Session);

            AvatarManager.CarryLifeState(woundedOld, fromWounded);
            Assert.AreEqual(35f, fromWounded.Health, 0.01f, "Смена скина вылечила раненого.");
        }

        /// <summary>
        /// Возрождение из призрака (T-35): тело призрака мертво, а игрок уже жив — новое тело
        /// входит с полным здоровьем, а не с нулём призрака.
        /// </summary>
        [Test]
        public void Тело_после_призрака_с_полным_здоровьем()
        {
            SilenceMirrorNoise();
            PlayerController ghost = CreatePlayer("A", _teamA, inZone: false);
            ghost.ServerEliminateSilently("тест");
            PlayerSession session = ghost.Session;
            InvokePrivateMethod(session, "ServerSetEliminated", false);

            PlayerController body = CreateAvatar("A_body", session);
            AvatarManager.CarryLifeState(ghost, body);

            Assert.IsTrue(body.IsAlive, "Возрождённый из призрака не жив.");
            Assert.AreEqual(100f, body.Health, 0.01f, "Возрождённый из призрака получил здоровье мёртвого тела.");
        }

        /// <summary>
        /// Отложенное возрождение ждёт игрока, а не тело (T-35): выбывший ходит призраком — другим
        /// аватаром, чем тот, для которого возрождение назначено (новый аватар в матче сразу
        /// становится призраком). Раньше обработчик зоны сравнивал аватар по ссылке и ждал
        /// уничтоженное тело вечно.
        /// </summary>
        [Test]
        public void Отложенное_возрождение_ждёт_игрока_а_не_тело()
        {
            SilenceMirrorNoise();
            PlayerController a = CreatePlayer("A", _teamA, inZone: true);
            PlayerController b = CreatePlayer("B", _teamB, inZone: true);
            StartMatchToEquipment(a, b);

            PlayerController fresh = CreatePlayer("A2", _teamA, inZone: false);
            _mode.ServerAdmitAvatar(fresh, continuesPrevious: false);
            Assert.IsFalse(fresh.IsAlive, "Контроль: новый аватар вне зоны выбыл.");

            PlayerController ghost = CreateAvatar("A2_ghost", fresh.Session);
            Enter(ghost, _teamA);

            Assert.IsFalse(fresh.Session.IsEliminated, "Призрак игрока дошёл до своей зоны в закупке — игрок не ожил.");
        }
    }
}

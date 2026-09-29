using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Жизненный цикл подписок отложенного респавна (находка MATCH-05, задача T-10).
    ///
    /// <c>EliminationMode.PrepareNextRound</c> вешает на <c>TeamSpawnZone.PlayerEntered</c>
    /// одноразовый обработчик для мёртвого игрока, которого нет в зоне. Обработчик снимал
    /// себя сам — но только если игрок вернулся. Не вернулся: подписка оставалась навсегда,
    /// замыкание держало ссылку на уничтоженный <c>PlayerController</c>, а в следующем
    /// раунде старый обработчик срабатывал и возрождал игрока посреди боя.
    ///
    /// Почему ярус A. <c>PrepareNextRound</c>, <c>Initialize</c> и <c>PlayerController.Respawn</c>
    /// помечены <c>[Server]</c> — вне активного сервера Mirror их молча заглушает,
    /// и тест зеленел бы, ничего не проверив.
    ///
    /// Как тест видит подписки. <c>PlayerEntered</c> — field-like event: поднять его снаружи
    /// класса нельзя, поэтому и счёт обработчиков, и «игрок вошёл в зону» идут через
    /// приватное поле делегата. Это единственный способ проверить утечку подписок,
    /// не заводя ради теста публичного API в боевом коде.
    /// </summary>
    public class RespawnSubscriptionTests : MirrorTestHarness
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private EliminationMode _mode;
        private PlayersManager _players;

        private TeamData _teamA;
        private TeamSpawnZone _zoneA;

        private PlayerSession _session;
        private PlayerController _player;
        private UxrActor _actor;

        /// <summary>Синтетические TeamData: это не ассеты проекта, их обязательно уничтожать.</summary>
        private readonly List<TeamData> _createdTeams = new List<TeamData>();

        private int _nextConnectionId = 1;

        [SetUp]
        public void PrepareRound()
        {
            SilenceMirrorNoise();

            // Команды синтетические: реестр к отложенному респавну отношения не имеет,
            // а после T-08 подсчёт от него больше не зависит.
            _teamA = CreateTeam("Синтетическая A", 911);

            // _teamStates внутри GameMode ходит к боевому PlayersManager (PlayersManagerRoster),
            // а не к подставному реестру машины раунда — менеджер здесь настоящий.
            _players = CreateManager<PlayersManager>("PlayersManager");

            _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(_mode);
            _mode.Initialize(new[] { _teamA });

            _zoneA = CreateZone("SpawnZoneA", _teamA);

            _session = CreateSession("PlayerA", _teamA);
            _player = CreateDeadAvatar("AvatarA");
            _session.ActiveAvatar = _player;
        }

        [TearDown]
        public void DropTeams()
        {
            foreach (TeamData team in _createdTeams)
            {
                if (team != null) UnityEngine.Object.DestroyImmediate(team);
            }
            _createdTeams.Clear();
        }

        // ── Сборка сцены теста ───────────────────────────────────────────────

        private TeamData CreateTeam(string displayName, int teamIndex)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.name = displayName;
            team.displayName = displayName;
            team.teamIndex = teamIndex;

            _createdTeams.Add(team);
            return team;
        }

        /// <summary>
        /// Зона спавна команды. <c>Awake</c> в EditMode не зовётся, поэтому внутренний
        /// коллайдер зоны остаётся пустым — <c>PrepareNextRound</c> его и не трогает,
        /// ему хватает <c>Team</c> и пустого списка находящихся внутри.
        /// </summary>
        private TeamSpawnZone CreateZone(string name, TeamData team)
        {
            GameObject go = CreateObject(name);
            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);
            return zone;
        }

        private PlayerSession CreateSession(string name, TeamData team)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            SpawnOnServer(session);

            session.TeamIndex = team.teamIndex;
            _players.RegisterSession(new NetworkConnectionToClient(_nextConnectionId++), session);
            return session;
        }

        /// <summary>
        /// Мёртвый аватар. <c>PlayerController.Awake</c> в EditMode не зовётся, поэтому
        /// ссылку на актора проставляем руками — иначе <c>IsAlive</c> падает с NRE.
        /// </summary>
        private PlayerController CreateDeadAvatar(string name)
        {
            GameObject go = CreateNetworkObject(name);
            _actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            SpawnOnServer(player);

            player._actor = _actor;
            Kill(player);
            return player;
        }

        private void Kill(PlayerController player)
        {
            _actor.Life = 0f;
            Assert.IsFalse(player.IsAlive, "Заготовка теста сломана: аватар обязан быть мёртвым.");
        }

        // ── Доступ к подпискам зоны ──────────────────────────────────────────

        /// <summary>Делегат события <c>PlayerEntered</c>. Null — подписчиков нет.</summary>
        private static Delegate ZoneSubscribers(TeamSpawnZone zone)
        {
            FieldInfo field = typeof(TeamSpawnZone).GetField("PlayerEntered", InstanceMembers);
            Assert.IsNotNull(field,
                "В TeamSpawnZone не найдено поле события PlayerEntered — тест несовместим с этой версией класса.");

            return field.GetValue(zone) as Delegate;
        }

        /// <summary>Сколько обработчиков висит на <c>PlayerEntered</c> прямо сейчас.</summary>
        private static int SubscriberCount(TeamSpawnZone zone)
        {
            Delegate subscribers = ZoneSubscribers(zone);
            return subscribers == null ? 0 : subscribers.GetInvocationList().Length;
        }

        /// <summary>Игрок вошёл в зону: поднимаем событие так же, как это делает сам триггер.</summary>
        private static void EnterZone(TeamSpawnZone zone, PlayerController player)
        {
            Delegate subscribers = ZoneSubscribers(zone);
            if (subscribers == null) return;

            subscribers.DynamicInvoke(zone, player);
        }

        // ── Сами проверки (MATCH-05) ─────────────────────────────────────────

        [Test]
        public void Подписки_отложенного_респавна_не_копятся_за_три_раунда()
        {
            SilenceMirrorNoise();

            for (int round = 1; round <= 3; round++)
            {
                _mode.PrepareNextRound();

                Assert.AreEqual(1, SubscriberCount(_zoneA),
                    "Раунд " + round + ": на PlayerEntered обязан висеть ровно один обработчик.\n" +
                    "Больше одного означает, что подписки прошлых раундов не снимаются и копятся (MATCH-05):\n" +
                    "замыкание держит ссылку на уничтоженный PlayerController, а лишний обработчик\n" +
                    "срабатывает не вовремя.");
            }
        }

        /// <summary>
        /// Погибший оживает только на своей базе и только до начала боя. Раньше отложенный
        /// респавн текущего раунда жил до следующего: не успевший к закупке (сработал предел
        /// возвращения на базу) оживал, дойдя до зоны, посреди боя.
        /// </summary>
        [Test]
        public void Опоздавший_на_базу_не_оживает_в_бою_этого_раунда()
        {
            SilenceMirrorNoise();

            _mode.PrepareNextRound();
            SetPrivateField(_mode, "_roundPhase", RoundPhase.Combat);

            EnterZone(_zoneA, _player);

            Assert.IsFalse(_player.IsAlive, "Погибший ожил в своей зоне посреди боя — возрождение только до боя.");
        }

        [Test]
        public void Вернувшийся_на_базу_до_боя_оживает()
        {
            SilenceMirrorNoise();

            _mode.PrepareNextRound();
            SetPrivateField(_mode, "_roundPhase", RoundPhase.Setup);

            EnterZone(_zoneA, _player);

            Assert.IsTrue(_player.IsAlive, "Погибший дошёл до своей зоны на подготовке, а не ожил.");
        }

        [Test]
        public void Пропущенный_респавн_не_срабатывает_в_бою_следующего_раунда()
        {
            SilenceMirrorNoise();

            // Раунд 1: игрок мёртв и вне зоны — заводится отложенная подписка.
            _mode.PrepareNextRound();
            Assert.AreEqual(1, SubscriberCount(_zoneA), "Раунд 1 обязан завести отложенную подписку.");

            // Игрок так и не вернулся в зону, но раунд кончился, и к новому он жив
            // (возродился штатно — телепортацией на старте раунда, не через зону).
            _actor.Life = 100f;

            // Раунд 2: живому игроку подписка не нужна, а прошлая обязана быть снята.
            _mode.PrepareNextRound();
            Assert.AreEqual(0, SubscriberCount(_zoneA),
                "К началу нового раунда на PlayerEntered не должно остаться ни одного обработчика:\n" +
                "живому игроку отложенный респавн не заводится, а прошлогодний снимается очисткой.");

            // Бой раунда 2: игрок погиб и забежал в свою зону.
            Kill(_player);
            EnterZone(_zoneA, _player);

            Assert.IsFalse(_player.IsAlive,
                "Игрок возродился от подписки прошлого раунда — это и есть MATCH-05.\n" +
                "Заход в зону во время боя обязан оставаться без последствий: респавн вне фазы\n" +
                "подготовки ломает раунд.");
        }
    }
}

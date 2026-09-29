using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.DevTools.Bots;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Выбор команды ботом (<see cref="BotTeamChoice"/>): бот — противник человеку.
    /// </summary>
    public class BotTeamChoiceTests
    {
        private TeamData _a, _b;

        [SetUp]
        public void Prepare()
        {
            _a = ScriptableObject.CreateInstance<TeamData>();
            _a.teamIndex = 1;
            _b = ScriptableObject.CreateInstance<TeamData>();
            _b.teamIndex = 2;
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(_a);
            Object.DestroyImmediate(_b);
        }

        private TeamData Choose(int current, int[] humans, int[] bots)
        {
            return BotTeamChoice.Choose(new[] { _a, _b }, current, humans, bots);
        }

        [Test]
        public void Никого_нет_первая_команда()
        {
            Assert.AreSame(_a, Choose(0, new int[0], new int[0]));
        }

        [Test]
        public void Человек_в_A_бот_идёт_в_B()
        {
            Assert.AreSame(_b, Choose(0, new[] { 1 }, new int[0]));
        }

        [Test]
        public void Человек_пришёл_в_команду_бота_бот_уходит()
        {
            Assert.AreSame(_a, Choose(2, new[] { 2 }, new int[0]));
        }

        [Test]
        public void Бот_в_хорошей_команде_остаётся()
        {
            Assert.AreSame(_b, Choose(2, new[] { 1 }, new int[0]));
        }

        [Test]
        public void При_равенстве_бот_не_мечется()
        {
            Assert.AreSame(_b, Choose(2, new int[0], new int[0]), "Бот ушёл из команды, которая не хуже лучшей.");
        }

        [Test]
        public void Второй_бот_уходит_от_первого()
        {
            Assert.AreSame(_b, Choose(0, new int[0], new[] { 1 }));
        }

        [Test]
        public void Команды_выравниваются_по_общему_числу()
        {
            // В A — человек, в B — два бота: третий бот — союзник человека.
            Assert.AreSame(_a, Choose(0, new[] { 1 }, new[] { 2, 2 }));
        }

        [Test]
        public void Человек_и_три_бота_два_на_два_и_никто_не_перебегает()
        {
            var bots = new List<int> { 0, 0, 0 };
            int[] humans = { 1 };

            // Боты подключаются по одному, затем несколько тиков пересчёта.
            for (int tick = 0; tick < 4; tick++)
            {
                for (int i = 0; i < bots.Count; i++)
                {
                    var others = new List<int>();
                    for (int j = 0; j < bots.Count; j++) if (j != i && bots[j] != 0) others.Add(bots[j]);
                    bots[i] = Choose(bots[i], humans, others.ToArray()).teamIndex;
                }
            }

            int inA = 1 + bots.FindAll(t => t == 1).Count;
            int inB = bots.FindAll(t => t == 2).Count;
            Assert.AreEqual(2, inA, "У человека должен быть один союзник-бот.");
            Assert.AreEqual(2, inB);
        }

        [Test]
        public void Союзник_не_уходит_при_ровном_счёте()
        {
            // Человек и бот в A, два бота в B — бот из A остаётся.
            Assert.AreSame(_a, Choose(1, new[] { 1 }, new[] { 2, 2 }));
        }

        [Test]
        public void Команда_не_из_режима_меняется()
        {
            Assert.AreSame(_a, Choose(99, new int[0], new int[0]));
        }
    }

    /// <summary>
    /// Бот для игры — обычный игрок: сессия в реестре без соединения, аватар без владельца.
    /// </summary>
    public class BotSessionTests : MirrorTestHarness
    {
        private PlayerSession CreateSession(string name, TeamData team)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;
            return session;
        }

        private PlayerController CreateAvatar(string name, PlayerSession session)
        {
            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            InvokePrivateMethod(player, "LinkSession", session);
            session.ActiveAvatar = player;
            actor.Life = 100f;
            return player;
        }

        /// <summary>
        /// Тело погибшего бота уничтожается сразу — его сменяет призрак (T-35). Выпавший из руки ствол
        /// остаётся в мире (его подберут, уберёт уборка раунда); уничтожать своё оружие бот вправе,
        /// только если тело убрали живым (бот удалён, сменил команду). Раньше тело жило после смерти, и
        /// бот успевал «отпустить» ствол в Update; с заменой тела ствол погибшего бота исчезал.
        /// </summary>
        [TestCase(true, false, true, TestName = "Живого_бота_убрали_ствол_убирается")]
        [TestCase(false, false, false, TestName = "Погибший_бот_ствол_остаётся_в_мире")]
        [TestCase(true, true, false, TestName = "Ствол_в_чужой_руке_не_трогаем")]
        public void Ствол_бота_при_уборке_тела(bool bodyAlive, bool heldByOther, bool removed)
        {
            Assert.AreEqual(removed, VrBattlegrounds.DevTools.Bots.BotGunner.ShouldRemoveWeaponWithBody(bodyAlive, heldByOther));
        }

        [Test]
        public void Бот_регистрируется_как_игрок_без_соединения()
        {
            SilenceMirrorNoise();
            PlayersManager players = CreateManager<PlayersManager>("PlayersManager");
            TeamData team = TeamRegistry.Instance.GetByIndex(1);
            PlayerSession bot = CreateSession("Bot", team);

            var connected = new List<PlayerSession>();
            System.Action<PlayerSession> handler = s => connected.Add(s);
            PlayersManager.SessionConnected += handler;
            try
            {
                players.RegisterBot(bot);
            }
            finally
            {
                PlayersManager.SessionConnected -= handler;
            }

            CreateAvatar("BotAvatar", bot);

            CollectionAssert.Contains(players.Sessions, bot, "Бота нет в списке сессий.");
            CollectionAssert.AreEqual(new[] { bot }, connected, "Режим не узнал о боте: нет SessionConnected.");
            CollectionAssert.Contains(new List<PlayerSession>(players.GetAlivePlayers(team)), bot,
                "Живой бот не виден условиям раунда.");
        }

        [Test]
        public void Бот_убирается_без_снимка_и_с_событием()
        {
            SilenceMirrorNoise();
            PlayersManager players = CreateManager<PlayersManager>("PlayersManager");
            PlayerSession bot = CreateSession("Bot", TeamRegistry.Instance.GetByIndex(1));
            players.RegisterBot(bot);

            var gone = new List<PlayerSession>();
            System.Action<PlayerSession> handler = s => gone.Add(s);
            PlayersManager.SessionDisconnected += handler;
            try
            {
                players.UnregisterBot(bot);
            }
            finally
            {
                PlayersManager.SessionDisconnected -= handler;
            }

            CollectionAssert.DoesNotContain(players.Sessions, bot);
            CollectionAssert.AreEqual(new[] { bot }, gone);
        }

        [Test]
        public void Игрока_с_соединением_UnregisterBot_не_трогает()
        {
            SilenceMirrorNoise();
            PlayersManager players = CreateManager<PlayersManager>("PlayersManager");
            PlayerSession human = CreateSession("Human", TeamRegistry.Instance.GetByIndex(1));
            players.RegisterSession(new NetworkConnectionToClient(77), human);

            players.UnregisterBot(human);

            CollectionAssert.Contains(players.Sessions, human, "UnregisterBot убрал живого игрока.");
        }

        [Test]
        public void Бота_убивает_урон_игрока_и_убийство_засчитывается()
        {
            SilenceMirrorNoise();
            PlayersManager players = CreateManager<PlayersManager>("PlayersManager");
            Series series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(series, "Awake");
            series.LoadMapOverride = _ => { };
            SpawnOnServer(series);
            series.ServerBegin(new[] { "MapA" });

            MapReferee manager = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(manager, "Awake");
            InvokePrivateMethod(manager, "RegisterActiveGameMode", CreateNetworkComponent<EliminationMode>("EliminationMode"));

            PlayerSession humanSession = CreateSession("Human", TeamRegistry.Instance.GetByIndex(1));
            players.RegisterSession(new NetworkConnectionToClient(77), humanSession);
            PlayerController human = CreateAvatar("HumanAvatar", humanSession);

            PlayerSession botSession = CreateSession("Bot", TeamRegistry.Instance.GetByIndex(2));
            botSession.DeviceToken = "bot_1";
            players.RegisterBot(botSession);
            PlayerController bot = CreateAvatar("BotAvatar", botSession);

            bot.GetComponent<UxrActor>().ReceiveImpact(human.GetComponent<UxrActor>(), default(RaycastHit), 500f);

            Assert.IsFalse(bot.IsAlive, "Бот не погиб от смертельного урона.");
            Assert.AreEqual(1, series.GetKills(humanSession, Series.Total), "Убийство бота не засчитано игроку.");
            Assert.AreEqual(1, series.GetDeaths(botSession, Series.Total), "Смерть не засчитана боту.");
            Assert.AreEqual(0, new List<PlayerSession>(players.GetAlivePlayers(botSession.Team)).Count,
                "Мёртвый бот всё ещё среди живых — раунд не закончится.");

            bot.Respawn();
            Assert.IsTrue(bot.IsAlive, "Бот не возродился.");
        }
    }

    /// <summary>Тело без владельца рассылает позу с сервера (<see cref="ServerAuthoredAvatar"/>).</summary>
    public class ServerAuthoredAvatarTests
    {
        [Test]
        public void Все_NetworkTransform_переключаются_на_ServerToClient()
        {
            var root = new GameObject("Avatar");
            try
            {
                root.AddComponent<NetworkIdentity>();
                var head = new GameObject("Camera");
                head.transform.SetParent(root.transform);
                NetworkTransformBase rootNt = root.AddComponent<NetworkTransformReliable>();
                NetworkTransformBase headNt = head.AddComponent<NetworkTransformReliable>();
                rootNt.syncDirection = SyncDirection.ClientToServer;
                headNt.syncDirection = SyncDirection.ClientToServer;

                ServerAuthoredAvatar.Prepare(root);

                Assert.AreEqual(SyncDirection.ServerToClient, rootNt.syncDirection);
                Assert.AreEqual(SyncDirection.ServerToClient, headNt.syncDirection, "Вложенный NetworkTransform не переключён.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Метка_владельца_без_соединения_не_падает()
        {
            Assert.AreEqual("без владельца", ServerAuthoredAvatar.OwnerLabel(null));
        }
    }
}

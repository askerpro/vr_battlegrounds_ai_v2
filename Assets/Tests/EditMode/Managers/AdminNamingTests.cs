using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// Админ задаёт названия команд на серию и ники игрокам (<see cref="AdminNaming"/>,
    /// <see cref="TeamNameService"/>, <see cref="TeamNames"/>). Показ команды — <see cref="TeamData.Name"/>.
    /// </summary>
    public class AdminNamingTests : MirrorTestHarness
    {
        private TeamNameService _service;
        private TeamData _team;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            TeamNames.Clear();
            AdminNaming.ClearNicknames();

            _service = CreateNetworkComponent<TeamNameService>("TeamNameService");
            InvokeLifecycleMethod(_service, "Awake");
            SpawnOnServer(_service);

            _team = TeamRegistry.Instance.GetByIndex(1);
        }

        [TearDown]
        public void Cleanup()
        {
            TeamNames.Clear();
            AdminNaming.ClearNicknames();
        }

        private PlayerSession CreateSession(string name, bool admin, string token = "")
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.IsAdmin = admin;
            session.DeviceToken = token;
            return session;
        }

        [TestCase("  Альфа   Браво  ", "Альфа Браво")]
        [TestCase("<b>Жирные</b>", "bЖирные/b")]
        [TestCase("   ", "")]
        [TestCase("Очень-очень длинное название команды", "Очень-очень длинное назв")]
        public void Ввод_чистится(string raw, string expected)
        {
            Assert.AreEqual(expected, TeamNames.Sanitize(raw));
        }

        [Test]
        public void Не_админ_не_переименует_команду()
        {
            SilenceMirrorNoise();
            string before = _team.Name;

            bool ok = AdminNaming.ServerRenameTeam(CreateSession("Игрок", admin: false), _team.teamIndex, "Захватчики");

            Assert.IsFalse(ok);
            Assert.AreEqual(before, _team.Name);
        }

        [Test]
        public void Админ_переименовывает_команду_и_сбрасывает_к_умолчанию()
        {
            SilenceMirrorNoise();
            PlayerSession admin = CreateSession("Админ", admin: true);

            Assert.IsTrue(AdminNaming.ServerRenameTeam(admin, _team.teamIndex, "  Альфа  "));
            Assert.AreEqual("Альфа", _team.Name, "Название не дошло до показа команды.");
            Assert.AreEqual("Альфа", _team.ToString());

            AdminNaming.ServerRenameTeam(admin, _team.teamIndex, "");
            Assert.AreEqual(_team.displayName, _team.Name, "Пустое название обязано вернуть имя по умолчанию.");
        }

        [Test]
        public void Админ_даёт_ник_игроку_и_аватару_и_ник_запоминается_по_устройству()
        {
            SilenceMirrorNoise();
            PlayerSession admin = CreateSession("Админ", admin: true);
            PlayerSession target = CreateSession("Player_1234", admin: false, token: "device-42");

            GameObject go = CreateNetworkObject("Avatar");
            go.AddComponent<UxrActor>();
            PlayerController avatar = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(avatar, "Awake");
            SpawnOnServer(avatar);
            target.ActiveAvatar = avatar;

            Assert.IsTrue(AdminNaming.ServerRenamePlayer(admin, target, "Сокол"));
            Assert.AreEqual("Сокол", target.PlayerName);
            Assert.AreEqual("Сокол", avatar.AvatarPlayerName, "Имя над аватаром не сменилось.");
            Assert.AreEqual("Сокол", AdminNaming.NicknameFor("device-42"), "Ник не запомнен по устройству — при переподключении пропадёт.");
        }

        [Test]
        public void Не_админ_не_даёт_ник()
        {
            SilenceMirrorNoise();
            PlayerSession target = CreateSession("Player_1", admin: false);

            Assert.IsFalse(AdminNaming.ServerRenamePlayer(CreateSession("Игрок", admin: false), target, "Сокол"));
            Assert.AreEqual("Player_1", target.PlayerName);
        }
    }
}

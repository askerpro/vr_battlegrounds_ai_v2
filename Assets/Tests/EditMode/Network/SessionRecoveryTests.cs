using Mirror;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Находка T-04: сохранение состояния игрока при отключении.
    ///
    /// Покрыта половина цикла — снимок при отключении. Вторая половина
    /// (переподключение восстанавливает позицию) в EditMode недостижима:
    /// <see cref="PlayersManager.HandlePlayerConnect"/> инстанцирует префабы и зовёт
    /// <c>NetworkServer.Spawn</c>, а Unity в EditMode не вызывает <c>Awake</c> у клона,
    /// из-за чего у его <c>NetworkIdentity</c> пуст массив <c>NetworkBehaviours</c>
    /// и спавн падает. Это уровень 2 (PlayMode), см. <c>Docs/testing.md</c>.
    ///
    /// Оба метода <see cref="SessionRecoveryManager"/> помечены <c>[Server]</c>,
    /// поэтому без харнесса тест был бы зелёным и пустым.
    /// </summary>
    public class SessionRecoveryTests : MirrorTestHarness
    {
        private const string DeviceToken = "test-device-token";

        protected override bool NeedsLocalClient => true; // нужен NetworkConnection для регистрации сессии

        private PlayersManager _players;
        private PlayerSession _session;

        [SetUp]
        public void PrepareSession()
        {
            SilenceMirrorNoise();

            _players = CreateManager<PlayersManager>("PlayersManager");
            CreateManager<SessionRecoveryManager>("SessionRecoveryManager");

            _session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(_session);

            _session.DeviceToken = DeviceToken;
            _session.PlayerName = "Rambo";
            _session.TeamIndex = 1;
            _session.Score = 5;
            _session.Kills = 3;
        }

        /// <summary>Создаёт аватар с заданным здоровьем и позицией и цепляет его к сессии.</summary>
        private PlayerController AttachAvatar(Vector3 position, float life)
        {
            GameObject avatarObject = CreateNetworkObject("Avatar");

            UxrActor actor = avatarObject.AddComponent<UxrActor>();
            // _life — приватное сериализованное поле. Публичный сеттер Life лезет
            // в UxrNetworkManager, которого в EditMode нет.
            SetPrivateField(actor, "_life", life);

            PlayerController avatar = avatarObject.AddComponent<PlayerController>();
            avatar._actor = actor; // Awake, который обычно это делает, в EditMode не зовётся

            EnableNetworking(avatarObject);
            SpawnOnServer(avatar);

            avatarObject.transform.position = position;
            _session.ActiveAvatar = avatar;

            return avatar;
        }

        [Test]
        public void Отключение_сохраняет_позицию_живого_игрока()
        {
            SilenceMirrorNoise();

            Vector3 position = new Vector3(5f, 1f, 7f);
            AttachAvatar(position, life: 88f);

            NetworkConnection connection = NetworkServer.localConnection;
            _players.RegisterSession(connection, _session);
            Assert.AreEqual(1, _players.Sessions.Count, "Сессия не зарегистрировалась.");

            _players.UnregisterSession(connection);
            Assert.AreEqual(0, _players.Sessions.Count, "Сессия не снялась с регистрации.");

            SessionSnapshot snapshot = SessionRecoveryManager.Instance.GetAndRemoveSavedSession(DeviceToken);
            Assert.IsNotNull(snapshot,
                "Снимок сессии не сохранён. UnregisterSession берёт аватар из session.ActiveAvatar — " +
                "если ссылка потерялась, позиция и здоровье не сохраняются никогда.");

            Assert.AreEqual(position, snapshot.Position, "Позиция аватара сохранена неверно.");
            Assert.AreEqual(88f, snapshot.Health, "Здоровье аватара сохранено неверно.");
            Assert.IsTrue(snapshot.NeedsPhysicalRestore,
                "Игрок был жив — при переподключении его надо вернуть на прежнее место.");

            Assert.AreEqual("Rambo", snapshot.PlayerName);
            Assert.AreEqual(1, snapshot.TeamIndex, "Команда не сохранена.");
            Assert.AreEqual(5, snapshot.Score, "Счёт не сохранён.");
        }

        [Test]
        public void Мёртвого_игрока_не_возвращают_на_место_гибели()
        {
            SilenceMirrorNoise();

            AttachAvatar(new Vector3(5f, 1f, 7f), life: 0f);

            NetworkConnection connection = NetworkServer.localConnection;
            _players.RegisterSession(connection, _session);
            _players.UnregisterSession(connection);

            SessionSnapshot snapshot = SessionRecoveryManager.Instance.GetAndRemoveSavedSession(DeviceToken);
            Assert.IsNotNull(snapshot, "Снимок сессии не сохранён.");
            Assert.IsFalse(snapshot.NeedsPhysicalRestore,
                "Мёртвый игрок не должен возрождаться на месте гибели — только в зоне спавна.");
        }

        // ── Снимок и карта (CAL-02) ─────────────────────────────────────────

        [Test]
        public void Снимок_помнит_карту_и_признак_калибровки()
        {
            SilenceMirrorNoise();

            _session.IsCalibrated = true;
            AttachAvatar(new Vector3(5f, 1f, 7f), life: 88f);

            NetworkConnection connection = NetworkServer.localConnection;
            _players.RegisterSession(connection, _session);
            _players.UnregisterSession(connection);

            SessionSnapshot snapshot = SessionRecoveryManager.Instance.GetAndRemoveSavedSession(DeviceToken);
            Assert.IsNotNull(snapshot, "Снимок сессии не сохранён.");

            Assert.AreEqual(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, snapshot.CapturedOnMap,
                "Снимок обязан помнить карту: Position в нём мировая, а мировая точка " +
                "осмысленна только на своей карте (CAL-02).");

            Assert.IsTrue(snapshot.IsCalibrated,
                "Калибровка — характеристика игрока, как команда и скин, и переживать отключение " +
                "обязана так же. Иначе вернувшийся откалиброванный игрок выглядит новичком, " +
                "чьё место можно назначить.");
        }

        [Test]
        public void Место_из_снимка_годится_только_на_своей_карте()
        {
            SessionSnapshot snapshot = new SessionSnapshot
            {
                NeedsPhysicalRestore = true,
                CapturedOnMap = "TestMap1",
                Position = new Vector3(5f, 1f, 7f)
            };

            Assert.IsTrue(snapshot.CanRestorePlaceOn("TestMap1"),
                "Карта та же — вернуть игрока туда, где он стоял, точнее любого пересчёта.");

            Assert.IsFalse(snapshot.CanRestorePlaceOn("TestMap2"),
                "Карта сменилась, а мировая позиция из снимка применяется как есть. " +
                "Арена в TestMap1 повёрнута на 90° относительно TestMap2: та же мировая точка " +
                "означает там другое место арены — разворот относительно баз и геометрии (CAL-02).");
        }

        [Test]
        public void Мёртвого_игрока_снимок_не_возвращает_на_место_даже_на_своей_карте()
        {
            SessionSnapshot snapshot = new SessionSnapshot
            {
                NeedsPhysicalRestore = false,
                CapturedOnMap = "TestMap1"
            };

            Assert.IsFalse(snapshot.CanRestorePlaceOn("TestMap1"),
                "Проверка карты не должна отменять прежнее условие: мёртвого игрока " +
                "на место гибели не возвращают.");
        }

        [Test]
        public void Снимок_выдаётся_только_один_раз()
        {
            SilenceMirrorNoise();

            AttachAvatar(Vector3.zero, life: 100f);

            NetworkConnection connection = NetworkServer.localConnection;
            _players.RegisterSession(connection, _session);
            _players.UnregisterSession(connection);

            Assert.IsNotNull(SessionRecoveryManager.Instance.GetAndRemoveSavedSession(DeviceToken),
                "Первый запрос должен вернуть снимок.");
            Assert.IsNull(SessionRecoveryManager.Instance.GetAndRemoveSavedSession(DeviceToken),
                "Снимок должен удаляться после выдачи, иначе старое состояние применится повторно.");
        }
    }
}

using NUnit.Framework;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Место игрока, которое он приносит с собой при подключении, — находка <b>CAL-02</b>.
    ///
    /// <para>
    /// Что доказывают тесты. <c>PhysicalSpaceSyncManager</c> копил <b>мировую</b> позу
    /// аватара, а <c>GamePlayerConnectMessage</c> клал её в сообщение подключения как есть.
    /// Мировая точка осмысленна только на своей карте: обе карты проекта собраны из одного
    /// префаба арены, но в <c>TestMap1</c> он повёрнут на 90° вокруг Y относительно
    /// <c>TestMap2</c> и <c>Lobby</c>. Теперь поза копится <b>в координатах якорей</b> —
    /// и переводится там, где старая сцена ещё жива, а не в момент отправки сообщения,
    /// когда клиент уже переехал на карту сервера.
    /// </para>
    ///
    /// <para>
    /// Ярус A (<see cref="MirrorTestHarness"/>) нужен ради уборки созданных якорей:
    /// <c>PhysicalSpaceAnchorFrame.TryBuildFromScene</c> ищет их по всей сцене, и
    /// оставленные от прошлого теста нашлись бы в следующем. Сеть здесь не участвует.
    /// </para>
    /// </summary>
    public class SavedAvatarPlaceTests : MirrorTestHarness
    {
        [SetUp]
        public void ResetPlacementCache() => LocalPlayerCalibration.Reset();

        [TearDown]
        public void ClearPlacementCache() => LocalPlayerCalibration.Reset();

        // ── Расстановка арены проекта (TestMap2, без поворота) ───────────────

        private static readonly Vector3 AnchorZero = new Vector3(-2.74f, -0.01f, -3.60f);
        private static readonly Vector3 AnchorOne = new Vector3(2.75f, -0.01f, -3.60f);

        private void PlaceAnchors()
        {
            GameObject first = CreateObject("Anchor1");
            first.transform.position = AnchorZero;
            first.AddComponent<PhysicalSpaceAnchor>().id = 0;

            GameObject second = CreateObject("Anchor2");
            second.transform.position = AnchorOne;
            second.AddComponent<PhysicalSpaceAnchor>().id = 1;
        }

        private static PhysicalSpaceAnchorFrame SceneFrame()
        {
            PhysicalSpaceAnchorFrame frame;
            string diagnosis;

            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out diagnosis),
                "Якоря на сцене теста стоят, система координат обязана построиться: " + diagnosis);

            return frame;
        }

        private PhysicalSpaceSyncManager CreateSync()
        {
            return CreateManager<PhysicalSpaceSyncManager>("PhysicalSpaceSyncManager");
        }

        // ── Замер ────────────────────────────────────────────────────────────

        [Test]
        public void Место_запоминается_в_координатах_якорей_а_не_в_мировых()
        {
            SilenceMirrorNoise();
            PlaceAnchors();

            PhysicalSpaceSyncManager sync = CreateSync();

            Vector3 stood = new Vector3(1.5f, 0f, 4f);
            sync.RecordLocalAvatarPlace(stood, Quaternion.identity);

            Vector3 place;
            Quaternion rotation;
            string map;
            Assert.IsTrue(sync.TryGetSavedAvatarPlace(out place, out rotation, out map),
                "Якоря на сцене есть, аватар свою позу сообщил — запоминать было чем.");

            Vector3 expected = SceneFrame().ToLocal(stood);
            Assert.AreEqual(0f, Vector3.Distance(expected, place), 1e-3f,
                "Поза обязана лежать в координатах якорей. Ровно это и было CAL-02: " +
                "хранилась мировая позиция, а на соседней карте она означает другое место арены.");

            Assert.Greater(Vector3.Distance(stood, place), 1f,
                "Опыт бессмысленен, если координаты якорей совпали с мировыми: " +
                "тогда проверка выше не отличает одно от другого.");
        }

        [Test]
        public void Без_якорей_место_не_запоминается()
        {
            SilenceMirrorNoise();

            PhysicalSpaceSyncManager sync = CreateSync();
            sync.RecordLocalAvatarPlace(new Vector3(1.5f, 0f, 4f), Quaternion.identity);

            Vector3 place;
            Quaternion rotation;
            string map;
            Assert.IsFalse(sync.TryGetSavedAvatarPlace(out place, out rotation, out map),
                "Без пары якорей мировую позу не к чему привязать. Запомнить её «на всякий случай» " +
                "хуже, чем не запомнить вовсе: непереводимая поза молча означает не то место.");
        }

        // ── Сообщение подключения ────────────────────────────────────────────

        [Test]
        public void Сообщение_подключения_несёт_позу_в_координатах_якорей()
        {
            SilenceMirrorNoise();
            PlaceAnchors();

            PhysicalSpaceSyncManager sync = CreateSync();

            Vector3 stood = new Vector3(1.5f, 0f, 4f);
            sync.RecordLocalAvatarPlace(stood, Quaternion.identity);

            GamePlayerConnectMessage msg = new GamePlayerConnectMessage("token", ClientDeviceType.VR, 0, 0);

            Assert.IsTrue(msg.hasAnchorPlace,
                "Клиенту есть что сказать о своём месте, а сообщение подключения об этом молчит.");

            Assert.AreEqual(0f, Vector3.Distance(SceneFrame().ToLocal(stood), msg.anchorPlacePosition), 1e-3f,
                "В сообщении обязана ехать поза относительно якорей. Раньше ехала мировая — " +
                "и сервер применял её дословно, на какой бы карте он ни стоял (CAL-02).");

            Assert.IsFalse(msg.hasCalibration && msg.calibration.IsCalibrated,
                "Игрок не калибровался, и сообщение обязано это признать: применять место " +
                "или нет, сервер решает именно по этому признаку.");
        }
    }

    /// <summary>Принятый этап4: место входит в единый снимок, преобразуется между картами и валидируется целиком.</summary>
    public class PlayerPlacementTests
    {
        private static void OnNamedMap(System.Action<string> check)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string previousName = scene.name;
            if (string.IsNullOrEmpty(previousName)) scene.name = "T50PlacementTests";
            try { check(scene.name); }
            finally { if (scene.IsValid() && scene.isLoaded) scene.name = previousName; }
        }

        [Test]
        public void Живой_серверный_снимок_имеет_приоритет_над_клиентской_позой_на_той_же_карте()
        {
            OnNamedMap(map =>
            {
                var oldClient = new PlayerCalibration(.1f, 1.7f, true).WithPlacement(
                    PlayerPlacement.Anchored(Vector3.one, Quaternion.identity, map));
                var saved = new SessionSnapshot
                {
                    Calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(
                        PlayerPlacement.Anchored(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 37f, 0f), map)),
                    CapturedOnMap = map, NeedsPhysicalRestore = true
                };
                var message = new GamePlayerConnectMessage { hasCalibration = true, calibration = oldClient };
                Assert.AreEqual(oldClient.WithPlacement(saved.Calibration.Placement), PlayersManager.InitialCalibration(message, saved));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Мёртвый_калиброванный_игрок_сохраняет_world_привязку_только_на_той_же_карте(bool sameMap)
        {
            OnNamedMap(map =>
            {
                string source = sameMap ? map : map + "_other";
                var calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(
                    PlayerPlacement.World(new Vector3(3f, 0f, 7f), Quaternion.identity, source));
                var saved = new SessionSnapshot { Calibration = calibration, CapturedOnMap = source, NeedsPhysicalRestore = false };
                var resolved = PlayersManager.InitialCalibration(default, saved);
                Assert.AreEqual(sameMap ? calibration.Placement : PlayerPlacement.None, resolved.Placement);
                Assert.AreEqual(calibration.WithPlacement(PlayerPlacement.None), resolved.WithPlacement(PlayerPlacement.None));
                Assert.IsFalse(saved.NeedsPhysicalRestore, "Физическая привязка не возрождает игрока.");
            });
        }

        [Test]
        public void Клиентский_world_снимок_не_считается_серверным_местом()
        {
            var calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(
                PlayerPlacement.World(new Vector3(3f, 0f, 7f), Quaternion.identity, "A"));
            var message = new GamePlayerConnectMessage { hasCalibration = true, calibration = calibration };
            Assert.AreEqual(calibration.WithPlacement(PlayerPlacement.None), PlayersManager.InitialCalibration(message, null));
        }

        [Test]
        public void Поза_переносится_между_сдвинутой_и_повёрнутой_парой_якорей()
        {
            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuild(new Vector3(-2f, 0f, -3f), new Vector3(3f, 0f, -3f), out var first, out var diagnosis), diagnosis);
            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuild(new Vector3(10f, 0f, 20f), new Vector3(10f, 0f, 25f), out var second, out diagnosis), diagnosis);
            Assert.IsTrue(PlayerPlacement.TryCapture(new Vector3(1f, 0f, 4f), Quaternion.Euler(0f, 37f, 0f), true, "A", first, out var place, out diagnosis), diagnosis);
            Assert.IsTrue(place.IsAnchored);
            Assert.IsTrue(place.TryResolve(true, "B", second, out var position, out var rotation, out diagnosis), diagnosis);
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, 23f), position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(Quaternion.Euler(0f, -53f, 0f), rotation), Is.LessThan(.001f));
        }

        [TestCase(true, "A", true)]
        [TestCase(true, "B", false)]
        [TestCase(false, "B", true)]
        public void Мировой_снимок_калиброванного_игрока_действует_только_на_своей_карте(bool calibrated, string map, bool expected)
        {
            var place = PlayerPlacement.World(new Vector3(1f, 0f, 4f), Quaternion.identity, "A");
            Assert.AreEqual(expected, place.TryResolve(calibrated, map, default, out _, out _, out _));
        }

        [Test]
        public void Якорная_поза_требует_калибровку_и_действующую_пару_якорей()
        {
            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuild(Vector3.zero, Vector3.forward * 5f, out var frame, out _));
            var place = PlayerPlacement.Anchored(Vector3.one, Quaternion.identity, "A");
            Assert.IsFalse(place.TryResolve(false, "B", frame, out _, out _, out _));
            Assert.IsFalse(place.TryResolve(true, "B", default, out _, out _, out _));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Нечисловая_поза_отвергает_весь_снимок(float invalid)
        {
            var calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(
                PlayerPlacement.World(new Vector3(invalid, 0f, 0f), Quaternion.identity, "A"));
            Assert.IsFalse(PlayerCalibrationRules.TryNormalize(calibration, out _));
        }

        [Test]
        public void Нулевой_или_нечисловой_поворот_не_становится_принятой_позой()
        {
            Assert.IsFalse(PlayerPlacement.TryNormalize(PlayerPlacement.World(Vector3.zero, new Quaternion(), "A"), out _));
            Assert.IsFalse(PlayerPlacement.TryNormalize(PlayerPlacement.World(Vector3.zero, new Quaternion(0f, float.NaN, 0f, 1f), "A"), out _));
        }

        [Test]
        public void Повторный_замер_пола_роста_или_признака_сохраняет_место()
        {
            var pose = PlayerPlacement.Anchored(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 37f, 0f), "A");
            var calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(pose);
            Assert.AreEqual(pose, calibration.WithFloor(.1f).Placement);
            Assert.AreEqual(pose, calibration.WithEyeHeight(1.7f).Placement);
            Assert.AreEqual(pose, calibration.WithCalibrated(false).Placement);
        }

        [Test]
        public void Mirror_передаёт_калибровку_и_позу_одним_значением()
        {
            var calibration = new PlayerCalibration(.37f, 1.6f, true).WithPlacement(
                PlayerPlacement.Anchored(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 37f, 0f), "A"));
            var message = new GamePlayerConnectMessage { deviceToken = "placement-roundtrip", hasCalibration = true, calibration = calibration };
            var writer = new NetworkWriter();
            writer.Write(message);
            var copy = new NetworkReader(writer.ToArray()).Read<GamePlayerConnectMessage>();
            Assert.IsTrue(copy.hasCalibration);
            Assert.AreEqual(message.deviceToken, copy.deviceToken);
            Assert.AreEqual(calibration, copy.calibration);
        }
    }
}

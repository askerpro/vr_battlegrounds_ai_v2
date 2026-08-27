using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Core;
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

            Assert.IsFalse(msg.isCalibrated,
                "Игрок не калибровался, и сообщение обязано это признать: применять место " +
                "или нет, сервер решает именно по этому биту.");
        }
    }
}

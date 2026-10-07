using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Devices;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Находка <b>VR-08</b>: смещение пола, снятое калибровкой высоты, не доезжало
    /// ни до одной чужой машины — ни у одного из шести аватарных префабов.
    ///
    /// <para>
    /// Почему так было. Калибровка пола двигает <c>UxrAvatar.CameraController</c> — <b>родителя</b>
    /// камеры. <c>NetworkTransform</c> во всех префабах стоит на самой <c>Camera</c> и синхронизирует
    /// <c>localPosition</c> (<c>coordinateSpace: Local</c>), то есть позицию камеры
    /// <i>относительно этого самого пивота</i>. Тело удалённого аватара собирает <c>UxrBodyIK</c>
    /// от мировой позиции камеры, поэтому несдвинутый пивот на чужой машине — это игрок,
    /// стоящий не на своей высоте.
    /// </para>
    ///
    /// <para>
    /// Решение: значение живёт на <see cref="PlayerSession" /> (с T-50 — <see cref="PlayerCalibration" />
    /// в метрах, единственный писатель — сервер) и применяется на каждой машине
    /// <see cref="AvatarCalibrationApplier" />. Сценарии смены своего аватара, хоста и повторного
    /// применения — в <see cref="PlayerCalibrationOwnerTests" />.
    /// </para>
    /// </summary>
    public class CalibrationHeightReplicationTests : MirrorTestHarness
    {
        /// <summary>Заметно отличается от нуля, чтобы промах не спрятался в epsilon.</summary>
        private const float TestOffset = 0.37f;

        /// <summary>Базовая высота пивота в «префабе» заглушки: намеренно не ноль.</summary>
        private const float PivotBaseY = 0.12f;

        [SetUp]
        public void ResetHands() => UxrControllerTracking.GlobalHeightOffset = 0f;

        [TearDown]
        public void RestoreHands() => UxrControllerTracking.GlobalHeightOffset = 0f;

        private void CreateSessionPair(out PlayerSession server, out PlayerSession client)
        {
            server = CreateNetworkComponent<PlayerSession>("ServerSession");
            SpawnOnServer(server);

            client = CreateNetworkComponent<PlayerSession>("ClientSession");
        }

        /// <summary>
        /// Аватар-заглушка с пивотом камеры там, где его ищет SDK: прямым потомком корня,
        /// с камерой внутри. В EditMode <c>UxrAvatar.Awake</c> не зовётся — поэтому
        /// <c>UxrAvatar.CameraController</c> проставляем руками.
        /// </summary>
        private PlayerController CreateStubAvatar(string name, PlayerSession session)
        {
            GameObject avatarObject = CreateNetworkObject(name);

            avatarObject.AddComponent<UxrActor>();
            UxrAvatar uxrAvatar = avatarObject.AddComponent<UxrAvatar>();
            avatarObject.AddComponent<UxrStandardAvatarController>();
            PlayerController avatar = avatarObject.AddComponent<PlayerController>();

            EnableNetworking(avatarObject);

            GameObject pivot = new GameObject("Camera Controller");
            pivot.transform.SetParent(avatarObject.transform);
            pivot.transform.localPosition = new Vector3(0.4f, PivotBaseY, -0.25f);

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(pivot.transform);
            cameraObject.AddComponent<Camera>();

            GameObject dummyForward = new GameObject(AvatarCalibrationApplier.DummyForwardName);
            dummyForward.transform.SetParent(avatarObject.transform);

            typeof(UxrAvatar)
                .GetProperty("CameraController")
                .GetSetMethod(true)
                .Invoke(uxrAvatar, new object[] { pivot.transform });

            avatar.SessionNetId = session.netId;
            SpawnOnServer(avatar);

            return avatar;
        }

        private static Transform PivotOf(PlayerController avatar) => avatar.transform.Find("Camera Controller");

        // ── Границы значения ──────────────────────────────────────────────────

        [Test]
        public void Сервер_обрезает_смещение_пола_вне_допустимых_границ()
        {
            PlayerCalibration normalized;

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(50f, 0f, false), out normalized),
                "Конечное число сервер обязан принять, пусть и обрезав.");
            Assert.AreEqual(PlayerCalibrationRules.MaxFloorOffset, normalized.FloorOffset, 1e-4f,
                "Смещение выше потолка не обрезано: клиент смог бы поднять свою голову куда угодно, " +
                "а вместе с ней и точку попадания.");

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(-50f, 0f, false), out normalized));
            Assert.AreEqual(-PlayerCalibrationRules.MaxFloorOffset, normalized.FloorOffset, 1e-4f,
                "Смещение ниже пола не обрезано: игрок ушёл бы под карту.");

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(TestOffset, 0f, false), out normalized));
            Assert.AreEqual(TestOffset, normalized.FloorOffset, 1e-4f, "Значение внутри границ не должно меняться.");
        }

        [Test]
        public void Сервер_отвергает_нечисловое_смещение_пола()
        {
            PlayerCalibration normalized;

            Assert.IsFalse(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(float.NaN, 0f, false), out normalized),
                "NaN обязан быть отвергнут: он расходится по матрицам трансформа.");
            Assert.IsFalse(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(float.NegativeInfinity, 0f, false), out normalized),
                "Бесконечность обязана быть отвергнута.");
        }

        // ── Репликация значения (ярус A+) ─────────────────────────────────────

        [Test]
        public void Калибровка_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            Assert.AreEqual(PlayerCalibration.None, client.Calibration, "Двойник должен стартовать без калибровки.");

            PlayerCalibration calibration = new PlayerCalibration(TestOffset, 1.63f, true);
            Assert.IsTrue(server.ServerAcceptCalibration(calibration, PlayerSession.CalibrationOrigin.Connect));

            ReplicateToClient(server, client);

            Assert.AreEqual(calibration, client.Calibration,
                "SyncVar калибровки не доехал до клиента — на чужих экранах игрок останется стоять " +
                "на исходной высоте и в исходных пропорциях (VR-08, VR-01).");
            Assert.IsTrue(client.IsCalibrated);
        }

        // ── Применение к аватару ──────────────────────────────────────────────

        [Test]
        public void Пол_применяется_к_чужому_аватару_и_не_трогает_руки_этой_машины()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("RemoteAvatar", server);
            server.ActiveAvatar = avatar;
            server.ServerAcceptCalibration(new PlayerCalibration(TestOffset, 0f, false), PlayerSession.CalibrationOrigin.Connect);

            Transform pivot = PivotOf(avatar);
            Assert.AreEqual(PivotBaseY + TestOffset, pivot.localPosition.y, 1e-4f,
                "Пивот камеры чужого аватара не сдвинут: это и есть VR-08 — калибровка пола остаётся " +
                "локальной, и на чужих экранах игрок стоит не на своей высоте.");
            Assert.AreEqual(0.4f, pivot.localPosition.x, 1e-4f, "Калибровка пола не должна двигать пивот вбок.");
            Assert.AreEqual(-0.25f, pivot.localPosition.z, 1e-4f, "Калибровка пола не должна двигать пивот вперёд-назад.");
            Assert.AreEqual(0f, UxrControllerTracking.GlobalHeightOffset, 1e-4f,
                "Пол чужого игрока сдвинул руки этой машины: трекинг контроллеров есть только у своего аватара.");
        }

        [Test]
        public void Пересозданный_аватар_получает_пол_заново_и_ровно_один_раз()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            // Обычный порядок: игрок калибруется в лобби, аватар появляется на карте.
            server.ServerAcceptCalibration(new PlayerCalibration(TestOffset, 0f, false), PlayerSession.CalibrationOrigin.Connect);

            PlayerController first = CreateStubAvatar("Avatar1", server);
            server.ActiveAvatar = first;
            Assert.AreEqual(PivotBaseY + TestOffset, PivotOf(first).localPosition.y, 1e-4f,
                "Первый аватар не получил смещение пола при связывании с сессией.");

            // Смена скина, команды или карты — аватар пересоздаётся, сессия живёт.
            PlayerController second = CreateStubAvatar("Avatar2", server);
            server.ActiveAvatar = second;

            // Повторные входы: решение сервера ещё раз (повторная доставка, второй вызывающий).
            server.ServerAcceptCalibration(server.Calibration, PlayerSession.CalibrationOrigin.Connect);
            server.ServerAcceptCalibration(server.Calibration, PlayerSession.CalibrationOrigin.Connect);

            Assert.AreEqual(PivotBaseY + TestOffset, PivotOf(second).localPosition.y, 1e-4f,
                "Новый аватар не на высоте калибровки: смещение не переехало на пересозданную куклу " +
                "либо наложилось повторно.");
        }
    }
}

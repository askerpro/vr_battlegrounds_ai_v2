using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
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
    /// Почему так было. <c>PhysicalSpaceSyncManager.ApplyHeightDelta</c> двигает
    /// <c>UxrAvatar.CameraController</c> — <b>родителя</b> камеры. <c>NetworkTransform</c>
    /// во всех префабах стоит на самой <c>Camera</c> и синхронизирует
    /// <c>localPosition</c> (<c>coordinateSpace: Local</c>), то есть позицию камеры
    /// <i>относительно этого самого пивота</i>. При калибровке пола она не меняется —
    /// меняется позиция пивота, а на нём <c>NetworkTransform</c> нет нигде. Итог:
    /// реплицировался трекинг головы внутри игровой зоны, а высота пола — нет.
    /// </para>
    ///
    /// <para>
    /// Почему это видно, а не «камера же невидимая». Тело удалённого аватара собирает
    /// <c>UxrBodyIK</c>, а шею он ставит от мировой позиции камеры
    /// (<c>UxrBodyIK.cs:227</c>); <c>UxrManager</c> при этом решает IK у <b>всех</b>
    /// аватаров, а не только у локального (<c>UxrManager.cs:1901-1908</c>). Значит
    /// несдвинутый пивот на чужой машине — это игрок, стоящий не на своей высоте.
    /// </para>
    ///
    /// <para>
    /// Решение выбрано симметричным T-14 (масштаб калибровки): значение живёт
    /// <c>SyncVar</c>-ом на <see cref="PlayerSession" /> и применяется локально на
    /// каждой машине. Правка префабов при этом не нужна вовсе — что важно, потому что
    /// шесть аватарных префабов уже разошлись по составу (см. VR-07).
    /// </para>
    ///
    /// <para>
    /// Чего эти тесты не проверяют: саму процедуру калибровки — она читает положение
    /// шлема и контроллеров, то есть требует железа (уровень 5 из <c>Docs/testing.md</c>).
    /// </para>
    /// </summary>
    public class CalibrationHeightReplicationTests : MirrorTestHarness
    {
        /// <summary>Заметно отличается от нуля, чтобы промах не спрятался в epsilon.</summary>
        private const float TestOffset = 0.37f;

        /// <summary>Базовая высота пивота в «префабе» заглушки: намеренно не ноль.</summary>
        private const float PivotBaseY = 0.12f;

        private void CreateSessionPair(out PlayerSession server, out PlayerSession client)
        {
            server = CreateNetworkComponent<PlayerSession>("ServerSession");
            SpawnOnServer(server);

            client = CreateNetworkComponent<PlayerSession>("ClientSession");
        }

        /// <summary>
        /// Аватар-заглушка с пивотом камеры там, где его ищет SDK: прямым потомком
        /// корня, с камерой внутри. Именно этот подъём делает <c>UxrAvatar.Awake</c>,
        /// а в EditMode Unity <c>Awake</c> не зовёт — поэтому
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
            pivot.transform.localPosition = new Vector3(0f, PivotBaseY, 0f);

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(pivot.transform);
            cameraObject.AddComponent<Camera>();

            // Связывание сессии с аватаром попутно применяет и масштаб калибровки,
            // а он ищет "Dummy Forward". При единичном масштабе правка вырождается
            // в ничто, но без объекта в лог ушло бы предупреждение не по делу.
            GameObject dummyForward = new GameObject("Dummy Forward");
            dummyForward.transform.SetParent(avatarObject.transform);

            // UxrAvatar.CameraController — свойство с приватным сеттером, его заполняет Awake,
            // а в EditMode Unity Awake не зовёт ни при AddComponent, ни при Instantiate.
            typeof(UxrAvatar)
                .GetProperty("CameraController")
                .GetSetMethod(true)
                .Invoke(uxrAvatar, new object[] { pivot.transform });

            avatar.SessionNetId = session.netId;
            SpawnOnServer(avatar);

            return avatar;
        }

        private static Transform PivotOf(PlayerController avatar)
        {
            return avatar.transform.Find("Camera Controller");
        }

        // ── Границы значения ──────────────────────────────────────────────────

        [Test]
        public void Сервер_обрезает_смещение_пола_вне_допустимых_границ()
        {
            float normalized;

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationHeightOffset(50f, out normalized),
                "Конечное число сервер обязан принять, пусть и обрезав.");
            Assert.AreEqual(PlayerSession.MaxCalibrationHeightOffset, normalized, 1e-4f,
                "Смещение выше потолка не обрезано: клиент смог бы поднять свою голову " +
                "куда угодно, а вместе с ней и точку попадания.");

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationHeightOffset(-50f, out normalized));
            Assert.AreEqual(-PlayerSession.MaxCalibrationHeightOffset, normalized, 1e-4f,
                "Смещение ниже пола не обрезано: игрок ушёл бы под карту.");

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationHeightOffset(TestOffset, out normalized));
            Assert.AreEqual(TestOffset, normalized, 1e-4f,
                "Значение внутри границ не должно меняться.");
        }

        [Test]
        public void Сервер_отвергает_нечисловое_смещение_пола()
        {
            float normalized;

            Assert.IsFalse(PlayerSession.TryNormalizeCalibrationHeightOffset(float.NaN, out normalized),
                "NaN обязан быть отвергнут: он расходится по матрицам трансформа.");
            Assert.IsFalse(PlayerSession.TryNormalizeCalibrationHeightOffset(float.NegativeInfinity, out normalized),
                "Бесконечность обязана быть отвергнута.");
        }

        // ── Репликация значения (ярус A+) ─────────────────────────────────────

        [Test]
        public void Смещение_пола_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            Assert.AreEqual(0f, client.CalibrationHeightOffset, 1e-4f,
                "Двойник должен стартовать с нулевым смещением.");

            server.CalibrationHeightOffset = TestOffset;

            ReplicateToClient(server, client);

            Assert.AreEqual(TestOffset, client.CalibrationHeightOffset, 1e-4f,
                "SyncVar CalibrationHeightOffset не доехал до клиента — на чужих экранах " +
                "игрок останется стоять на исходной высоте (VR-08).");
        }

        // ── Применение к аватару ──────────────────────────────────────────────

        [Test]
        public void Смещение_применяется_к_чужому_аватару()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("RemoteAvatar", server);
            Transform pivot = PivotOf(avatar);

            Assert.IsNull(UxrAvatar.LocalAvatar,
                "Тест бессмыслен, если аватар оказался локальным: проверяется как раз чужой.");

            PhysicalSpaceSyncManager.ShiftAvatarCameraPivot(avatar.GetComponent<UxrAvatar>(), TestOffset);

            Assert.AreEqual(PivotBaseY + TestOffset, pivot.localPosition.y, 1e-4f,
                "Пивот камеры чужого аватара не сдвинут: это и есть VR-08 — калибровка пола " +
                "остаётся локальной, и на чужих экранах игрок стоит не на своей высоте.");
        }

        [Test]
        public void Сдвиг_пивота_не_трогает_горизонтальные_оси()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("RemoteAvatar", server);
            Transform pivot = PivotOf(avatar);
            pivot.localPosition = new Vector3(0.4f, PivotBaseY, -0.25f);

            PhysicalSpaceSyncManager.ShiftAvatarCameraPivot(avatar.GetComponent<UxrAvatar>(), TestOffset);

            Assert.AreEqual(0.4f, pivot.localPosition.x, 1e-4f, "Калибровка пола не должна двигать пивот вбок.");
            Assert.AreEqual(-0.25f, pivot.localPosition.z, 1e-4f, "Калибровка пола не должна двигать пивот вперёд-назад.");
        }

        [Test]
        public void Пересозданный_аватар_получает_смещение_заново_и_ровно_один_раз()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            // Обычный порядок: игрок калибруется в лобби, аватар появляется на карте.
            server.CalibrationHeightOffset = TestOffset;

            PlayerController first = CreateStubAvatar("Avatar1", server);
            server.ActiveAvatar = first;

            Assert.AreEqual(PivotBaseY + TestOffset, PivotOf(first).localPosition.y, 1e-4f,
                "Первый аватар не получил смещение пола при связывании с сессией.");

            // Смена скина, команды или карты — аватар пересоздаётся, сессия живёт.
            PlayerController second = CreateStubAvatar("Avatar2", server);
            server.ActiveAvatar = second;

            Assert.AreEqual(PivotBaseY + TestOffset, PivotOf(second).localPosition.y, 1e-4f,
                "Новый аватар остался на исходной высоте: смещение не переехало на пересозданную " +
                "куклу. Калибровка живёт в сессии именно затем, чтобы переживать смену скина и карты.");

            // Повторное применение не должно поднять аватар ещё раз: метод сдвигает
            // пивот, а не ставит абсолютное значение, и зовут его двое — хук SyncVar
            // и связывание аватара, — причём порядок их не определён.
            InvokePrivateMethod(server, "ApplyCalibrationHeightOffset");
            InvokePrivateMethod(server, "ApplyCalibrationHeightOffset");

            Assert.AreEqual(PivotBaseY + TestOffset, PivotOf(second).localPosition.y, 1e-4f,
                "Смещение наложилось повторно: игрок уезжал бы вверх на каждой пересборке связи.");
        }
    }
}

using NUnit.Framework;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// T-14 (находка VR-01): пропорции игрока, снятые калибровкой роста, должны доезжать
    /// до всех машин и применяться там к аватару.
    ///
    /// <para>
    /// Что было до правки. Масштаб <c>Dummy Forward</c> ставился только у
    /// <c>UxrAvatar.LocalAvatar</c>. У этого объекта нет и не может быть своего
    /// <c>NetworkTransform</c> — его создаёт сам SDK в рантайме (<c>UxrBodyIK.Initialize</c>), — а на
    /// корневых <c>NetworkTransform</c> аватарных префабов <c>syncScale</c> выключен. Поэтому на чужих
    /// экранах игрок оставался стандартного роста, а коллайдеры расходились с картинкой.
    /// </para>
    ///
    /// <para>
    /// С T-50 рост едет в метрах (<see cref="PlayerCalibration.EyeHeight" />), а масштаб каждая машина
    /// считает для каждой модели по её <c>EyesBaseHeight</c> (<see cref="AvatarCalibrationApplier" />).
    /// Перенос между моделями и смена своего аватара — <see cref="PlayerCalibrationOwnerTests" />.
    /// Совпадение картинки у двух живых клиентов — сценарий яруса C <c>calibration-scale-replication</c>.
    /// </para>
    /// </summary>
    public class CalibrationScaleReplicationTests : MirrorTestHarness
    {
        private const float EyesBase = 1.84f;

        /// <summary>Рост глаз, заметно отличный от базы модели: масштаб 1,25.</summary>
        private const float TestEyeHeight = EyesBase * 1.25f;

        private const float TestScale = 1.25f;

        private void CreateSessionPair(out PlayerSession server, out PlayerSession client)
        {
            server = CreateNetworkComponent<PlayerSession>("ServerSession");
            SpawnOnServer(server);

            client = CreateNetworkComponent<PlayerSession>("ClientSession");
        }

        /// <summary>
        /// Аватар-заглушка: то немногое из настоящего префаба, что нужно масштабированию.
        /// <c>Dummy Forward</c> и <c>_bodyIK</c> в рантайме делает <c>UxrStandardAvatarController.Awake</c>,
        /// а в EditMode Unity <c>Awake</c> не зовёт — поэтому собираем их руками.
        /// </summary>
        private PlayerController CreateStubAvatar(string name, PlayerSession session)
        {
            GameObject avatarObject = CreateNetworkObject(name);

            avatarObject.AddComponent<UxrActor>();
            avatarObject.AddComponent<UxrAvatar>();
            UxrStandardAvatarController controller = avatarObject.AddComponent<UxrStandardAvatarController>();
            PlayerController avatar = avatarObject.AddComponent<PlayerController>();

            EnableNetworking(avatarObject);

            GameObject dummyForward = new GameObject(AvatarCalibrationApplier.DummyForwardName);
            dummyForward.transform.SetParent(avatarObject.transform);

            UxrBodyIKSettings settings = new UxrBodyIKSettings();
            SetPrivateField(settings, "_eyesBaseHeight", EyesBase);
            SetPrivateField(controller, "_bodyIKSettings", settings);
            SetPrivateField(controller, "_bodyIK", new UxrBodyIK());

            avatar.SessionNetId = session.netId;
            SpawnOnServer(avatar);

            return avatar;
        }

        private static Transform DummyForwardOf(PlayerController avatar) =>
            avatar.transform.Find(AvatarCalibrationApplier.DummyForwardName);

        private static void Accept(PlayerSession session, float eyeHeight) =>
            session.ServerAcceptCalibration(new PlayerCalibration(0f, eyeHeight, false), PlayerSession.CalibrationOrigin.Connect);

        // ── Границы значения ──────────────────────────────────────────────────

        [Test]
        public void Сервер_обрезает_рост_вне_допустимых_границ()
        {
            PlayerCalibration normalized;

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, 9f, false), out normalized),
                "Конечное число сервер обязан принять, пусть и обрезав.");
            Assert.AreEqual(PlayerCalibrationRules.MaxEyeHeight, normalized.EyeHeight, 1e-4f,
                "Рост выше потолка не обрезан: клиент смог бы раздуть свои коллайдеры.");

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, 0.3f, false), out normalized));
            Assert.AreEqual(PlayerCalibrationRules.MinEyeHeight, normalized.EyeHeight, 1e-4f,
                "Рост ниже пола не обрезан: игрок стал бы почти непопадаемым.");

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, 0f, false), out normalized));
            Assert.AreEqual(0f, normalized.EyeHeight, "Ноль — «рост не калибровался», а не «очень маленький игрок».");

            Assert.IsTrue(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, TestEyeHeight, false), out normalized));
            Assert.AreEqual(TestEyeHeight, normalized.EyeHeight, 1e-4f, "Значение внутри границ не должно меняться.");
        }

        [Test]
        public void Сервер_отвергает_нечисловой_рост()
        {
            PlayerCalibration normalized;

            Assert.IsFalse(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, float.NaN, false), out normalized),
                "NaN обязан быть отвергнут: он расходится по матрицам трансформа и ломает аватар целиком.");
            Assert.IsFalse(PlayerCalibrationRules.TryNormalize(new PlayerCalibration(0f, float.PositiveInfinity, false), out normalized),
                "Бесконечность обязана быть отвергнута.");
        }

        // ── Применение к аватару ──────────────────────────────────────────────

        [Test]
        public void Масштаб_применяется_к_любому_аватару_а_не_только_к_локальному()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("RemoteAvatar", server);
            Assert.AreEqual(Vector3.one, DummyForwardOf(avatar).localScale, "Заглушка должна начинать с единичного масштаба.");
            Assert.IsNull(UxrAvatar.LocalAvatar,
                "Тест бессмыслен, если аватар оказался локальным: проверяется как раз чужой.");

            server.ActiveAvatar = avatar;
            Accept(server, TestEyeHeight);

            Assert.AreEqual(TestScale, DummyForwardOf(avatar).localScale.x, 1e-4f,
                "Чужой аватар не отмасштабирован: это и есть VR-01 — игрок на других машинах остаётся " +
                "в исходных пропорциях.");
        }

        [Test]
        public void Повторное_применение_того_же_роста_ничего_не_ломает()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("Avatar", server);
            server.ActiveAvatar = avatar;

            Accept(server, TestEyeHeight);
            Accept(server, TestEyeHeight);
            Accept(server, TestEyeHeight);

            Assert.AreEqual(TestScale, DummyForwardOf(avatar).localScale.x, 1e-4f,
                "Масштаб обязан быть идемпотентным: его применяют и хук SyncVar, и связывание аватара, " +
                "и порядок этих двух событий не определён.");
        }

        [Test]
        public void Пересозданный_аватар_получает_масштаб_заново()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            // Сначала игрок откалибровался, аватара ещё нет — обычный порядок.
            Accept(server, TestEyeHeight);

            PlayerController first = CreateStubAvatar("Avatar1", server);
            server.ActiveAvatar = first;
            Assert.AreEqual(TestScale, DummyForwardOf(first).localScale.x, 1e-4f,
                "Первый аватар не получил масштаб при связывании с сессией.");

            PlayerController second = CreateStubAvatar("Avatar2", server);
            server.ActiveAvatar = second;
            Assert.AreEqual(TestScale, DummyForwardOf(second).localScale.x, 1e-4f,
                "Новый аватар остался стандартного роста: масштаб не переехал на пересозданную куклу.");
        }
    }
}

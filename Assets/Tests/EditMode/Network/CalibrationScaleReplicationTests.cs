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
    /// Что было до правки. <c>PhysicalSpaceSyncManager.ApplyScale</c> масштабировал
    /// <c>Dummy Forward</c> только у <c>UxrAvatar.LocalAvatar</c>. У этого объекта нет
    /// и не может быть своего <c>NetworkTransform</c> — его создаёт сам SDK в рантайме
    /// (<c>UxrBodyIK.Initialize</c>), — а на корневых <c>NetworkTransform</c> всех
    /// аватарных префабов <c>syncScale</c> выключен (проверено в редакторе).
    /// Поэтому на чужих экранах игрок оставался стандартного роста, а коллайдеры,
    /// висящие на костях, расходились с картинкой.
    /// </para>
    ///
    /// <para>
    /// Чего эти тесты не проверяют. Саму процедуру калибровки: она читает положение
    /// шлема и контроллеров, то есть требует железа (уровень 5 из <c>Docs/testing.md</c>).
    /// Здесь проверяется всё, что после неё: границы значения, репликация и применение
    /// к аватару — своему и чужому. Совпадение картинки у двух живых клиентов проверяет
    /// сценарий яруса C <c>calibration-scale-replication</c>.
    /// </para>
    /// </summary>
    public class CalibrationScaleReplicationTests : MirrorTestHarness
    {
        /// <summary>Заметно отличается от единицы, чтобы промах не спрятался в epsilon.</summary>
        private const float TestScale = 1.25f;

        /// <summary>Создаёт сессию на сервере и её клиентского двойника.</summary>
        private void CreateSessionPair(out PlayerSession server, out PlayerSession client)
        {
            server = CreateNetworkComponent<PlayerSession>("ServerSession");
            SpawnOnServer(server);

            client = CreateNetworkComponent<PlayerSession>("ClientSession");
        }

        /// <summary>
        /// Аватар-заглушка: то немногое из настоящего префаба, что нужно масштабированию.
        ///
        /// <para>
        /// <c>Dummy Forward</c> и <c>_bodyIK</c> в рантайме делает
        /// <c>UxrStandardAvatarController.Awake</c>, а в EditMode Unity <c>Awake</c>
        /// не зовёт ни при <c>AddComponent</c>, ни при <c>Instantiate</c> — поэтому
        /// собираем их руками. <c>_bodyIK</c> ставится пустым: код правит в нём два
        /// <c>Vector3</c>, и нулевые значения для проверки масштаба годятся, а вот
        /// <c>null</c> увёл бы выполнение в ветку ошибки.
        /// </para>
        /// </summary>
        private PlayerController CreateStubAvatar(string name, PlayerSession session)
        {
            GameObject avatarObject = CreateNetworkObject(name);

            avatarObject.AddComponent<UxrActor>();
            UxrAvatar uxrAvatar = avatarObject.AddComponent<UxrAvatar>();
            UxrStandardAvatarController controller = avatarObject.AddComponent<UxrStandardAvatarController>();
            PlayerController avatar = avatarObject.AddComponent<PlayerController>();

            EnableNetworking(avatarObject);

            GameObject dummyForward = new GameObject("Dummy Forward");
            dummyForward.transform.SetParent(avatarObject.transform);

            SetPrivateField(controller, "_bodyIK", new UxrBodyIK());

            Assert.IsNotNull(uxrAvatar, "UxrAvatar не добавился — заглушка аватара непригодна.");

            avatar.SessionNetId = session.netId;
            SpawnOnServer(avatar);

            return avatar;
        }

        private static Transform DummyForwardOf(PlayerController avatar)
        {
            return avatar.transform.Find("Dummy Forward");
        }

        // ── Границы значения ──────────────────────────────────────────────────

        [Test]
        public void Сервер_обрезает_масштаб_вне_допустимых_границ()
        {
            float normalized;

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationScale(9f, out normalized),
                "Конечное число сервер обязан принять, пусть и обрезав.");
            Assert.AreEqual(PlayerSession.MaxCalibrationScale, normalized, 1e-4f,
                "Масштаб выше потолка не обрезан: клиент смог бы раздуть свои коллайдеры.");

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationScale(0.01f, out normalized));
            Assert.AreEqual(PlayerSession.MinCalibrationScale, normalized, 1e-4f,
                "Масштаб ниже пола не обрезан: игрок стал бы почти непопадаемым.");

            Assert.IsTrue(PlayerSession.TryNormalizeCalibrationScale(TestScale, out normalized));
            Assert.AreEqual(TestScale, normalized, 1e-4f,
                "Значение внутри границ не должно меняться.");
        }

        [Test]
        public void Сервер_отвергает_нечисловой_масштаб()
        {
            float normalized;

            Assert.IsFalse(PlayerSession.TryNormalizeCalibrationScale(float.NaN, out normalized),
                "NaN обязан быть отвергнут: он расходится по матрицам трансформа и ломает аватар целиком.");
            Assert.IsFalse(PlayerSession.TryNormalizeCalibrationScale(float.PositiveInfinity, out normalized),
                "Бесконечность обязана быть отвергнута.");
        }

        // ── Репликация значения (ярус A+) ─────────────────────────────────────

        [Test]
        public void Масштаб_калибровки_долетает_до_клиента()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            Assert.AreEqual(1f, client.CalibrationScale, 1e-4f,
                "Двойник должен стартовать с единичным масштабом.");

            server.CalibrationScale = TestScale;

            ReplicateToClient(server, client);

            Assert.AreEqual(TestScale, client.CalibrationScale, 1e-4f,
                "SyncVar CalibrationScale не доехал до клиента — на чужих экранах игрок " +
                "останется стандартного роста, а коллайдеры разойдутся с картинкой (VR-01).");
        }

        // ── Применение к аватару ──────────────────────────────────────────────

        [Test]
        public void Масштаб_применяется_к_любому_аватару_а_не_только_к_локальному()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("RemoteAvatar", server);
            Transform dummyForward = DummyForwardOf(avatar);

            Assert.AreEqual(Vector3.one, dummyForward.localScale,
                "Заглушка должна начинать с единичного масштаба.");

            // Ключ находки: аватар заведомо не UxrAvatar.LocalAvatar — статик пуст,
            // Awake в EditMode не звался. Старый ApplyScale работал только с ним
            // и здесь не сделал бы ничего.
            Assert.IsNull(UxrAvatar.LocalAvatar,
                "Тест бессмыслен, если аватар оказался локальным: проверяется как раз чужой.");

            PhysicalSpaceSyncManager.ApplyScaleToAvatar(avatar.GetComponent<UxrAvatar>(), TestScale);

            Assert.AreEqual(new Vector3(TestScale, TestScale, TestScale), dummyForward.localScale,
                "Чужой аватар не отмасштабирован: это и есть VR-01 — игрок ростом 1.5 м " +
                "на других машинах остаётся в исходных пропорциях.");
        }

        [Test]
        public void Повторное_применение_того_же_масштаба_ничего_не_ломает()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            PlayerController avatar = CreateStubAvatar("Avatar", server);
            Transform dummyForward = DummyForwardOf(avatar);
            UxrAvatar uxrAvatar = avatar.GetComponent<UxrAvatar>();

            PhysicalSpaceSyncManager.ApplyScaleToAvatar(uxrAvatar, TestScale);
            PhysicalSpaceSyncManager.ApplyScaleToAvatar(uxrAvatar, TestScale);
            PhysicalSpaceSyncManager.ApplyScaleToAvatar(uxrAvatar, TestScale);

            Assert.AreEqual(new Vector3(TestScale, TestScale, TestScale), dummyForward.localScale,
                "Масштаб обязан быть идемпотентным: его применяют и хук SyncVar, и связывание " +
                "аватара, и порядок этих двух событий не определён.");
        }

        [Test]
        public void Пересозданный_аватар_получает_масштаб_заново()
        {
            SilenceMirrorNoise();

            PlayerSession server, client;
            CreateSessionPair(out server, out client);

            // Сначала игрок откалибровался, аватара ещё нет — обычный порядок:
            // калибруются в лобби, аватар появляется на карте.
            server.CalibrationScale = TestScale;

            PlayerController first = CreateStubAvatar("Avatar1", server);
            server.ActiveAvatar = first;

            Assert.AreEqual(new Vector3(TestScale, TestScale, TestScale), DummyForwardOf(first).localScale,
                "Первый аватар не получил масштаб при связывании с сессией.");

            // Смена скина, команды или карты — аватар пересоздаётся, сессия живёт.
            PlayerController second = CreateStubAvatar("Avatar2", server);
            server.ActiveAvatar = second;

            Assert.AreEqual(new Vector3(TestScale, TestScale, TestScale), DummyForwardOf(second).localScale,
                "Новый аватар остался стандартного роста: масштаб не переехал на пересозданную куклу. " +
                "Пропорции живут в сессии именно затем, чтобы переживать смену скина и карты.");
        }
    }
}

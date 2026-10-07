using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core.Components;
using UltimateXR.Devices;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// T-50: класс ошибки «несколько писателей производного состояния аватара, применение
    /// дельтами, владение по гоночному признаку». Тесты держат весь класс, а не баг B1.
    ///
    /// <para>
    /// Инвариант, который проверяется во всех сценариях: после любой последовательности
    /// событий (связь сессии с аватаром, старт своего аватара в UltimateXR, повторный хук,
    /// смена модели) производное состояние аватара равно функции от базы префаба и калибровки
    /// игрока — пивот камеры = база + пол, <c>Dummy Forward</c> = рост глаз / <c>EyesBaseHeight</c>
    /// модели, поля BodyIK = база × масштаб, смещение рук = пол (только у своего).
    /// </para>
    ///
    /// <para>
    /// Наблюдаемые величины одинаковы для любой реализации; от реализации зависит только
    /// раздел «Драйвер» — как «откалиброваться», «принять на сервере» и «прийти хуку».
    /// Свой аватар объявляется двумя способами сразу: регистрацией в списке
    /// <c>UxrAvatar</c> (на ней держался <c>UxrAvatar.LocalAvatar</c> старого кода) и
    /// признаком <c>isLocalPlayer</c> сессии.
    /// </para>
    /// </summary>
    public abstract class CalibrationOwnerFixture : MirrorTestHarness
    {
        // ── Модели (EyesBaseHeight префабов) ─────────────────────────────────

        protected const float CyborgEyes = 1.84f; // PlayerBase / Cyborg
        protected const float MefEyes    = 1.72f;
        protected const float HeavyEyes  = 1.71f;

        /// <summary>Базовая высота пивота камеры в «префабе»: намеренно не ноль.</summary>
        protected const float PivotBaseY = 0.12f;

        protected static readonly Vector3 NeckBase    = new Vector3(0f, -0.11f, -0.07f);
        protected static readonly Vector3 ForwardBase = new Vector3(0.01f, -1.52f, 0.03f);

        protected const float Floor     = 0.37f;
        protected const float EyeHeight = 1.60f;

        protected const float Eps = 1e-4f;

        private static readonly FieldInfo AvatarTypeList = typeof(UxrComponent<UxrAvatar>)
            .GetField("s_typeComponents", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UxrAvatar> _registered = new List<UxrAvatar>();

        protected PhysicalSpaceSyncManager Sync;

        protected sealed class Stub
        {
            public PlayerController Controller;
            public UxrAvatar Uxr;
            public Transform Pivot;
            public Transform DummyForward;
            public UxrBodyIK BodyIK;
            public float EyesBase;
        }

        [SetUp]
        public void PrepareCalibrationFixture()
        {
            SilenceMirrorNoise();
            Assert.IsNotNull(AvatarTypeList, "UltimateXR: нет UxrComponent<T>.s_typeComponents — харнесс несовместим.");
            UxrControllerTracking.GlobalHeightOffset = 0f;
            Sync = CreateManager<PhysicalSpaceSyncManager>("PhysicalSpaceSyncManager");
            DriverReset();
        }

        [TearDown]
        public void CleanupCalibrationFixture()
        {
            var list = (List<UxrAvatar>)AvatarTypeList.GetValue(null);
            foreach (UxrAvatar avatar in _registered) list.Remove(avatar);
            _registered.Clear();

            UxrControllerTracking.GlobalHeightOffset = 0f;
            typeof(PhysicalSpaceSyncManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
            DriverReset();
        }

        // ── Сборка ───────────────────────────────────────────────────────────

        protected static void MarkAsLocalPlayer(NetworkBehaviour behaviour)
        {
            typeof(NetworkIdentity).GetProperty("isLocalPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetSetMethod(true).Invoke(behaviour.netIdentity, new object[] { true });
        }

        /// <summary>Аватар-заглушка с тем, что пишет калибровка: пивот камеры, Dummy Forward, BodyIK.</summary>
        protected Stub CreateAvatar(string name, PlayerSession session, float eyesBase, bool own,
                                    NetworkConnectionToClient owner = null)
        {
            GameObject go = CreateNetworkObject(name);

            go.AddComponent<UxrActor>();
            UxrAvatar uxr = go.AddComponent<UxrAvatar>();
            UxrStandardAvatarController controller = go.AddComponent<UxrStandardAvatarController>();
            PlayerController pc = go.AddComponent<PlayerController>();

            EnableNetworking(go);

            GameObject pivot = new GameObject("Camera Controller");
            pivot.transform.SetParent(go.transform);
            pivot.transform.localPosition = new Vector3(0.02f, PivotBaseY, -0.03f);

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(pivot.transform);
            cameraObject.AddComponent<Camera>();

            typeof(UxrAvatar).GetProperty("CameraController").GetSetMethod(true).Invoke(uxr, new object[] { pivot.transform });

            GameObject dummy = new GameObject("Dummy Forward");
            dummy.transform.SetParent(go.transform);

            UxrBodyIKSettings settings = new UxrBodyIKSettings();
            SetPrivateField(settings, "_eyesBaseHeight", eyesBase);
            SetPrivateField(controller, "_bodyIKSettings", settings);

            UxrBodyIK bodyIK = new UxrBodyIK();
            SetPrivateField(bodyIK, "_neckPosRelativeToEyes", NeckBase);
            SetPrivateField(bodyIK, "_avatarForwardPosRelativeToNeck", ForwardBase);
            SetPrivateField(controller, "_bodyIK", bodyIK);

            pc.SessionNetId = session.netId;

            if (owner != null) NetworkServer.Spawn(go, owner);
            else SpawnOnServer(pc);

            if (own)
            {
                // Порядок списка — порядок Awake в игре: старый аватар раньше нового.
                ((List<UxrAvatar>)AvatarTypeList.GetValue(null)).Add(uxr);
                _registered.Add(uxr);
            }

            return new Stub { Controller = pc, Uxr = uxr, Pivot = pivot.transform, DummyForward = dummy.transform, BodyIK = bodyIK, EyesBase = eyesBase };
        }

        protected void DestroyAvatar(Stub stub)
        {
            ((List<UxrAvatar>)AvatarTypeList.GetValue(null)).Remove(stub.Uxr);
            _registered.Remove(stub.Uxr);
            NetworkServer.Destroy(stub.Controller.gameObject);
        }

        // ── Наблюдаемое ──────────────────────────────────────────────────────

        protected static float FloorOn(Stub s) => s.Pivot.localPosition.y - PivotBaseY;

        protected static float ScaleOn(Stub s) => s.DummyForward.localScale.x;

        protected static Vector3 NeckOn(Stub s) =>
            (Vector3)typeof(UxrBodyIK).GetField("_neckPosRelativeToEyes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s.BodyIK);

        protected static Vector3 ForwardOn(Stub s) =>
            (Vector3)typeof(UxrBodyIK).GetField("_avatarForwardPosRelativeToNeck", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s.BodyIK);

        protected static void AssertDerived(Stub s, float floor, float eyeHeight, string context)
        {
            float scale = eyeHeight / s.EyesBase;

            Assert.AreEqual(floor, FloorOn(s), Eps, $"{context}: пивот камеры сдвинут на {FloorOn(s):F3} м, ждали {floor:F3} м (база префаба + пол).");
            Assert.AreEqual(0.02f, s.Pivot.localPosition.x, Eps, $"{context}: калибровка пола сдвинула пивот вбок.");
            Assert.AreEqual(-0.03f, s.Pivot.localPosition.z, Eps, $"{context}: калибровка пола сдвинула пивот вперёд-назад.");
            Assert.AreEqual(scale, ScaleOn(s), Eps,
                $"{context}: Dummy Forward = {ScaleOn(s):F4}, ждали рост {eyeHeight:F2} / EyesBaseHeight {s.EyesBase:F2} = {scale:F4}.");
            Assert.That(Vector3.Distance(NeckBase * scale, NeckOn(s)), Is.LessThan(Eps),
                $"{context}: _neckPosRelativeToEyes = {NeckOn(s)}, ждали база × {scale:F4} = {NeckBase * scale}.");
            Assert.That(Vector3.Distance(ForwardBase * scale, ForwardOn(s)), Is.LessThan(Eps),
                $"{context}: _avatarForwardPosRelativeToNeck = {ForwardOn(s)}, ждали база × {scale:F4} = {ForwardBase * scale}.");
        }

        protected static void AssertHands(float floor, string context)
        {
            Assert.AreEqual(floor, UxrControllerTracking.GlobalHeightOffset, Eps,
                $"{context}: смещение рук {UxrControllerTracking.GlobalHeightOffset:F3} м, а пол {floor:F3} м — руки не на контроллерах (B1).");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Драйвер: всё, что зависит от реализации.
        //  Стадия 0 (красный прогон на origin/dev 03da356c) шла через PSM.ApplyHeightDelta/ApplyScale,
        //  PlayerSession.ApplyCalibrationHeightOffset/Scale и PlayersManager.ApplyPhysicalPlace со
        //  снимком, хранившим только признак. Здесь — путь T-50.
        // ══════════════════════════════════════════════════════════════════

        protected void DriverReset()
        {
            // Как новый процесс: ни записи, ни подписчиков от сессий прошлого теста.
            typeof(LocalPlayerCalibration).GetMethod("ResetOnLoad", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
        }

        /// <summary>Свой игрок прошёл процедуру калибровки (пол, затем рост) на своём текущем аватаре, сервер принял.</summary>
        protected void CalibrateOwn(PlayerSession session, float floor, float eyeHeight)
        {
            PlayerCalibration measured = new PlayerCalibration(floor, eyeHeight, true);
            session.RequestCalibration(measured);

            if (NetworkClient.active)
            {
                // Хост: команда идёт через локальное соединение — её тело исполнит сервер.
                PumpNetwork(6);
            }
            else
            {
                // Без клиента Mirror команду не отправит: тело команды — ServerAcceptCalibration.
                session.ServerAcceptCalibration(measured, PlayerSession.CalibrationOrigin.Player, SentRequest(session));
            }
        }

        protected static int SentRequest(PlayerSession session) =>
            (int)typeof(PlayerSession).GetField("_sentCalibrationRequest", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(session);

        /// <summary>Чужой игрок откалибровался у себя на модели с <paramref name="calibratedOnEyes"/>; значение приехало.</summary>
        protected void CalibrateRemote(PlayerSession session, float floor, float eyeHeight, float calibratedOnEyes)
        {
            session.ServerAcceptCalibration(new PlayerCalibration(floor, eyeHeight, true), PlayerSession.CalibrationOrigin.Player);
        }

        /// <summary>Хук SyncVar калибровки пришёл ещё раз (повторная доставка, второй вход, хост).</summary>
        protected static void HookFires(PlayerSession session)
        {
            InvokePrivateMethod(session, "OnCalibrationChanged", session.Calibration, session.Calibration);
        }

        /// <summary>UltimateXR поднял LocalAvatarStarted для своего аватара.</summary>
        protected void OwnAvatarStarted(Stub stub)
        {
            InvokePrivateMethod(Sync, "UxrAvatar_LocalAvatarStarted", stub.Uxr, new UxrAvatarStartedEventArgs(stub.Uxr));
        }

        /// <summary>Сервер восстановил сессию из снимка отключённого откалиброванного игрока.</summary>
        protected static void ServerRestoresFromSnapshot(PlayerSession session, float floor, float eyeHeight)
        {
            SessionSnapshot snapshot = new SessionSnapshot { Calibration = new PlayerCalibration(floor, eyeHeight, true) };
            GamePlayerConnectMessage restartedClient = default(GamePlayerConnectMessage); // клиент без данных
            session.ServerAcceptCalibration(PlayersManager.InitialCalibration(restartedClient, snapshot),
                                            PlayerSession.CalibrationOrigin.Connect);
        }

        /// <summary>
        /// Сервер ответил на последний запрос, не меняя значения (отказ в бою): ровно то, что пишет
        /// <c>ServerAcceptCalibration</c> при отказе, — номер ответа без нового значения.
        /// </summary>
        protected static void ServerRejectsLastRequest(PlayerSession session)
        {
            SetPrivateField(session, "_answeredCalibrationRequest", SentRequest(session));
            InvokePrivateMethod(session, "OnCalibrationAnswered", 0, SentRequest(session));
        }
    }

    /// <summary>Ярус A: выделенный сервер / удалённый клиент, свой аватар объявлен признаками.</summary>
    public class PlayerCalibrationOwnerTests : CalibrationOwnerFixture
    {
        private PlayerSession CreateOwnSession()
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>("OwnSession");
            SpawnOnServer(session);
            MarkAsLocalPlayer(session);
            return session;
        }

        private PlayerSession CreateRemoteSession()
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>("RemoteSession");
            SpawnOnServer(session);
            return session;
        }

        [Test]
        public void Смена_своего_аватара_при_живом_старом__связь_раньше_старта()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub a = CreateAvatar("Cyborg_A", session, CyborgEyes, own: true);
            session.ActiveAvatar = a.Controller;
            OwnAvatarStarted(a);
            CalibrateOwn(session, Floor, EyeHeight);
            AssertDerived(a, Floor, EyeHeight, "Контроль, первый аватар");

            // B1: ReplaceBody — новый в сети и связан, старый ещё жив; потом старый уничтожен,
            // и только в следующем кадре UltimateXR поднимает LocalAvatarStarted нового.
            Stub b = CreateAvatar("Cyborg_B", session, CyborgEyes, own: true);
            session.ActiveAvatar = b.Controller;
            DestroyAvatar(a);
            OwnAvatarStarted(b);

            AssertDerived(b, Floor, EyeHeight, "Новый свой аватар");
            AssertHands(Floor, "Новый свой аватар");
        }

        [Test]
        public void Смена_своего_аватара_при_живом_старом__старт_раньше_связи()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub a = CreateAvatar("Cyborg_A", session, CyborgEyes, own: true);
            session.ActiveAvatar = a.Controller;
            OwnAvatarStarted(a);
            CalibrateOwn(session, Floor, EyeHeight);

            Stub b = CreateAvatar("Cyborg_B", session, CyborgEyes, own: true);
            OwnAvatarStarted(b);
            session.ActiveAvatar = b.Controller;
            DestroyAvatar(a);

            AssertDerived(b, Floor, EyeHeight, "Новый свой аватар");
            AssertHands(Floor, "Новый свой аватар");
        }

        [Test]
        public void Повторное_применение_к_своему_аватару_не_накапливает_пол_и_масштаб()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub a = CreateAvatar("Cyborg_A", session, CyborgEyes, own: true);
            session.ActiveAvatar = a.Controller;
            OwnAvatarStarted(a);
            CalibrateOwn(session, Floor, EyeHeight);

            // Повторные входы: старт менеджера при живом аватаре, повторный хук, второй старт.
            OwnAvatarStarted(a);
            HookFires(session);
            HookFires(session);
            OwnAvatarStarted(a);

            AssertDerived(a, Floor, EyeHeight, "После повторных применений");
            AssertHands(Floor, "После повторных применений");
        }

        [Test]
        public void Рост_в_метрах_переносится_на_свои_модели_с_другим_EyesBaseHeight()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub cyborg = CreateAvatar("Cyborg", session, CyborgEyes, own: true);
            session.ActiveAvatar = cyborg.Controller;
            OwnAvatarStarted(cyborg);
            CalibrateOwn(session, Floor, EyeHeight);
            AssertDerived(cyborg, Floor, EyeHeight, "Контроль, Cyborg");

            Stub mef = CreateAvatar("MEF", session, MefEyes, own: true);
            DestroyAvatar(cyborg);
            session.ActiveAvatar = mef.Controller;
            OwnAvatarStarted(mef);
            AssertDerived(mef, Floor, EyeHeight, "Калибровка на Cyborg → MEF");

            Stub heavy = CreateAvatar("Heavy", session, HeavyEyes, own: true);
            DestroyAvatar(mef);
            session.ActiveAvatar = heavy.Controller;
            OwnAvatarStarted(heavy);
            AssertDerived(heavy, Floor, EyeHeight, "Калибровка на Cyborg → MEF → Heavy");
            AssertHands(Floor, "Heavy");
        }

        [Test]
        public void Рост_чужого_игрока_ставится_по_EyesBaseHeight_модели_на_этой_машине()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateRemoteSession();

            // Чужой игрок калибровался у себя на Cyborg, а у нас видна его MEF.
            Stub mef = CreateAvatar("RemoteMEF", session, MefEyes, own: false);
            session.ActiveAvatar = mef.Controller;
            CalibrateRemote(session, Floor, EyeHeight, calibratedOnEyes: CyborgEyes);

            AssertDerived(mef, Floor, EyeHeight, "Чужая MEF");
            AssertHands(0f, "Чужой аватар не должен трогать руки этой машины");

            Stub heavy = CreateAvatar("RemoteHeavy", session, HeavyEyes, own: false);
            session.ActiveAvatar = heavy.Controller;
            NetworkServer.Destroy(mef.Controller.gameObject);
            HookFires(session);

            AssertDerived(heavy, Floor, EyeHeight, "Чужой Heavy");
            AssertHands(0f, "Чужой аватар не должен трогать руки этой машины");
        }

        [Test]
        public void Поля_BodyIK_ставятся_от_базы_а_не_от_прошлого_масштаба()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateRemoteSession();

            Stub avatar = CreateAvatar("Remote", session, CyborgEyes, own: false);
            session.ActiveAvatar = avatar.Controller;
            CalibrateRemote(session, 0f, EyeHeight, calibratedOnEyes: CyborgEyes);

            // Сторонний писатель масштаба (повторная инициализация, другой компонент):
            // относительная правка new/old после этого умножает поля IK второй раз.
            avatar.DummyForward.localScale = Vector3.one;
            HookFires(session);

            AssertDerived(avatar, 0f, EyeHeight, "После стороннего сброса масштаба");
        }

        [Test]
        public void Предсказание_сразу_на_своём_аватаре_а_отказ_сервера_откатывает_его()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub a = CreateAvatar("Cyborg_A", session, CyborgEyes, own: true);
            session.ActiveAvatar = a.Controller;
            session.ServerAcceptCalibration(new PlayerCalibration(0.1f, 1.70f, true), PlayerSession.CalibrationOrigin.Connect);
            AssertDerived(a, 0.1f, 1.70f, "Контроль: значение сервера");

            // Замер ушёл серверу: свой аватар показывает его сразу, не дожидаясь ответа.
            session.RequestCalibration(new PlayerCalibration(Floor, EyeHeight, true));
            AssertDerived(a, Floor, EyeHeight, "Предсказание до ответа сервера");
            AssertHands(Floor, "Предсказание до ответа сервера");

            // Сервер отказал (бой): аватар и руки возвращаются к значению сервера тем же применителем.
            ServerRejectsLastRequest(session);
            AssertDerived(a, 0.1f, 1.70f, "После отказа сервера");
            AssertHands(0.1f, "После отказа сервера");
            Assert.AreEqual(new PlayerCalibration(0.1f, 1.70f, true), LocalPlayerCalibration.Current,
                "Машина запомнила отклонённый замер: при следующем подключении он ушёл бы серверу снова.");
        }

        [Test]
        public void Решение_сервера_запоминается_машиной_и_уезжает_в_сообщении_подключения()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateOwnSession();

            Stub a = CreateAvatar("Cyborg_A", session, CyborgEyes, own: true);
            session.ActiveAvatar = a.Controller;

            // Рост выше предела: сервер обрежет, и машина обязана запомнить обрезанное.
            CalibrateOwn(session, Floor, 9f);

            PlayerCalibration decided = new PlayerCalibration(Floor, PlayerCalibrationRules.MaxEyeHeight, true);
            Assert.AreEqual(decided, session.Calibration, "Контроль: сервер обрезал рост.");
            Assert.IsTrue(LocalPlayerCalibration.HasValue);
            Assert.AreEqual(decided, LocalPlayerCalibration.Current, "Машина не запомнила решение сервера.");

            GamePlayerConnectMessage msg = new GamePlayerConnectMessage("token", VrBattlegrounds.Core.ClientDeviceType.VR, 0, 0);
            Assert.IsTrue(msg.hasCalibration, "Переподключение без калибровки: игрок вернётся стандартным.");
            Assert.AreEqual(decided, msg.calibration);
            Assert.AreEqual(decided, PlayersManager.InitialCalibration(msg, new SessionSnapshot()),
                "Данные клиента новее снимка: клиент пришёл со своим значением.");
        }

        [Test]
        public void Клиент_без_данных_ничего_не_несёт_и_сервер_берёт_снимок()
        {
            SilenceMirrorNoise();

            GamePlayerConnectMessage msg = new GamePlayerConnectMessage("token", VrBattlegrounds.Core.ClientDeviceType.VR, 0, 0);
            Assert.IsFalse(msg.hasCalibration, "Перезапущенный клиент не знает своей калибровки и не должен её публиковать.");

            PlayerCalibration saved = new PlayerCalibration(Floor, EyeHeight, true);
            Assert.AreEqual(saved, PlayersManager.InitialCalibration(msg, new SessionSnapshot { Calibration = saved }),
                "Без данных клиента первичная калибровка — из снимка.");
            Assert.AreEqual(PlayerCalibration.None, PlayersManager.InitialCalibration(msg, null),
                "Ни данных, ни снимка — игрок не калибровался.");
        }
    }

    /// <summary>Нечисловой замер не должен заменять принятую запись и уезжать вместо снимка сервера.</summary>
    public class LocalCalibrationInputTests : CalibrationOwnerFixture
    {
        [TestCase(float.NaN, 1.6f)]
        [TestCase(float.PositiveInfinity, 1.6f)]
        [TestCase(float.NegativeInfinity, 1.6f)]
        [TestCase(0.37f, float.NaN)]
        [TestCase(0.37f, float.PositiveInfinity)]
        [TestCase(0.37f, float.NegativeInfinity)]
        public void Нечисловой_замер_не_портит_память_и_следующее_подключение(float floor, float eyeHeight)
        {
            PlayerCalibration accepted = new PlayerCalibration(Floor, EyeHeight, true);
            LocalPlayerCalibration.Adopt(accepted);
            int submitted = 0;
            System.Action<PlayerCalibration> listener = value => submitted++;
            LocalPlayerCalibration.Submitted += listener;
            try
            {
                LocalPlayerCalibration.Submit(new PlayerCalibration(floor, eyeHeight, false));

                Assert.AreEqual(accepted, LocalPlayerCalibration.Current,
                    "Отклонённый нечисловой замер затёр принятую калибровку в памяти машины.");
                Assert.AreEqual(0, submitted, "Нечисловой замер должен отвергаться до публикации.");
                GamePlayerConnectMessage msg = new GamePlayerConnectMessage("token", VrBattlegrounds.Core.ClientDeviceType.VR, 0, 0);
                Assert.AreEqual(accepted, msg.calibration,
                    "Подключение приносит повреждённый замер вместо принятой калибровки.");
            }
            finally
            {
                LocalPlayerCalibration.Submitted -= listener;
            }
        }
    }

    /// <summary>Ярус B: хост — сервер и свой клиент делят экземпляры, хуки зовутся в сеттере.</summary>
    public class PlayerCalibrationOwnerHostTests : CalibrationOwnerFixture
    {
        protected override bool NeedsLocalClient => true;

        private PlayerSession CreateHostSession()
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>("HostSession");
            NetworkServer.Spawn(session.gameObject, NetworkServer.localConnection);
            PumpNetwork();
            MarkAsLocalPlayer(session);
            return session;
        }

        [Test]
        public void Хост__смена_своего_аватара_ставит_пол_ровно_один_раз()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateHostSession();

            Stub a = CreateAvatar("Host_A", session, CyborgEyes, own: true, owner: NetworkServer.localConnection);
            session.ActiveAvatar = a.Controller;
            OwnAvatarStarted(a);
            CalibrateOwn(session, Floor, EyeHeight);

            Stub b = CreateAvatar("Host_B", session, MefEyes, own: true, owner: NetworkServer.localConnection);
            session.ActiveAvatar = b.Controller;
            DestroyAvatar(a);
            PumpNetwork();
            OwnAvatarStarted(b);

            AssertDerived(b, Floor, EyeHeight, "Хост, новый свой аватар");
            AssertHands(Floor, "Хост, новый свой аватар");
        }

        [Test]
        public void Хост__клиент_без_данных_не_затирает_снимок_калибровки()
        {
            SilenceMirrorNoise();
            PlayerSession session = CreateHostSession();

            // Сервер помнит откалиброванного игрока (снимок отключения), клиент перезапущен — данных нет.
            ServerRestoresFromSnapshot(session, Floor, EyeHeight);
            Assert.IsTrue(session.IsCalibrated, "Контроль: снимок восстановил признак калибровки.");

            session.OnStartClient();
            PumpNetwork(6);

            Assert.IsTrue(session.IsCalibrated,
                "Клиент без данных опубликовал «не откалиброван» поверх снимка: следующая смена карты " +
                "поставит игрока в зону команды.");

            Stub avatar = CreateAvatar("Host_Avatar", session, MefEyes, own: true, owner: NetworkServer.localConnection);
            session.ActiveAvatar = avatar.Controller;
            PumpNetwork();
            OwnAvatarStarted(avatar);

            AssertDerived(avatar, Floor, EyeHeight, "После переподключения из снимка");
            AssertHands(Floor, "После переподключения из снимка");
        }
    }
}

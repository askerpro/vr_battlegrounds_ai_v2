using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Core.StateSync;
using UltimateXR.Devices;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    // Технический адаптер SDK 54: прежние ожидания проверяют устройство конкретного аватара.
    public sealed class CalibrationTrackingStub : UxrControllerTracking
    {
        public override System.Type RelatedControllerInputType => typeof(UxrControllerInput);
        public override string SDKDependency => string.Empty;
    }

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
            Sync = CreateManager<PhysicalSpaceSyncManager>("PhysicalSpaceSyncManager");
            DriverReset();
        }

        [TearDown]
        public void CleanupCalibrationFixture()
        {
            var list = (List<UxrAvatar>)AvatarTypeList.GetValue(null);
            foreach (UxrAvatar avatar in _registered) list.Remove(avatar);
            _registered.Clear();

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
            go.AddComponent<CalibrationTrackingStub>();
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
            float hands = 0f;
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                PlayerSession session = identity.GetComponent<PlayerSession>();
                if (session != null && session.isLocalPlayer && session.ActiveAvatar != null)
                    hands = session.ActiveAvatar.GetComponent<CalibrationTrackingStub>().HeightOffset;
            }
            Assert.AreEqual(floor, hands, Eps,
                $"{context}: смещение рук {hands:F3} м, а пол {floor:F3} м — руки не на контроллерах (B1).");
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
            // Связь сессии, а не глобальный LocalAvatar, определяет применяемое устройство.
            PlayerSession session = stub.Controller.Session;
            if (session != null && session.ActiveAvatar == stub.Controller)
                AvatarCalibrationApplier.For(stub.Uxr).Apply(session.EffectiveCalibration, session.isLocalPlayer);
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

    /// <summary>Ярус B: локальный клиент отправляет команды; доставка ответа управляется PumpNetwork.</summary>
    public class PlayerCalibrationOwnerTests : CalibrationOwnerFixture
    {
        protected override bool NeedsLocalClient => true;

        [Test]
        public void Замер_своего_игрока_отправляется_без_ошибок_клиентского_контекста()
        {
            PlayerSession session = CreateOwnSession();
            Stub avatar = CreateAvatar("ClientContextAvatar", session, CyborgEyes, own: true);
            session.ActiveAvatar = avatar.Controller;
            var errors = new List<string>();
            Application.LogCallback listener = (message, stack, type) =>
            {
                if (type == LogType.Error && message.Contains("CmdRequestCalibration")) errors.Add(message);
            };
            Application.logMessageReceived += listener;
            try
            {
                CalibrateOwn(session, Floor, EyeHeight);
                Assert.IsEmpty(errors, "Замер требует подключённого клиента, а не подавления ошибки Mirror.");
                Assert.AreEqual(new PlayerCalibration(Floor, EyeHeight, true), session.Calibration,
                    "Команда должна доставить замер серверу.");
            }
            finally { Application.logMessageReceived -= listener; }
        }

        private static PlayerCalibration At(Vector3 position) =>
            new PlayerCalibration(Floor, EyeHeight, true).WithPlacement(PlayerPlacement.World(
                position, Quaternion.Euler(0f, 42f, 0f), UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));

        private static void AssertCalibrationPose(PlayerCalibration expected, PlayerCalibration actual, string message = null)
        {
            Assert.AreEqual(expected.FloorOffset, actual.FloorOffset, message);
            Assert.AreEqual(expected.EyeHeight, actual.EyeHeight, message);
            Assert.AreEqual(expected.IsCalibrated, actual.IsCalibrated, message);
            Assert.AreEqual(expected.Placement.Space, actual.Placement.Space, message);
            Assert.AreEqual(expected.Placement.CapturedOnMap, actual.Placement.CapturedOnMap, message);
            Vector3 position = actual.Placement.Position;
            Quaternion rotation = actual.Placement.Rotation;
            foreach (float component in new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w })
                Assert.IsFalse(float.IsNaN(component) || float.IsInfinity(component), "Принятая поза должна быть конечной.");
            Assert.That(Vector3.Distance(expected.Placement.Position, actual.Placement.Position), Is.LessThan(Eps), message);
            // Нормализация может менять последние биты quaternion, сохраняя ту же ориентацию.
            Quaternion normalized = expected.Placement.Rotation.normalized;
            Vector4 reference = new Vector4(normalized.x, normalized.y, normalized.z, normalized.w);
            Vector4 value = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            Assert.That(Mathf.Min(Vector4.Distance(reference, value), Vector4.Distance(-reference, value)), Is.LessThan(.000001f), message);
            Assert.That(Mathf.Abs(Quaternion.Dot(actual.Placement.Rotation, actual.Placement.Rotation) - 1f), Is.LessThan(.000001f), message);
        }

        [Test]
        public void Смена_тела_не_затирает_принятую_якорную_позу_запаздывающим_корнем()
        {
            PlayerSession session = CreateOwnSession();
            var accepted = new PlayerCalibration(Floor, EyeHeight, true).WithPlacement(
                PlayerPlacement.Anchored(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 37f, 0f), "A"));
            session.ServerAcceptCalibration(accepted, PlayerSession.CalibrationOrigin.Connect);
            Assert.IsTrue(session.ServerCapturePlacement(new Vector3(99f, 0f, 99f), Quaternion.identity, "смена тела"));
            Assert.AreEqual(accepted, session.Calibration);
        }

        private sealed class SdkCapture : System.IDisposable
        {
            public readonly UxrManager Manager;
            public int Moves;
            public byte[] Bytes;
            private readonly UxrAvatar _avatar;
            private readonly FieldInfo _singletonField;
            private readonly object _previousSingleton;
            private readonly System.EventHandler<UxrSyncEventArgs> _listener;
            private bool _disposed;

            public SdkCapture(UxrAvatar avatar)
            {
                _avatar = avatar;
                for (System.Type type = typeof(UxrManager); type != null; type = type.BaseType)
                {
                    var field = type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null) { _singletonField = field; break; }
                }
                Assert.IsNotNull(_singletonField, "SDK singleton изменился: обновить харнесс, а не использовать чужой менеджер.");
                _previousSingleton = _singletonField.GetValue(null);
                Manager = new GameObject("T50SdkCaptureManager").AddComponent<UxrManager>();
                _listener = (sender, args) =>
                {
                    if (args is UxrMethodInvokedSyncEventArgs method && method.MethodName == "MoveAvatarRootTo")
                    {
                        Moves++;
                        Bytes = args.SerializeEventBinary(Manager);
                    }
                };
                try
                {
                    _singletonField.SetValue(null, Manager);
                    Manager.RegisterIfNecessary();
                    avatar.RegisterIfNecessary();
                    ((IUxrStateSync)Manager).StateChanged += _listener;
                }
                catch (System.Exception registrationError)
                {
                    try { Dispose(); }
                    catch (System.Exception cleanupError) { throw new System.AggregateException(registrationError, cleanupError); }
                    throw;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { ((IUxrStateSync)Manager).StateChanged -= _listener; }
                finally
                {
                    try
                    {
                        try { if (_avatar != null) _avatar.Unregister(); }
                        finally { Manager.Unregister(); }
                    }
                    finally
                    {
                        try
                        {
                            if (ReferenceEquals(_singletonField.GetValue(null), Manager)) _singletonField.SetValue(null, _previousSingleton);
                        }
                        finally { Object.DestroyImmediate(Manager.gameObject); }
                    }
                }
                Assert.AreSame(_previousSingleton, _singletonField.GetValue(null), "SDK singleton должен пережить тест без подмены.");
            }
        }

        [Test]
        public void Принятая_поза_публикуется_один_раз_после_ответа_сервера()
        {
            PlayerSession session = CreateOwnSession();
            Stub avatar = CreateAvatar("AcceptedPlacement", session, CyborgEyes, true, NetworkServer.localConnection);
            session.ActiveAvatar = avatar.Controller;
            PlayerCalibration original = At(Vector3.zero);
            session.ServerAcceptCalibration(original, PlayerSession.CalibrationOrigin.Connect);
            using (var sdk = new SdkCapture(avatar.Uxr))
            {
                PlayerCalibration measured = At(new Vector3(10f, 0f, 5f));
                session.RequestCalibration(measured);
                Assert.That(Vector3.Distance(avatar.Controller.transform.position, measured.Placement.Position), Is.LessThan(Eps));
                AssertCalibrationPose(original, session.Calibration, "Предсказание не меняет серверный снимок.");
                Assert.AreEqual(0, sdk.Moves, "Предсказание не должно публиковать SDK-событие.");
                PumpNetwork(6);
                AssertCalibrationPose(measured, session.Calibration);
                Assert.AreEqual(1, sdk.Moves, "Принятие должно опубликовать ровно один перенос.");
                InvokePrivateMethod(session, "OnCalibrationAnswered", SentRequest(session), SentRequest(session));
                Assert.AreEqual(1, sdk.Moves, "Повторный ответ не переносит корень ещё раз.");
                AssertCalibrationPose(measured, LocalPlayerCalibration.Current);
            }
        }

        [Test]
        public void Отказ_позы_откатывает_корень_и_кэш_без_SDK_события()
        {
            PlayerSession session = CreateOwnSession();
            Stub avatar = CreateAvatar("RejectedPlacement", session, CyborgEyes, true, NetworkServer.localConnection);
            session.ActiveAvatar = avatar.Controller;
            PlayerCalibration original = At(Vector3.zero);
            session.ServerAcceptCalibration(original, PlayerSession.CalibrationOrigin.Connect);
            using (var sdk = new SdkCapture(avatar.Uxr))
            {
                session.RequestCalibration(At(new Vector3(10f, 0f, 5f)));
                // Доставка ответа управляется тестом; команду до TearDown не прокручиваем.
                ServerRejectsLastRequest(session);
                Assert.That(Vector3.Distance(avatar.Controller.transform.position, original.Placement.Position), Is.LessThan(Eps));
                Assert.That(Quaternion.Angle(avatar.Controller.transform.rotation, original.Placement.Rotation), Is.LessThan(.001f));
                AssertCalibrationPose(original, LocalPlayerCalibration.Current);
                Assert.AreEqual(0, sdk.Moves);
            }
        }

        [Test]
        public void SDK_replay_сохраняет_корень_при_задержке_локальной_позы_камеры()
        {
            PlayerSession session = CreateOwnSession();
            Stub avatar = CreateAvatar("RootReplay", session, CyborgEyes, true, NetworkServer.localConnection);
            Transform camera = avatar.Uxr.CameraComponent.transform;
            camera.localPosition = new Vector3(1f, 1.7f, .4f);
            camera.localRotation = Quaternion.Euler(0f, 23f, 0f);
            using (var sdk = new SdkCapture(avatar.Uxr))
            {
                Vector3 target = new Vector3(10f, 0f, 8f);
                Quaternion yaw = Quaternion.Euler(0f, 37f, 0f);
                sdk.Manager.MoveAvatarRootTo(avatar.Uxr, target, yaw);
                Assert.IsNotNull(sdk.Bytes, "Проверка должна пройти через бинарное SDK-событие.");
                Vector3 laggingHead = new Vector3(.8f, 1.7f, .2f);
                camera.localPosition = laggingHead;
                avatar.Controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var replay = sdk.Manager.ExecuteStateSyncEvent(sdk.Bytes);
                Assert.IsFalse(replay.IsError);
                Assert.That(Vector3.Distance(avatar.Controller.transform.position, target), Is.LessThan(Eps));
                Assert.That(Quaternion.Angle(avatar.Controller.transform.rotation, yaw), Is.LessThan(.001f));
                Assert.AreEqual(laggingHead, camera.localPosition);
                Assert.That(Quaternion.Angle(camera.localRotation, Quaternion.Euler(0f, 23f, 0f)), Is.LessThan(.001f));
            }
        }

        [Test]
        public void Смещение_выключенного_устройства_своего_аватара_не_меняет_чужое()
        {
            PlayerSession own = CreateOwnSession();
            Stub current = CreateAvatar("OwnTracking", own, CyborgEyes, true);
            own.ActiveAvatar = current.Controller;
            var tracking = current.Uxr.GetComponent<CalibrationTrackingStub>();
            tracking.enabled = false;
            PlayerSession remote = CreateRemoteSession();
            Stub other = CreateAvatar("ForeignTracking", remote, MefEyes, false);
            remote.ActiveAvatar = other.Controller;
            var foreign = other.Uxr.GetComponent<CalibrationTrackingStub>();
            foreign.HeightOffset = -.12f;
            CalibrateOwn(own, Floor, EyeHeight);
            CalibrateRemote(remote, -.2f, 1.7f, MefEyes);
            Assert.AreEqual(Floor, tracking.HeightOffset, Eps);
            Assert.AreEqual(-.12f, foreign.HeightOffset, Eps);
            Stub replacement = CreateAvatar("ReplacementTracking", own, HeavyEyes, true);
            own.ActiveAvatar = replacement.Controller;
            Assert.AreEqual(Floor, replacement.Uxr.GetComponent<CalibrationTrackingStub>().HeightOffset, Eps);
            Assert.AreEqual(-.12f, foreign.HeightOffset, Eps);
        }

        [Test]
        public void Архивирование_не_переносит_тело_и_наблюдение_старого_аватара_игнорируется()
        {
            PlayerSession session = CreateOwnSession();
            Stub old = CreateAvatar("OldObservedBody", session, CyborgEyes, true);
            session.ActiveAvatar = old.Controller;
            PlayerCalibration original = At(Vector3.zero);
            session.ServerAcceptCalibration(original, PlayerSession.CalibrationOrigin.Connect);
            PlayerPlacement archived = At(new Vector3(20f, 0f, 4f)).Placement;
            Vector3 bodyPosition = old.Controller.transform.position;
            session.ServerRememberPlacement(archived);
            Assert.AreEqual(bodyPosition, old.Controller.transform.position);
            AssertCalibrationPose(original.WithPlacement(archived), session.Calibration);
            Stub replacement = CreateAvatar("NewObservedBody", session, MefEyes, true);
            session.ActiveAvatar = replacement.Controller;
            var tracker = session.gameObject.AddComponent<PlayerPlacementTracker>();
            old.Controller.transform.position = new Vector3(99f, 0f, 99f);
            tracker.RecordAvatar(old.Uxr);
            AssertCalibrationPose(original.WithPlacement(archived), session.Calibration);
        }

        private PlayerSession CreateOwnSession()
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>("OwnSession");
            NetworkServer.Spawn(session.gameObject, NetworkServer.localConnection);
            PumpNetwork();
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

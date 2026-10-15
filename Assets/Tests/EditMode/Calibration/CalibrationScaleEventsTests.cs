using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Animation.IK;
using UltimateXR.Core;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Tests.Calibration
{
    /// <summary>Контракт событий вокруг принятой пользователем смены масштаба тела.</summary>
    public class CalibrationScaleEventsTests
    {
        private const float EyesBase = 2f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Vector3 NeckBase = new Vector3(0.2f, 0.3f, 0.4f);
        private static readonly Vector3 ForwardBase = new Vector3(0.5f, 0.6f, 0.7f);

        private GameObject _root;
        private Transform _dummy;
        private UxrBodyIK _bodyIK;
        private AvatarCalibrationApplier _applier;
        private HashSet<UxrManager> _managersBefore;
        private bool _ignoredErrorsBefore;

        [SetUp]
        public void Создать_состояние_после_инициализации_SDK()
        {
            _ignoredErrorsBefore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = false;
            _managersBefore = new HashSet<UxrManager>(Resources.FindObjectsOfTypeAll<UxrManager>());
            _root = new GameObject("CalibrationScaleEvents");
            _dummy = new GameObject(AvatarCalibrationApplier.DummyForwardName).transform;
            _dummy.SetParent(_root.transform, false);
            _applier = _root.AddComponent<AvatarCalibrationApplier>();
            _bodyIK = new UxrBodyIK();
            SetField(_bodyIK, "_neckPosRelativeToEyes", NeckBase);
            SetField(_bodyIK, "_avatarForwardPosRelativeToNeck", ForwardBase);

            // EditMode не запускает инициализацию BodyIK. Здесь нужна только уже снятая база,
            // чтобы проверять публичный Apply и наблюдаемое состояние в callbacks без сетевого харнесса.
            SetField(_applier, "_baseCaptured", true);
            SetField(_applier, "_dummyForward", _dummy);
            SetField(_applier, "_eyesBaseHeight", EyesBase);
            SetField(_applier, "_bodyIK", _bodyIK);
            SetField(_applier, "_neckBase", NeckBase);
            SetField(_applier, "_forwardBase", ForwardBase);
        }

        [TearDown]
        public void Удалить_только_созданные_тестом_объекты()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            if (UxrManager.HasInstance && !_managersBefore.Contains(UxrManager.Instance))
                UnityEngine.Object.DestroyImmediate(UxrManager.Instance.gameObject);
            LogAssert.ignoreFailingMessages = _ignoredErrorsBefore;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Callbacks_обрамляют_масштаб_и_BodyIK_в_одном_Apply(bool ownAvatar)
        {
            var snapshots = new List<(string phase, float oldScale, float newScale, float actualScale,
                                      Vector3 neck, Vector3 forward, float applied, int frame)>();
            void Capture(string phase, AvatarCalibrationApplier sender, float oldScale, float newScale)
            {
                Assert.AreSame(_applier, sender);
                snapshots.Add((phase, oldScale, newScale, _dummy.localScale.x,
                    GetField<Vector3>(_bodyIK, "_neckPosRelativeToEyes"),
                    GetField<Vector3>(_bodyIK, "_avatarForwardPosRelativeToNeck"), sender.AppliedScale, Time.frameCount));
            }

            _applier.ScaleChanging += (sender, oldScale, newScale) => Capture("before", sender, oldScale, newScale);
            _applier.ScaleChanged += (sender, oldScale, newScale) => Capture("after", sender, oldScale, newScale);
            int frame = Time.frameCount;
            _applier.Apply(Calibration(1.25f), ownAvatar);

            Assert.AreEqual(2, snapshots.Count);
            Assert.AreEqual("before", snapshots[0].phase);
            Assert.AreEqual("after", snapshots[1].phase);
            foreach (var snapshot in snapshots)
            {
                Assert.AreEqual(1f, snapshot.oldScale);
                Assert.AreEqual(1.25f, snapshot.newScale);
                Assert.AreEqual(frame, snapshot.frame);
            }
            Assert.AreEqual(1f, snapshots[0].actualScale);
            Assert.AreEqual(NeckBase, snapshots[0].neck);
            Assert.AreEqual(ForwardBase, snapshots[0].forward);
            Assert.AreEqual(1f, snapshots[0].applied);
            Assert.AreEqual(1.25f, snapshots[1].actualScale);
            Assert.AreEqual(NeckBase * 1.25f, snapshots[1].neck);
            Assert.AreEqual(ForwardBase * 1.25f, snapshots[1].forward);
            Assert.AreEqual(1.25f, snapshots[1].applied);
        }

        [Test]
        public void Повтор_и_изменение_только_пола_не_посылают_события_масштаба()
        {
            int before = 0, after = 0;
            _applier.ScaleChanging += (_, _, _) => before++;
            _applier.ScaleChanged += (_, _, _) => after++;
            PlayerCalibration calibration = Calibration(1.25f);
            _applier.Apply(calibration, false);
            _applier.Apply(calibration, false);
            _applier.Apply(calibration.WithFloor(0.2f), false);
            Assert.AreEqual(1, before);
            Assert.AreEqual(1, after);
        }

        [TestCase(0.00005f, 0)]
        [TestCase(0.0002f, 1)]
        public void Порог_отсекает_шум_но_не_реальную_смену(float delta, int expected)
        {
            int before = 0, after = 0;
            _applier.ScaleChanging += (_, _, _) => before++;
            _applier.ScaleChanged += (_, _, _) => after++;
            _applier.Apply(Calibration(1f + delta), false);
            Assert.AreEqual(expected, before);
            Assert.AreEqual(expected, after);
        }

        [Test]
        public void Сброс_калибровки_возвращает_базу_через_оба_события()
        {
            _applier.Apply(Calibration(1.25f), false);
            var values = new List<(float oldScale, float newScale)>();
            _applier.ScaleChanging += (_, oldScale, newScale) => values.Add((oldScale, newScale));
            _applier.ScaleChanged += (_, oldScale, newScale) => values.Add((oldScale, newScale));
            _applier.Apply(PlayerCalibration.None, false);
            Assert.AreEqual(2, values.Count);
            foreach (var value in values)
            {
                Assert.AreEqual(1.25f, value.oldScale);
                Assert.AreEqual(1f, value.newScale);
            }
            Assert.AreEqual(Vector3.one, _dummy.localScale);
            Assert.AreEqual(NeckBase, GetField<Vector3>(_bodyIK, "_neckPosRelativeToEyes"));
        }

        [Test]
        public void Старое_значение_берётся_с_тела_а_не_из_предыдущего_результата()
        {
            _applier.Apply(Calibration(1.25f), false);
            _dummy.localScale = Vector3.one * 0.9f;
            float observedOld = 0f;
            int after = 0;
            _applier.ScaleChanging += (_, oldScale, _) => observedOld = oldScale;
            _applier.ScaleChanged += (_, _, _) => after++;
            _applier.Apply(Calibration(1.25f), false);
            Assert.AreEqual(0.9f, observedOld);
            Assert.AreEqual(1, after);
            Assert.AreEqual(1.25f, _dummy.localScale.x);
        }

        [Test]
        public void Исключения_подписчиков_не_прерывают_калибровку_и_остальные_callbacks()
        {
            var calls = new List<string>();
            _applier.ScaleChanging += (_, _, _) => throw new InvalidOperationException("before-marker");
            _applier.ScaleChanging += (_, _, _) => calls.Add("before");
            _applier.ScaleChanged += (_, _, _) => throw new InvalidOperationException("after-marker");
            _applier.ScaleChanged += (_, _, _) => calls.Add("after");
            LogAssert.Expect(LogType.Error, new Regex(@"\[AvatarCalibrationApplier\].*обработчик ScaleChanging.*before-marker"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[AvatarCalibrationApplier\].*обработчик ScaleChanged.*after-marker"));

            Assert.DoesNotThrow(() => _applier.Apply(Calibration(1.25f), false));

            CollectionAssert.AreEqual(new[] { "before", "after" }, calls);
            Assert.AreEqual(1.25f, _dummy.localScale.x);
            Assert.AreEqual(Calibration(1.25f), _applier.Applied);
            Assert.AreEqual(NeckBase * 1.25f, GetField<Vector3>(_bodyIK, "_neckPosRelativeToEyes"));
        }

        [Test]
        public void Отсутствующее_тело_не_объявляет_смену_масштаба()
        {
            SetField(_applier, "_dummyForward", null);
            int calls = 0;
            _applier.ScaleChanging += (_, _, _) => calls++;
            _applier.ScaleChanged += (_, _, _) => calls++;
            _applier.Apply(Calibration(1.25f), false);
            Assert.AreEqual(0, calls);
        }

        private static PlayerCalibration Calibration(float scale) => new PlayerCalibration(0f, EyesBase * scale, false);

        private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static T GetField<T>(object target, string name) => (T)Field(target, name).GetValue(target);

        private static FieldInfo Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, $"Поле SDK/харнесса переименовано: {target.GetType().Name}.{name}");
            return field;
        }
    }
}

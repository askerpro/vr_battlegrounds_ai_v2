using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Анимация стены арсенала: шторка и выдвижная полка.
    ///
    /// Что доказывает. Анимация открытия/закрытия играет, только если поза действительно
    /// меняется, и никогда не прыгает в противоположную позу. Раньше было два дефекта:
    /// <list type="bullet">
    /// <item>пустые <c>Idle_Open</c>/<c>Idle_Closed</c> с Write Defaults показывали позу
    /// префаба (закрытую) — открытая стена после анимации прыгала в закрытую, а закрытие
    /// начиналось с прыжка в открытую;</item>
    /// <item>триггер, поданный в состояние без перехода по нему, залёживался и срабатывал
    /// позже — открытие тут же сменялось закрытием.</item>
    /// </list>
    ///
    /// Работает на настоящих <c>ArsenalWall.controller</c> и <c>Arsenal_Open.anim</c>:
    /// дефекты жили именно в ассетах, двойник их бы не увидел. Аниматор крутится
    /// вручную через <c>Animator.Update</c>.
    /// </summary>
    public class ArsenalAnimatorTests
    {
        private const string ControllerPath = "Assets/Animations/Arsenal/ArsenalWall.controller";
        private const float Step = 1f / 30f;

        /// <summary>Шторка в закрытой и в открытой позе различается больше чем на метр.</summary>
        private const float PoseTolerance = 0.01f;

        private GameObject _root;
        private Animator _unityAnimator;
        private ArsenalAnimator _arsenalAnimator;
        private Transform _shutter;
        private Transform _shelf;

        private float _closedShutterY;
        private float _openShutterY;
        private float _closedShelfZ;
        private float _openShelfZ;

        [SetUp]
        public void SetUp()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"Нет контроллера {ControllerPath}");

            _root = new GameObject("ArsenalAnimatorTest");
            _shutter = new GameObject("Shutter").transform;
            _shutter.SetParent(_root.transform, false);
            _shelf = new GameObject("ShelfRoot").transform;
            _shelf.SetParent(_root.transform, false);

            // Эталонные позы — прямо из клипа: первый и последний кадр.
            AnimationClip clip = controller.animationClips[0];
            clip.SampleAnimation(_root, 0f);
            _closedShutterY = _shutter.localPosition.y;
            _closedShelfZ = _shelf.localPosition.z;
            clip.SampleAnimation(_root, clip.length);
            _openShutterY = _shutter.localPosition.y;
            _openShelfZ = _shelf.localPosition.z;
            Assert.That(Mathf.Abs(_openShutterY - _closedShutterY), Is.GreaterThan(0.5f),
                "Клип не двигает шторку — эталон позы неверен.");

            _unityAnimator = _root.AddComponent<Animator>();
            _unityAnimator.runtimeAnimatorController = controller;
            _arsenalAnimator = _root.AddComponent<ArsenalAnimator>();

            // В EditMode Unity не зовёт Awake.
            Invoke("Awake");

            _unityAnimator.Rebind();
            _unityAnimator.Update(0f);
            Tick(0.1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── Тесты ─────────────────────────────────────────────

        [Test]
        public void Open_ThenIdle_HoldsOpenPose()
        {
            _arsenalAnimator.PlayOpenSequence();
            Tick(3f);

            AssertOpenPose("после открытия и простоя");
            Assert.That(_arsenalAnimator.IsAnimating, Is.False);

            _arsenalAnimator.PlayCloseSequence();
            Tick(3f);

            AssertClosedPose("после закрытия и простоя");
        }

        [Test]
        public void SetOpenImmediate_ShowsOpenPose()
        {
            _arsenalAnimator.SetOpenImmediate();
            Tick(0.5f);

            AssertOpenPose("после мгновенного открытия");
        }

        [Test]
        public void OpenWhileOpen_DoesNotMove_AndCompletesAtOnce()
        {
            _arsenalAnimator.PlayOpenSequence();
            Tick(3f);

            int completed = 0;
            _arsenalAnimator.PlayOpenSequence(() => completed++);

            Assert.That(completed, Is.EqualTo(1), "Колбэк повторного открытия должен прийти сразу.");

            float maxDeviation = TickTrackingDeviation(3f, _openShutterY);
            Assert.That(maxDeviation, Is.LessThan(PoseTolerance),
                "Повторное открытие открытой стены сдвинуло шторку.");
        }

        [Test]
        public void CloseWhileClosed_DoesNotLeaveStaleCommand()
        {
            int completed = 0;
            _arsenalAnimator.PlayCloseSequence(() => completed++);
            Assert.That(completed, Is.EqualTo(1), "Колбэк повторного закрытия должен прийти сразу.");

            float maxDeviation = TickTrackingDeviation(1f, _closedShutterY);
            Assert.That(maxDeviation, Is.LessThan(PoseTolerance), "Закрытие закрытой стены сдвинуло шторку.");

            // Залежавшийся Close раньше срабатывал сразу после открытия.
            _arsenalAnimator.PlayOpenSequence();
            Tick(5f);

            AssertOpenPose("после открытия, которому предшествовало лишнее закрытие");
        }

        [Test]
        public void ReverseMidway_ContinuesFromCurrentPose()
        {
            float normalMaxStep = MaxStepWhileOpening();

            // Заново с закрытой позы: открыть наполовину и развернуть.
            _arsenalAnimator.SetClosedImmediate();
            Tick(0.1f);

            int openCompleted = 0;
            int closeCompleted = 0;

            _arsenalAnimator.PlayOpenSequence(() => openCompleted++);
            Tick(0.6f);

            float midY = _shutter.localPosition.y;
            Assert.That(Mathf.Abs(midY - _closedShutterY), Is.GreaterThan(0.1f), "За 0.6 с стена не поехала.");

            _arsenalAnimator.PlayCloseSequence(() => closeCompleted++);

            float maxStep = 0f;
            float previous = midY;

            for (float t = 0f; t < 3f; t += Step)
            {
                TickOnce();
                float y = _shutter.localPosition.y;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - previous));
                previous = y;
            }

            Assert.That(maxStep, Is.LessThan(normalMaxStep * 2f + PoseTolerance),
                $"Разворот прыгнул: шаг {maxStep:F3} при обычном максимуме {normalMaxStep:F3}.");
            AssertClosedPose("после разворота");
            Assert.That(openCompleted, Is.EqualTo(0), "Прерванное открытие не должно сообщать о завершении.");
            Assert.That(closeCompleted, Is.EqualTo(1));
        }

        [Test]
        public void OpenCompletes_ExactlyOnce()
        {
            int completed = 0;
            _arsenalAnimator.PlayOpenSequence(() => completed++);
            Tick(5f);

            Assert.That(completed, Is.EqualTo(1));
        }

        // ── Помощники ─────────────────────────────────────────

        /// <summary>Наибольший сдвиг шторки за шаг при обычном открытии с нуля.</summary>
        private float MaxStepWhileOpening()
        {
            _arsenalAnimator.PlayOpenSequence();

            float maxStep = 0f;
            float previous = _shutter.localPosition.y;

            for (float t = 0f; t < 3f; t += Step)
            {
                TickOnce();
                float y = _shutter.localPosition.y;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - previous));
                previous = y;
            }

            return maxStep;
        }

        private float TickTrackingDeviation(float seconds, float expectedShutterY)
        {
            float maxDeviation = 0f;

            for (float t = 0f; t < seconds; t += Step)
            {
                TickOnce();
                maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(_shutter.localPosition.y - expectedShutterY));
            }

            return maxDeviation;
        }

        private void Tick(float seconds)
        {
            for (float t = 0f; t < seconds; t += Step)
                TickOnce();
        }

        private void TickOnce()
        {
            _unityAnimator.Update(Step);
            Invoke("Update");
        }

        private void Invoke(string method)
        {
            MethodInfo info = typeof(ArsenalAnimator).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"У ArsenalAnimator нет метода {method}");
            info.Invoke(_arsenalAnimator, null);
        }

        private void AssertOpenPose(string when)
        {
            Assert.That(_shutter.localPosition.y, Is.EqualTo(_openShutterY).Within(PoseTolerance), $"Шторка не открыта {when}.");
            Assert.That(_shelf.localPosition.z, Is.EqualTo(_openShelfZ).Within(PoseTolerance), $"Полка не выдвинута {when}.");
        }

        private void AssertClosedPose(string when)
        {
            Assert.That(_shutter.localPosition.y, Is.EqualTo(_closedShutterY).Within(PoseTolerance), $"Шторка не закрыта {when}.");
            Assert.That(_shelf.localPosition.z, Is.EqualTo(_closedShelfZ).Within(PoseTolerance), $"Полка не задвинута {when}.");
        }
    }
}

using NUnit.Framework;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Player.Input;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>
    /// Бот на вводе устройства (контракт tasks/bots-input/contracts/bot-input-design.md; итерации 1 и 2 приняты
    /// пользователем 2026-10-20/21). Поведение на стенде и в матче проверено вручную (демо Input Smoke, Respawn с ботами);
    /// здесь — правила пилота как у человека (рука не длиннее руки, присед, шаг вбок без доворота курса, предел ходьбы),
    /// повтор Grip без хвата и флаг UltimateXR patch 89 «ввод внешнего аватара на этой машине».
    /// </summary>
    public class BotInputTests
    {
        private GameObject _root;
        private UxrAvatar _avatar;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BotInputTests.Avatar");
            _root.transform.SetPositionAndRotation(new Vector3(2f, 0f, -3f), Quaternion.Euler(0f, 90f, 0f));
            _avatar = _root.AddComponent<UxrAvatar>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void Пилот_ставит_голову_над_корнем_и_курс_по_корню()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);

            Assert.That((pilot.FloorPosition - _root.transform.position).magnitude, Is.LessThan(1e-4f));
            Assert.That(pilot.HeadPosition.y, Is.EqualTo(AvatarPilot.DefaultEyeHeight).Within(1e-4f));
            Assert.That(Mathf.DeltaAngle(pilot.Yaw, 90f), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Пилот_продолжает_голову_при_смене_тела_на_той_же_карте()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);
            pilot.StepTowards(_root.transform.position + Vector3.forward * 3f, AvatarPilot.MaxWalkSpeed, 1f);
            Vector3 head = pilot.HeadPosition;

            pilot.Bind(_avatar, keepHead: true);
            Assert.That((pilot.HeadPosition - head).magnitude, Is.LessThan(1e-4f), "физическая голова не телепортируется");

            pilot.Bind(_avatar, keepHead: false);
            Assert.That((pilot.FloorPosition - _root.transform.position).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void Ходьба_головой_не_быстрее_предела_и_корень_стоит()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);
            Vector3 start = pilot.FloorPosition;

            pilot.StepTowards(start + Vector3.forward * 10f, 5f, 1f);

            Assert.That((pilot.FloorPosition - start).magnitude, Is.EqualTo(AvatarPilot.MaxWalkSpeed).Within(1e-3f));
            Assert.That(_root.transform.position, Is.EqualTo(new Vector3(2f, 0f, -3f)), "корень не пишет никто");
        }

        [Test]
        public void Шаг_вбок_не_доворачивает_курс_взгляд_задаёт_курс()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);
            float yaw = pilot.Yaw;

            pilot.StepTowards(pilot.FloorPosition + Vector3.forward, AvatarPilot.MaxWalkSpeed, 0.1f, 0.01f, turn: false);
            Assert.That(Mathf.DeltaAngle(pilot.Yaw, yaw), Is.EqualTo(0f).Within(1e-3f), "стрейф: курс прежний");

            pilot.StepTowards(pilot.FloorPosition + Vector3.forward, AvatarPilot.MaxWalkSpeed, 0.1f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(pilot.Yaw, yaw)), Is.GreaterThan(1f), "обычный шаг поворачивает к цели");
        }

        [Test]
        public void Присед_не_ниже_колена_и_не_двигает_пол()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);
            Vector3 floor = pilot.FloorPosition;

            for (int i = 0; i < 50; i++) pilot.CrouchTowards(0f, 0.1f);

            Assert.That(pilot.CurrentEyeHeight, Is.EqualTo(AvatarPilot.MinCrouchEyeHeight).Within(1e-4f));
            Assert.That((pilot.FloorPosition - floor).magnitude, Is.LessThan(1e-4f));

            for (int i = 0; i < 50; i++) pilot.StandUp(0.1f);
            Assert.That(pilot.CurrentEyeHeight, Is.EqualTo(pilot.EyeHeight).Within(1e-4f));
        }

        [Test]
        public void Рука_дотягивается_только_на_длину_руки()
        {
            var pilot = new AvatarPilot();
            pilot.Bind(_avatar, keepHead: false);
            Vector3 head = pilot.HeadPosition;

            Assert.That(pilot.CanReach(head + Vector3.forward * (AvatarPilot.MaxReach - 0.01f)), Is.True);
            Assert.That(pilot.CanReach(head + Vector3.forward * (AvatarPilot.MaxReach + 0.01f)), Is.False);
        }

        [Test]
        public void Grip_без_хвата_разжимается_и_сжимается_снова()
        {
            int pressed = 0, released = 0;
            int frames = BlazeInputSource.GripRetryFrames;
            for (int i = 0; i < frames; i++)
                Assert.That(BlazeInputSource.GripWithRetry(true, ref pressed, ref released), Is.True, $"кадр {i}: держать");

            Assert.That(BlazeInputSource.GripWithRetry(true, ref pressed, ref released), Is.False, "нет хвата — разжать");
            for (int i = 0; i < BlazeInputSource.GripReleaseFrames; i++)
                Assert.That(BlazeInputSource.GripWithRetry(true, ref pressed, ref released), Is.False, "разжата");
            Assert.That(BlazeInputSource.GripWithRetry(true, ref pressed, ref released), Is.True, "сжать снова — новый фронт");

            Assert.That(BlazeInputSource.GripWithRetry(false, ref pressed, ref released), Is.False);
            Assert.That(pressed, Is.Zero);
            Assert.That(released, Is.Zero);
        }

        [Test]
        public void Флаг_ввода_на_этой_машине_только_у_внешнего_аватара_с_флагом()
        {
            _avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
            Assert.That(_avatar.HasInputHere, Is.False, "внешний аватар без ввода — как удалённый игрок");

            _avatar.InputDriven = true;
            Assert.That(_avatar.HasInputHere, Is.True, "бот на сервере: ввод на этой машине");

            _avatar.InputDriven = false;
            Assert.That(_avatar.HasInputHere, Is.False);
        }
    }
}

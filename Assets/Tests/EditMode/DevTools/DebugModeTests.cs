using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Режим отладки: жест «оба стика 2 с» (порог, сброс, одно срабатывание на удержание),
    /// служба состояния, серверное правило прав админа, точки телепорта и выбор скина кукол.
    /// </summary>
    public class DebugModeTests
    {
        [TearDown]
        public void TearDown() => DebugMode.Set(false, "тест");

        // ── Жест: удержание обоих стиков ───────────────────────────────────

        /// <summary>Держать с шагом кадра <paramref name="dt"/> от <paramref name="from"/> до <paramref name="to"/>; сколько раз сработало.</summary>
        private static int Hold(DebugHoldGesture g, float from, float to, float dt = 1f / 72f)
        {
            int fired = 0;
            for (float t = from; t <= to + 1e-4f; t += dt)
                if (g.Update(true, t)) fired++;
            return fired;
        }

        [Test]
        public void Hold_TwoSeconds_FiresOnce()
        {
            var g = new DebugHoldGesture();
            Assert.AreEqual(1, Hold(g, 0f, 5f), "Одно удержание — одно переключение, даже если держат дольше.");
        }

        [Test]
        public void Hold_ShorterThanTwoSeconds_DoesNotFire()
        {
            var g = new DebugHoldGesture();
            Assert.AreEqual(0, Hold(g, 0f, DebugHoldGesture.DefaultHoldSeconds - 0.1f));
        }

        [Test]
        public void Hold_ReleaseResetsTimer()
        {
            var g = new DebugHoldGesture();
            Assert.AreEqual(0, Hold(g, 0f, 1.5f));
            Assert.IsFalse(g.Update(false, 1.6f));
            Assert.AreEqual(0, Hold(g, 1.7f, 3.5f), "После отпускания отсчёт с нуля: 1,8 с нового удержания мало.");
            Assert.AreEqual(1, Hold(g, 3.5f, 3.8f));
        }

        [Test]
        public void Hold_ReleaseAndHoldAgain_FiresAgain()
        {
            var g = new DebugHoldGesture();
            Assert.AreEqual(1, Hold(g, 0f, 2.5f));
            g.Update(false, 2.6f);
            Assert.AreEqual(1, Hold(g, 3f, 5.5f), "Повторное удержание — повторное переключение (выключить режим).");
        }

        // ── Служба состояния ────────────────────────────────────────────────

        [Test]
        public void DebugMode_RaisesChangedOnlyOnChange()
        {
            var seen = new List<bool>();
            System.Action<bool> handler = seen.Add;
            DebugMode.Changed += handler;
            try
            {
                DebugMode.Set(false, "тест");
                DebugMode.Toggle("тест");
                DebugMode.Set(true, "тест");
                DebugMode.Toggle("тест");
            }
            finally
            {
                DebugMode.Changed -= handler;
            }

            CollectionAssert.AreEqual(new[] { true, false }, seen);
            Assert.IsFalse(DebugMode.Enabled);
        }

        // ── Права админа на сервере ─────────────────────────────────────────

        [Test]
        public void ServerAllows_DevelopmentOrFlag()
        {
            Assert.IsTrue(DebugAdminPolicy.ServerAllows(true, new string[0]));
            Assert.IsFalse(DebugAdminPolicy.ServerAllows(false, new[] { "server.exe", "-logFile", "x.log" }));
            Assert.IsTrue(DebugAdminPolicy.ServerAllows(false, new[] { "server.exe", DebugAdminPolicy.CommandLineFlag }));
            Assert.IsFalse(DebugAdminPolicy.ServerAllows(false, null));
        }

        [Test]
        public void Decide_GrantsOnlyWhenServerAllows()
        {
            Assert.AreEqual(DebugAdminDecision.Grant, DebugAdminPolicy.Decide(true, false, false, true));
            Assert.AreEqual(DebugAdminDecision.Rejected, DebugAdminPolicy.Decide(true, false, false, false), "Прод без флага — права не выдаются.");
            Assert.AreEqual(DebugAdminDecision.AlreadyAdmin, DebugAdminPolicy.Decide(true, true, false, false), "Хост — админ и так.");
        }

        [Test]
        public void Decide_RevokesOnlyWhatDebugGranted()
        {
            Assert.AreEqual(DebugAdminDecision.Revoke, DebugAdminPolicy.Decide(false, true, true, true));
            Assert.AreEqual(DebugAdminDecision.Nothing, DebugAdminPolicy.Decide(false, true, false, true), "Хост выключением режима прав не теряет.");
        }

        // ── Телепорт ────────────────────────────────────────────────────────

        [Test]
        public void ArsenalStandPoint_InFrontOfShelves_FacingWall()
        {
            DebugTeleportTargets.ArsenalStandPoint(new Vector3(-1.64f, 0f, -0.4f), Vector3.right, out Vector3 pos, out Quaternion rot);
            Assert.AreEqual(-1.64f + DebugTeleportTargets.ArsenalStandDistance, pos.x, 1e-4f);
            Assert.AreEqual(0f, pos.y, 1e-4f, "Высота — пол стены.");
            Assert.That(Vector3.Dot(rot * Vector3.forward, Vector3.left), Is.GreaterThan(0.999f), "Смотрит на стену.");
        }

        [Test]
        public void ComparePlace_OrderIndependentOfInput()
        {
            var a = new List<Vector3> { new Vector3(1.64f, 0, 0.41f), new Vector3(-0.41f, 0, 1.64f), new Vector3(0.4f, 0, -1.64f), new Vector3(-1.64f, 0, -0.4f) };
            var b = new List<Vector3>(a);
            b.Reverse();
            a.Sort(DebugTeleportTargets.ComparePlace);
            b.Sort(DebugTeleportTargets.ComparePlace);
            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(-1.64f, a[0].x, 1e-4f);
        }

        [Test]
        public void ComparePlace_IgnoresSubCentimetreNoise()
        {
            Assert.AreEqual(0, DebugTeleportTargets.ComparePlace(new Vector3(1f, 0f, 2f), new Vector3(1.0001f, 0f, 2.0001f)));
        }

        // ── Скин кукол на планшете ──────────────────────────────────────────

        [Test]
        public void CycleSkin_GoesThroughMixedAndEverySkin()
        {
            int skin = StressTestLayout.MixedSkins;
            var seen = new List<int>();
            for (int i = 0; i < 4; i++) { skin = MenuPerfTests.CycleSkin(skin, 3, +1); seen.Add(skin); }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, StressTestLayout.MixedSkins }, seen);

            Assert.AreEqual(2, MenuPerfTests.CycleSkin(StressTestLayout.MixedSkins, 3, -1));
            Assert.AreEqual(StressTestLayout.MixedSkins, MenuPerfTests.CycleSkin(5, 0, +1), "Скинов нет — вперемешку.");
        }
    }
}

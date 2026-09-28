using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Расстановка кукол стресс-теста: выбор скина, ряды, кольцо «по карте», протокол
    /// скинов в отчёте и перенос новых полей конфига через сетевое сообщение.
    /// </summary>
    public class StressTestLayoutTests
    {
        // ── Скин ────────────────────────────────────────────────────────────

        [Test]
        public void SkinIndex_Mixed_CyclesAllPrefabs()
        {
            int[] got = new int[7];
            for (int i = 0; i < got.Length; i++) got[i] = StressTestLayout.SkinIndex(3, StressTestLayout.MixedSkins, i);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }, got);
        }

        [Test]
        public void SkinIndex_SingleSkin_AlwaysThatPrefab()
        {
            for (int i = 0; i < 9; i++) Assert.AreEqual(2, StressTestLayout.SkinIndex(3, 2, i));
            Assert.IsTrue(StressTestLayout.IsSingleSkin(3, 2));
        }

        [Test]
        public void SkinIndex_OutOfRange_FallsBackToMixed()
        {
            Assert.IsFalse(StressTestLayout.IsSingleSkin(3, 3));
            Assert.AreEqual(1, StressTestLayout.SkinIndex(3, 3, 4));
            Assert.AreEqual(1, StressTestLayout.SkinIndex(3, -7, 1));
        }

        [Test]
        public void SkinIndex_NoPrefabs_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, StressTestLayout.SkinIndex(0, StressTestLayout.MixedSkins, 0));
            Assert.AreEqual(-1, StressTestLayout.SkinIndex(0, 0, 0));
        }

        // ── Ряды ────────────────────────────────────────────────────────────

        [Test]
        public void RowOffset_NineByFive_CentersRowsInFront()
        {
            // 5 в первом ряду, 4 во втором — оба центрированы по X.
            Vector3 first = StressTestLayout.RowOffset(0, 9, 5, 1.2f, 2.5f);
            Vector3 fifth = StressTestLayout.RowOffset(4, 9, 5, 1.2f, 2.5f);
            Assert.AreEqual(-2.4f, first.x, 1e-4f);
            Assert.AreEqual( 2.4f, fifth.x, 1e-4f);
            Assert.AreEqual( 2.5f, first.z, 1e-4f);
            Assert.AreEqual( 0f,   first.y, 1e-4f);

            Vector3 secondFirst = StressTestLayout.RowOffset(5, 9, 5, 1.2f, 2.5f);
            Vector3 secondLast  = StressTestLayout.RowOffset(8, 9, 5, 1.2f, 2.5f);
            Assert.AreEqual(-1.8f, secondFirst.x, 1e-4f);
            Assert.AreEqual( 1.8f, secondLast.x,  1e-4f);
            Assert.AreEqual( 3.7f, secondFirst.z, 1e-4f);
        }

        // ── Кольцо «по карте» ───────────────────────────────────────────────

        [Test]
        public void RingPlacement_RadiusWithinRange_OneSectorPerPuppet()
        {
            const int count = 9;
            var rng = new System.Random(12345);

            for (int i = 0; i < count; i++)
            {
                StressTestLayout.RingPlacement(i, count, 8f, 15f, 0f, rng, out Vector3 offset, out float yaw);

                Assert.AreEqual(0f, offset.y, 1e-5f);
                float r = new Vector2(offset.x, offset.z).magnitude;
                Assert.That(r, Is.InRange(8f - 1e-3f, 15f + 1e-3f), $"кукла {i}: радиус {r}");
                Assert.That(yaw, Is.InRange(0f, 360f));

                // Угол от +Z по часовой (как Quaternion.Euler(0, a, 0) * forward) — в своём секторе.
                float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, 360f);
                Assert.That(angle, Is.InRange(i * 40f - 1e-2f, (i + 1) * 40f + 1e-2f), $"кукла {i}: угол {angle}");
            }
        }

        [Test]
        public void RingPlacement_NineInRing_OneToFourInHundredDegreeView_ForAnySeedAndHeading()
        {
            const int count = 9;
            for (int seed = 0; seed < 200; seed++)
            {
                var rng = new System.Random(seed);
                float phase = (float)rng.NextDouble() * 360f;
                var angles = new float[count];
                for (int i = 0; i < count; i++)
                {
                    StressTestLayout.RingPlacement(i, count, 8f, 15f, phase, rng, out Vector3 offset, out _);
                    angles[i] = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                }

                for (int heading = 0; heading < 360; heading += 15)
                {
                    int inView = 0;
                    foreach (float a in angles)
                    {
                        if (Mathf.Abs(Mathf.DeltaAngle(heading, a)) <= 50f) inView++;
                    }
                    Assert.That(inView, Is.InRange(1, 4), $"seed {seed}, взгляд {heading}°: в кадре {inView}");
                }
            }
        }

        [Test]
        public void FallbackRadii_ShrinksTowardsMinimum_FirstIsOriginal()
        {
            var buffer = new float[8];
            int n = StressTestLayout.FallbackRadii(12f, 2f, buffer);

            Assert.AreEqual(12f, buffer[0]);
            Assert.GreaterOrEqual(n, 2);
            for (int i = 1; i < n; i++)
            {
                Assert.Less(buffer[i], buffer[i - 1]);
                Assert.GreaterOrEqual(buffer[i], 2f);
            }
        }

        [Test]
        public void FallbackRadii_BelowMinimum_OnlyOriginal()
        {
            var buffer = new float[8];
            Assert.AreEqual(1, StressTestLayout.FallbackRadii(1f, 2f, buffer));
            Assert.AreEqual(1f, buffer[0]);
        }

        // ── Отчёт и сообщение ───────────────────────────────────────────────

        [Test]
        public void Report_AddSkins_AccumulatesDistinctInOrder()
        {
            var report = new PerfRunReport();
            report.AddSkins("MEF");
            report.AddSkins("Heavy, MEF");
            report.AddSkins(null);
            report.AddSkins("");
            Assert.AreEqual("MEF, Heavy", report.puppetSkins);
        }

        [Test]
        public void RequestMessage_RoundTrip_CarriesSkinAndRunType()
        {
            var config = new StressTestConfig { puppetSkin = 2, perSkinPhases = true, mapSpreadPhase = false, phaseSeconds = 12f };
            StressTestConfig back = StressTestRequestMessage.Start(config).ToConfig();

            Assert.AreEqual(2, back.puppetSkin);
            Assert.IsTrue(back.perSkinPhases);
            Assert.IsFalse(back.mapSpreadPhase);
            Assert.AreEqual(12f, back.phaseSeconds);
        }

        [Test]
        public void DefaultConfig_IsMixedStandardRunWithMapPhase()
        {
            var config = new StressTestConfig();
            Assert.AreEqual(StressTestLayout.MixedSkins, config.puppetSkin);
            Assert.IsFalse(config.perSkinPhases);
            Assert.IsTrue(config.mapSpreadPhase);
        }
    }
}

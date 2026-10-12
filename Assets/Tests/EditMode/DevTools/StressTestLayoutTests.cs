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

        // ── Раскладка «арена» ───────────────────────────────────────────────

        private static readonly Rect LobbyFloor = Rect.MinMaxRect(-7.75f, -9.48f, 7.75f, 9.48f);
        private static readonly Rect Station = Rect.MinMaxRect(-1.5f, 7.48f, 1.5f, 9.48f);

        private static System.Collections.Generic.List<StressTestLayout.ArenaSpot> Arena(
            int count, int seed, Rect floor, params Rect[] exclusions)
        {
            var spots = new System.Collections.Generic.List<StressTestLayout.ArenaSpot>();
            int placed = StressTestLayout.ArenaPlacement(floor, StressTestLayout.ArenaWallMargin, exclusions,
                StressTestLayout.ArenaExclusionMargin, count, 1.2f, seed, spots);
            Assert.AreEqual(placed, spots.Count);
            return spots;
        }

        [Test]
        public void Arena_Lobby_AllNinePlaced_InsideWallsOutsideStation_Spaced()
        {
            var spots = Arena(9, StressTestLayout.ArenaDefaultSeed + 9, LobbyFloor, Station);
            Assert.AreEqual(9, spots.Count);

            float m = StressTestLayout.ArenaWallMargin;
            float e = StressTestLayout.ArenaExclusionMargin;
            Rect station = Rect.MinMaxRect(Station.xMin - e, Station.yMin - e, Station.xMax + e, Station.yMax + e);
            for (int i = 0; i < spots.Count; i++)
            {
                Vector2 p = spots[i].Position;
                Assert.GreaterOrEqual(p.x, LobbyFloor.xMin + m - 1e-3f, "отступ от западной стены");
                Assert.LessOrEqual(p.x, LobbyFloor.xMax - m + 1e-3f, "отступ от восточной стены");
                Assert.GreaterOrEqual(p.y, LobbyFloor.yMin + m - 1e-3f, "отступ от южной стены");
                Assert.LessOrEqual(p.y, LobbyFloor.yMax - m + 1e-3f, "отступ от северной стены");
                Assert.IsFalse(station.Contains(p), "кукла в запретной зоне станции: " + i);
                for (int j = i + 1; j < spots.Count; j++)
                    Assert.GreaterOrEqual(Vector2.Distance(p, spots[j].Position), 1.2f - 1e-3f, "шаг между куклами");
            }
        }

        [Test]
        public void Arena_SameSeed_SameLayout_OtherSeed_Differs()
        {
            var a = Arena(9, 42, LobbyFloor, Station);
            var b = Arena(9, 42, LobbyFloor, Station);
            var c = Arena(9, 43, LobbyFloor, Station);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Position, b[i].Position);
                Assert.AreEqual(a[i].YawDeg, b[i].YawDeg);
            }
            bool differs = false;
            for (int i = 0; i < a.Count; i++) differs |= a[i].Position != c[i].Position;
            Assert.IsTrue(differs, "другой seed — другая раскладка");
        }

        [Test]
        public void Arena_FacingVaries()
        {
            var spots = Arena(9, StressTestLayout.ArenaDefaultSeed + 9, LobbyFloor, Station);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var s in spots) { min = Mathf.Min(min, s.YawDeg); max = Mathf.Max(max, s.YawDeg); }
            Assert.Greater(max - min, 90f, "куклы смотрят в разные стороны");
        }

        [Test]
        public void Arena_SmallFloor_PlacesFewerWithoutOverlap()
        {
            var spots = Arena(9, 7, Rect.MinMaxRect(0f, 0f, 3f, 3f));
            Assert.Less(spots.Count, 9, "на тесном полу недостача, а не наложение");
            for (int i = 0; i < spots.Count; i++)
                for (int j = i + 1; j < spots.Count; j++)
                    Assert.GreaterOrEqual(Vector2.Distance(spots[i].Position, spots[j].Position), 1.2f - 1e-3f);

            Assert.AreEqual(0, Arena(9, 7, Rect.MinMaxRect(0f, 0f, 1.5f, 1.5f)).Count, "пол меньше двойного отступа");
        }

        [Test]
        public void Arena_IsFree_Respected()
        {
            var spots = new System.Collections.Generic.List<StressTestLayout.ArenaSpot>();
            StressTestLayout.ArenaPlacement(LobbyFloor, StressTestLayout.ArenaWallMargin, new Rect[0],
                StressTestLayout.ArenaExclusionMargin, 9, 1.2f, 5, spots, p => p.x >= 0f);
            Assert.Greater(spots.Count, 0);
            foreach (var s in spots) Assert.GreaterOrEqual(s.Position.x, 0f);
        }

        [Test]
        public void Arena_ZeroCount_Empty()
        {
            Assert.AreEqual(0, Arena(0, 1, LobbyFloor).Count);
        }
    }
}

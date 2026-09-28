using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Чистая логика стресс-теста: перцентили, решение «писать ли строку в лог»,
    /// отбор всплесков и буфер задержки кукол. Всё, что на шлеме не перепроверить
    /// глазами, — здесь.
    /// </summary>
    public class PerfLogicTests
    {
        // ── Перцентили ──────────────────────────────────────────────────────

        [Test]
        public void Summarize_NearestRank_ReturnsRealSamples()
        {
            var samples = new float[100];
            for (int i = 0; i < 100; i++) samples[i] = i + 1; // 1..100

            PerfSummary s = PerfStats.Summarize(samples);

            Assert.AreEqual(100, s.count);
            Assert.AreEqual(50f, s.p50);
            Assert.AreEqual(95f, s.p95);
            Assert.AreEqual(99f, s.p99);
            Assert.AreEqual(100f, s.max);
            Assert.AreEqual(50.5f, s.mean, 1e-4f);
        }

        [Test]
        public void Summarize_UnsortedInput_IsSortedInternally()
        {
            PerfSummary s = PerfStats.Summarize(new[] { 30f, 10f, 20f });
            Assert.AreEqual(20f, s.p50);
            Assert.AreEqual(30f, s.max);
        }

        [Test]
        public void Summarize_Empty_IsZero()
        {
            Assert.AreEqual(0, PerfStats.Summarize(new float[0]).count);
        }

        [Test]
        public void OverBudgetShare_CountsStrictlyLonger()
        {
            float share = PerfStats.OverBudgetShare(new[] { 10f, 13.9f, 14f, 30f }, 13.9f);
            Assert.AreEqual(0.5f, share, 1e-4f);
        }

        // ── Всплески ────────────────────────────────────────────────────────

        [Test]
        public void IsSpike_SingleLongFrame_OnStableLevel()
        {
            Assert.IsTrue(PerfStats.IsSpike(40f, 13.9f, 13.9f, 1.5f, 2f));
        }

        [Test]
        public void IsSpike_SustainedDrop_IsNotSpike()
        {
            // Шлем стабильно на 45 FPS: кадр 22 мс при уровне 22 мс — новый уровень, не всплеск.
            Assert.IsFalse(PerfStats.IsSpike(23f, 22f, 13.9f, 1.5f, 2f));
        }

        [Test]
        public void IsSpike_LevelUnknown_IsNotSpike()
        {
            // Разгон: первое окно ещё не закрыто. Раньше каждый кадр дольше бюджета
            // в разгоне шёл в лог всплеском — прогон в редакторе дал их 17 подряд.
            Assert.IsFalse(PerfStats.IsSpike(40f, 0f, 13.9f, 1.5f, 2f));
        }

        [Test]
        public void IsSpike_SlightlyOverBudget_IsNotSpike()
        {
            Assert.IsFalse(PerfStats.IsSpike(16f, 7f, 13.9f, 1.5f, 2f));
        }

        // ── Фильтр изменений ────────────────────────────────────────────────

        private static PerfChangeFilter TimeFilter() =>
            new PerfChangeFilter(new[] { new ChangeThreshold(1f, 0.15f) });

        [Test]
        public void Filter_FirstWindow_AlwaysWritten()
        {
            PerfChangeFilter filter = TimeFilter();
            var changed = new bool[1];
            Assert.IsTrue(filter.Evaluate(new[] { 13f }, changed));
        }

        [Test]
        public void Filter_SmallJitter_NotWritten()
        {
            PerfChangeFilter filter = TimeFilter();
            var changed = new bool[1];
            filter.Evaluate(new[] { 13f }, changed);
            filter.Commit(new[] { 13f });

            Assert.IsFalse(filter.Evaluate(new[] { 13.8f }, changed), "0,8 мс — ниже абсолютного порога");
            Assert.IsFalse(filter.Evaluate(new[] { 14.5f }, changed), "+1,5 мс от 13 — ниже 15%");
        }

        [Test]
        public void Filter_SignificantChange_Written()
        {
            PerfChangeFilter filter = TimeFilter();
            var changed = new bool[1];
            filter.Evaluate(new[] { 13f }, changed);
            filter.Commit(new[] { 13f });

            Assert.IsTrue(filter.Evaluate(new[] { 18f }, changed));
            Assert.IsTrue(changed[0]);
        }

        [Test]
        public void Filter_SlowDrift_ComparedWithLastWritten_EventuallyWritten()
        {
            // Дрейф по 0,5 мс за окно: каждое окно от предыдущего — шум, но от последней
            // записанной строки уход копится и в итоге обязан попасть в лог.
            PerfChangeFilter filter = TimeFilter();
            var changed = new bool[1];
            filter.Evaluate(new[] { 10f }, changed);
            filter.Commit(new[] { 10f });

            bool written = false;
            for (float v = 10.5f; v <= 13f && !written; v += 0.5f)
            {
                written = filter.Evaluate(new[] { v }, changed);
            }
            Assert.IsTrue(written);
        }

        [Test]
        public void Filter_Reset_NextWindowWrittenAgain()
        {
            PerfChangeFilter filter = TimeFilter();
            var changed = new bool[1];
            filter.Evaluate(new[] { 13f }, changed);
            filter.Commit(new[] { 13f });
            filter.Reset();

            Assert.IsTrue(filter.Evaluate(new[] { 13f }, changed), "первая строка новой фазы — точка отсчёта");
        }

        // ── Буфер задержки ──────────────────────────────────────────────────

        private static AvatarPoseSample At(float x) => new AvatarPoseSample { headPosition = new Vector3(x, 0f, 0f) };

        [Test]
        public void Buffer_ReturnsLatestNotAfterRequestedTime()
        {
            var buffer = new PoseDelayBuffer(8);
            buffer.Add(1.0f, At(1));
            buffer.Add(1.1f, At(2));
            buffer.Add(1.2f, At(3));

            Assert.IsTrue(buffer.TrySample(1.15f, out AvatarPoseSample s));
            Assert.AreEqual(2f, s.headPosition.x);
        }

        [Test]
        public void Buffer_OlderThanBuffer_ReturnsOldest()
        {
            var buffer = new PoseDelayBuffer(8);
            buffer.Add(5.0f, At(1));
            buffer.Add(5.1f, At(2));

            Assert.IsTrue(buffer.TrySample(0f, out AvatarPoseSample s));
            Assert.AreEqual(1f, s.headPosition.x);
        }

        [Test]
        public void Buffer_Wraps_OldestIsOverwritten()
        {
            var buffer = new PoseDelayBuffer(3);
            for (int i = 0; i < 5; i++) buffer.Add(i, At(i)); // остаются 2, 3, 4

            Assert.AreEqual(3, buffer.Count);
            Assert.IsTrue(buffer.TrySample(-1f, out AvatarPoseSample oldest));
            Assert.AreEqual(2f, oldest.headPosition.x);
            Assert.IsTrue(buffer.TrySample(3.5f, out AvatarPoseSample mid));
            Assert.AreEqual(3f, mid.headPosition.x);
        }

        [Test]
        public void Buffer_Empty_ReturnsFalse()
        {
            Assert.IsFalse(new PoseDelayBuffer(4).TrySample(1f, out _));
        }
    }
}

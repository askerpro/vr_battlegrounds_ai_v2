using System;
using System.Collections.Generic;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Сводка по ряду замеров одной величины за фазу стресс-теста.
    /// </summary>
    [Serializable]
    public struct PerfSummary
    {
        public int   count;
        public float mean;
        public float p50;
        public float p95;
        public float p99;
        public float max;

        public override string ToString()
        {
            return count == 0
                ? "нет данных"
                : FormattableString.Invariant($"p50 {p50:F1} · p95 {p95:F1} · p99 {p99:F1} · max {max:F1} · mean {mean:F1}");
        }
    }

    /// <summary>
    /// Чистая статистика замеров: перцентили, доля кадров вне бюджета, отбор всплесков.
    /// Без Unity — проверяется юнит-тестом.
    /// </summary>
    public static class PerfStats
    {
        /// <summary>
        /// Перцентили методом ближайшего ранга: значение, которое реально было в ряду,
        /// а не интерполяция между двумя кадрами — так «p99 = 31 мс» означает
        /// настоящий кадр, который можно найти в CSV.
        /// </summary>
        public static PerfSummary Summarize(IReadOnlyList<float> samples)
        {
            var summary = new PerfSummary();
            if (samples == null || samples.Count == 0) return summary;

            var sorted = new float[samples.Count];
            double sum = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                sorted[i] = samples[i];
                sum += samples[i];
            }
            Array.Sort(sorted);

            summary.count = sorted.Length;
            summary.mean  = (float)(sum / sorted.Length);
            summary.p50   = Percentile(sorted, 0.50f);
            summary.p95   = Percentile(sorted, 0.95f);
            summary.p99   = Percentile(sorted, 0.99f);
            summary.max   = sorted[sorted.Length - 1];
            return summary;
        }

        /// <summary>Перцентиль по уже отсортированному ряду, ближайший ранг.</summary>
        public static float Percentile(float[] sorted, float fraction)
        {
            if (sorted == null || sorted.Length == 0) return 0f;

            // Поправка на float: 0.99f чуть больше 0,99, и без неё ранг p99 из 100 кадров
            // округлялся бы вверх до сотого — перцентиль совпадал бы с максимумом.
            int rank = (int)Math.Ceiling(fraction * (double)sorted.Length - 1e-4);
            int index = Math.Min(Math.Max(rank - 1, 0), sorted.Length - 1);
            return sorted[index];
        }

        /// <summary>Доля кадров (0..1), не уложившихся в бюджет.</summary>
        public static float OverBudgetShare(IReadOnlyList<float> frameMs, float budgetMs)
        {
            if (frameMs == null || frameMs.Count == 0) return 0f;

            int over = 0;
            for (int i = 0; i < frameMs.Count; i++)
            {
                if (frameMs[i] > budgetMs) over++;
            }
            return (float)over / frameMs.Count;
        }

        /// <summary>
        /// Всплеск — одиночный кадр, выбившийся из текущего уровня: длиннее бюджета в
        /// <paramref name="budgetFactor"/> раз <b>и</b> длиннее текущего уровня
        /// (<paramref name="levelMs"/>, среднее последнего окна) в <paramref name="levelFactor"/> раз.
        ///
        /// <para>
        /// Второе условие — против потопа: если шлем стабильно просел до 45 FPS, каждый кадр
        /// длиннее бюджета, но это не всплеск, а новый уровень, и его уже записало окно.
        /// </para>
        /// </summary>
        public static bool IsSpike(float frameMs, float levelMs, float budgetMs, float budgetFactor, float levelFactor)
        {
            // Уровень ещё не известен (первое окно не закрыто) — сравнивать не с чем.
            // Без этого весь разгон, где кадр дольше бюджета, писался бы всплеском на каждый кадр.
            if (budgetMs <= 0f || levelMs <= 0f) return false;
            if (frameMs <= budgetMs * budgetFactor) return false;
            return frameMs > levelMs * levelFactor;
        }

        /// <summary>Бюджет кадра в миллисекундах для частоты обновления дисплея.</summary>
        public static float BudgetMs(float refreshRateHz)
        {
            return refreshRateHz > 1f ? 1000f / refreshRateHz : 1000f / 72f;
        }
    }
}

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Решения <see cref="PerformanceLevelInstaller"/> без обращения к рантайму Oculus — чтобы их
    /// доставал юнит-тест.
    /// </summary>
    public static class PerformanceLevelPolicy
    {
        /// <summary>Допустимый диапазон подсказки Oculus (<c>Performance.TrySetCPULevel</c>).</summary>
        public const int MinLevel = 0;
        public const int MaxLevel = 4;

        public static int Clamp(int level)
        {
            if (level < MinLevel) return MinLevel;
            if (level > MaxLevel) return MaxLevel;
            return level;
        }

        /// <summary>
        /// Переставлять ли подсказку. Уровень — только подсказка: система снижает его при
        /// перегреве и может сбросить после паузы приложения или перезапуска дисплея. Если
        /// система держит уровень ниже запрошенного, просьбу повторяем, но не чаще
        /// <paramref name="retryIntervalSeconds"/>, чтобы не спорить с термоуправлением каждый кадр.
        /// </summary>
        /// <param name="displayRestarted">XR-дисплей новый или перезапущен — ставить заново сразу.</param>
        /// <param name="reportedLevel">Уровень, который отдаёт система; &lt; 0 — неизвестен.</param>
        public static bool ShouldApply(bool displayRestarted, int targetLevel, int reportedLevel,
            float now, float lastApplyTime, float retryIntervalSeconds)
        {
            if (displayRestarted) return true;
            if (reportedLevel < 0 || reportedLevel >= targetLevel) return false;
            return now - lastApplyTime >= retryIntervalSeconds;
        }
    }
}

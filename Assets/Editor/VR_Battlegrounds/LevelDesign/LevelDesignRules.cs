namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Пороги правил левел-дизайна в одном месте. Номера — из <c>Docs/level-design-principles.md</c>;
    /// значения с пометкой «[адаптация]» в документе ещё не проверены плейтестом — менять
    /// здесь и там одновременно.
    /// </summary>
    public static class LevelDesignRules
    {
        /// <summary>Выше этого препятствие не перешагнуть — клетка непроходима.</summary>
        public const float StepHeight = 0.3f;

        /// <summary>Верх полосы тела: твёрдое между <see cref="StepHeight"/> и этим — не пройти; выше — перемычка двери.</summary>
        public const float BodyTop = 1.9f;

        /// <summary>Глаза стоящего игрока над полом.</summary>
        public const float EyeHeight = 1.7f;

        /// <summary>Глаза присевшего игрока: окно на уровне груди работает только для него.</summary>
        public const float CrouchEyeHeight = 1.1f;

        /// <summary>Позы, в которых игроки могут увидеть друг друга: контакт есть, если видят хоть в одной паре.</summary>
        public static readonly float[] EyeHeights = { EyeHeight, CrouchEyeHeight };

        /// <summary>LD-23: проход для одного игрока не уже, м.</summary>
        public const float MinPassageWidth = 1.0f;

        /// <summary>Уже этого не протиснуться — для ходьбы это стена, а не проход, м.</summary>
        public const float SqueezeWidth = 0.5f;

        /// <summary>LD-25: недостижимый участок меньше этого — шум сетки, а не карман, м².</summary>
        public const float MinPocketArea = 0.5f;

        /// <summary>LD-20: допуск на высоту класса укрытия, м.</summary>
        public const float CoverTolerance = 0.02f;

        /// <summary>LD-20: три класса высоты укрытий — суффикс имени префаба и диапазон, м.</summary>
        public static readonly CoverClass[] CoverClasses =
        {
            new CoverClass("Low", 1.0f, 1.2f),
            new CoverClass("Mid", 1.5f, 1.6f),
            new CoverClass("Tall", 2.0f, 2.5f),
        };

        /// <summary>§1: граница ближнего и среднего боя, м.</summary>
        public const float CloseRange = 4f;

        /// <summary>§1: граница среднего и дальнего боя, м.</summary>
        public const float LongRange = 9f;

        /// <summary>Шаг выборки точек для видимости и обходов, м.</summary>
        public const float SampleStep = 0.5f;

        public readonly struct CoverClass
        {
            public readonly string Suffix;
            public readonly float Min;
            public readonly float Max;

            public CoverClass(string suffix, float min, float max)
            {
                Suffix = suffix;
                Min = min;
                Max = max;
            }
        }
    }
}

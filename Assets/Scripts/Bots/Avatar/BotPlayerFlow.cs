using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Bots
{
    /// <summary>Исход решения «жать курок» по запросу Blaze (<see cref="BotGunner.DecideTriggerPress"/>).</summary>
    public enum BotShotOutcome
    {
        /// <summary>Запроса не было.</summary>
        None,
        /// <summary>Не бой, нет цели/оружия в руке, бот мёртв, оружие выключено фазой, идёт StateSync.</summary>
        Blocked,
        /// <summary>Путь пули от дула до точки прицеливания перекрыт геометрией мира/укрытием/собственным телом или оружием.</summary>
        Occluded,
        /// <summary>Фактический луч дула отклонён от точки прицеливания ИИ больше допуска (ствол ещё не наведён).</summary>
        NotAimed,
        /// <summary>Жать курок: все условия выполнены (выстрел делает SDK по курку).</summary>
        Fired
    }

    /// <summary>Параметры player-flow ботов (бот — обычный аватар игрока без владельца).</summary>
    public static class BotPlayerFlowSettings
    {
        /// <summary>Рост глаз бота, метры (в границах <see cref="PlayerCalibrationRules"/>).</summary>
        public const float EyeHeight = 1.65f;

        /// <summary>
        /// Каденция raycast-а видимости цели (<see cref="BotGunner.SeesTarget"/>) в кадрах. Гейт выстрела от неё не
        /// зависит: луч дула пускается в кадре выстрела.
        /// </summary>
        public const int SeesIntervalFrames = 3;

        /// <summary>
        /// Допуск гейта выстрела: угол фактического луча дула к точке прицеливания ИИ, градусы. Промах по силуэту при
        /// неточности ИИ — нормальный выстрел; гейт проверяет только, что ствол смотрит туда, куда целится ИИ.
        /// </summary>
        public const float MaxShotAimErrorDegrees = 2f;

        /// <summary>Калибровка сессии бота: только рост глаз, пол — база префаба, без якорей.</summary>
        public static PlayerCalibration Calibration => new PlayerCalibration(0f, EyeHeight, false);
    }
}

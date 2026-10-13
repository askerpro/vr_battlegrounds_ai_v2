using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Bots
{
    /// <summary>Исход запроса выстрела Blaze (<see cref="BotGunner.ExecuteRequestedShot"/>).</summary>
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
        /// <summary>Эта машина не автор предмета.</summary>
        NotAuthor,
        /// <summary>Нет допустимого профиля готовности/учёта.</summary>
        NotReady,
        /// <summary>RequestAutomationPreparation вернул false (перезарядка/досылание идёт).</summary>
        Preparing,
        /// <summary>SDK отказал в выстреле (темп, CanUse).</summary>
        Rejected,
        /// <summary>
        /// Первый выстрел после экипировки ждёт хвата поддержки (оружие с точкой поддержки, в т.ч. пистолет) или таймаута
        /// <see cref="BotRuntimeController.SupportTimeoutSeconds"/> (eq-round2, решение эксперта). Запрос снят без выстрела.
        /// </summary>
        AwaitingSupport,
        /// <summary>TryToShootRound(0) вернул true.</summary>
        Fired
    }

    /// <summary>Замер в момент выстрела (до TryToShootRound, поза кадра после всех стадий) — для стенда.</summary>
    public struct BotShotSample
    {
        public int Frame;
        public float Time;
        public BotShotOutcome Outcome;
        /// <summary>Ошибка дула к точке прицеливания ИИ; false — не измерена.</summary>
        public bool HasError;
        public BotAimMath.MuzzleError Error;
        /// <summary>Применённая поправка отдачи (тангаж, курс), градусы.</summary>
        public UnityEngine.Vector2 Compensation;
        /// <summary>Угол фактического луча дула к точке прицеливания ИИ в гейте, градусы (NaN — гейт до угла не дошёл).</summary>
        public float GateAimErrorDeg;
        /// <summary>Первый коллайдер на пути пули при <see cref="BotShotOutcome.Occluded"/>; иначе null.</summary>
        public UnityEngine.Collider Blocker;
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

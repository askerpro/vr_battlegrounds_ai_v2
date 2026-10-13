using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>Куда драйвер ведёт кисть бота в player-flow (этап pf-equipment).</summary>
    public enum BotHandGoal
    {
        /// <summary>Цели ещё не было (первый кадр тела).</summary>
        None,
        /// <summary>Поза покоя относительно корпуса (<see cref="BotArmRestGeometry"/>): без оружия, после отпускания.</summary>
        Rest,
        /// <summary>К снапу основной рукояти неудерживаемого оружия: в кобуре, у кисти после покупки, на полу.</summary>
        ReachPrimary,
        /// <summary>Оружие в руке: поза из W* (<see cref="BotWeaponAim.TrySolve"/>) — прицел или опущенный ствол.</summary>
        Aim,
        /// <summary>Левая кисть к снапу поддержки оружия в правой руке.</summary>
        Support,
        /// <summary>Оружие в руке к якорю кобуры (поза оружия, выровненного по якорю, — геометрия SDK).</summary>
        Stow
    }

    /// <summary>
    /// Видимая траектория кисти бота: при смене цели (<see cref="BotHandGoal"/>) поза, которую драйвер пишет как
    /// контроллер, интерполируется по времени от последней <b>заданной</b> позы к текущей цели (цель может двигаться),
    /// с плавным разгоном/торможением. Длительность зависит от пути и поворота, в пределах
    /// <see cref="MinSeconds"/>…<see cref="MaxSeconds"/>; короче <see cref="InstantMeters"/>/<see cref="InstantDegrees"/> —
    /// без интерполяции. Живой скелет не читается: начало — прошлая команда, конец — цель кадра.
    /// Один экземпляр на кисть, владелец — <see cref="BotRuntimeController"/>. Без аллокаций.
    /// </summary>
    public sealed class BotHandTrajectory
    {
        /// <summary>Нижняя и верхняя граница длительности подвода, секунды.</summary>
        public const float MinSeconds = 0.25f;
        public const float MaxSeconds = 0.4f;

        /// <summary>
        /// Потолок прибытия для движущейся цели (eq-round2): цель кадра может уходить от начальной — длительность считается
        /// от расстояния, а прибытие по факту допускается до этого предела. Им же меряет стенд.
        /// </summary>
        public const float CeilingSeconds = 0.45f;

        /// <summary>Скорость, по которой оценивается длительность подвода, м/с и град/с.</summary>
        public const float MetersPerSecond = 1.6f;
        public const float DegreesPerSecond = 450f;

        /// <summary>Ближе этого цель достигается сразу (кисть уже на месте).</summary>
        public const float InstantMeters = 0.01f;
        public const float InstantDegrees = 3f;

        private BotHandGoal _goal = BotHandGoal.None;
        private Pose _from;
        private Pose _last;
        private bool _hasLast;
        private float _start;
        private float _seconds;

        /// <summary>Текущая цель.</summary>
        public BotHandGoal Goal => _goal;

        /// <summary>Когда началась текущая цель (Time.time).</summary>
        public float GoalSince => _start;

        /// <summary>Длительность подвода к текущей цели, секунды (0 — сразу).</summary>
        public float GoalSeconds => _seconds;

        /// <summary>Кисть дошла до цели в последнем шаге.</summary>
        public bool Arrived { get; private set; }

        /// <summary>Последняя выданная команда.</summary>
        public Pose Last => _last;

        public bool HasLast => _hasLast;

        /// <summary>
        /// Начальная команда нового тела (поза покоя), если команд ещё не было: первая цель тела ведётся от неё
        /// траекторией, а не ставится сразу («назначен и сразу прибыл» — eq-round1).
        /// </summary>
        public void Seed(in Pose pose)
        {
            if (_hasLast) return;
            _last = pose;
            _hasLast = true;
        }

        /// <summary>
        /// Поза кисти этого кадра к цели <paramref name="goal"/> с позой <paramref name="target"/>. Смена цели начинает
        /// новый подвод от последней команды; без прошлой команды (первый кадр тела) — сразу в цель.
        /// </summary>
        public Pose Step(BotHandGoal goal, in Pose target, float now)
        {
            if (goal != _goal)
            {
                _goal = goal;
                _start = now;
                _from = _hasLast ? _last : target;
                _seconds = _hasLast ? Duration(_from, target) : 0f;
            }

            Pose pose;
            if (_seconds <= 0f || now - _start >= _seconds)
            {
                pose = target;
                Arrived = true;
            }
            else
            {
                float t = Mathf.Clamp01((now - _start) / _seconds);
                t = t * t * (3f - 2f * t); // плавный разгон и торможение
                pose = new Pose(Vector3.Lerp(_from.position, target.position, t), Quaternion.Slerp(_from.rotation, target.rotation, t));
                Arrived = false;
            }

            _last = pose;
            _hasLast = true;
            return pose;
        }

        /// <summary>Длительность подвода по пути и повороту; короткий — сразу.</summary>
        private static float Duration(in Pose from, in Pose to)
        {
            float meters = Vector3.Distance(from.position, to.position);
            float degrees = Quaternion.Angle(from.rotation, to.rotation);
            if (meters < InstantMeters && degrees < InstantDegrees) return 0f;
            return Mathf.Clamp(Mathf.Max(meters / MetersPerSecond, degrees / DegreesPerSecond), MinSeconds, MaxSeconds);
        }

        /// <summary>
        /// Цель сдвинулась после начала подвода (снап на движущемся теле): если до конца осталось меньше, чем нужно на
        /// новый остаток пути, длительность продлевается, но не дальше <see cref="CeilingSeconds"/>.
        /// </summary>
        public void Retarget(in Pose target, float now)
        {
            if (!_hasLast || Arrived || _seconds <= 0f) return;
            float meters = Vector3.Distance(_last.position, target.position);
            float need = (now - _start) + meters / MetersPerSecond;
            if (need > _seconds) _seconds = Mathf.Min(CeilingSeconds, need);
        }
    }
}

// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 38: оценщик движения игрока — только из входа (шлем), раз в кадр, до решения тела.
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 38: «идёт ли игрок, куда и как быстро» — единственный источник этого решения для IK тела
    ///     (выпрямление корпуса по взгляду) и ног (шаг, скорость, направление клипов, наклон корпуса из клипа).
    ///     <para>
    ///         <b>Вход — только шлем.</b> Точка — шея, вычисленная из позы камеры (как её ставит <see cref="UxrBodyIK" />), по
    ///         горизонтали. Ни <c>Dummy Forward</c>, ни корень копии рига, ни изгиб корпуса сюда не попадают: это выходы
    ///         решателей, и решение по ним замыкает петлю (поворот головы → ноги повернулись → наклон из клипа сдвинул опору →
    ///         «тело идёт» → корпус выпрямился за взглядом → ноги снова поворачиваются). У чужого аватара вход — реплицированная
    ///         голова, логика та же.
    ///     </para>
    ///     <para>
    ///         <b>Шум шлема — не ходьба.</b> Скорость — по смещению за окно <see cref="VelocityWindow" />, а не за кадр: дрожь
    ///         ±3 мм и рывок трекинга 1–3 см дают за кадр 0,2–1,8 м/с, за окно — сотые. «Идёт» включается, только если и
    ///         скорость за <see cref="StartWindow" /> не ниже <see cref="StartSpeed" />, и точка ушла от места стояния дальше
    ///         <see cref="StartDistance" />: поворот и наклон головы держат шею на месте, медленный шаг 0,3 м/с — нет. Место
    ///         стояния медленно идёт за головой (переминание не копится в шаг). Выключается после
    ///         <see cref="StopHoldTime" /> ниже <see cref="StopSpeed" />. Скачок дальше <see cref="TeleportDistance" /> за
    ///         вызов — телепорт: сброс.
    ///     </para>
    /// </summary>
    public sealed class UxrBodyMotion
    {
        #region Public Types & Data

        /// <summary>Окно скорости, с.</summary>
        public const float VelocityWindow = 0.05f;

        /// <summary>Окно скорости для начала ходьбы, с.</summary>
        public const float StartWindow = 0.2f;

        /// <summary>Скорость за <see cref="StartWindow" />, с которой может начаться ходьба, м/с.</summary>
        public const float StartSpeed = 0.2f;

        /// <summary>Насколько точка под шеей должна уйти от места стояния, чтобы начать ходьбу, м.</summary>
        public const float StartDistance = 0.08f;

        /// <summary>
        ///     VR Battlegrounds patch 39: порог ухода от места стояния для этого оценщика, м (по умолчанию <see cref="StartDistance" />).
        ///     Оценщик таза (<see cref="UxrPelvisEstimate" />) поднимает его в приседе: на коленях таз сдвигается, не шагая.
        /// </summary>
        public float StartDistanceNow { get; set; } = StartDistance;

        /// <summary>Скорость (за <see cref="VelocityWindow" />), ниже которой ходьба кончается, м/с.</summary>
        public const float StopSpeed = 0.1f;

        /// <summary>Сколько скорость должна быть ниже <see cref="StopSpeed" />, чтобы ходьба кончилась, с.</summary>
        public const float StopHoldTime = 0.15f;

        /// <summary>Постоянная времени, с которой место стояния идёт за головой, пока игрок стоит, с.</summary>
        public const float AnchorFollowTime = 1.5f;

        /// <summary>Скачок за вызов дальше этого — телепорт, м.</summary>
        public const float TeleportDistance = 0.75f;

        /// <summary>Сглаженная скорость точки под шеей по горизонтали, м/с (мировые оси).</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Модуль <see cref="Velocity" />, м/с.</summary>
        public float Speed => Velocity.magnitude;

        /// <summary>Идёт ли игрок (гистерезис, без дрожи и рывков трекинга).</summary>
        public bool IsWalking { get; private set; }

        /// <summary>Точка под шеей последнего вызова (мировая, на высоте шеи).</summary>
        public Vector3 Position { get; private set; }

        /// <summary>Было ли хоть одно обновление после сброса.</summary>
        public bool HasData => _count > 0;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Новая поза шеи из входа (камеры). Звать раз в кадр решения, до IK тела.
        /// </summary>
        /// <param name="neckPosition">Шея, вычисленная из позы камеры, мировая</param>
        /// <param name="up">Вертикаль аватара</param>
        /// <param name="time">Время кадра, с (пропущенные кадры чужого аватара — не беда: окна по времени)</param>
        public void Update(Vector3 neckPosition, Vector3 up, float time)
        {
            Vector3 p = neckPosition - Vector3.Project(neckPosition, up);
            Position = neckPosition;

            if (_count == 0 || time <= _times[Newest])
            {
                if (_count == 0)
                {
                    Restart(p, time);
                }

                return;
            }

            float dt = time - _times[Newest];
            if ((p - _positions[Newest]).magnitude > TeleportDistance)
            {
                Restart(p, time);
                return;
            }

            Push(p, time);

            Vector3 rawVelocity = (p - PositionAt(time - VelocityWindow)) / VelocityWindow;
            Velocity = Vector3.SmoothDamp(Velocity, rawVelocity, ref _velocitySmooth, VelocitySmoothTime, Mathf.Infinity, dt);

            if (IsWalking)
            {
                _belowStopTime = Velocity.magnitude < StopSpeed ? _belowStopTime + dt : 0f;
                if (_belowStopTime >= StopHoldTime)
                {
                    IsWalking = false;
                    _anchor   = p;
                }
            }
            else
            {
                float startSpeed = (p - PositionAt(time - StartWindow)).magnitude / StartWindow;
                if (startSpeed >= StartSpeed && (p - _anchor).magnitude >= StartDistanceNow) // патч 39: порог на экземпляр
                {
                    IsWalking      = true;
                    _belowStopTime = 0f;
                }
                else
                {
                    _anchor = Vector3.Lerp(_anchor, p, 1f - Mathf.Exp(-dt / AnchorFollowTime));
                }
            }

            if (IsWalking)
            {
                _anchor = p;
            }
        }

        /// <summary>Сброс: следующий вызов начинает с места (телепорт, включение, перенос аватара).</summary>
        public void Reset()
        {
            _count         = 0;
            _head          = 0;
            Velocity       = Vector3.zero;
            _velocitySmooth = Vector3.zero;
            IsWalking      = false;
            _belowStopTime = 0f;
        }

        #endregion

        #region Private Methods

        private int Newest => (_head - 1 + Capacity) % Capacity;

        private void Restart(Vector3 p, float time)
        {
            Reset();
            _anchor = p;
            Push(p, time);
        }

        private void Push(Vector3 p, float time)
        {
            _positions[_head] = p;
            _times[_head]     = time;
            _head             = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }

        /// <summary>Позиция в момент <paramref name="time" /> (линейно между записями; раньше первой — первая).</summary>
        private Vector3 PositionAt(float time)
        {
            int newer = Newest;
            for (int i = 1; i < _count; i++)
            {
                int older = (Newest - i + Capacity) % Capacity;
                if (_times[older] <= time)
                {
                    float span = _times[newer] - _times[older];
                    float k    = span > 1e-6f ? (time - _times[older]) / span : 0f;
                    return Vector3.Lerp(_positions[older], _positions[newer], k);
                }

                newer = older;
            }

            return _positions[newer];
        }

        #endregion

        #region Private Types & Data

        private const int   Capacity           = 128; // > StartWindow при 240 Гц
        private const float VelocitySmoothTime = 0.02f;

        private readonly Vector3[] _positions = new Vector3[Capacity];
        private readonly float[]   _times     = new float[Capacity];
        private          int       _head;
        private          int       _count;
        private          Vector3   _velocitySmooth;
        private          Vector3   _anchor;
        private          float     _belowStopTime;

        #endregion
    }
}

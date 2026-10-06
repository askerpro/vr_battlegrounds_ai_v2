// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 37: шаги ног по клипам с root motion.
//
// ЛИЦЕНЗИЯ. Адаптация кода Final IK (RootMotion, Pärtel Lang): IKSolverVRLocomotion_Animated.Solve_Animated и ограничение
// угла корня из IKSolverVR.Spine.Solve. Final IK куплен в Asset Store, EULA разрешает менять и встраивать его код в свой
// проект. Файл остаётся внутри проекта: при публикации форка UltimateXR его выносить НЕЛЬЗЯ.
// --------------------------------------------------------------------------------------------------------------------
using System;
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 37: где стоят стопы — локомоция VRIK в режиме <c>Animated</c> без компонента VRIK.
    ///     <para>
    ///         Корень копии рига с Animator (клипы с root motion) ходит за опорой тела: параметры
    ///         <see cref="MoveXParam" />/<see cref="MoveZParam" /> — скорость опоры плюс её отрыв от корня в осях корня,
    ///         <see cref="IsMovingParam" /> — по порогу, <see cref="SpeedParam" /> — чтобы root motion клипа догонял опору,
    ///         <see cref="TurnParam" /> — поворот на месте, когда корпус отвернулся от корня. Клип сам двигает корень (стопы
    ///         стоят), остаток доводит lerp корня к опоре и предел <see cref="maxRootOffset" />.
    ///     </para>
    ///     <para>
    ///         Отличие от VRIK: опора — не голова, а <c>Dummy Forward</c> UltimateXR (точка на полу под шеей после IK тела
    ///         этого кадра), направление корпуса — тоже его: таз аватара ставит UltimateXR, стопы должны стоять под ним.
    ///     </para>
    ///     <para>Контракт контроллера клипов — имена параметров и состояний ниже (их же ставит утилита сборки контроллера).</para>
    /// </summary>
    [Serializable]
    public sealed class UxrLegLocomotion
    {
        #region Animator contract

        /// <summary>Скорость в осях корня, вправо (вектор смешивания клипов хода).</summary>
        public const string MoveXParam = "Legs_MoveX";

        /// <summary>Скорость в осях корня, вперёд.</summary>
        public const string MoveZParam = "Legs_MoveZ";

        /// <summary>Идёт ли шаг (переход покой ⇄ ход).</summary>
        public const string IsMovingParam = "Legs_IsMoving";

        /// <summary>Множитель скорости состояния хода.</summary>
        public const string SpeedParam = "Legs_Speed";

        /// <summary>Поворот на месте: −0,5 — 90° влево, +0,5 — вправо.</summary>
        public const string TurnParam = "Legs_Turn";

        /// <summary>Набор клипов (стойка): 0, 1, 2… — смысл задаёт игра (у нас: без оружия, пистолет, винтовка).</summary>
        public const string StanceParam = "Legs_Stance";

        /// <summary>Состояние покоя (первое в контроллере).</summary>
        public const string IdleState = "Legs_Idle";

        /// <summary>Состояние хода.</summary>
        public const string MoveState = "Legs_Move";

        /// <summary>Имя перехода «ход → покой» (по нему корень доводится к опоре со скоростью остановки).</summary>
        public const string StopTransition = "Legs_Stop";

        #endregion

        #region Inspector Properties/Serialized Fields

        [Tooltip("Порог начала шага, м/с. Ниже — тело переминается на месте, выше — включается шаг. Больше — аватар дольше стоит и догоняет игрока рывками; меньше — ноги семенят от покачивания головы. Обычно 0,2–0,4.")]
        public float moveThreshold = 0.3f;

        [Tooltip("Самый медленный темп клипа шага (доля обычного). Меньше — на очень медленном ходу ноги шагают медленнее, а не скользят; слишком мало — шаг «в замедленной съёмке». Обычно 0,1–0,2.")]
        public float minAnimationSpeed = 0.1f;

        [Tooltip("Самый быстрый темп клипа (доля обычного). Больше — ноги успевают за очень быстрым игроком частыми шагами; меньше — на рывке ноги отстают и скользят. Обычно 3.")]
        public float maxAnimationSpeed = 3f;

        [Tooltip("Плавность смены направления и скорости шага, с. Больше — переходы мягче, но ноги позже реагируют на смену хода; меньше — резче, возможна дрожь. Обычно 0,1.")]
        public float animationSmoothTime = 0.1f;

        [Tooltip("Где стоят ноги относительно опоры тела, м (x — вправо, y — вперёд). 0 — ноги под шеей. Положительный y — ноги впереди головы (корпус откинут назад).")]
        public Vector2 standOffset;

        [Tooltip("Как сильно ноги подтягиваются к телу на ходу, сверх самих шагов. Больше — ноги не отстают, но стопы чаще скользят; меньше — честные шаги, но тело может уходить от ног. Обычно 20–30.")]
        public float rootLerpSpeedWhileMoving = 30f;

        [Tooltip("То же при остановке: как быстро ноги встают под тело. Обычно 10.")]
        public float rootLerpSpeedWhileStopping = 10f;

        [Tooltip("То же при повороте на месте. Обычно 10.")]
        public float rootLerpSpeedWhileTurning = 10f;

        [Tooltip("Насколько далеко тело может уйти от ног, м, прежде чем ноги подтянутся принудительно (со скольжением). Больше — меньше скольжения, но тело дальше выходит за опору; меньше — ноги всегда под телом. Обычно 0,4–0,5.")]
        public float maxRootOffset = 0.5f;

        [Tooltip("Насколько корпус может отвернуться от ног на ходу, градусы; сверх — ноги доворачиваются рывком. Обычно 10.")]
        public float maxRootAngleMoving = 10f;

        [Tooltip("То же стоя, градусы. Больше — можно смотреть вбок без перестановки ног. Обычно 60–90.")]
        public float maxRootAngleStanding = 60f;

        [Tooltip("С какого поворота корпуса ноги начинают переступать на месте, градусы. Больше — переступают реже (можно оглядываться), меньше — переступают от лёгкого поворота. Обычно 20–30.")]
        public float turnStartAngle = 20f;

        [Tooltip("При каком остатке поворота ноги перестают переступать, градусы. Меньше — ноги разворачиваются точнее за корпусом. Обычно 6–18.")]
        public float turnStopAngle = 6f;

        [Tooltip("Как быстро прекращается переступание после конца поворота. Больше — ноги не «докручивают» лишнее. Обычно 12.")]
        public float turnReleaseSpeed = 12f;

        [Tooltip("Длина шага. Больше 1 — шаги длиннее и реже, меньше — короче и чаще. Обычно 1.")]
        public float stepLengthMlp = 1f;

        [Tooltip("Скорость обычного шага в клипах, м/с. Ниже неё играет шаг своего направления (вперёд, вбок, по диагонали), а не смесь соседних. 0 — как у VRIK (на медленном ходу клипы смешиваются). Для клипов Mixamo — 1,8.")]
        public float blendRing = 1.8f;

        [Tooltip("Как длина шага зависит от скорости на медленном ходу: 0 — шаг всегда полный, реже на медленном ходу; 1 — шаг тем короче, чем медленнее, частота почти постоянная; 0,5 — как у человека (и длина, и частота растут со скоростью).")]
        [Range(0f, 1f)]
        public float blendRingExponent = 0.5f;

        #endregion

        #region Public Types & Data

        /// <summary>Идёт ли шаг.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Горизонтальная скорость опоры тела, м/с (для наклона бегущего).</summary>
        public Vector3 BodyVelocity { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>Сброс: корень стоит, параметры аниматора — покой.</summary>
        public void Reset(Transform root, Animator animator)
        {
            _lastSpeedRootPos = root.position;
            _lastEndRootPos   = root.position;
            _lastCorrection   = Vector3.zero;
            _velocityLocal    = _velocityLocalV = Vector3.zero;
            _turn             = 0f;
            _turningOnSpot    = false;
            IsMoving          = false;
            _currentAnimationSmoothTime = 0.05f;
            _stopMoveTimer    = 1f;
            _maxRootAngle     = maxRootAngleStanding;
            _firstFrame       = true;
            BodyVelocity      = Vector3.zero;

            if (animator == null)
            {
                return;
            }

            animator.SetFloat(s_moveX, 0f);
            animator.SetFloat(s_moveZ, 0f);
            animator.SetBool(s_isMoving, false);
            animator.SetFloat(s_speed, 1f);
            animator.SetFloat(s_turn, 0f);
        }

        /// <summary>
        ///     Кадр локомоции: после оценки Animator (root motion уже сдвинул корень), до следующей. Двигает и поворачивает
        ///     <paramref name="root" />, пишет параметры в <paramref name="animator" /> — клипы возьмут их на следующей оценке.
        /// </summary>
        /// <param name="bodyPivot">Опора тела на полу (Dummy Forward), мировая</param>
        /// <param name="bodyForward">Направление корпуса (Dummy Forward.forward)</param>
        /// <param name="scale">Масштаб аватара</param>
        /// <param name="rootMotion">Сдвиг корня клипом в этом кадре (root motion, уже применённый к корню)</param>
        /// <param name="motion">
        ///     Патч 38: оценщик движения игрока по шлему (вход). Задан — «идёт ли», скорость и направление берутся из него;
        ///     сами ноги добавляют только подтягивание к опоре (отрыв корня дальше <see cref="moveThreshold" />). null —
        ///     по-старому: скорость — по движению опоры за кадр.
        /// </param>
        public void Solve(Transform root, Animator animator, Vector3 bodyPivot, Vector3 bodyForward, float scale, float deltaTime, Vector3 rootMotion, UxrBodyMotion motion = null)
        {
            if (deltaTime <= 0f || root == null || animator == null)
            {
                return;
            }

            Vector3    rootUp       = root.up;
            Quaternion readRotation = root.rotation;

            // Внешнее движение корня (не клипом и не нами) — как у VRIK: платформа, телепорт.
            Vector3 externalDelta = root.position - _lastEndRootPos - rootMotion;

            // --- Угол корня (IKSolverVR.Spine.Solve: maxRootAngle) — до локомоции, как в порядке VRIK.
            Vector3 face = Vector3.ProjectOnPlane(bodyForward, rootUp);
            if (face.sqrMagnitude > 1e-6f)
            {
                float fix = RootAngleFix(SignedYaw(root.rotation, face), _maxRootAngle);
                if (fix != 0f)
                {
                    Quaternion q     = Quaternion.AngleAxis(fix, rootUp);
                    Vector3    pivot = animator.pivotPosition;
                    root.SetPositionAndRotation(pivot + q * (root.position - pivot), q * root.rotation);
                }
            }

            // --- Опора (цель головы у VRIK) и её скорость.
            Vector3 headTargetPos = bodyPivot + root.rotation * new Vector3(standOffset.x, 0f, standOffset.y) * scale;
            if (_firstFrame)
            {
                _lastHeadTargetPos = headTargetPos;
                _firstFrame        = false;
            }

            Vector3 headTargetVelocity = motion != null ? Flatten(motion.Velocity, rootUp) : Flatten((headTargetPos - _lastHeadTargetPos) / deltaTime, rootUp);
            _lastHeadTargetPos = headTargetPos;
            BodyVelocity       = headTargetVelocity;

            Vector3 offset = headTargetPos - root.position;
            offset -= externalDelta;
            offset -= _lastCorrection;
            offset =  Flatten(offset, rootUp);

            // --- Поворот на месте: корпус отвернулся от корня (гистерезис начала/конца).
            float angle = face.sqrMagnitude > 1e-6f ? SignedYaw(root.rotation, face) : 0f;
            _turningOnSpot = UpdateTurnOnSpot(_turningOnSpot, Mathf.Abs(angle), turnStartAngle, turnStopAngle);
            bool  isTurning  = _turningOnSpot;
            float turnTarget = isTurning ? angle / 90f : 0f;
            _turn = Mathf.Lerp(_turn, turnTarget, deltaTime * (isTurning ? 3f : turnReleaseSpeed));
            animator.SetFloat(s_turn, _turn * 2f);

            // --- Скорость в осях корня, сглаживание.
            Vector3 velocityLocalTarget = Quaternion.Inverse(readRotation) * (headTargetVelocity + offset);
            velocityLocalTarget *= stepLengthMlp;

            float smoothTarget = isTurning && !IsMoving ? 0.2f : animationSmoothTime;
            _currentAnimationSmoothTime = Mathf.Lerp(_currentAnimationSmoothTime, smoothTarget, deltaTime * 20f);
            _velocityLocal = Vector3.SmoothDamp(_velocityLocal, velocityLocalTarget, ref _velocityLocalV, _currentAnimationSmoothTime, Mathf.Infinity, deltaTime);
            float velLocalMag = _velocityLocal.magnitude / stepLengthMlp;

            Vector3 blend = BlendVector(_velocityLocal / scale);
            animator.SetFloat(s_moveX, blend.x);
            animator.SetFloat(s_moveZ, blend.z);

            // --- Идёт ли шаг (при ходьбе порог ×0,9 — гистерезис).
            float m = moveThreshold * scale;
            if (IsMoving)
            {
                m *= 0.9f;
            }

            bool isMovingRaw = _velocityLocal.sqrMagnitude > m * m;
            if (motion != null)
            {
                // Патч 38: шаг — когда игрок идёт (оценщик по шлему) или ноги отстали от опоры дальше порога (догнать тело).
                // Отрыв — состояние самих ног относительно звена выше (опоры), а не выход нижнего звена: петли нет.
                isMovingRaw = motion.IsWalking || offset.sqrMagnitude > m * m;
            }
            if (isMovingRaw)
            {
                _stopMoveTimer = 0f;
            }
            else
            {
                _stopMoveTimer += deltaTime;
            }

            IsMoving = _stopMoveTimer < 0.05f;

            float maxRootAngleTarget = IsMoving ? maxRootAngleMoving : maxRootAngleStanding;
            _maxRootAngle = Mathf.SmoothDamp(_maxRootAngle, maxRootAngleTarget, ref _maxRootAngleV, 0.2f, Mathf.Infinity, deltaTime);

            animator.SetBool(s_isMoving, IsMoving);

            // --- Скорость проигрывания: root motion клипа должен догонять опору.
            Vector3 currentRootPos = root.position - externalDelta - _lastCorrection;
            Vector3 rootVelocity   = (currentRootPos - _lastSpeedRootPos) / deltaTime;
            _lastSpeedRootPos = root.position;
            float rootVelocityMag = rootVelocity.magnitude;

            float animSpeedTarget = minAnimationSpeed;
            if (rootVelocityMag > 0f && isMovingRaw)
            {
                animSpeedTarget = _animSpeed * (velLocalMag / rootVelocityMag);
            }

            animSpeedTarget = Mathf.Clamp(animSpeedTarget, minAnimationSpeed, maxAnimationSpeed);
            _animSpeed      = Mathf.SmoothDamp(_animSpeed, animSpeedTarget, ref _animSpeedV, 0.05f, Mathf.Infinity, deltaTime);
            animator.SetFloat(s_speed, _animSpeed);

            // --- Подтягивание корня к опоре и предел отрыва.
            bool  isStopping          = animator.GetAnimatorTransitionInfo(0).IsUserName(StopTransition);
            float rootLerpSpeedTarget = 0f;
            if (IsMoving)
            {
                rootLerpSpeedTarget = rootLerpSpeedWhileMoving;
            }

            if (isStopping)
            {
                rootLerpSpeedTarget = rootLerpSpeedWhileStopping;
            }

            if (isTurning)
            {
                rootLerpSpeedTarget = rootLerpSpeedWhileTurning;
            }

            rootLerpSpeedTarget *= Mathf.Max(headTargetVelocity.magnitude, 0.2f);
            _rootLerpSpeed      =  Mathf.Lerp(_rootLerpSpeed, rootLerpSpeedTarget, deltaTime * 20f);

            // Опора — на высоте корня.
            headTargetPos += Vector3.Project(root.position - headTargetPos, rootUp);

            Vector3 p       = root.position;
            Vector3 newRoot = p;
            if (maxRootOffset > 0f)
            {
                if (_rootLerpSpeed > 0f)
                {
                    newRoot = Vector3.Lerp(p, headTargetPos, _rootLerpSpeed * deltaTime);
                }

                _lastCorrection = newRoot - p;

                offset = Flatten(headTargetPos - newRoot, rootUp);
                float offsetMag = offset.magnitude;
                float limit     = maxRootOffset * scale;
                if (offsetMag > limit)
                {
                    _lastCorrection += offset - offset / offsetMag * limit;
                    newRoot         =  p + _lastCorrection;
                }
            }
            else
            {
                _lastCorrection = headTargetPos - p;
                newRoot         = headTargetPos;
            }

            root.position   = newRoot;
            _lastEndRootPos = newRoot;
        }

        /// <summary>
        ///     Вектор смешивания клипов хода (оси корня, без масштаба). С <see cref="blendRing" /> медленный ход играет клип
        ///     своего направления (вместе с покоем в центре смешивания), а частоту шагов задаёт скорость проигрывания: она
        ///     сравнивает скорость опоры с root motion клипа (<see cref="SpeedParam" />).
        /// </summary>
        public Vector3 BlendVector(Vector3 velocityLocal)
        {
            velocityLocal.y = 0f;
            float mag = velocityLocal.magnitude;
            if (blendRing <= 0f || mag < 1e-3f || mag >= blendRing)
            {
                return velocityLocal;
            }

            return velocityLocal * (blendRing * Mathf.Pow(mag / blendRing, blendRingExponent) / mag);
        }

        /// <summary>
        ///     Гистерезис поворота на месте: начать, когда отворот корпуса от ног больше <paramref name="startAngle" />;
        ///     закончить, когда меньше <paramref name="stopAngle" /> (не больше порога начала).
        /// </summary>
        public static bool UpdateTurnOnSpot(bool turning, float absAngle, float startAngle, float stopAngle)
        {
            if (!turning)
            {
                return absAngle > startAngle;
            }

            return absAngle >= Mathf.Min(stopAngle, startAngle);
        }

        /// <summary>
        ///     Ограничение угла корня (maxRootAngle VRIK): на сколько градусов повернуть ноги, чтобы корпус был отвёрнут от
        ///     них не больше <paramref name="maxAngle" />. 0 — в пределах.
        /// </summary>
        public static float RootAngleFix(float faceAngle, float maxAngle)
        {
            if (faceAngle > maxAngle)
            {
                return faceAngle - maxAngle;
            }

            if (faceAngle < -maxAngle)
            {
                return faceAngle + maxAngle;
            }

            return 0f;
        }

        #endregion

        #region Private Methods

        /// <summary>Рысканье направления <paramref name="face" /> в осях <paramref name="rootRotation" />, ° (&gt; 0 — вправо).</summary>
        private static float SignedYaw(Quaternion rootRotation, Vector3 face)
        {
            Vector3 local = Quaternion.Inverse(rootRotation) * face;
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        private static Vector3 Flatten(Vector3 v, Vector3 normal)
        {
            return v - Vector3.Project(v, normal);
        }

        #endregion

        #region Private Types & Data

        private static readonly int s_moveX    = Animator.StringToHash(MoveXParam);
        private static readonly int s_moveZ    = Animator.StringToHash(MoveZParam);
        private static readonly int s_isMoving = Animator.StringToHash(IsMovingParam);
        private static readonly int s_speed    = Animator.StringToHash(SpeedParam);
        private static readonly int s_turn     = Animator.StringToHash(TurnParam);

        private Vector3 _velocityLocal;
        private Vector3 _velocityLocalV;
        private Vector3 _lastCorrection;
        private Vector3 _lastHeadTargetPos;
        private Vector3 _lastSpeedRootPos;
        private Vector3 _lastEndRootPos;
        private float   _rootLerpSpeed;
        private float   _animSpeed = 1f;
        private float   _animSpeedV;
        private float   _stopMoveTimer = 1f;
        private float   _turn;
        private bool    _turningOnSpot;
        private float   _maxRootAngle;
        private float   _maxRootAngleV;
        private float   _currentAnimationSmoothTime = 0.05f;
        private bool    _firstFrame                 = true;

        #endregion
    }
}

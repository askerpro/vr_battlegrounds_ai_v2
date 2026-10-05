// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 39: оценка таза и наклона корпуса игрока — только из входа (шея из шлема).
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 39: где таз игрока и насколько он наклонил корпус — по шее из шлема. Одна высота головы не
    ///     отличает полуприсед с наклоном (голова низко и впереди, таз высоко) от колена на полу (корпус прямой, таз низко),
    ///     а движение шеи — качание корпуса на коленях от шага.
    ///     <para>
    ///         <b>Таз</b> (<see cref="Anchor" />, по горизонтали) — под шеей, пока игрок стоит прямо (шея не ниже
    ///         <see cref="UprightBand" /> от своей стоячей высоты) или бежит. Опустил шею — таз стоит, а смещение шеи от него —
    ///         наклон корпуса: <c>наклон = asin(смещение / длина корпуса)</c>, <c>высота таза = шея − длина·cos(наклон)</c>.
    ///         Наклон ограничен асимметрично (в осях взгляда): вперёд до <see cref="MaxForwardLean" />, вбок до
    ///         <see cref="MaxSideLean" />, назад — только <see cref="MaxBackLean" />: люди почти не откидываются назад, и шея,
    ///         ушедшая назад, — это сдвиг таза (сел на пятки), а не откидка корпуса. Шея за пределами — таз тянется за ней.
    ///     </para>
    ///     <para>
    ///         <b>Шаг</b> — по движению таза (<see cref="Motion" />), а не шеи: качание корпуса на коленях таз не двигает, и
    ///         ноги стоят. В приседе порог ухода таза от места больше (<see cref="CrouchStartDistance" />).
    ///     </para>
    ///     <para>
    ///         Вход — только шея из шлема, взгляд и пол; ни <c>Dummy Forward</c>, ни ноги сюда не попадают (правило потока
    ///         данных). Ограничение: «гусиный шаг» двигает таз, только когда шея вышла за пределы наклона.
    ///     </para>
    /// </summary>
    public sealed class UxrPelvisEstimate
    {
        #region Public Types & Data

        /// <summary>Шея ниже своей стоячей высоты не больше чем на столько — игрок стоит прямо, м без масштаба.</summary>
        public const float UprightBand = 0.06f;

        /// <summary>Как быстро стоячая высота шеи забывает максимум, м/с (переобулся, снял шлем).</summary>
        public const float UprightDecay = 0.005f;

        /// <summary>Постоянная времени, с которой таз идёт за шеей стоя прямо, с.</summary>
        public const float UprightFollowTime = 0.05f;

        /// <summary>Скорость шеи, м/с, выше которой таз идёт за ней и не стоя прямо (бег с пригибанием).</summary>
        public const float RunSpeed = 2f;

        /// <summary>Пределы наклона корпуса, градусы: вперёд, вбок, назад.</summary>
        public const float MaxForwardLean = 50f, MaxSideLean = 35f, MaxBackLean = 6f;

        /// <summary>Порог ухода таза от места стояния для шага в приседе, м (стоя — <see cref="UxrBodyMotion.StartDistance" />).</summary>
        public const float CrouchStartDistance = 0.2f;

        /// <summary>Сдвиг таза от места ног больше этого, м, — таз ушёл: место переносится (шаг).</summary>
        public const float RebaseDistance = 0.3f;

        /// <summary>Высота таза над полом с упором в минимум (наклоном корпуса), м без масштаба.</summary>
        public float PelvisHeight { get; private set; }

        /// <summary>Высота таза без упора — по шее и наклону игрока, м без масштаба (решение «на колене»).</summary>
        public float RawPelvisHeight { get; private set; }

        /// <summary>Наклон корпуса, градусы (0 — прямо).</summary>
        public float LeanAngle { get; private set; }

        /// <summary>Шея минус таз по горизонтали, мировой (с масштабом).</summary>
        public Vector3 LeanOffset { get; private set; }

        /// <summary>Направление наклона корпуса по горизонтали (единичное, мировое).</summary>
        public Vector3 LeanDirection { get; private set; }

        /// <summary>Наклон вперёд, добавленный, чтобы таз не ушёл ниже минимума, градусы (0 — не понадобился).</summary>
        public float ForcedLean { get; private set; }

        /// <summary>Место ног (опора шагов) по горизонтали, мировое.</summary>
        public Vector3 Anchor => _anchor;

        /// <summary>Таз по горизонтали (место ног + сдвиг таза), мировой.</summary>
        public Vector3 Pelvis { get; private set; }

        /// <summary>Шея минус место ног по горизонтали — сдвиг опоры шагов от шеи, мировой.</summary>
        public Vector3 StepOffset { get; private set; }

        public bool IsUpright { get; private set; }

        /// <summary>Идёт ли игрок, скорость — по движению таза (для ног).</summary>
        public UxrBodyMotion Motion { get; } = new UxrBodyMotion();

        #endregion

        #region Public Methods

        public void Reset()
        {
            _has = false;
            Motion.Reset();
        }

        /// <param name="neck">Шея из шлема, мировая</param>
        /// <param name="floor">Точка пола (корень аватара)</param>
        /// <param name="up">Вертикаль аватара</param>
        /// <param name="look">Взгляд (камера), мировой</param>
        /// <param name="scale">Масштаб аватара (калибровка роста)</param>
        /// <param name="torsoLength">Таз → шея стоя, м без масштаба</param>
        /// <param name="neckSpeed">Скорость шеи по горизонтали, м/с (оценщик шеи)</param>
        /// <param name="crouch">Присед 0..1 (порог шага таза)</param>
        /// <param name="dt">Шаг времени</param>
        public void Update(Vector3 neck, Vector3 floor, Vector3 up, Vector3 look, float scale, float torsoLength, float neckSpeed, float crouch, float minPelvis, float minZone, float dt)
        {
            scale = Mathf.Max(scale, 1e-3f);
            float   h    = Vector3.Dot(neck - floor, up) / scale;
            Vector3 flat = neck - up * Vector3.Dot(neck - floor, up);

            if (!_has || Vector3.Distance(flat, _anchor) > TeleportDistance * scale)
            {
                _has     = true;
                _upright = h;
                _anchor  = flat;
                Motion.Reset();
            }

            float length = Mathf.Max(torsoLength, 0.1f);
            _upright  = Mathf.Max(h, _upright - UprightDecay * dt);
            IsUpright = h >= _upright - UprightBand;
            Vector3 shift = Vector3.zero;
            if (IsUpright || neckSpeed > RunSpeed)
            {
                _anchor = Vector3.Lerp(_anchor, flat, 1f - Mathf.Exp(-dt / UprightFollowTime));
            }
            else
            {
                // Шея дальше, чем объясняет наклон, — таз сдвинут (сел на пятки, подался вбок) относительно места, где ноги:
                // сдвиг считается заново каждый кадр от места (без памяти — качание туда-обратно его не накапливает). Назад наклон
                // почти ничего не объясняет. Сдвиг больше RebaseDistance — таз действительно ушёл: место переносится (шаг).
                Vector3 fwd = LookForward(look, up);
                Vector3 right  = Vector3.Cross(up, fwd);
                Vector3 offset = flat - _anchor;
                float   z      = Vector3.Dot(offset, fwd);
                float   x      = Vector3.Dot(offset, right);
                float   L      = length * scale;
                float   zc     = Mathf.Clamp(z, -L * Mathf.Sin(MaxBackLean * Mathf.Deg2Rad), L * Mathf.Sin(MaxForwardLean * Mathf.Deg2Rad));
                float   xc     = Mathf.Clamp(x, -L * Mathf.Sin(MaxSideLean * Mathf.Deg2Rad), L * Mathf.Sin(MaxSideLean * Mathf.Deg2Rad));
                shift = fwd * (z - zc) + right * (x - xc);
                if (shift.magnitude > RebaseDistance * scale)
                {
                    _anchor += shift;
                    shift    = Vector3.zero;
                }
            }

            Pelvis     = _anchor + shift;
            StepOffset = flat - _anchor;
            LeanOffset = flat - Pelvis;
            float sin = Mathf.Clamp(LeanOffset.magnitude / scale / length, 0f, MaxLeanSin);
            float cos = Mathf.Sqrt(1f - sin * sin);
            LeanAngle    = Mathf.Asin(sin) * Mathf.Rad2Deg;
            PelvisHeight = h - length * cos;

            // Таз не ниже minPelvis (как minHeadHeight + moveBodyBackWhenCrouching у VRIK, но голова остаётся в шлеме): голова
            // ниже, чем позволяет таз, — излишек забирает наклон корпуса ВПЕРЁД (таз уходит назад и вверх), до MaxForcedLean.
            RawPelvisHeight = PelvisHeight;
            Vector3 lookFwd = LookForward(look, up);
            ForcedLean = 0f;

            // Наклон по шее — вектор (направление × угол) в плоскости пола. Назад шея наклон не объясняет: составляющая назад
            // отбрасывается (её уже забрал сдвиг таза), остаётся вбок; иначе (на месте) — вперёд по корпусу. Прежде при малой
            // составляющей назад весь наклон вбок подменялся наклоном вперёд — корпус валился вперёд-вбок и выкручивался.
            Vector3 off = LeanOffset;
            float   back = Vector3.Dot(off, lookFwd);
            if (back < 0f)
            {
                off -= lookFwd * back;
            }

            Vector3 lean = (off.magnitude > LeanDirMin * scale ? off.normalized : lookFwd) * LeanAngle;

            {
                // Мягкий упор таза в минимум (без «колена» на кривой): таз = min + Z·g((таз − min)/Z), g(x) = x при x ≥ 1,
                // (1+x)²/4 при −1 ≤ x ≤ 1, 0 ниже — C1-кривая, начинается на Z выше минимума (средний присед). Недостающий
                // наклон добавляется ВПЕРЁД по корпусу (как у VRIK — таз назад, корпус вперёд), к наклону по шее — векторно.
                float zone = Mathf.Max(minZone, 0.02f);
                float x    = (PelvisHeight - minPelvis) / zone;
                float g    = x >= 1f ? x : x <= -1f ? 0f : (1f + x) * (1f + x) * 0.25f;
                float soft = minPelvis + zone * g;
                if (soft > PelvisHeight)
                {
                    float c   = Mathf.Clamp((h - soft) / length, Mathf.Cos(MaxForcedLean * Mathf.Deg2Rad), 1f);
                    float req = Mathf.Acos(c) * Mathf.Rad2Deg;
                    float now = lean.magnitude;
                    if (req > now)
                    {
                        float vf  = Vector3.Dot(lean, lookFwd);
                        float add = -vf + Mathf.Sqrt(Mathf.Max(0f, vf * vf - now * now + req * req));
                        lean      += lookFwd * add;
                        ForcedLean = add;
                    }
                }
            }

            LeanAngle     = Mathf.Min(lean.magnitude, MaxForcedLean);
            LeanDirection = lean.sqrMagnitude > 1e-6f ? lean.normalized : lookFwd;
            PelvisHeight  = h - length * Mathf.Cos(LeanAngle * Mathf.Deg2Rad);

            // Шаг — по месту ног (_anchor), а не по тазу: сдвиг таза на коленях — не шаг; перенос места — шаг.
            Motion.StartDistanceNow = Mathf.Lerp(UxrBodyMotion.StartDistance, CrouchStartDistance, Mathf.Clamp01(crouch)) * scale;
            Motion.Update(_anchor + up * Vector3.Dot(neck - floor, up), up, Time.time);
        }

        #endregion

        private static Vector3 LookForward(Vector3 look, Vector3 up)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(look, up);
            return fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        }

        #region Private Types & Data

        private const float TeleportDistance = 0.75f;
        private const float MaxForcedLean    = 55f; // больше — с наклоном покоя груди (~17°) корпус переваливался за горизонталь («пол-оборота»)
        private const float LeanDirMin       = 0.03f;
        private const float MaxLeanSin       = 0.94f; // ~70°

        private bool    _has;
        private float   _upright;
        private Vector3 _anchor;

        #endregion
    }
}

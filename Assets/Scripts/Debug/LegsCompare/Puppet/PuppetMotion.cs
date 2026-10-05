using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Состояние «игрока в шлеме» для программ стенда <see cref="AvatarPuppetStand"/>: где голова (ход по арене, поворот,
    /// присед, наклоны, взгляд вниз) — в осях стенда. Перенос модели головы из старого <c>LegsComparisonRig</c> (удалён):
    /// наклон — вокруг точки на высоте <see cref="LeanPivotHeight"/>, взгляд вниз — вокруг шеи (ниже и позади глаз).
    /// </summary>
    public sealed class PuppetMotion
    {
        public const float LeanPivotHeight = 1.0f;

        /// <summary>Шея относительно глаз в осях головы: центр поворота головы при взгляде вниз.</summary>
        private static readonly Vector3 NeckFromEyes = new Vector3(0f, -0.12f, -0.08f);

        public Vector3 Move;        // ход в осях стенда: x — вбок, z — вперёд
        public float Yaw;           // поворот тела (корпуса игрока), °
        public float HeadYaw;       // поворот одной головы относительно тела, ° (> 0 — вправо); кисти не двигаются
        public float Crouch;        // опускание головы, м
        public float LeanForward;   // наклон вперёд, °
        public float LeanSide;      // наклон вправо, °
        public float LookDown;      // взгляд вниз одной шеей, °
        public float HandsFollowHead; // доля поворота головы, на которую кисти уходят за ней (плечи доворачиваются), 0..1
        public float Noise;         // шум трекинга шлема: 0 — нет, 1 — как в шлеме (дрожь ±3 мм/кадр, рывки 1–3 см)

        // Шум трекинга: дрожь кадра и редкий рывок (держится несколько кадров и возвращается). Детерминированный — прогоны
        // сравнимы.
        private System.Random _rng = new System.Random(NoiseSeed);
        private Vector3 _jitter, _jerk;
        private int _jerkFrames;
        private const int NoiseSeed = 4242;
        private const float JitterMeters = 0.003f, JerksPerSecond = 0.4f, JerkMin = 0.01f, JerkMax = 0.03f;

        // Бег: пригибание и качание головы (как шлем на бегу, не рельс).
        private float _runEffort, _runPhase, _runCrouch, _runLean;

        public void Reset()
        {
            Move = Vector3.zero;
            Yaw = HeadYaw = Crouch = LeanForward = LeanSide = LookDown = Noise = HandsFollowHead = 0f;
            _rng = new System.Random(NoiseSeed);
            _jitter = _jerk = Vector3.zero;
            _jerkFrames = 0;
            _runEffort = _runPhase = _runCrouch = _runLean = 0f;
        }

        /// <summary>
        /// Шаг хода головой со скоростью <paramref name="velocity"/> (оси стенда): смещение, а на скорости — пригибание
        /// до 15 см и наклон до 10° к 3,5 м/с с качанием ±2 см (~2,8 Гц).
        /// </summary>
        public void Walk(Vector3 velocity, float dt)
        {
            Move += velocity * dt;
            float speed = velocity.magnitude;
            float effort = Mathf.Clamp01(speed / 3.5f);
            _runEffort = Mathf.Lerp(_runEffort, effort, 1f - Mathf.Exp(-dt / 0.15f));
            _runPhase += dt * 2.8f * 2f * Mathf.PI * Mathf.Clamp01(speed / 1f);
            _runCrouch = 0.15f * _runEffort + 0.02f * _runEffort * (0.5f - 0.5f * Mathf.Cos(_runPhase * 2f));
            _runLean = speed > 1e-4f ? 10f * _runEffort * Mathf.Max(0f, Vector3.Dot(velocity / speed, Vector3.forward)) : 0f;
        }

        /// <summary>
        /// Кадр шума трекинга (звать раз в кадр): дрожь ±3 мм по каждой оси и редкий рывок 1–3 см на 3–8 кадров — так
        /// дрожит поза шлема Quest. При <see cref="Noise"/> = 0 — без шума.
        /// </summary>
        public void TickNoise(float dt)
        {
            if (Noise <= 0f)
            {
                _jitter = _jerk = Vector3.zero;
                _jerkFrames = 0;
                return;
            }
            _jitter = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)) * (JitterMeters * Noise);
            if (_jerkFrames > 0 && --_jerkFrames == 0) _jerk = Vector3.zero;
            if (_jerkFrames == 0 && Rand(0f, 1f) < JerksPerSecond * dt)
            {
                float a = Rand(0f, 2f * Mathf.PI);
                Vector3 dir = new Vector3(Mathf.Cos(a), Rand(-0.3f, 0.3f), Mathf.Sin(a)).normalized;
                _jerk = dir * (Rand(JerkMin, JerkMax) * Noise);
                _jerkFrames = 3 + _rng.Next(6);
            }
        }

        private float Rand(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        /// <summary>
        /// Поза глаз (камеры) в осях стенда при росте глаз <paramref name="eyeHeight"/>: тело (ход, поворот, наклоны), затем
        /// голова вокруг шеи — поворот <see cref="HeadYaw"/> и взгляд вниз <see cref="LookDown"/>.
        /// </summary>
        public Pose Head(float eyeHeight)
        {
            Quaternion trunk = Quaternion.Euler(0f, Yaw, 0f) * Quaternion.Euler(LeanForward + _runLean, 0f, -LeanSide);
            Vector3 eye = BodyEye(eyeHeight);
            Quaternion look = Quaternion.Euler(0f, HeadYaw, 0f) * Quaternion.Euler(LookDown, 0f, 0f);
            Vector3 neck = eye + trunk * NeckFromEyes;
            eye = neck + trunk * look * Quaternion.Inverse(trunk) * (eye - neck);
            return new Pose(eye + _jitter + _jerk, trunk * look);
        }

        /// <summary>Глаза без поворота одной головы (точка отсчёта кистей): кисти держит тело, а не голова.</summary>
        public Vector3 BodyEye(float eyeHeight)
        {
            Quaternion trunk = Quaternion.Euler(0f, Yaw, 0f) * Quaternion.Euler(LeanForward + _runLean, 0f, -LeanSide);
            Vector3 pivot = Vector3.up * (LeanPivotHeight - Crouch - _runCrouch);
            return Move + pivot + trunk * (Vector3.up * (eyeHeight - LeanPivotHeight));
        }

        /// <summary>«Таз» манипулятора — точка наклона корпуса над полом, м (стоя — <see cref="LeanPivotHeight"/>).</summary>
        public float PelvisHeight => LeanPivotHeight - Crouch - _runCrouch;

        /// <summary>Корпус: поворот без наклонов — кисти держатся у корпуса, а не у наклонённой головы.</summary>
        public Quaternion Body => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Поворот кистей: корпус плюс доля <see cref="HandsFollowHead"/> поворота головы (как в шлеме — плечи идут за взглядом).</summary>
        public Quaternion Hands => Quaternion.Euler(0f, Yaw + HeadYaw * HandsFollowHead, 0f);

        /// <summary>Тело на полу: позиция хода и поворот корпуса, в осях стенда.</summary>
        public Pose BodyOnFloor => new Pose(new Vector3(Move.x, 0f, Move.z), Body);
    }
}

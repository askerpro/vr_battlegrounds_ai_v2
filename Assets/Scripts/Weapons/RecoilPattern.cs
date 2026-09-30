using System;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Картина накопленной отдачи ствола (T-38) — чистая логика без Unity-объектов.
    ///
    /// <para>
    /// Отдача UltimateXR не копится: каждый выстрел заново запускает один и тот же короткий подброс, и очередью ствол
    /// держится лазером. Здесь выстрел добавляет «нагрев» (+1), угол — функция нагрева: подброс растёт к потолку
    /// (<c>1 − e^−x</c>), рыскание ходит по синусу — картина у ствола всегда одна, не случайна. Пауза остужает нагрев
    /// с постоянной скоростью, начиная через <see cref="CoolDelay"/> после выстрела (иначе очередь 600 в минуту не
    /// копилась бы): от потолка до нуля — за <see cref="RecoveryTime"/> паузы.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class RecoilPattern
    {
        /// <summary>Нагрев, на котором картина упирается в потолок: дальше очередь угол не растит.</summary>
        public const float MaxHeat = 30f;

        /// <summary>Сколько секунд после выстрела ствол ещё не остывает — дольше интервала очереди самого медленного автомата.</summary>
        public const float CoolDelay = 0.12f;

        // Частота рыскания по нагреву: полный период — за ~12 выстрелов.
        private const float YawFrequency = 0.5f;

        [Tooltip("Подброс первого выстрела, градусы (дальше растёт медленнее — к потолку).")]
        [SerializeField] private float _kickDegrees = 1f;

        [Tooltip("Потолок подброса за очередь, градусы.")]
        [SerializeField] private float _maxPitchDegrees = 6f;

        [Tooltip("Размах бокового рыскания, градусы (картина — синус по выстрелам, не случайность).")]
        [SerializeField] private float _maxYawDegrees = 2f;

        [Tooltip("Секунд паузы, за которые ствол возвращается с потолка к нулю.")]
        [SerializeField] private float _recoveryTime = 0.5f;

        [Tooltip("Во сколько раз сильнее отдача, если оружие держит одна рука.")]
        [SerializeField] private float _oneHandMultiplier = 2f;

        public RecoilPattern() { }

        public RecoilPattern(float kickDegrees, float maxPitchDegrees, float maxYawDegrees, float recoveryTime, float oneHandMultiplier = 2f)
        {
            _kickDegrees = kickDegrees;
            _maxPitchDegrees = maxPitchDegrees;
            _maxYawDegrees = maxYawDegrees;
            _recoveryTime = recoveryTime;
            _oneHandMultiplier = oneHandMultiplier;
        }

        public float KickDegrees => _kickDegrees;
        public float MaxPitchDegrees => _maxPitchDegrees;
        public float MaxYawDegrees => _maxYawDegrees;
        public float RecoveryTime => _recoveryTime;
        public float OneHandMultiplier => _oneHandMultiplier;

        /// <summary>Нагрев: сколько «выстрелов» накоплено сейчас.</summary>
        public float Heat { get; private set; }

        private float _sinceShot = float.MaxValue;

        public void Shot()
        {
            Heat = Mathf.Min(MaxHeat, Heat + 1f);
            _sinceShot = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (Heat <= 0f) return;

            float before = _sinceShot;
            _sinceShot += deltaTime;
            float cooling = Mathf.Min(deltaTime, _sinceShot - Mathf.Max(CoolDelay, before));
            if (cooling <= 0f) return;

            float rate = MaxHeat / Mathf.Max(0.01f, _recoveryTime - CoolDelay);
            Heat = Mathf.Max(0f, Heat - rate * cooling);
        }

        public void Reset()
        {
            Heat = 0f;
            _sinceShot = float.MaxValue;
        }

        /// <summary>Подброс ствола вверх, градусы.</summary>
        public float Pitch(bool oneHand)
        {
            if (Heat <= 0f || _maxPitchDegrees <= 0f) return 0f;
            float pitch = _maxPitchDegrees * (1f - Mathf.Exp(-Heat * _kickDegrees / _maxPitchDegrees));
            return pitch * Hands(oneHand);
        }

        /// <summary>Рыскание вбок, градусы (знак — сторона).</summary>
        public float Yaw(bool oneHand)
        {
            if (Heat <= 0f) return 0f;
            // Первые выстрелы — почти прямо вверх, дальше ствол гуляет вбок (как «змейка» CS).
            float grow = 1f - Mathf.Exp(-Heat / 3f);
            return _maxYawDegrees * grow * Mathf.Sin(Heat * YawFrequency) * Hands(oneHand);
        }

        private float Hands(bool oneHand) => oneHand ? _oneHandMultiplier : 1f;
    }
}

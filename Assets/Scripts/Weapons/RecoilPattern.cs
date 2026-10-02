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

        // ── Перевод из CS2 (T-38): одна формула на весь арсенал ──────────────────────
        // recoil_magnitude CS2 → подброс первого выстрела. Калибровка: AK-47 (30) ≈ 1°, ПП (18–21) ≈ 0,6–0,75°.

        /// <summary>Градусов подброса на единицу <c>recoil_magnitude</c> CS2.</summary>
        public const float KickPerCs2Magnitude = 0.035f;

        /// <summary>Потолок очереди — подброс стольких первых выстрелов, но не выше <see cref="MaxPitchCap"/>.</summary>
        public const float CeilingShots = 6f;

        /// <summary>Наибольший потолок подброса, градусы: дробовик и Deagle упираются в него с первого выстрела.</summary>
        public const float MaxPitchCap = 10f;

        /// <summary>Рыскание очереди — доля потолка: у автоматов (CS2 <c>is_full_auto</c>) ствол гуляет вбок, у остальных почти нет.</summary>
        public const float YawShareFullAuto = 0.35f, YawShareSemi = 0.1f;

        /// <summary>Наибольшее рыскание, градусы.</summary>
        public const float MaxYawCap = 2.5f;

        /// <summary>Возврат ствола в паузе, с: в CS2 спад отдачи общий для всего оружия (<c>weapon_recoil_decay*</c>).</summary>
        public const float Cs2RecoveryTime = 0.45f;

        /// <summary>Множитель одной руки: как у отдачи UltimateXR (6° одной против 2° двумя — втрое, здесь мягче).</summary>
        public const float DefaultOneHandMultiplier = 2f;

        /// <summary>Картина накопленной отдачи по CS2 <c>recoil_magnitude</c> и <c>is_full_auto</c>.</summary>
        public static RecoilPattern FromCs2(float recoilMagnitude, bool fullAuto)
        {
            float kick = recoilMagnitude * KickPerCs2Magnitude;
            float maxPitch = Mathf.Min(MaxPitchCap, kick * CeilingShots);
            float maxYaw = Mathf.Min(MaxYawCap, maxPitch * (fullAuto ? YawShareFullAuto : YawShareSemi));
            return new RecoilPattern(kick, maxPitch, maxYaw, Cs2RecoveryTime, DefaultOneHandMultiplier);
        }

        // ── Толчок UltimateXR (UxrFirearmTrigger) — только видимый ──────────────────
        // SDK заново запускает один и тот же толчок на каждый выстрел. Если он длиннее интервала очереди, то не успевает
        // погаснуть: у скорострельного оружия ствол постоянно задран и дрожит (класс ошибки «разброс MP5K», T-38).
        // Поэтому длительность — доля интервала спуска: к следующему выстрелу толчок погас и прицел не сбивает.

        /// <summary>Толчок SDK двумя руками — во столько раз больше подброса первого выстрела, но не больше <see cref="SdkKickCapDegrees"/>.</summary>
        public const float SdkKickScale = 1.5f;
        public const float SdkKickCapDegrees = 6f;

        /// <summary>Отдача назад, м на градус толчка, не больше <see cref="SdkOffsetCap"/>.</summary>
        public const float SdkOffsetPerDegree = 0.01f, SdkOffsetCap = 0.04f;

        /// <summary>Длительность толчка — доля интервала спуска, не дольше <see cref="SdkDurationMax"/>.</summary>
        public const float SdkDurationShare = 0.8f, SdkDurationMax = 0.3f;

        /// <summary>Угол толчка SDK, градусы (<c>RecoilAngleOneHand/TwoHands</c>).</summary>
        public float SdkAngle(bool oneHand) => Mathf.Min(SdkKickCapDegrees, _kickDegrees * SdkKickScale) * Hands(oneHand);

        /// <summary>Отдача назад SDK, м (<c>RecoilOffsetOneHand/TwoHands</c>, по −z осей отдачи).</summary>
        public float SdkOffset(bool oneHand) => Mathf.Min(SdkOffsetCap, _kickDegrees * SdkOffsetPerDegree) * Hands(oneHand);

        /// <summary>Длительность толчка SDK, с, при интервале спуска <paramref name="shotInterval"/> с.</summary>
        public static float SdkDuration(float shotInterval) => Mathf.Min(SdkDurationMax, SdkDurationShare * shotInterval);

        public bool SameAs(RecoilPattern other) =>
            other != null && Mathf.Approximately(_kickDegrees, other._kickDegrees) && Mathf.Approximately(_maxPitchDegrees, other._maxPitchDegrees) &&
            Mathf.Approximately(_maxYawDegrees, other._maxYawDegrees) && Mathf.Approximately(_recoveryTime, other._recoveryTime) &&
            Mathf.Approximately(_oneHandMultiplier, other._oneHandMultiplier);

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

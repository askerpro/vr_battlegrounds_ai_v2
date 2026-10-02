using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Историческая модель конуса Counter-Strike 2 (T-38) — чистая логика без Unity-объектов.
    /// С 2026-10-02 игровой код использует только SpreadOffset / ToRotation / ToDegrees для дроби.
    /// Модель inaccuracy оставлена для компиляции существующих тестов до проверки пользователем;
    /// ни пули, ни дробь её не применяют.
    ///
    /// <para>
    /// <b>Модель CS.</b> Неточность = <c>inaccuracy_stand</c> + доля <c>inaccuracy_move</c> по скорости + накопленный
    /// штраф. Каждый выстрел прибавляет к штрафу <c>inaccuracy_fire</c> <i>после</i> себя — поэтому первый выстрел
    /// после паузы летит с неточностью стоя (у винтовок и пистолетов это доли градуса). Штраф спадает
    /// экспоненциально: за <c>recovery_time</c> — до 10 % (<c>×e^(−ln10·dt/T)</c>, как в исходниках CS:GO). Очередь
    /// упирается в потолок сама: штраф перед выстрелом при темпе с интервалом Δ стремится к
    /// <c>fire·k/(1−k)</c>, <c>k = e^(−ln10·Δ/T)</c> (<see cref="SteadyPenalty"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Направление.</b> Как в CS: смещение неточности (радиус равномерно по 0…неточность, угол случайный) плюс
    /// смещение разброса (0…spread) — точки гуще к центру. Единицы — тангенс × 1000 (мрад), как в скрипте CS.
    /// Случайность детерминирована: <c>(seed, номер выстрела, поток)</c> → число, без общего генератора. У дроби
    /// смещение неточности одно на залп (поток 0), разброс — свой у каждой дробины (поток 1 + номер).
    /// </para>
    ///
    /// <para>
    /// <b>VR.</b> Приседа и прыжка нет. Бег стиком невозможен — ходьба физическая, поэтому неточность движения берётся
    /// малой долей (<see cref="MoveShare"/>) по горизонтальной скорости головы: от <see cref="MoveSpeedStart"/> (шаг
    /// на месте, покачивание не штрафуется) до <see cref="MoveSpeedFull"/>.
    /// </para>
    /// </summary>
    public sealed class WeaponAccuracy
    {
        /// <summary>Доля, до которой спадает штраф за <c>recovery_time</c> (CS: <c>log(10) / recovery_time</c>).</summary>
        public const float RecoveredFraction = 0.1f;

        /// <summary>Горизонтальная скорость головы, м/с, с которой начинается штраф движения.</summary>
        public const float MoveSpeedStart = 0.5f;

        /// <summary>Скорость, м/с, на которой штраф движения полный (быстрый шаг в комнате).</summary>
        public const float MoveSpeedFull = 2.5f;

        /// <summary>Доля <c>inaccuracy_move</c> CS2 на полной скорости: в CS это бег 250 ед/с, в VR — шаг.</summary>
        public const float MoveShare = 0.1f;

        private const float TwoPi = Mathf.PI * 2f;

        private readonly SpreadPattern _pattern;

        public WeaponAccuracy(SpreadPattern pattern) => _pattern = pattern ?? new SpreadPattern();

        public SpreadPattern Pattern => _pattern;

        /// <summary>Накопленный штраф за стрельбу, мрад.</summary>
        public float Penalty { get; private set; }

        /// <summary>Выстрел сделан: штраф растёт после него (сам выстрел шёл с прежней неточностью).</summary>
        public void Shot() => Penalty += _pattern.InaccuracyFire;

        public void Tick(float deltaTime)
        {
            if (Penalty <= 0f || deltaTime <= 0f) return;
            Penalty *= Decay(deltaTime, _pattern.RecoveryTime);
            if (Penalty < 1e-4f) Penalty = 0f;
        }

        public void Reset() => Penalty = 0f;

        /// <summary>Штраф движения, мрад, при горизонтальной скорости головы <paramref name="speed"/> м/с.</summary>
        public float MovePenalty(float speed) =>
            _pattern.InaccuracyMove * MoveShare * Mathf.Clamp01((speed - MoveSpeedStart) / (MoveSpeedFull - MoveSpeedStart));

        /// <summary>Радиус неточности следующего выстрела, мрад (без разброса ствола).</summary>
        public float Inaccuracy(float moveSpeed) => _pattern.InaccuracyStand + MovePenalty(moveSpeed) + Penalty;

        /// <summary>Наибольшее отклонение следующего выстрела, мрад: неточность + разброс.</summary>
        public float Cone(float moveSpeed) => Inaccuracy(moveSpeed) + _pattern.Spread;

        /// <summary>Во сколько раз уменьшается штраф за <paramref name="deltaTime"/> при времени восстановления <paramref name="recoveryTime"/>.</summary>
        public static float Decay(float deltaTime, float recoveryTime) =>
            Mathf.Exp(Mathf.Log(RecoveredFraction) * deltaTime / Mathf.Max(0.01f, recoveryTime));

        /// <summary>Потолок штрафа перед выстрелом в бесконечной очереди с интервалом <paramref name="interval"/> с.</summary>
        public float SteadyPenalty(float interval)
        {
            float k = Decay(interval, _pattern.RecoveryTime);
            return k >= 1f ? float.PositiveInfinity : _pattern.InaccuracyFire * k / (1f - k);
        }

        /// <summary>
        /// Смещение выстрела, мрад (x — вправо, y — вверх): неточность (поток 0, общий на залп) + разброс дробины
        /// <paramref name="pellet"/> (поток 1 + номер).
        /// </summary>
        public static Vector2 Offset(float inaccuracy, float spread, uint seed, uint shot, uint pellet = 0) =>
            InaccuracyOffset(inaccuracy, seed, shot) + Disc(spread, seed, shot, 1u + pellet);

        /// <summary>Смещение неточности залпа, мрад: одно на все дробины выстрела.</summary>
        public static Vector2 InaccuracyOffset(float inaccuracy, uint seed, uint shot) => Disc(inaccuracy, seed, shot, 0u);

        /// <summary>Смещение разброса дробины <paramref name="pellet"/>, мрад.</summary>
        public static Vector2 SpreadOffset(float spread, uint seed, uint shot, uint pellet) => Disc(spread, seed, shot, 1u + pellet);

        /// <summary>Поворот относительно дула для смещения в мрад (тангенсы × 1000): +y — вверх, +x — вправо.</summary>
        public static Quaternion ToRotation(Vector2 offsetMrad) =>
            Quaternion.Euler(-Mathf.Atan(offsetMrad.y * 0.001f) * Mathf.Rad2Deg, Mathf.Atan(offsetMrad.x * 0.001f) * Mathf.Rad2Deg, 0f);

        /// <summary>Мрад (тангенс × 1000) → градусы.</summary>
        public static float ToDegrees(float mrad) => Mathf.Atan(mrad * 0.001f) * Mathf.Rad2Deg;

        // Как в CS: радиус равномерно по 0…radius (гуще к центру), угол — равномерно.
        private static Vector2 Disc(float radius, uint seed, uint shot, uint stream)
        {
            if (radius <= 0f) return Vector2.zero;
            float r = Unit(Hash(seed, shot, stream * 2u)) * radius;
            float angle = Unit(Hash(seed, shot, stream * 2u + 1u)) * TwoPi;
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        private static float Unit(uint hash) => (hash >> 8) * (1f / 16777216f);

        // Перемешивание трёх чисел (вариант финализатора murmur3): одинаковый вход — одинаковый выход на любой машине.
        private static uint Hash(uint a, uint b, uint c)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ (c + 0x165667B1u) * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }
    }
}

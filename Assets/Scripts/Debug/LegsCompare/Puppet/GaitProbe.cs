using System.Globalization;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Ритм шагов одной стойки для стенда <c>LegsComparisonRig</c> (удалён) (программа <c>SlowWalk</c>): сколько шагов,
    /// какой длины и сколько скользит опорная стопа. Цель — шаги синхронны со смещением, как у живого человека: длина
    /// шага почти постоянна, частота растёт со скоростью, стопа в опоре стоит.
    ///
    /// <para>
    /// Перенос стопы — пока её кость движется по горизонтали быстрее <see cref="SwingSpeed"/>; кончился — стопа встала.
    /// Перенос короче <see cref="MinStep"/> — дрожь, не шаг. Длина шага — путь стопы за перенос (у человека при ходьбе
    /// ≈ 2 × «шаг между стопами»).
    /// </para>
    /// </summary>
    public sealed class GaitProbe
    {
        /// <summary>Стопа в переносе, если движется быстрее, м/с (стопа в опоре — около 0, в переносе медленного шага ≥ 0,6).</summary>
        public const float SwingSpeed = 0.25f;

        /// <summary>Перенос короче — не шаг, м.</summary>
        public const float MinStep = 0.04f;

        private readonly Transform _left, _right;
        private readonly Foot _l = new Foot(), _r = new Foot();

        private sealed class Foot
        {
            public Vector3 Prev;
            public bool HasPrev, Swing;
            public Vector3 LiftPos;
            public int Steps;
            public float StepSum;
        }

        public GaitProbe(Transform leftFoot, Transform rightFoot)
        {
            _left = leftFoot;
            _right = rightFoot;
        }

        public void ResetSegment()
        {
            foreach (Foot f in new[] { _l, _r })
            {
                f.HasPrev = false;
                f.Swing = false;
                f.Steps = 0;
                f.StepSum = 0f;
            }
        }

        public void Measure(float dt)
        {
            Measure(_l, _left.position, dt);
            Measure(_r, _right.position, dt);
        }

        private static void Measure(Foot f, Vector3 p, float dt)
        {
            if (!f.HasPrev)
            {
                f.Prev = p;
                f.HasPrev = true;
                return;
            }

            float d = Flat(p - f.Prev);
            bool swing = d / Mathf.Max(dt, 1e-4f) > SwingSpeed;
            if (swing && !f.Swing) f.LiftPos = f.Prev;
            if (!swing && f.Swing)
            {
                float step = Flat(p - f.LiftPos);
                if (step >= MinStep)
                {
                    f.Steps++;
                    f.StepSum += step;
                }
            }

            f.Swing = swing;
            f.Prev = p;
        }

        /// <summary>
        /// Строка таблицы: шагов (Л+П), шаг/с, длина шага (путь стопы за перенос), м, шагов на метр пути головы.
        /// Скольжение — не здесь (по кости стопы оно завышено), а в <see cref="FootContactProbe"/>.
        /// </summary>
        public string Summary(float duration, float headDistance)
        {
            int steps = _l.Steps + _r.Steps;
            float length = steps > 0 ? (_l.StepSum + _r.StepSum) / steps : 0f;
            float perM = headDistance > 1e-3f ? steps / headDistance : 0f;
            return string.Format(CultureInfo.InvariantCulture, "{0} | {1:0.00} | {2:0.00} | {3:0.0}",
                steps, steps / Mathf.Max(duration, 1e-3f), length, perM);
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}

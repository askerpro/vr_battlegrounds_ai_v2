using System;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Порог значимого изменения одной метрики. Изменение значимо, только если превышены
    /// <b>оба</b> порога: абсолютный отсекает дрожание малых величин (0,3 → 0,5 мс — это +66%,
    /// но смотреть там не на что), относительный — дрожание больших (310 → 312 батчей).
    /// </summary>
    [Serializable]
    public struct ChangeThreshold
    {
        public float absolute;
        public float relative;

        public ChangeThreshold(float absolute, float relative)
        {
            this.absolute = absolute;
            this.relative = relative;
        }

        public bool IsSignificant(float previous, float current)
        {
            float delta = Math.Abs(current - previous);
            if (delta < absolute) return false;

            float reference = Math.Max(Math.Abs(previous), Math.Abs(current));
            return delta >= relative * reference;
        }
    }

    /// <summary>
    /// Решает, писать ли очередное окно метрик в лог: пишется, только если хоть одна
    /// метрика значимо ушла от <b>последней записанной</b> строки. Сравнение именно
    /// с записанной, а не с предыдущим окном — иначе медленный дрейф (утечка, прогрев)
    /// по 5% за окно не попал бы в лог никогда.
    /// </summary>
    public sealed class PerfChangeFilter
    {
        private readonly ChangeThreshold[] _thresholds;
        private readonly float[] _lastWritten;
        private bool _hasWritten;

        public PerfChangeFilter(ChangeThreshold[] thresholds)
        {
            _thresholds  = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
            _lastWritten = new float[thresholds.Length];
        }

        /// <summary>Значения последней записанной строки. Валидны после первой записи.</summary>
        public float[] LastWritten => _lastWritten;

        public bool HasWritten => _hasWritten;

        /// <summary>
        /// Заполняет <paramref name="changed"/> флагами изменившихся метрик и возвращает,
        /// нужно ли писать строку. Первое окно пишется всегда — это точка отсчёта.
        /// </summary>
        public bool Evaluate(float[] current, bool[] changed)
        {
            bool any = !_hasWritten;

            for (int i = 0; i < _thresholds.Length; i++)
            {
                bool significant = !_hasWritten || _thresholds[i].IsSignificant(_lastWritten[i], current[i]);
                changed[i] = significant;
                any |= significant;
            }

            return any;
        }

        /// <summary>Запоминает строку как записанную.</summary>
        public void Commit(float[] current)
        {
            Array.Copy(current, _lastWritten, _lastWritten.Length);
            _hasWritten = true;
        }

        /// <summary>Сброс на новой фазе: первая строка фазы — снова точка отсчёта.</summary>
        public void Reset()
        {
            _hasWritten = false;
        }
    }
}

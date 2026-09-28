namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Таймер удержания жеста режима отладки: <b>оба стика нажаты</b> <see cref="DefaultHoldSeconds"/>
    /// секунд — одно срабатывание на одно удержание. Отпустили раньше — отсчёт с нуля; держат
    /// дальше — повторно не срабатывает, пока не отпустят.
    ///
    /// <para>
    /// Логика удержания перенесена из прежнего жеста стресс-теста (<c>StressTestLauncher</c>, те же
    /// 2 с) и вынесена в чистый класс: время и состояние кнопок приходят снаружи
    /// (<see cref="DebugGestureInput"/>), поэтому правило проверяется юнит-тестом.
    /// </para>
    /// </summary>
    public sealed class DebugHoldGesture
    {
        public const float DefaultHoldSeconds = 2f;

        private readonly float _holdSeconds;
        private float _heldSince = -1f;
        private bool _fired;

        public DebugHoldGesture() : this(DefaultHoldSeconds) { }

        public DebugHoldGesture(float holdSeconds)
        {
            _holdSeconds = holdSeconds;
        }

        /// <summary>
        /// Кадр: <paramref name="held"/> — оба стика нажаты, <paramref name="now"/> — время.
        /// Истина — ровно в кадр, когда удержание достигло порога.
        /// </summary>
        public bool Update(bool held, float now)
        {
            if (!held)
            {
                _heldSince = -1f;
                _fired = false;
                return false;
            }

            if (_heldSince < 0f) _heldSince = now;
            if (_fired || now - _heldSince < _holdSeconds) return false;

            _fired = true;
            return true;
        }
    }
}

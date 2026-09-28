using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Живой замер кадра режима отладки: среднее и худшее время кадра за окно 0,5 с.
    /// Цифры читает панель управления экрана «Перф-тесты» (<see cref="AverageMs"/>,
    /// <see cref="WorstMs"/>); по кнопке «Оверлей кадра» (экран «Отладка») они же идут на
    /// табличку <see cref="PerfOverlay"/>.
    ///
    /// <para>
    /// Меряет только в режиме отладки. Табличку, пока идёт стресс-тест, не трогает: её занимает
    /// <c>StressTestClientSession</c>. Строка собирается раз в окно — сборка каждый кадр сама
    /// давала бы мусор в замер.
    /// </para>
    /// </summary>
    public sealed class DebugPerfReadout : MonoBehaviour
    {
        private const float Window = 0.5f;

        /// <summary>Показывать ли цифры на табличке.</summary>
        public static bool Visible { get; private set; }

        /// <summary>Среднее время кадра последнего окна, мс; 0 — ещё не мерили.</summary>
        public static float AverageMs { get; private set; }

        /// <summary>Худший кадр последнего окна, мс.</summary>
        public static float WorstMs { get; private set; }

        /// <summary>Строка вида «кадр 14,2 мс (71 FPS) · худший 21,8 мс» или пусто.</summary>
        public static string Format() =>
            AverageMs > 0f ? $"кадр {AverageMs:F1} мс ({1000f / AverageMs:F0} FPS) · худший {WorstMs:F1} мс" : string.Empty;

        private float _sum;
        private float _max;
        private int _frames;
        private float _windowStart;

        public static void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible;
            GameLog.Debug.Info($"[DebugMode] Оверлей кадра {(visible ? "включён" : "выключен")}.");
            if (!visible && !StressTestClientSession.IsRunning) PerfOverlay.Hide();
        }

        private void OnEnable() => DebugMode.Changed += OnDebugModeChanged;

        private void OnDisable() => DebugMode.Changed -= OnDebugModeChanged;

        private static void OnDebugModeChanged(bool enabled)
        {
            if (enabled) return;
            SetVisible(false);
            AverageMs = 0f;
            WorstMs = 0f;
        }

        private void Update()
        {
            if (!DebugMode.Enabled)
            {
                _frames = 0;
                return;
            }

            float ms = Time.unscaledDeltaTime * 1000f;
            if (_frames == 0)
            {
                _sum = 0f;
                _max = 0f;
                _windowStart = Time.unscaledTime;
            }

            _sum += ms;
            if (ms > _max) _max = ms;
            _frames++;

            if (Time.unscaledTime - _windowStart < Window) return;

            AverageMs = _sum / _frames;
            WorstMs = _max;
            _frames = 0;

            if (Visible && !StressTestClientSession.IsRunning) PerfOverlay.Show(Format());
        }
    }
}

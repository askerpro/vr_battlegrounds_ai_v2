// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 66: форма сигнала вибрации — общий ассет.
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateXR.Haptics
{
    /// <summary>
    ///     VR Battlegrounds patch 66: форма сигнала вибрации заданной длины — сила 0..1 по отрезкам времени. Только форма:
    ///     сила, приоритет, повтор и кулдаун задаёт точка интеграции в <see cref="UxrHapticClip" />. Одну форму используют
    ///     многие клипы, поэтому правка формы меняет все места сразу. Частоты нет: на Quest в стеке Unity XR вибрация —
    ///     только амплитуда и длительность.
    /// </summary>
    [CreateAssetMenu(fileName = "Waveform", menuName = "VR Battlegrounds/Haptics/Waveform")]
    public sealed class UxrHapticWaveform : ScriptableObject
    {
        /// <summary>Отрезок формы: сила 0..1 на заданное время. Сила 0 — пауза, она тоже часть формы.</summary>
        [Serializable]
        public struct Segment
        {
            [Range(0, 1)] public float Amplitude;
            [Min(0)]      public int   DurationMs;

            public Segment(float amplitude, int durationMs)
            {
                Amplitude  = amplitude;
                DurationMs = durationMs;
            }
        }

        [SerializeField] private List<Segment> _segments = new List<Segment> { new Segment(1.0f, 100) };

        [Tooltip("Что это за форма и где используется — для инспектора.")]
        [SerializeField] [TextArea] private string _description;

        /// <summary>Отрезки формы.</summary>
        public List<Segment> Segments => _segments;

        public string Description => _description;

        /// <summary>Длина формы, с.</summary>
        public float LengthSeconds
        {
            get
            {
                int ms = 0;
                foreach (Segment segment in _segments) ms += Mathf.Max(0, segment.DurationMs);
                return ms * 0.001f;
            }
        }

        /// <summary>
        ///     Сила формы в момент <paramref name="t" /> от её начала и конец текущего отрезка (с от начала). false — форма
        ///     закончилась.
        /// </summary>
        public bool Sample(float t, out float amplitude, out float segmentEnd)
        {
            float start = 0f;
            foreach (Segment segment in _segments)
            {
                float end = start + Mathf.Max(0, segment.DurationMs) * 0.001f;
                if (t < end)
                {
                    amplitude  = Mathf.Clamp01(segment.Amplitude);
                    segmentEnd = end;
                    return true;
                }
                start = end;
            }

            amplitude  = 0f;
            segmentEnd = start;
            return false;
        }

        /// <summary>Правка ассета в инспекторе: звучащие голоса читают форму заново.</summary>
        public static event Action<UxrHapticWaveform> Changed;

        private void OnValidate() => Changed?.Invoke(this);
    }
}

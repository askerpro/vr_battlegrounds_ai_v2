using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEngine;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Смешивание клипов на моторе руки — чистая логика без Unity-времени: время подаётся снаружи, вывод —
    /// в <see cref="IHapticDevice" />. Клип — <see cref="UxrHapticClip" /> с формой (<see cref="UxrHapticWaveform" />, SDK-патч 66):
    /// форма задаёт силу по времени, клип — силу, приоритет, паузу повтора и кулдаун. Правила
    /// (<c>tasks/haptics-system/Details.md</c>, п. 2):
    /// <list type="bullet">
    /// <item>запущенный клип — голос руки; все голоса идут по настоящему времени, слышны они или нет;</item>
    /// <item>слышен только высший приоритет среди голосов внутри формы; суммы нет — она стирает рисунок;</item>
    /// <item>внутри приоритета: Normal (физика) — максимум амплитуд; остальные — разовый поверх непрерывного, затем самый новый;</item>
    /// <item>разовый низшего приоритета не откладывается: под высшим он идёт беззвучно и заканчивается по своему времени;</item>
    /// <item>непрерывный под высшим беззвучен и продолжает жить; пауза между повторами мотор не занимает;</item>
    /// <item>кулдаун — на пару (клип, рука).</item>
    /// </list>
    /// Голос держит ссылку на ассет формы и читает его каждый тик, поэтому правка формы в инспекторе слышна сразу.
    /// На Quest каждый вызов мотора обрывает текущую вибрацию, поэтому писать в мотор может только микшер.
    /// </summary>
    public sealed class HapticMixer
    {
        /// <summary>Сколько голосов держит рука; лишний вытесняет самый старый разовый.</summary>
        public const int MaxVoicesPerHand = 8;

        /// <summary>Запас длительности импульса сверх конца отрезка: перекрывает задержку до следующего тика.</summary>
        public const float ImpulseMargin = 0.02f;

        private sealed class Voice
        {
            public int Id;
            public UxrHapticWaveform Waveform;
            public UxrHapticPriority Priority;
            public float Gain;
            public float GapSeconds;
            public float Start;
            public bool Continuous;
        }

        private sealed class Hand
        {
            public readonly List<Voice> Voices = new List<Voice>(MaxVoicesPerHand);
            public float SentAmplitude;
            public float SentUntil;
        }

        private readonly IHapticDevice _device;
        private readonly Hand[] _hands = { new Hand(), new Hand() };
        private readonly Dictionary<(UxrHapticClip, UxrHandSide), float> _lastStart = new Dictionary<(UxrHapticClip, UxrHandSide), float>();
        private int _nextId = 1;

        public HapticMixer(IHapticDevice device) => _device = device;

        /// <summary>
        /// Запустить клип на руке. <paramref name="continuous" /> — повторять форму с паузой клипа, пока не вызван
        /// <see cref="End" />. <paramref name="gain" /> умножает силу клипа. Возвращает id голоса или 0, если запуск отброшен
        /// (нет формы, пустая форма, нулевая сила, кулдаун). Вывод на мотор — сразу: отдача должна совпасть с выстрелом.
        /// </summary>
        public int Play(UxrHapticClip clip, UxrHandSide side, float gain, bool continuous, float now)
        {
            if (clip == null || !clip.HasWaveform || clip.Waveform.LengthSeconds <= 0f) return 0;
            float total = clip.WaveformGain * gain;
            if (total <= 0f) return 0;

            var key = (clip, side);
            if (clip.CooldownSeconds > 0f && _lastStart.TryGetValue(key, out float last) && now - last < clip.CooldownSeconds)
                return 0;
            _lastStart[key] = now;

            Hand hand = _hands[(int)side];
            if (hand.Voices.Count >= MaxVoicesPerHand) EvictOldest(hand);

            var voice = new Voice
            {
                Id = _nextId++, Waveform = clip.Waveform, Priority = clip.Priority, Gain = total,
                GapSeconds = clip.RepeatGapMs * 0.001f, Start = now, Continuous = continuous
            };
            hand.Voices.Add(voice);
            Output(side, now, force: true);
            return voice.Id;
        }

        /// <summary>Остановить голос (непрерывный или ещё звучащий разовый).</summary>
        public void End(int voiceId, float now)
        {
            for (int h = 0; h < _hands.Length; h++)
            {
                List<Voice> voices = _hands[h].Voices;
                for (int i = 0; i < voices.Count; i++)
                {
                    if (voices[i].Id != voiceId) continue;
                    voices.RemoveAt(i);
                    Output((UxrHandSide)h, now, force: false);
                    return;
                }
            }
        }

        public bool IsActive(int voiceId)
        {
            foreach (Hand hand in _hands)
                foreach (Voice voice in hand.Voices)
                    if (voice.Id == voiceId) return true;
            return false;
        }

        /// <summary>Погасить все голоса обеих рук и моторы.</summary>
        public void StopAll()
        {
            for (int h = 0; h < _hands.Length; h++)
            {
                _hands[h].Voices.Clear();
                _hands[h].SentAmplitude = 0f;
                _hands[h].SentUntil = 0f;
                _device.Stop((UxrHandSide)h);
            }
        }

        /// <summary>Продвинуть время: убрать закончившиеся голоса и обновить моторы.</summary>
        public void Tick(float now)
        {
            for (int h = 0; h < _hands.Length; h++)
            {
                List<Voice> voices = _hands[h].Voices;
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    Voice voice = voices[i];
                    bool gone = voice.Waveform == null || voice.Waveform.LengthSeconds <= 0f;
                    if (gone || (!voice.Continuous && now - voice.Start >= voice.Waveform.LengthSeconds)) voices.RemoveAt(i);
                }
                Output((UxrHandSide)h, now, force: false);
            }
        }

        /// <summary>Правка формы в инспекторе: переслать моторам новые значения без ожидания смены отрезка.</summary>
        public void Refresh(float now)
        {
            for (int h = 0; h < _hands.Length; h++) Output((UxrHandSide)h, now, force: true);
        }

        /// <summary>Сколько голосов на руке (включая беззвучные под высшим приоритетом) — для отладки.</summary>
        public int VoiceCount(UxrHandSide side) => _hands[(int)side].Voices.Count;

        /// <summary>Слышимая амплитуда руки в момент <paramref name="now" /> (до записи в мотор) — для тестов и отладки.</summary>
        public float Evaluate(UxrHandSide side, float now) => Evaluate(_hands[(int)side], now, out _);

        private void Output(UxrHandSide side, float now, bool force)
        {
            Hand hand = _hands[(int)side];
            float amplitude = Evaluate(hand, now, out float changeAt);

            if (amplitude <= 0f)
            {
                if (hand.SentAmplitude > 0f) _device.Stop(side);
                hand.SentAmplitude = 0f;
                hand.SentUntil = 0f;
                return;
            }

            bool changed = !Mathf.Approximately(amplitude, hand.SentAmplitude);
            bool expiring = now >= hand.SentUntil - ImpulseMargin;
            if (!force && !changed && !expiring) return;

            float seconds = Mathf.Max(changeAt - now + ImpulseMargin, ImpulseMargin);
            _device.Send(side, amplitude, seconds);
            hand.SentAmplitude = amplitude;
            hand.SentUntil = now + seconds;
        }

        /// <summary>Амплитуда руки и момент, когда она может измениться (конец текущего отрезка у звучащих голосов).</summary>
        private static float Evaluate(Hand hand, float now, out float changeAt)
        {
            changeAt = float.PositiveInfinity;

            UxrHapticPriority top = 0;
            foreach (Voice voice in hand.Voices)
            {
                if (!Sample(voice, now, out _, out float end, out bool occupied)) continue;
                changeAt = Mathf.Min(changeAt, end);
                if (occupied && voice.Priority > top) top = voice.Priority;
            }
            if (top == 0) return 0f;

            if (top == UxrHapticPriority.Normal)
            {
                float max = 0f;
                foreach (Voice voice in hand.Voices)
                    if (voice.Priority == top && Sample(voice, now, out float a, out _, out bool occupied) && occupied)
                        max = Mathf.Max(max, a * voice.Gain);
                return Mathf.Clamp01(max);
            }

            Voice winner = null;
            foreach (Voice voice in hand.Voices)
            {
                if (voice.Priority != top || !Sample(voice, now, out _, out _, out bool occupied) || !occupied) continue;
                if (winner == null || (winner.Continuous && !voice.Continuous) ||
                    (winner.Continuous == voice.Continuous && voice.Id > winner.Id))
                    winner = voice;
            }
            Sample(winner, now, out float amplitude, out _, out _);
            return Mathf.Clamp01(amplitude * winner.Gain);
        }

        /// <summary>
        /// Состояние голоса в момент <paramref name="now" />. false — голос закончился. <paramref name="occupied" /> — голос
        /// внутри формы (включая паузы формы), а не в паузе между повторами. <paramref name="end" /> — абсолютное время
        /// следующей смены отрезка или начала повтора.
        /// </summary>
        private static bool Sample(Voice voice, float now, out float amplitude, out float end, out bool occupied)
        {
            UxrHapticWaveform waveform = voice.Waveform;
            amplitude = 0f;
            occupied = false;
            end = float.PositiveInfinity;
            if (waveform == null) return false;

            float length = waveform.LengthSeconds;
            if (length <= 0f) return false;

            float local = Mathf.Max(0f, now - voice.Start);
            float cycleStart = voice.Start;
            float period = length + voice.GapSeconds;

            if (voice.Continuous)
            {
                float cycles = Mathf.Floor(local / period);
                cycleStart += cycles * period;
                local -= cycles * period;
            }

            if (waveform.Sample(local, out amplitude, out float segmentEnd))
            {
                occupied = true;
                end = cycleStart + segmentEnd;
                return true;
            }

            if (!voice.Continuous) return false;
            end = cycleStart + period;
            return true;
        }

        private static void EvictOldest(Hand hand)
        {
            int index = hand.Voices.FindIndex(v => !v.Continuous);
            hand.Voices.RemoveAt(index >= 0 ? index : 0);
        }
    }
}

using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Снимок позы локального аватара за кадр — ровно то, что remote-аватар получает
    /// по сети: корень, голова и две кисти (цели <c>NetworkTransform</c>) плюс позы пальцев.
    ///
    /// <para>
    /// Голова и кисти хранятся в координатах корня аватара, корень — относительно корня
    /// на старте прогона. Так снимок переносится на куклу, стоящую в другом месте:
    /// кукла повторяет движения, а не координаты.
    /// </para>
    /// </summary>
    public struct AvatarPoseSample
    {
        public Vector3    rootPosition;
        public Quaternion rootRotation;
        public Vector3    headPosition;
        public Quaternion headRotation;
        public Vector3    leftHandPosition;
        public Quaternion leftHandRotation;
        public Vector3    rightHandPosition;
        public Quaternion rightHandRotation;
        public string     leftHandPose;
        public float      leftHandBlend;
        public string     rightHandPose;
        public float      rightHandBlend;
    }

    /// <summary>
    /// Кольцевой буфер поз с отметкой времени. Каждая кукла читает его со своей задержкой:
    /// десять одинаковых движений в один кадр — неправдоподобная нагрузка (все выстрелы
    /// и все смены поз сошлись бы в одном кадре), а разнесённые по времени похожи на живых.
    /// </summary>
    public sealed class PoseDelayBuffer
    {
        private readonly float[]            _times;
        private readonly AvatarPoseSample[] _samples;
        private int _head;   // куда писать следующий
        private int _count;

        public PoseDelayBuffer(int capacity)
        {
            capacity = Mathf.Max(2, capacity);
            _times   = new float[capacity];
            _samples = new AvatarPoseSample[capacity];
        }

        public int Count => _count;

        public void Clear()
        {
            _head  = 0;
            _count = 0;
        }

        /// <summary>Добавляет снимок. Время обязано не убывать.</summary>
        public void Add(float time, AvatarPoseSample sample)
        {
            _times[_head]   = time;
            _samples[_head] = sample;
            _head = (_head + 1) % _times.Length;
            if (_count < _times.Length) _count++;
        }

        /// <summary>
        /// Последний снимок, снятый не позже <paramref name="time"/>. Если запрошено время
        /// старше всего буфера — самый старый снимок: кукла на старте стоит в первой позе,
        /// пока не наберётся её задержка.
        /// </summary>
        public bool TrySample(float time, out AvatarPoseSample sample)
        {
            sample = default;
            if (_count == 0) return false;

            // Идём от свежего к старому: задержки короткие, нужный снимок близко к голове.
            for (int i = 1; i <= _count; i++)
            {
                int index = (_head - i + _times.Length) % _times.Length;
                if (_times[index] <= time)
                {
                    sample = _samples[index];
                    return true;
                }
            }

            int oldest = (_head - _count + _times.Length) % _times.Length;
            sample = _samples[oldest];
            return true;
        }
    }
}

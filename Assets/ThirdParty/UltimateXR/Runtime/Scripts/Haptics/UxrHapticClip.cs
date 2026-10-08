// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrHapticClip.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using UnityEngine;

namespace UltimateXR.Haptics
{
    /// <summary>
    ///     Describes a haptic clip. It is possible to specify an audio clip whose wave will be used as a primary source for
    ///     the vibration, but also a secondary clip type that will be used if the device doesn't support audio clips as haptic
    ///     feedback.
    ///     If no audio clip is specified, the fallback clip type will always be used.
    /// </summary>
    [Serializable]
    public class UxrHapticClip
    {
        #region Inspector Properties/Serialized Fields

        [SerializeField]               private AudioClip         _clip;
        [SerializeField] [Range(0, 1)] private float             _clipAmplitude           = 1.0f;
        [SerializeField]               private UxrHapticMode     _hapticMode              = UxrHapticMode.Mix;
        [SerializeField]               private UxrHapticClipType _fallbackClipType        = UxrHapticClipType.None;
        [SerializeField] [Range(0, 1)] private float             _fallbackAmplitude       = 1.0f;
        [SerializeField]               private float             _fallbackDurationSeconds = -1.0f;

        // VR Battlegrounds patch 66: форма сигнала-ассет и параметры воспроизведения в точке интеграции. Клип с формой
        // играет игровой сервис вибрации (приоритет, повтор, кулдаун); поля выше — прежний путь SDK, когда формы нет.
        [SerializeField]               private UxrHapticWaveform _waveform;
        [SerializeField] [Range(0, 1)] private float             _waveformGain       = 1.0f;
        [SerializeField]               private UxrHapticPriority _priority           = UxrHapticPriority.Normal;
        [SerializeField] [Min(0)]      private int               _repeatGapMs;
        [SerializeField] [Min(0)]      private float             _cooldownSeconds;
        [SerializeField] [Range(0, 1)] private float             _secondaryHandGain  = 1.0f;

        #endregion

        #region Public Types & Data

        /// <summary>
        ///     Gets or sets the primary <see cref="AudioClip" /> to use as source for vibration. If the device does not support
        ///     audio
        ///     clips as sources or this value is null, <see cref="FallbackClipType" /> will be used.
        /// </summary>
        public AudioClip Clip
        {
            get => _clip;
            set => _clip = value;
        }

        /// <summary>
        ///     Gets or sets the amplitude to play <see cref="Clip" />. Valid range is [0.0, 1.0].
        /// </summary>
        public float ClipAmplitude
        {
            get => _clipAmplitude;
            set => _clipAmplitude = value;
        }

        /// <summary>
        ///     Gets or sets whether to replace or mix the clip with any current haptic feedback being played.
        /// </summary>
        public UxrHapticMode HapticMode
        {
            get => _hapticMode;
            set => _hapticMode = value;
        }

        /// <summary>
        ///     Gets or sets the fallback clip: A value from a pre-defined set of procedurally generated haptic feedback clips. It
        ///     will be
        ///     used if the current device can't play <see cref="AudioClip" /> as haptics or <see cref="Clip" /> is not assigned.
        /// </summary>
        public UxrHapticClipType FallbackClipType
        {
            get => _fallbackClipType;
            set => _fallbackClipType = value;
        }

        /// <summary>
        ///     Gets or sets the amplitude to play the fallback clip (1.0f = use default).
        /// </summary>
        public float FallbackAmplitude
        {
            get => _fallbackAmplitude;
            set => _fallbackAmplitude = value;
        }

        /// <summary>
        ///     Gets or sets the duration in seconds of the fallback clip (negative = use predefined).
        /// </summary>
        public float FallbackDurationSeconds
        {
            get => _fallbackDurationSeconds;
            set => _fallbackDurationSeconds = value;
        }

        /// <summary>
        ///     VR Battlegrounds patch 66: форма сигнала (общий ассет). Задана — клип играет игровой сервис вибрации, а поля
        ///     <see cref="Clip" /> и Fallback* не используются.
        /// </summary>
        public UxrHapticWaveform Waveform
        {
            get => _waveform;
            set => _waveform = value;
        }

        /// <summary>VR Battlegrounds patch 66: есть ли у клипа форма.</summary>
        public bool HasWaveform => _waveform != null;

        /// <summary>VR Battlegrounds patch 66: множитель силы формы в этой точке интеграции, 0..1.</summary>
        public float WaveformGain
        {
            get => _waveformGain;
            set => _waveformGain = Mathf.Clamp01(value);
        }

        /// <summary>VR Battlegrounds patch 66: приоритет на моторе руки; слышен высший среди звучащих.</summary>
        public UxrHapticPriority Priority
        {
            get => _priority;
            set => _priority = value;
        }

        /// <summary>
        ///     VR Battlegrounds patch 66: пауза между повторами формы, мс, когда сигнал держится непрерывно (пока длится
        ///     состояние). Разовые события играют форму один раз.
        /// </summary>
        public int RepeatGapMs
        {
            get => _repeatGapMs;
            set => _repeatGapMs = Mathf.Max(0, value);
        }

        /// <summary>VR Battlegrounds patch 66: повторный запуск этого клипа на той же руке раньше срока отбрасывается, с.</summary>
        public float CooldownSeconds
        {
            get => _cooldownSeconds;
            set => _cooldownSeconds = Mathf.Max(0f, value);
        }

        /// <summary>VR Battlegrounds patch 66: множитель силы для второй руки на том же предмете, 0..1.</summary>
        public float SecondaryHandGain
        {
            get => _secondaryHandGain;
            set => _secondaryHandGain = Mathf.Clamp01(value);
        }

        #endregion

        #region Constructors & Finalizer

        /// <summary>
        ///     VR Battlegrounds patch 66: конструктор без параметров, чтобы Unity при создании вложенного экземпляра
        ///     выполнял инициализаторы полей (сила формы 1, приоритет Normal).
        /// </summary>
        public UxrHapticClip() : this(null)
        {
        }

        /// <summary>
        ///     Public constructor.
        /// </summary>
        /// <param name="clip">The audio clip</param>
        /// <param name="fallbackClipType">The fallback clip if the primary audio clip is null</param>
        /// <param name="hapticMode">The haptic mixing mode</param>
        /// <param name="clipAmplitude">The amplitude of the audio clip</param>
        /// <param name="fallbackAmplitude">The amplitude of the fallback clip</param>
        /// <param name="fallbackDurationSeconds">The duration in seconds of the fallback clip (negative = use predefined)</param>
        public UxrHapticClip(AudioClip         clip                    = null,
                             UxrHapticClipType fallbackClipType        = UxrHapticClipType.None,
                             UxrHapticMode     hapticMode              = UxrHapticMode.Mix,
                             float             clipAmplitude           = 1.0f,
                             float             fallbackAmplitude       = 1.0f,
                             float             fallbackDurationSeconds = -1.0f)
        {
            Clip                    = clip;
            FallbackClipType        = fallbackClipType;
            HapticMode              = hapticMode;
            ClipAmplitude           = clipAmplitude;
            FallbackAmplitude       = fallbackAmplitude;
            FallbackDurationSeconds = fallbackDurationSeconds;
        }

        #endregion
    }
}
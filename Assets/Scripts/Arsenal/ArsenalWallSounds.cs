using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Звуки стены арсенала: решётка и полки едут со звуком, закрытая стена щёлкает засовом. Слушает
    /// <see cref="ArsenalAnimator.SequenceStarted"/>/<see cref="ArsenalAnimator.SequenceCompleted"/> — анимация идёт на каждой
    /// машине (состояние стены реплицируется), поэтому и звук у всех без отдельной синхронизации.
    ///
    /// <para>
    /// Решётка и полки — два источника: клип один на всю анимацию (1,5 с), и при развороте посреди хода прежний звук
    /// обрывается (<c>Stop</c> + <c>Play</c>), а не наслаивается. Засов — <c>PlayOneShot</c> по завершении закрытия.
    /// </para>
    /// Проверка — <c>ArsenalSoundsTests</c>.
    /// </summary>
    [RequireComponent(typeof(ArsenalAnimator))]
    public sealed class ArsenalWallSounds : MonoBehaviour
    {
        [Tooltip("Источник решётки (центр стены).")]
        [SerializeField] private AudioSource _shutterSource;
        [Tooltip("Источник полок (у ShelfRoot).")]
        [SerializeField] private AudioSource _shelfSource;

        [SerializeField] private AudioClip _shutterOpen;
        [SerializeField] private AudioClip _shutterClose;
        [SerializeField] private AudioClip _shelfOpen;
        [SerializeField] private AudioClip _shelfClose;
        [Tooltip("Щелчок засова, когда стена закрылась.")]
        [SerializeField] private AudioClip _latchClosed;

        /// <summary>Сыгран звук стены. Для тестов и отладки.</summary>
        public static event Action<ArsenalWallSounds, AudioClip> Played;

        public AudioSource ShutterSource => _shutterSource;
        public AudioSource ShelfSource => _shelfSource;

        /// <summary>Все источники и клипы заданы.</summary>
        public bool IsConfigured => _shutterSource != null && _shelfSource != null && _shutterOpen != null && _shutterClose != null
                                    && _shelfOpen != null && _shelfClose != null && _latchClosed != null;

        private ArsenalAnimator _animator;

        private void Awake() => _animator = GetComponent<ArsenalAnimator>();

        private void OnEnable()
        {
            if (_animator == null) _animator = GetComponent<ArsenalAnimator>();
            _animator.SequenceStarted += OnSequenceStarted;
            _animator.SequenceCompleted += OnSequenceCompleted;
        }

        private void OnDisable()
        {
            _animator.SequenceStarted -= OnSequenceStarted;
            _animator.SequenceCompleted -= OnSequenceCompleted;
        }

        private void OnSequenceStarted(bool open)
        {
            Restart(_shutterSource, open ? _shutterOpen : _shutterClose);
            Restart(_shelfSource, open ? _shelfOpen : _shelfClose);
        }

        private void OnSequenceCompleted(bool open)
        {
            if (open || _shutterSource == null || _latchClosed == null) return;
            _shutterSource.PlayOneShot(_latchClosed);
            Played?.Invoke(this, _latchClosed);
        }

        private void Restart(AudioSource source, AudioClip clip)
        {
            if (source == null || clip == null) return;
            source.Stop();
            source.clip = clip;
            source.Play();
            Played?.Invoke(this, clip);
            GameLog.Arsenal.Verbose($"[ArsenalWallSounds] {name}: {clip.name}", this);
        }
    }
}

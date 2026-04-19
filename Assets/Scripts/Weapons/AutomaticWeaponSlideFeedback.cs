using UltimateXR.Audio;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    ///     Отслеживает движение граббабельного затвора по локальной оси (как <see cref="UxrShotgunPump" />).
    ///     При <see cref="_chamberRoundOnSlideReturn" /> на обратном ходе вызывается <see cref="UxrFirearmWeapon.Reload" />.
    ///     Для Semi/Fully с флагом Use Has Reloaded For Semi And Full Auto в UltimateXR «патрон в патроннике» сбрасывается при
    ///     смене магазина, а не после каждого выстрела.
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    public sealed class AutomaticWeaponSlideFeedback : UxrComponent
    {
        #region Inspector

        [SerializeField] private int _triggerIndex;

        [Tooltip("Граббабл цевья / рукояти затвора (дочерний объект с UxrGrabbableObject).")]
        [SerializeField] private UxrGrabbableObject _slide;

        [Tooltip("Локальное направление оси хода затвора (достаточно оси; будет нормализовано). Должно совпадать с осью Translation в UxrGrabbableObject.")]
        [SerializeField] private Vector3 _localSlideDirection = Vector3.forward;

        [Tooltip("Эталонная длина полного хода (используется только |вектор|). Задай ≈ модулю Translation Offset Min/Max по этой оси, иначе порог Slide Threshold будет промахиваться.")]
        [SerializeField] private Vector3 _localSlideReferenceOffset = Vector3.forward * 0.2f;

        [SerializeField] [Range(0f, 1f)] private float _slideThreshold = 0.7f;

        [Tooltip("Автовозврат затвора в позицию покоя, когда его отпустили.")]
        [SerializeField] private bool _autoReturnOnRelease = true;

        [Tooltip("Скорость автовозврата затвора (м/с вдоль оси хода).")]
        [SerializeField] [Min(0f)] private float _autoReturnSpeed = 1.5f;

        [Tooltip("Вколоть патрон (Reload): на возврате затвора в переднее положение. Работает вместе с UxrShotCycle.ManualReload.")]
        [SerializeField] private bool _chamberRoundOnSlideReturn = true;

        [Tooltip("Писать в консоль этапы цикла затвора (оттяжка, возврат, завершение перезарядки).")]
        [SerializeField] private bool _logBoltCycle;

        [SerializeField] private UxrAudioSample _audioSlideForward = new UxrAudioSample();

        [SerializeField] private UxrAudioSample _audioSlideBack = new UxrAudioSample();

        [SerializeField] private UxrAudioSample _audioSlideForwardWhenLoaded = new UxrAudioSample();

        [SerializeField] private UxrAudioSample _audioSlideBackWhenLoaded = new UxrAudioSample();

        [SerializeField] private UxrHapticClip _hapticForward = new UxrHapticClip(null, UxrHapticClipType.Click);

        [SerializeField] private UxrHapticClip _hapticBack = new UxrHapticClip(null, UxrHapticClipType.Click);

        [SerializeField] private UxrHapticClip _hapticForwardWhenLoaded = new UxrHapticClip(null, UxrHapticClipType.Slide);

        [SerializeField] private UxrHapticClip _hapticBackWhenLoaded = new UxrHapticClip(null, UxrHapticClipType.Slide);

        #endregion

        #region Unity

        protected override void Awake()
        {
            base.Awake();

            _state   = SlideState.WaitForward;
            _firearm = GetComponent<UxrFirearmWeapon>();
            CaptureSlideRestLocalPosition();
        }

        private void Start()
        {
            // После инициализации UXR позиция покоя затвора может отличаться от кадра Awake.
            CaptureSlideRestLocalPosition();
        }

        private void LateUpdate()
        {
            if (_slide == null)
            {
                return;
            }

            float denom = _localSlideReferenceOffset.magnitude;
            if (denom < 1e-5f)
            {
                return;
            }

            Vector3 dir = _localSlideDirection;
            if (dir.sqrMagnitude < 1e-8f)
            {
                return;
            }

            dir.Normalize();

            bool isGrabbed = UxrGrabManager.Instance != null && UxrGrabManager.Instance.IsBeingGrabbed(_slide);

            if (_autoReturnOnRelease && !isGrabbed)
            {
                ApplyAutoReturn(dir);
            }

            Vector3 delta   = _slide.transform.localPosition - _localStart;
            float   absDist = Mathf.Abs(Vector3.Dot(delta, dir));
            float   current = absDist / denom;

            // Лог во время взаимодействия (когда игрок держит затвор)
            if (_logBoltCycle && isGrabbed)
            {
                if (Time.time >= _nextLogTime)
                {
                    _nextLogTime = Time.time + 1.0f;
                    LogBoltCycle($"Тяга: Абс={absDist:F3}м, Норм={current:P0} (Порог={_slideThreshold:P0})");
                }
            }

            if (_state == SlideState.WaitForward && current > _slideThreshold)
            {
                _state = SlideState.WaitBack;

                bool loaded = _firearm != null && _firearm.IsLoaded(_triggerIndex);
                PlayForwardFeedback(loaded);
                LogBoltCycle($"Затвор оттянут! Переход в WaitBack. (Норм={current:F2})");
            }
            else if (_state == SlideState.WaitBack && current < _slideThreshold * 0.9f)
            {
                bool loaded = _firearm != null && _firearm.IsLoaded(_triggerIndex);
                PlayBackFeedback(loaded);

                if (_chamberRoundOnSlideReturn && _firearm != null)
                {
                    _firearm.Reload(_triggerIndex);
                    LogBoltCycle($"Затвор возвращён! Перезарядка (Reload) выполнена. (Норм={current:F2})");
                }
                else
                {
                    LogBoltCycle($"Затвор возвращён! (Норм={current:F2})");
                }

                _state = SlideState.WaitForward;
            }
        }

        #endregion

        #region Private methods

        private void PlayForwardFeedback(bool loaded)
        {
            Vector3 pos = _slide.transform.position;

            if (loaded && HasClip(_audioSlideForwardWhenLoaded))
            {
                _audioSlideForwardWhenLoaded.Play(pos);
            }
            else
            {
                _audioSlideForward.Play(pos);
            }

            TryHaptic(loaded ? _hapticForwardWhenLoaded : _hapticForward);
        }

        private void PlayBackFeedback(bool loaded)
        {
            Vector3 pos = _slide.transform.position;

            if (loaded && HasClip(_audioSlideBackWhenLoaded))
            {
                _audioSlideBackWhenLoaded.Play(pos);
            }
            else
            {
                _audioSlideBack.Play(pos);
            }

            TryHaptic(loaded ? _hapticBackWhenLoaded : _hapticBack);
        }

        private void TryHaptic(UxrHapticClip clip)
        {
            if (UxrGrabManager.Instance == null || !UxrGrabManager.Instance.GetGrabbingHand(_slide, 0, out UxrGrabber grabber) ||
                grabber.Avatar.AvatarMode != UxrAvatarMode.Local)
            {
                return;
            }

            UxrAvatar.LocalAvatarInput.SendHapticFeedback(grabber.Side, clip);
        }

        private void ApplyAutoReturn(Vector3 direction)
        {
            if (_autoReturnSpeed <= 0f)
            {
                return;
            }

            Vector3 localPosition = _slide.transform.localPosition;
            float   axisDelta     = Vector3.Dot(_localStart - localPosition, direction);
            float   step          = _autoReturnSpeed * Time.deltaTime;

            if (Mathf.Abs(axisDelta) <= step)
            {
                _slide.transform.localPosition = localPosition + direction * axisDelta;
                return;
            }

            _slide.transform.localPosition = localPosition + direction * Mathf.Sign(axisDelta) * step;
        }

        private static bool HasClip(UxrAudioSample sample)
        {
            return sample != null && sample.Clip != null;
        }

        private void LogBoltCycle(string message)
        {
            if (!_logBoltCycle)
            {
                return;
            }

            if (GameSettings.Instance != null)
            {
                GameLog.Info(GameSettings.Instance.LogLevelWeaponSystem, $"[Bolt][{gameObject.name}] {message}", this);
            }
            else
            {
                GameLog.Info(LogLevel.Info, $"[Bolt][{gameObject.name}] {message}", this);
            }
        }

        private void CaptureSlideRestLocalPosition()
        {
            _localStart = _slide != null ? _slide.transform.localPosition : Vector3.zero;
        }

        #endregion

        #region Private data

        private enum SlideState
        {
            WaitForward,
            WaitBack
        }

        private UxrFirearmWeapon _firearm;
        private Vector3          _localStart;
        private SlideState       _state;
        private float            _nextLogTime;

        #endregion
    }
}

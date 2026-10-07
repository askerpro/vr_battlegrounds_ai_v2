using UltimateXR.Audio;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using System;

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

        [Tooltip("Граббабл цевья / рукояти затвора (дочерний объект с UxrGrabbableObject). Ось и длина хода берутся из его Translation Limits (режим Restrict Local Offset).")]
        [SerializeField] private UxrGrabbableObject _slide;

        [Tooltip("Доля полного хода затвора, после которой оттяжка засчитана.")]
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

        #region Public types & data

        public float SlideThreshold => _slideThreshold;
        public UxrGrabbableObject Slide => _slide;
        public Vector3 RestLocalPosition => _localStart;
        public float PhysicalPositionEpsilon => FrontPositionEpsilon();
        public float AutoReturnSpeed => _autoReturnSpeed;
        /// <summary>Пружина возвращает отпущенную ручку (у помпы FABARM — нет; S4 плана WeaponSystem).</summary>
        public bool AutoReturnOnRelease => _autoReturnOnRelease;
        public float SlideTravelLength => TryGetSlideTravel(_slide, out _, out float length) ? length : 0f;
        public bool IsActionHeld => _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
        public float SignedSlideProgress => TryGetSlideTravel(_slide, out Vector3 axis, out float length)
            ? Vector3.Dot(_slide.transform.localPosition - _localStart, axis) / length : 0f;
        private WeaponReadinessController ReadinessAdapter => GetComponent<WeaponReadinessController>();
        private bool HasLedgerAdapter => ReadinessAdapter != null && ReadinessAdapter.IsConfigured && _firearm.UsesReadinessLedger(_triggerIndex);

        public void NotifyLedgerManualCompletion()
        {
            if (!HasLedgerAdapter || !_firearm.HasChamberRound(_triggerIndex)) return;
            ClearLedgerVisualHold();
            PlayBackFeedback(true);
            ManualCycleCompleted?.Invoke();
        }

        public void ClearLedgerVisualHold()
        {
            if (!HasLedgerAdapter) return;
            _cosmeticHold = false;
            CancelManualCycle(); // Только legacy gesture fields, не pose/SDK ledger.
        }

        public bool TryGetNumericalEndpointDiagnostics(out float coordinate, out float length, out float epsilon)
        {
            coordinate = 0f; epsilon = 0f;
            if (!TryGetSlideTravel(_slide, out _, out length) || !IsFinite(length) || length <= 0f ||
                !IsFinite(_localStart.x) || !IsFinite(_localStart.y) || !IsFinite(_localStart.z)) return false;
            coordinate = Mathf.Max(Mathf.Abs(_localStart.x), Mathf.Abs(_localStart.y), Mathf.Abs(_localStart.z), length);
            epsilon = EndpointRoundingAllowanceUnits * SinglePrecisionRelativeSpacing * coordinate;
            // Invalid/extreme диапазон не превращаем в широкий физический endpoint.
            // Ratio/coordinate выводит temporary preflight; это не Transform error bound.
            return IsFinite(coordinate) && IsFinite(epsilon) && epsilon > 0f && epsilon < length && epsilon * epsilon > 0f;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private const float SinglePrecisionRelativeSpacing = 1.192092896e-7f; // 2^-23, не System.Single.Epsilon.
        private const float EndpointRoundingAllowanceUnits = 8f; // Эвристика, не доказанный operation-count bound.
        public event Action ManualCycleCompleted;

        /// <summary>Передаёт законченную открытую визуальную позу физическому механизму без зачёта тяги.</summary>
        public void HoldVisualPose(Vector3 localOffset)
        {
            if (_slide == null) return;
            _slide.transform.localPosition = _localStart + localOffset;
            CancelManualCycle();
            _cosmeticHold = true;
        }

        /// <summary>Визуальное сопряжение деталей не отменяет уже начатый ручной возврат.</summary>
        public void RequireManualCatch(float progress)
        {
            // Положение отдельной детали проверяется физическим endpoint визуальной системы.
            // Передача открытого затвора руке не вводит новую оттяжку или процентный порог.
        }

        public void BeginVisualHandoff(Vector3 localOffset)
        {
            // Перехват существующей ручной тяги не сбрасывает зачтённый обратный ход.
            if (_slide == null || (!_cosmeticHold && localOffset.sqrMagnitude < 1e-10f)) return;
            _slide.transform.localPosition += localOffset;
            _cosmeticHold = false;
            // Зачтённый ручной возврат сохраняется при передаче визуальной позы.
            // Отдельные Action-детали должны самостоятельно достичь штатного положения.
        }

        public void CancelVisualHold()
        {
            if (!HasLedgerAdapter && _cosmeticHold && _slide != null) _slide.transform.localPosition = _localStart;
            _cosmeticHold = false;
            CancelManualCycle();
        }

        /// <summary>
        ///     Ось и длина полного хода затвора — из <c>Translation Limits</c> его граббабла, чтобы ход
        ///     задавался в одном месте. Полный ход — более длинный из векторов Min/Max: затвор ходит
        ///     от покоя в одну сторону. Ложь, если ход не ограничен локальным смещением или нулевой.
        /// </summary>
        public static bool TryGetSlideTravel(UxrGrabbableObject slide, out Vector3 direction, out float length)
        {
            direction = Vector3.zero;
            length    = 0f;

            if (slide == null || slide.TranslationConstraint != UxrTranslationConstraintMode.RestrictLocalOffset)
            {
                return false;
            }

            Vector3 min    = slide.TranslationLimitsMin;
            Vector3 max    = slide.TranslationLimitsMax;
            Vector3 travel = min.sqrMagnitude >= max.sqrMagnitude ? min : max;

            length = travel.magnitude;
            if (length < 1e-5f)
            {
                return false;
            }

            direction = travel / length;
            return true;
        }

        /// <summary>Доля пройденного хода затвора относительно позиции покоя, 0…1.</summary>
        public float GetSlideProgress()
        {
            if (!TryGetSlideTravel(_slide, out Vector3 direction, out float length))
            {
                return 0f;
            }

            return Mathf.Abs(Vector3.Dot(_slide.transform.localPosition - _localStart, direction)) / length;
        }

        #endregion

        #region Unity

        protected override void Awake()
        {
            base.Awake();

            _state   = SlideState.WaitForward;
            _firearm = GetComponent<UxrFirearmWeapon>();
            CaptureSlideRestLocalPosition();
            if (_slide != null) _slide.Grabbing += HandleSlideGrabbing;

            if (_slide != null && !TryGetSlideTravel(_slide, out _, out _))
            {
                GameLog.WeaponSystem.Warning($"[Bolt][{gameObject.name}] У затвора '{_slide.name}' нет хода: нужен Translation Constraint = Restrict Local Offset и ненулевые Translation Limits. Перезарядка затвором отключена.", this);
            }
        }

        private void Start()
        {
            // После инициализации UXR позиция покоя затвора может отличаться от кадра Awake.
            CaptureSlideRestLocalPosition();
        }

        private void LateUpdate()
        {
            if (!TryGetSlideTravel(_slide, out Vector3 dir, out float denom))
            {
                return;
            }

            bool isGrabbed = UxrGrabManager.Instance != null && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            if (HasLedgerAdapter)
            {
                // Spring только physical; никакой второй Reload/ready writer.
                if (_autoReturnOnRelease && !isGrabbed && !_cosmeticHold && !ReadinessAdapter.OwnsActionPose)
                    ApplyAutoReturn(dir);
                ReadinessAdapter.RefreshPhysicalActionState(_triggerIndex);
                return;
            }
            if (_cosmeticHold && !isGrabbed) return;

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

            // Право дослать принадлежит ручному циклу основного держателя оружия.
            // Потерянный хват/блокировка не оставляют отложенный Reload после восстановления.
            if (!HasManualContext())
            {
                CancelManualCycle();
                return;
            }

            if (_pendingClose)
            {
                CompletePhysicalClose();
                return;
            }

            float epsilon = FrontPositionEpsilon();
            float requiredPull = _slideThreshold;
            if (_state == SlideState.WaitForward && _manualInteraction && isGrabbed && absDist > epsilon &&
                current + epsilon / denom >= requiredPull)
            {
                _state = SlideState.WaitBack;
                PlayForwardFeedback(_firearm.IsLoaded(_triggerIndex));
                LogBoltCycle($"Ручная оттяжка/зацепление достигнуты. (Норм={current:F2})");
            }
            else if (_state == SlideState.WaitBack && delta.sqrMagnitude <= epsilon * epsilon)
            {
                // Сначала потребляем цикл и фиксируем магазин именно в момент закрытия ручки.
                // Пустое закрытие не сможет дослать из магазина, вставленного позже, пока
                // остальные визуальные каналы возвращаются к физическому переднему упору.
                _state = SlideState.WaitForward;
                _manualInteraction = false;
                _pendingClose = true;
                _closingMagazine = GetValidInstalledMagazine(out _closingAnchor);
                CompletePhysicalClose();
            }
        }

        private void HandleSlideGrabbing(object sender, UxrManipulationEventArgs e)
        {
            if (HasLedgerAdapter || !isActiveAndEnabled || !HasManualContext() || _pendingClose) return;
            _manualInteraction = true;
            if (_cosmeticHold)
            {
                _cosmeticHold = false;
                // Empty уже открыл контактный затвор. Новый задний ход не требуется;
                // физическое закрытие должно начаться от ручного захвата этой позы.
                if ((_slide.transform.localPosition - _localStart).sqrMagnitude >
                    FrontPositionEpsilon() * FrontPositionEpsilon())
                    _state = SlideState.WaitBack;
            }
        }

        protected override void OnDisable()
        {
            CancelManualCycle();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_slide != null) _slide.Grabbing -= HandleSlideGrabbing;
            base.OnDestroy();
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

        // Восемь операций float над локальной координатой/ходом: численный запас,
        // а не процент физического хода. Передний упор проверяется по всему offset.
        private float FrontPositionEpsilon()
        {
            TryGetSlideTravel(_slide, out _, out float length);
            float coordinate = Mathf.Max(Mathf.Abs(_localStart.x), Mathf.Abs(_localStart.y), Mathf.Abs(_localStart.z), length);
            return EndpointRoundingAllowanceUnits * SinglePrecisionRelativeSpacing * coordinate;
        }

        private bool HasManualContext()
        {
            return _firearm != null && _firearm.isActiveAndEnabled && _firearm.CanUse &&
                   (!UxrManager.HasInstance || !UxrManager.Instance.IsInsideStateSync) && UxrGrabManager.HasInstance &&
                   UxrGrabManager.Instance.isActiveAndEnabled &&
                   _firearm.TryGetTriggerGrip(_triggerIndex, out UxrGrabbableObject grip, out int point) &&
                   UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber mainHand) &&
                   mainHand != null && mainHand.Avatar != null && StateEventAuthority.IsAuthoredHere(mainHand.Avatar) &&
                   StateEventAuthority.IsAuthorOfItem(_firearm);
        }

        private UxrGrabbableObject GetValidInstalledMagazine(out UxrGrabbableObjectAnchor anchor)
        {
            anchor = null;
            if (!_firearm.TryGetTriggerMagazineAnchor(_triggerIndex, out anchor) ||
                !anchor.isActiveAndEnabled || !anchor.gameObject.activeInHierarchy) return null;
            UxrGrabbableObject placed = anchor.CurrentPlacedObject;
            if (placed == null || placed.CurrentAnchor != anchor || !anchor.IsCompatibleObject(placed) ||
                placed.GetComponent<UxrFirearmMag>() == null || _firearm.GetAmmoLeft(_triggerIndex) <= 0) return null;
            return placed;
        }

        private void CompletePhysicalClose()
        {
            float epsilon = FrontPositionEpsilon();
            if ((_slide.transform.localPosition - _localStart).sqrMagnitude > epsilon * epsilon) return;
            var visuals = GetComponent<WeaponMechanismVisuals>();
            if (visuals != null && visuals.isActiveAndEnabled && !visuals.IsManualActionPhysicallyClosed(FrontPositionEpsilon())) return;
            UxrGrabbableObject expectedMagazine = _closingMagazine;
            UxrGrabbableObjectAnchor expectedAnchor = _closingAnchor;
            CancelManualCycle(); // До синхронизируемого вызова/события: reentrant Update не дублирует цикл.
            bool loaded = _firearm.IsLoaded(_triggerIndex);
            bool reloaded = false;
            if (_chamberRoundOnSlideReturn && HasManualContext() && expectedMagazine != null &&
                GetValidInstalledMagazine(out UxrGrabbableObjectAnchor currentAnchor) == expectedMagazine &&
                currentAnchor == expectedAnchor)
            {
                _firearm.Reload(_triggerIndex);
                loaded = _firearm.IsLoaded(_triggerIndex);
                reloaded = loaded;
                LogBoltCycle("Передний упор достигнут; ручное досылание (Reload) выполнено.");
            }
            PlayBackFeedback(loaded);
            if (reloaded) ManualCycleCompleted?.Invoke();
        }

        private void CancelManualCycle()
        {
            _state = SlideState.WaitForward;
            _manualInteraction = false;
            _pendingClose = false;
            _closingMagazine = null;
            _closingAnchor = null;
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

            GameLog.WeaponSystem.Info($"[Bolt][{gameObject.name}] {message}", this);
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
        private bool             _cosmeticHold;
        private bool             _manualInteraction;
        private bool             _pendingClose;
        private UxrGrabbableObject _closingMagazine;
        private UxrGrabbableObjectAnchor _closingAnchor;

        #endregion
    }
}

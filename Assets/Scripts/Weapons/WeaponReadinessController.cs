using System;
using UltimateXR.Core;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Политика и одноразовое физическое evidence; боезапас пишет только SDK ledger.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrFirearmWeapon)), DefaultExecutionOrder(210)]
    public sealed class WeaponReadinessController : MonoBehaviour
    {
        [SerializeField] private int _triggerIndex;
        [SerializeField] private WeaponReadinessProfile _profile;
        [SerializeField] private Transform _body;
        [SerializeField] private ChamberActionBinding[] _requiredBindings;
        [SerializeField] private float _emptyRearTime = -1f;
        private float _validatedEmptyRearTime;
        private Vector3 _validatedTravelDirection;
        private float _validatedTravelLength;
        private UxrFirearmWeapon _weapon;
        private AutomaticWeaponSlideFeedback _feedback;
        private WeaponMechanismVisuals _visuals;
        private readonly ChamberPoseReturnDriver _driver = new ChamberPoseReturnDriver();
        private bool _configured, _initializing, _commandInFlight, _started, _tearingDown;
        // Только происхождение разрешённой automation-команды; M/C остаются исключительно SDK.
        private bool _hasAutomationCorrelation;
        private uint _automationRevision, _automationCycle, _automationShot;
        private UxrGrabbableObject _automationMagazine;
        private UxrGrabbableObjectAnchor _automationAnchor;
        private UxrGrabber _automationHand;
        private bool _manualContact, _manualMoved, _manualRear;
        private uint _manualRearShotSequence;
        private bool _hadContext, _rearGateReached, _manualOpenedRear;
        private float _lastContactProgress;
        private UxrGrabbableObject _observedMagazine, _expectedMagazine;
        private UxrGrabbableObjectAnchor _expectedAnchor;
        private UxrGrabber _cycleMainHand;
        private uint _expectedRevision, _cycleSequence;
        private bool _cycleActive, _automaticCycle;
        private bool _emptyReturn;
        private bool _snapshotMagazineBaseline;
        private ChamberCompletionEvidence _reserved;
        private bool _hasReserved;
        private WeaponChamberPolicy _policy;
        private WeaponEmptyPose _emptyPose;
        private WeaponPhysicalCapability _physical;

        public bool IsConfigured => _configured;
        public WeaponReadinessProfile Profile => _profile;
        public WeaponEmptyPose EmptyPose => _emptyPose;
        /// <summary>
        /// Владелец позы Action: return driver или удерживаемая HoldOpen-поза. Все rest-writers
        /// (пружина ручки, возврат после отпускания, ResetVisuals) обязаны спрашивать этот предикат.
        /// </summary>
        public bool OwnsActionPose => _driver.Active || HoldsEmptyActionOpen;

        /// <summary>
        /// HoldOpen после последнего патрона: Action остаётся в validated rear, пока игрок сам не
        /// начнёт ручной цикл (BeginAction снимает PostShotEmptyAction). Источник — только SDK state
        /// и профиль, без локальной копии; одинаково у автора и наблюдателя/late join.
        /// </summary>
        public bool HoldsEmptyActionOpen
        {
            get
            {
                if (!_configured || _physical != WeaponPhysicalCapability.ActionTravel || _emptyPose != WeaponEmptyPose.HoldOpen ||
                    _cycleActive || _emptyReturn || _weapon == null) return false;
                UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
                return state != null && state.ReadinessInitialized && state.PostShotEmptyAction &&
                       !state.ChamberRound && !state.ActionOpen && !state.ChamberCyclePending;
            }
        }
        public bool IsActionAtRest => IsPhysicallyClosed();
        public float EmptyRearTime => _validatedEmptyRearTime;

        public bool TryGetManualPositionMapping(Transform target, out Vector3 rest, out Vector3 rear)
        {
            rest = rear = Vector3.zero;
            if (!_configured || _requiredBindings == null) return false;
            foreach (ChamberActionBinding binding in _requiredBindings)
            {
                if (binding.Target != target) continue;
                rest = binding.RestPosition; rear = binding.RearPosition;
                return true;
            }
            return false;
        }

        public bool TryGetManualRotationMapping(Transform target, out Quaternion rest, out Quaternion rear)
        {
            rest = rear = Quaternion.identity;
            if (!_configured || _requiredBindings == null) return false;
            foreach (ChamberActionBinding binding in _requiredBindings)
            {
                if (binding.Target != target || !binding.AnimateRotation) continue;
                rest = binding.RestRotation; rear = binding.RearRotation;
                return true;
            }
            return false;
        }

        private bool ValidatePhysicalConfiguration(WeaponReadinessProfile profile, ChamberActionBinding[] bindings, out float validatedEmptyRearTime, out string error)
        {
            error = null;
            validatedEmptyRearTime = -1f;
            if (_weapon == null || _triggerIndex < 0 || _triggerIndex >= _weapon.TriggerCount ||
                !_weapon.TryGetTriggerMagazineAnchor(_triggerIndex, out _))
            { error = "Нет поддерживаемого trigger magazine anchor."; return false; }
            if (profile.PhysicalCapability == WeaponPhysicalCapability.NoAction)
            {
                if ((_feedback != null && _feedback.Slide != null) || (bindings != null && bindings.Length != 0) ||
                    (_visuals != null && _visuals.GetRequiredActionTargets().Length != 0))
                { error = "NoAction не принимает скрытый Action/bindings."; return false; }
                return true;
            }
            if (_triggerIndex != 0)
            { error = "Текущий visual Action channel связан с trigger0; другой trigger не имеет physical mapping."; return false; }
            if (_feedback == null || _visuals == null || _feedback.Slide == null || _body == null || _body != _visuals.Body ||
                bindings == null || bindings.Length == 0 ||
                !HasFixedGrabRotation(_feedback.Slide) ||
                !_feedback.TryGetNumericalEndpointDiagnostics(out _, out float length, out float epsilon) ||
                !IsFinite(_feedback.SlideThreshold) || _feedback.SlideThreshold <= 0f || _feedback.SlideThreshold > 1f ||
                !IsFinite(_feedback.AutoReturnSpeed) || _feedback.AutoReturnSpeed <= 0f)
            { error = "Нет валидного Action travel/rest/visual mapping/speed."; return false; }
            var targets = new System.Collections.Generic.HashSet<Transform>();
            foreach (ChamberActionBinding binding in bindings)
            {
                if (binding == null || binding.Target == null || !binding.Target.IsChildOf(_feedback.Slide.transform) ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body) ||
                    !targets.Add(binding.Target) || !Finite(binding.RestPosition) || !Finite(binding.RearPosition) ||
                    !UnitRotation(binding.RestRotation) || !UnitRotation(binding.RearRotation) ||
                    (_visuals.RequiresActionRotation(binding.Target) && !binding.AnimateRotation) ||
                    !_visuals.TryGetSourceRestBodyPose(binding.Target, out Pose rest) ||
                    (rest.position - binding.RestPosition).sqrMagnitude > epsilon * epsilon ||
                    !ChamberActionBinding.RotationNear(rest.rotation, binding.RestRotation))
                { error = "Невалидный/повторный Action target или rest не совпадает с source."; return false; }
                bool translation = (binding.RearPosition - binding.RestPosition).sqrMagnitude > epsilon * epsilon;
                bool rotation = binding.AnimateRotation && !ChamberActionBinding.RotationNear(binding.RestRotation, binding.RearRotation);
                if (rotation && Mathf.Abs(Quaternion.Dot(binding.RestRotation.normalized, binding.RearRotation.normalized)) <= 8f * 1.192092896e-7f)
                { error = "180° shortest-arc mapping неоднозначен."; return false; }
                if (!translation && !rotation)
                { error = "Required Action не имеет проверяемого progression."; return false; }
            }
            foreach (Transform target in _visuals.GetRequiredActionTargets())
                if (!targets.Contains(target)) { error = "Mapping пропускает required Action part."; return false; }
            WeaponMechanismMotion.Cycle empty = _visuals.Motion != null ? _visuals.Motion.Empty : null;
            if (empty != null && (empty.HoldEnd || profile.EmptyPose == WeaponEmptyPose.HoldOpen))
            {
                float time = empty.HoldEnd ? empty.Duration : _emptyRearTime;
                if (!_visuals.TryValidateEmptyRearPose(time, bindings, epsilon))
                { error = "Empty не имеет валидированной rear-позы Action/grabbable; настройка не включена."; return false; }
                validatedEmptyRearTime = time;
            }
            else if (profile.EmptyPose == WeaponEmptyPose.HoldOpen)
            { error = "HoldOpen требует существующего Empty track и validated rear time."; return false; }
            return true;
        }

        private static bool Finite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool UnitRotation(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w) &&
            Mathf.Abs(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w - 1f) <= 1e-5f;

        private void Start()
        {
            _started = true;
            if (_profile != null) Configure(_profile, _requiredBindings, out _);
        }

        private void OnEnable()
        {
            UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized;
            UxrStateSaveImplementer.StateSerialized += HandleStateSerialized;
            if (_started && _profile != null) Configure(_profile, _requiredBindings, out _);
        }

        public bool Configure(WeaponReadinessProfile profile, ChamberActionBinding[] bindings, out string error)
        {
            error = null;
            if (_commandInFlight) { error = "Нельзя менять physical profile во время ledger command/evidence."; return false; }
            _weapon = GetComponent<UxrFirearmWeapon>();
            _feedback = GetComponent<AutomaticWeaponSlideFeedback>();
            _visuals = GetComponent<WeaponMechanismVisuals>();
            if (_body == null) _body = _visuals != null ? _visuals.Body : transform;
            if (profile == null) { error = "Не задан readiness profile."; return false; }
            if (!profile.TryValidate(out error)) return false;
            if (profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo)
            { error = "LegacyAmmo сохраняет прежний adapter, ledger не включается."; return false; }
            if (!ValidatePhysicalConfiguration(profile, bindings, out float validatedEmptyRearTime, out error)) return false;
            if (profile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber &&
                (_weapon.GetComponent<CartridgeIntake>() == null ||
                 Array.Find(_weapon.GetComponentsInChildren<UxrFirearmMag>(true),
                    store => store.IsFixedAmmoStore && store.FixedStoreWeapon == _weapon && store.FixedStoreTrigger == _triggerIndex) == null))
            { error = "FixedStoreChamber требует явного fixed reservoir и shell receiver."; return false; }
            if ((_weapon.CanAuthorReadinessAction != null && _weapon.CanAuthorReadinessAction.Target != this) ||
                (_weapon.RefreshPhysicalActionState != null && _weapon.RefreshPhysicalActionState.Target != this) ||
                (_weapon.ValidateChamberCompletion != null && _weapon.ValidateChamberCompletion.Target != this) ||
                (_weapon.ValidatePhysicalActionClosed != null && _weapon.ValidatePhysicalActionClosed.Target != this) ||
                (_weapon.ValidatePostShotEmptyActionRest != null && _weapon.ValidatePostShotEmptyActionRest.Target != this) ||
                (_weapon.CanPrepareReadinessForAutomation != null && _weapon.CanPrepareReadinessForAutomation.Target != this))
            { error = "SDK ports уже принадлежат другому adapter."; return false; }
            _emptyReturn = false; _driver.Cancel();
            CancelPendingCycle();
            if (_configured && _feedback != null && _feedback.Slide != null) _feedback.Slide.Grabbing -= HandleGrabbing;
            _profile = profile; _requiredBindings = CopyBindings(bindings);
            _policy = profile.ChamberPolicy; _emptyPose = profile.EmptyPose; _physical = profile.PhysicalCapability;
            _validatedEmptyRearTime = validatedEmptyRearTime;
            if (_physical == WeaponPhysicalCapability.ActionTravel)
                AutomaticWeaponSlideFeedback.TryGetSlideTravel(_feedback.Slide, out _validatedTravelDirection, out _validatedTravelLength);
            _configured = true;
            _weapon.CanAuthorReadinessAction = CanAuthor;
            _weapon.RefreshPhysicalActionState = RefreshPhysicalActionState;
            _weapon.ValidateChamberCompletion = ValidateChamberCompletion;
            _weapon.ValidatePhysicalActionClosed = ValidatePhysicalActionClosed;
            _weapon.ValidatePostShotEmptyActionRest = ValidatePostShotEmptyActionRest;
            _weapon.CanPrepareReadinessForAutomation = ValidateAutomationCapability;
            _weapon.StateChanged -= HandleAutomationStateChanged;
            _weapon.StateChanged += HandleAutomationStateChanged;
            // Query-only dependency port. Единственный trigger episode owner появится на этапе3.
            if (_weapon.EvaluateLocalTriggerAttempt == null)
                _weapon.EvaluateLocalTriggerAttempt = (trigger, hand) => _weapon.QueryReadinessDecision(trigger);
            if (!_weapon.TryEnableReadiness(_triggerIndex))
            { _configured = false; error = "SDK runtime trigger/anchor ещё не готов."; return false; }
            _observedMagazine = CurrentMagazine(out _);
            if (_feedback != null && _feedback.Slide != null) _feedback.Slide.Grabbing += HandleGrabbing;
            bool wasUninitialized = _weapon.GetReadinessState(_triggerIndex)?.ReadinessInitialized != true;
            bool initialized;
            _initializing = true;
            try { initialized = _weapon.TryInitializeReadiness(_triggerIndex); }
            finally { _initializing = false; }
            if (wasUninitialized && initialized && HasContext(out UxrGrabber initialHand))
                CaptureAutomationCorrelation(_weapon.GetReadinessState(_triggerIndex), initialHand);
            return true;
        }

        private bool CanAuthor(int trigger) => trigger == _triggerIndex && _configured && (isActiveAndEnabled || _tearingDown) &&
            IsPhysicalConfigurationCurrent() && (!UxrManager.HasInstance || !UxrManager.Instance.IsInsideStateSync) && StateEventAuthority.IsAuthorOfItem(_weapon);

        private bool IsPhysicalConfigurationCurrent()
        {
            if (_physical == WeaponPhysicalCapability.NoAction)
                return (_feedback == null || _feedback.Slide == null) &&
                    (_requiredBindings == null || _requiredBindings.Length == 0) &&
                    (_visuals == null || _visuals.GetRequiredActionTargets().Length == 0);
            if (_feedback == null || _feedback.Slide == null || _visuals == null || _body == null || _requiredBindings == null ||
                !HasFixedGrabRotation(_feedback.Slide) ||
                !_feedback.TryGetNumericalEndpointDiagnostics(out _, out float length, out _) ||
                !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_feedback.Slide, out Vector3 direction, out _) ||
                length != _validatedTravelLength || !direction.Equals(_validatedTravelDirection)) return false;
            foreach (ChamberActionBinding binding in _requiredBindings)
                if (binding == null || binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body)) return false;
            return true;
        }

        private bool HasContext(out UxrGrabber mainHand)
        {
            mainHand = null;
            return isActiveAndEnabled && _weapon != null && _weapon.isActiveAndEnabled && _weapon.CanUse &&
                CanAuthor(_triggerIndex) && UxrGrabManager.HasInstance && UxrGrabManager.Instance.isActiveAndEnabled &&
                _weapon.TryGetTriggerGrip(_triggerIndex, out UxrGrabbableObject grip, out int point) &&
                UxrGrabManager.Instance.GetGrabbingHand(grip, point, out mainHand) && mainHand != null &&
                mainHand.Avatar != null && StateEventAuthority.IsAuthoredHere(mainHand.Avatar);
        }

        private static bool HasFixedGrabRotation(UxrGrabbableObject slide)
        {
            if (slide == null) return false;
            if (slide.RotationConstraint == UxrRotationConstraintMode.Locked) return true;
            // Native builder использует Restrict с нулевыми пределами: физически
            // тот же запрет вращения. Проверка capability не меняет SDK constraints.
            return slide.RotationConstraint == UxrRotationConstraintMode.RestrictLocalRotation &&
                slide.RotationAngleLimitsMin.Equals(Vector3.zero) && slide.RotationAngleLimitsMax.Equals(Vector3.zero);
        }

        private UxrGrabbableObject CurrentMagazine(out UxrGrabbableObjectAnchor anchor)
        {
            anchor = null;
            if (!_weapon.TryGetTriggerMagazineAnchor(_triggerIndex, out anchor) || !anchor.isActiveAndEnabled ||
                !anchor.gameObject.activeInHierarchy) return null;
            UxrGrabbableObject magazine = anchor.CurrentPlacedObject;
            return magazine != null && magazine.CurrentAnchor == anchor && anchor.IsCompatibleObject(magazine) &&
                magazine.GetComponent<UxrFirearmMag>() != null ? magazine : null;
        }

        private void HandleGrabbing(object sender, UxrManipulationEventArgs args)
        {
            if (!HasContext(out _) || _physical != WeaponPhysicalCapability.ActionTravel) return;
            _hadContext = true;
            _manualContact = true;
            _manualMoved = false;
            _lastContactProgress = _feedback.SignedSlideProgress;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
            CaptureRearEvidence(state);
            // Сам grab/cosmetic handoff не создаёт цикл. Уже подтверждённая Empty rear C0 — исключение.
        }

        private void CaptureRearEvidence(UxrFirearmReadinessState state)
        {
            // Геометрический rear capture принадлежит конкретному Empty-эпизоду SDK.
            // Сам по себе cached bool не даёт разрешения после потребления origin.
            _manualRear = state?.PostShotEmptyAction == true && !state.ChamberRound &&
                _visuals != null && _visuals.IsValidatedEmptyRearPose(_validatedEmptyRearTime, _requiredBindings, _feedback.PhysicalPositionEpsilon);
            _manualRearShotSequence = _manualRear ? state.ShotSequence : 0;
        }

        public bool RequestChamber(ChamberRequestOrigin origin, uint pressSequence)
        {
            if (_commandInFlight || !_configured || !HasContext(out UxrGrabber mainHand) || _cycleActive ||
                (origin != ChamberRequestOrigin.MagazineInsert && origin != ChamberRequestOrigin.TriggerAssist) ||
                _weapon.HasChamberRound(_triggerIndex) ||
                (origin == ChamberRequestOrigin.MagazineInsert && _policy != WeaponChamberPolicy.AutoOnMagazineInsert) ||
                (origin == ChamberRequestOrigin.TriggerAssist && _policy != WeaponChamberPolicy.TriggerAssistPrepareOnly)) return false;
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            if (magazine == null || _weapon.GetMagazineRounds(_triggerIndex) <= 0) return false;
            if (!BeginCycle(mainHand, magazine, anchor, true)) return false;
            if (IsPhysicallyClosed()) CompleteOwnedCycle(ChamberCompletionKind.Chamber);
            else if (_physical == WeaponPhysicalCapability.ActionTravel && !_feedback.IsActionHeld)
                BeginReturnDriver();
            // Это команда prepare-only. Source.Shoot и trigger episode здесь отсутствуют.
            return true;
        }

        private bool BeginCycle(UxrGrabber hand, UxrGrabbableObject magazine, UxrGrabbableObjectAnchor anchor, bool automatic)
        {
            UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
            if (state == null || !state.ReadinessInitialized || state.CycleSequence == uint.MaxValue) return false;
            if (_emptyReturn)
            {
                _emptyReturn = false; _driver.Cancel();
                _visuals?.EndOwnedChamberReturn();
            }
            _commandInFlight = true;
            bool began;
            try { began = _weapon.TryBeginManualAction(_triggerIndex, state.Revision, state.CycleSequence + 1); }
            finally { _commandInFlight = false; }
            if (!began) return false;
            state = _weapon.GetReadinessState(_triggerIndex);
            _cycleMainHand = hand; _expectedMagazine = magazine; _expectedAnchor = anchor;
            _expectedRevision = state.Revision; _cycleSequence = state.CycleSequence;
            _cycleActive = true; _automaticCycle = automatic;
            return true;
        }

        private void LateUpdate()
        {
            if (!_configured) return;
            RefreshSavedEmptyPresentation();
            RefreshPhysicalActionState(_triggerIndex);
            if (_physical == WeaponPhysicalCapability.NoAction) return;
            if (_emptyReturn)
            {
                if (_driver.Active) _driver.Advance(Time.deltaTime, _feedback.AutoReturnSpeed, _feedback.IsActionHeld, _feedback.PhysicalPositionEpsilon);
                if (!_driver.Active)
                {
                    _emptyReturn = false;
                    _visuals?.CompleteOwnedEmptyReturn(_weapon.GetReadinessState(_triggerIndex).ShotSequence, _driver.Completed && IsPhysicallyClosed());
                    _feedback.ClearLedgerVisualHold();
                    _visuals?.EndOwnedChamberReturn();
                }
                return; // Pure Empty animation; C/Ready/feed commands здесь отсутствуют.
            }
            if (!_cycleActive || !_automaticCycle || !HasContext(out _)) return;
            if (!_driver.Active && !_feedback.IsActionHeld && !IsPhysicallyClosed()) BeginReturnDriver();
            if (_driver.Active) _driver.Advance(Time.deltaTime, _feedback.AutoReturnSpeed, _feedback.IsActionHeld, _feedback.PhysicalPositionEpsilon);
            RefreshPhysicalActionState(_triggerIndex);
        }

        private void BeginReturnDriver()
        {
            _emptyReturn = false; // Передача текущих captured poses новому chamber purpose без teleport.
            // Visual adapter отдаёт только chamber/Empty channels, ordinary Fire остаётся independent.
            _visuals?.BeginOwnedChamberReturn();
            _driver.Begin(_body, _feedback.Slide, _feedback.RestLocalPosition, _requiredBindings, _feedback.PhysicalPositionEpsilon, _visuals);
        }

        public void RequestEmptyReturn()
        {
            if (!_configured || _physical != WeaponPhysicalCapability.ActionTravel || _cycleActive ||
                _emptyPose != WeaponEmptyPose.ReturnToRest || _weapon.GetReadinessState(_triggerIndex)?.PostShotEmptyAction != true) return;
            _visuals?.BeginOwnedChamberReturn();
            _emptyReturn = _driver.Begin(_body, _feedback.Slide, _feedback.RestLocalPosition, _requiredBindings, _feedback.PhysicalPositionEpsilon, _visuals);
            if (!_emptyReturn) { _feedback.ClearLedgerVisualHold(); _visuals?.EndOwnedChamberReturn(); }
        }

        public void ServiceFinishedEmptyPresentation(bool sourceHoldEnd)
        {
            if (!_configured || _visuals == null || _cycleActive || _emptyReturn || _feedback.IsActionHeld ||
                !IsPhysicalConfigurationCurrent()) return;
            var state = _weapon.GetReadinessState(_triggerIndex);
            if (state == null || !state.PostShotEmptyAction || state.ActionOpen || state.ChamberCyclePending) return;
            if (sourceHoldEnd)
            {
                if (!_visuals.RestoreSavedEmptyPresentation(true, _validatedEmptyRearTime, _requiredBindings, _feedback.PhysicalPositionEpsilon)) return;
                if (_emptyPose == WeaponEmptyPose.ReturnToRest) RequestEmptyReturn();
            }
            else _visuals.RestoreSavedEmptyPresentation(false, _validatedEmptyRearTime, _requiredBindings, _feedback.PhysicalPositionEpsilon);
        }

        // Pure snapshot presentation. Local history выбирает, нужно ли рисовать, но не даёт право Begin/feed.
        private void RefreshSavedEmptyPresentation()
        {
            if (!_configured || _physical != WeaponPhysicalCapability.ActionTravel || _visuals == null ||
                !IsPhysicalConfigurationCurrent()) return;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
            if (state?.ReadinessInitialized != true) return;
            if (!state.PostShotEmptyAction)
            {
                if (!_cycleActive && !_emptyReturn) _visuals.ClearConsumedEmptyPresentation();
                return;
            }
            if (_cycleActive || _emptyReturn || _feedback.IsActionHeld ||
                (_visuals.HasEmptyPresentationFor(state.ShotSequence) && !_visuals.IsEmptyActionApplicationDeferred)) return;
            if (_visuals.IsEmptyActionApplicationDeferred && _emptyPose == WeaponEmptyPose.ReturnToRest && !IsPhysicallyClosed())
            { RequestEmptyReturn(); return; }
            _visuals.RestoreSavedEmptyPresentation(_emptyPose == WeaponEmptyPose.HoldOpen,
                _validatedEmptyRearTime, _requiredBindings, _feedback.PhysicalPositionEpsilon);
        }

        public void CancelPendingCycle()
        {
            ClearAutomationCorrelation();
            _driver.Cancel(); _emptyReturn = false;
            _hasReserved = false;
            _manualContact = false; _manualMoved = false; _manualRear = false; _manualRearShotSequence = 0;
            _rearGateReached = false;
            _manualOpenedRear = false;
            _cycleActive = false; _automaticCycle = false; _cycleMainHand = null;
            _visuals?.EndOwnedChamberReturn();
            if (_weapon == null || !_configured || _commandInFlight || !CanAuthor(_triggerIndex)) return;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
            if (state?.ChamberCyclePending != true) return;
            _commandInFlight = true;
            try { _weapon.TryCancelReadinessCycle(_triggerIndex, state.Revision); }
            finally { _commandInFlight = false; }
        }

        private void OnDisable()
        {
            UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized;
            if (_weapon != null) _weapon.StateChanged -= HandleAutomationStateChanged;
            if (_weapon != null && ReferenceEquals(_weapon.CanPrepareReadinessForAutomation?.Target, this))
                _weapon.CanPrepareReadinessForAutomation = null;
            _tearingDown = true;
            try { CancelPendingCycle(); }
            finally { _tearingDown = false; }
            _driver.Cancel(); _emptyReturn = false;
            _visuals?.EndOwnedChamberReturn();
            if (_feedback != null && _feedback.Slide != null) _feedback.Slide.Grabbing -= HandleGrabbing;
        }

        // Статическое событие переживает объект, уничтоженный без OnDisable (DestroyImmediate в EditMode):
        // обращение к isActiveAndEnabled уничтоженного компонента бросает MissingReferenceException.
        private void OnDestroy() { UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized; }

        private void HandleStateSerialized(object sender, UxrStateSaveEventArgs args)
        {
            if (this == null) { UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized; return; }
            if (!isActiveAndEnabled || _weapon == null || !ReferenceEquals(sender, _weapon) ||
                args == null || args.Serializer == null || !args.Serializer.IsReading) return;
            ClearAutomationCorrelation();
            // Event приходит до загрузки остальных компонентов. Здесь нет чтения
            // topology/позы, SDK команд, transform writes или borrowed args lifetime.
            _driver.Cancel(); _emptyReturn = false;
            _hasReserved = false; _reserved = default;
            _cycleActive = false; _automaticCycle = false;
            _cycleMainHand = null; _expectedMagazine = null; _expectedAnchor = null;
            _expectedRevision = _cycleSequence = 0;
            _manualContact = _manualMoved = _manualRear = false; _manualRearShotSequence = 0;
            _rearGateReached = _manualOpenedRear = _hadContext = false;
            _snapshotMagazineBaseline = true; // Load не является новым magazine insert.
            _visuals?.InvalidateActionPresentationAfterStateLoad();
        }

        public void RefreshPhysicalActionState(int trigger)
        {
            if (!_configured || trigger != _triggerIndex || _commandInFlight ||
                (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync)) return;
            if (!HasContext(out UxrGrabber mainHand))
            {
                _hadContext = false;
                CancelPendingCycle();
                return;
            }
            bool restoredContext = !_hadContext;
            _hadContext = true;
            // Только manual/chamber каналы; метод не продвигает cosmetic Fire timer.
            _visuals?.RefreshOwnedManualPose();
            UxrFirearmReadinessState state = _weapon.GetReadinessState(trigger);
            if (state?.ReadinessInitialized != true) return;
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            bool changedMagazine = !_snapshotMagazineBaseline && magazine != _observedMagazine;
            _snapshotMagazineBaseline = false;
            _observedMagazine = magazine;
            if (_cycleActive && (state.Revision != _expectedRevision || state.CycleSequence != _cycleSequence ||
                magazine != _expectedMagazine || anchor != _expectedAnchor || mainHand != _cycleMainHand))
            {
                CancelPendingCycle();
                state = _weapon.GetReadinessState(trigger);
            }
            // Snapshot/новый автор не наследуют право commit старого жеста.
            if (!_cycleActive && state.ChamberCyclePending)
            {
                CancelPendingCycle();
                state = _weapon.GetReadinessState(trigger);
            }
            if (state.CurrentMagazine != magazine)
            {
                _commandInFlight = true;
                try { _weapon.TryReconcileReadiness(trigger, state.Revision); }
                finally { _commandInFlight = false; }
                state = _weapon.GetReadinessState(trigger);
                if (state.CurrentMagazine != magazine) return;
            }
            if (!_cycleActive && state.ActionOpen && !state.ChamberCyclePending && IsPhysicallyClosed())
            {
                CompleteOwnedCycle(ChamberCompletionKind.CloseOnly);
                state = _weapon.GetReadinessState(trigger);
            }
            if (!_cycleActive && !_emptyReturn && state.PostShotEmptyAction && HasServicedEmptyRest(state) && IsPhysicallyClosed())
            {
                CompleteOwnedCycle(ChamberCompletionKind.EmptyRest);
                state = _weapon.GetReadinessState(trigger);
            }
            if (changedMagazine && magazine != null && _policy == WeaponChamberPolicy.AutoOnMagazineInsert)
                RequestChamber(ChamberRequestOrigin.MagazineInsert, 0);
            if (_physical != WeaponPhysicalCapability.ActionTravel) return;
            bool held = _feedback.IsActionHeld;
            float progress = _feedback.SignedSlideProgress;
            if (!IsFinite(progress)) { CancelPendingCycle(); return; }
            if (!_cycleActive && held)
            {
                if (!_manualContact || restoredContext)
                {
                    if (_visuals != null && !_visuals.CaptureCurrentManualContact()) return;
                    _manualContact = true; _manualMoved = false;
                    _lastContactProgress = progress;
                    CaptureRearEvidence(state);
                }
                float normalizedEpsilon = _feedback.PhysicalPositionEpsilon / _feedback.SlideTravelLength;
                float delta = progress - _lastContactProgress;
                bool actualRearMove = delta > normalizedEpsilon && progress > normalizedEpsilon;
                bool rearReturn = _manualRear && state.PostShotEmptyAction && !state.ChamberRound &&
                    _manualRearShotSequence == state.ShotSequence && delta < -normalizedEpsilon;
                // Реальное движение удерживаемого Action из captured cosmetic handoff:
                // C1 закрывается CloseOnly, без extraction/feed. Один Fire tilt/grab не Begin.
                bool retainedForwardMove = state.ChamberRound && !state.ActionOpen && delta < -normalizedEpsilon &&
                    Mathf.Max(progress, _lastContactProgress) > normalizedEpsilon;
                if (actualRearMove || rearReturn || retainedForwardMove)
                {
                    _manualMoved = true;
                    if (BeginCycle(mainHand, magazine, anchor, false))
                    {
                        _rearGateReached = rearReturn; // Уже rear C0 не создаёт вторую extraction.
                        _manualOpenedRear = actualRearMove;
                    }
                }
                _lastContactProgress = progress;
            }
            if (!_cycleActive) return;
            if (_automaticCycle)
            {
                if (IsPhysicallyClosed()) CompleteOwnedCycle(ChamberCompletionKind.Chamber);
                return;
            }
            state = _weapon.GetReadinessState(trigger);
            if (held && progress > _lastContactProgress + _feedback.PhysicalPositionEpsilon / _feedback.SlideTravelLength)
                _manualOpenedRear = true;
            _lastContactProgress = progress;
            if (!_rearGateReached && _manualOpenedRear && TryGetMinimumProgress(out float minimum) && minimum >= _feedback.SlideThreshold)
            {
                if (state.ExtractedCycleSequence != state.CycleSequence)
                {
                    _commandInFlight = true;
                    bool extracted;
                    try { extracted = _weapon.TryExtractChamberRound(trigger, state.Revision, state.CycleSequence); }
                    finally { _commandInFlight = false; }
                    if (!extracted) { CancelPendingCycle(); return; }
                    _expectedRevision = _weapon.GetReadinessState(trigger).Revision;
                }
                _rearGateReached = true;
            }
            if (!IsPhysicallyClosed()) return;
            if (_rearGateReached) CompleteOwnedCycle(ChamberCompletionKind.Chamber);
            else
            {
                CancelPendingCycle();
                CompleteOwnedCycle(ChamberCompletionKind.CloseOnly);
                // Закрытый partial cycle погасил permission, но тот же удерживаемый
                // Action уже имеет наблюдённый передний baseline. Следующий реальный
                // задний ход должен считаться движением, а не новым initial grab rear.
                if (held && IsPhysicallyClosed())
                {
                    _manualContact = true;
                    _lastContactProgress = _feedback.SignedSlideProgress;
                }
            }
            if (!held) _manualContact = false;
        }

        private bool IsPhysicallyClosed()
        {
            if (_physical == WeaponPhysicalCapability.NoAction) return true;
            if (_feedback == null || _feedback.Slide == null || _body == null || _requiredBindings == null) return false;
            float epsilon = _feedback.PhysicalPositionEpsilon;
            if (!IsFinite(epsilon) || epsilon <= 0f || !Finite(_feedback.Slide.transform.localPosition) ||
                (_feedback.Slide.transform.localPosition - _feedback.RestLocalPosition).sqrMagnitude > epsilon * epsilon) return false;
            foreach (ChamberActionBinding binding in _requiredBindings)
            {
                if (binding == null || binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body) ||
                    !_visuals.TryGetSourceLocalRestPose(binding.Target, out Pose rest) ||
                    !Finite(binding.Target.localPosition) ||
                    (binding.Target.localPosition - rest.position).sqrMagnitude > epsilon * epsilon ||
                    !ChamberActionBinding.RotationNear(binding.Target.localRotation, rest.rotation)) return false;
            }
            return true;
        }

        private bool TryGetMinimumProgress(out float progress)
        {
            progress = _feedback != null ? _feedback.SignedSlideProgress : 0f;
            if (_physical != WeaponPhysicalCapability.ActionTravel || _requiredBindings == null || _requiredBindings.Length == 0) return false;
            foreach (ChamberActionBinding binding in _requiredBindings)
            {
                if (binding == null || binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body) ||
                    !binding.TryGetProgress(_body, _feedback.PhysicalPositionEpsilon, out float current)) return false;
                progress = Mathf.Min(progress, current);
            }
            return true;
        }

        private bool ValidateChamberCompletion(int trigger, uint revision, UxrGrabbableObject magazine)
        {
            if (trigger != _triggerIndex || !CanAuthor(trigger) || !IsPhysicallyClosed()) return false;
            if (_initializing)
            {
                UxrFirearmReadinessState initial = _weapon.GetReadinessState(trigger);
                return initial?.ReadinessInitialized != true && (initial?.Revision ?? 0) == revision &&
                       CurrentMagazine(out _) == magazine;
            }
            return _hasReserved && _reserved.Kind == ChamberCompletionKind.Automation
                ? ValidateReserved(ChamberCompletionKind.Automation, trigger, revision, magazine)
                : ValidateReserved(ChamberCompletionKind.Chamber, trigger, revision, magazine);
        }

        private bool ValidatePhysicalActionClosed(int trigger, uint revision, UxrGrabbableObject magazine) =>
            ValidateReserved(ChamberCompletionKind.CloseOnly, trigger, revision, magazine);

        private bool ValidatePostShotEmptyActionRest(int trigger, uint revision, UxrGrabbableObject magazine) =>
            ValidateReserved(ChamberCompletionKind.EmptyRest, trigger, revision, magazine);

        private bool ValidateReserved(ChamberCompletionKind kind, int trigger, uint revision, UxrGrabbableObject magazine)
        {
            if (!_commandInFlight || !_hasReserved || _reserved.Kind != kind || _reserved.TriggerIndex != trigger ||
                trigger != _triggerIndex || _reserved.Revision != revision || _reserved.Magazine != magazine ||
                !HasContext(out _) || !IsPhysicallyClosed()) return false;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(trigger);
            UxrGrabbableObject current = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            return state?.ReadinessInitialized == true && state.Revision == revision &&
                   state.CycleSequence == _reserved.CycleSequence && state.CurrentMagazine == magazine &&
                   current == magazine && anchor == _reserved.Anchor &&
                   (kind == ChamberCompletionKind.Chamber ? state.ChamberCyclePending :
                    kind == ChamberCompletionKind.CloseOnly ? state.ActionOpen && !state.ChamberCyclePending :
                    kind == ChamberCompletionKind.Automation ? !state.ChamberRound && !state.ActionOpen && !state.ChamberCyclePending &&
                        IsAutomationCorrelationCurrent(state, out _) && magazine != null :
                    state.PostShotEmptyAction && !state.ChamberRound && !state.ActionOpen && !state.ChamberCyclePending && HasServicedEmptyRest(state));
        }

        private bool HasServicedEmptyRest(UxrFirearmReadinessState state)
        {
            if (state == null || !state.PostShotEmptyAction || state.ChamberRound || state.ActionOpen || state.ChamberCyclePending) return false;
            // NoAction preflight доказал отсутствие Action targets/work; immediate service
            // даёт только stack evidence, independent cosmetic Source не блокируется.
            return _physical == WeaponPhysicalCapability.NoAction ? IsPhysicalConfigurationCurrent() :
                _visuals != null && _visuals.HasServicedEmptyRest(state.ShotSequence);
        }

        private bool CompleteOwnedCycle(ChamberCompletionKind kind)
        {
            if (_commandInFlight || !HasContext(out _) || !IsPhysicallyClosed()) return false;
            UxrFirearmReadinessState before = _weapon.GetReadinessState(_triggerIndex);
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            if (before?.ReadinessInitialized != true || before.CurrentMagazine != magazine) return false;
            if (kind == ChamberCompletionKind.EmptyRest && !HasServicedEmptyRest(before)) return false;
            if (kind == ChamberCompletionKind.Chamber &&
                (!_cycleActive || before.Revision != _expectedRevision || before.CycleSequence != _cycleSequence ||
                 magazine != _expectedMagazine || anchor != _expectedAnchor)) return false;
            // Evidence доступно только в синхронном stack frame выбранной команды.
            _reserved = new ChamberCompletionEvidence(_triggerIndex, before.Revision, before.CycleSequence, magazine, anchor, kind);
            _hasReserved = true; _commandInFlight = true;
            bool completed;
            try
            {
                completed = kind == ChamberCompletionKind.Chamber
                    ? _weapon.TryCompleteChamber(_triggerIndex, before.Revision, magazine)
                    : kind == ChamberCompletionKind.CloseOnly
                        ? _weapon.TryConfirmPhysicalActionClosed(_triggerIndex, before.Revision, magazine)
                        : _weapon.TryAcknowledgePostShotEmptyActionRest(_triggerIndex, before.Revision, magazine);
            }
            finally
            {
                _hasReserved = false; _commandInFlight = false;
                // Evidence consumed даже при отказе/exception: не resurrect/retry driver.
                _driver.Cancel(); _emptyReturn = false; _cycleActive = false; _automaticCycle = false;
                _visuals?.EndOwnedChamberReturn();
            }
            if (!completed) return false;
            if (kind == ChamberCompletionKind.EmptyRest && IsAutomationCorrelationCurrent(before, out UxrGrabber ackHand))
            {
                UxrFirearmReadinessState after = _weapon.GetReadinessState(_triggerIndex);
                if (after != null && after.Revision == before.Revision + 1 &&
                    after.ShotSequence == before.ShotSequence && after.CycleSequence == before.CycleSequence &&
                    after.CurrentMagazine == before.CurrentMagazine && !after.PostShotEmptyAction &&
                    after.ChamberRound == before.ChamberRound && !after.ActionOpen && !after.ChamberCyclePending)
                    CaptureAutomationCorrelation(after, ackHand);
                else ClearAutomationCorrelation();
            }
            // NotifyLedgerManualCompletion только actual C0→C1; CloseOnly не притворяется feed.
            if (kind == ChamberCompletionKind.Chamber && !before.ChamberRound && _weapon.HasChamberRound(_triggerIndex))
                _feedback?.NotifyLedgerManualCompletion();
            return true;
        }


        private void ClearAutomationCorrelation()
        {
            _hasAutomationCorrelation = false;
            _automationRevision = _automationCycle = _automationShot = 0;
            _automationMagazine = null; _automationAnchor = null; _automationHand = null;
        }

        private void CaptureAutomationCorrelation(UxrFirearmReadinessState state, UxrGrabber hand)
        {
            ClearAutomationCorrelation();
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            if (state?.ReadinessInitialized != true || hand == null || state.CurrentMagazine != magazine) return;
            _automationRevision = state.Revision; _automationCycle = state.CycleSequence;
            _automationShot = state.ShotSequence; _automationMagazine = magazine;
            _automationAnchor = anchor; _automationHand = hand; _hasAutomationCorrelation = true;
        }

        private bool IsAutomationCorrelationCurrent(UxrFirearmReadinessState state, out UxrGrabber hand)
        {
            hand = null;
            if (!_hasAutomationCorrelation || state == null || !HasContext(out hand)) return false;
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            return hand == _automationHand && state.Revision == _automationRevision &&
                state.CycleSequence == _automationCycle && state.ShotSequence == _automationShot &&
                state.CurrentMagazine == _automationMagazine && magazine == _automationMagazine &&
                anchor == _automationAnchor;
        }

        private void HandleAutomationStateChanged(object sender, UxrSyncEventArgs args)
        {
            if (!ReferenceEquals(sender, _weapon) || !(args is UxrMethodInvokedSyncEventArgs method)) return;
            UxrFirearmReadinessCommit commit = null;
            foreach (object parameter in method.Parameters)
                if (parameter is UxrFirearmReadinessCommit candidate && candidate.TriggerIndex == _triggerIndex)
                { commit = candidate; break; }
            if (commit == null) return;
            // Только собственный stack ACK может rebаse прежний Shot token после успешного return.
            if (commit.Operation == UxrFirearmReadinessOperation.EmptyRestAcknowledged &&
                _commandInFlight && _hasReserved && _reserved.Kind == ChamberCompletionKind.EmptyRest &&
                commit.ExpectedRevision == _reserved.Revision) return;
            ClearAutomationCorrelation();
            if (method.MethodName != "CommitShotSynced" ||
                commit.Operation != UxrFirearmReadinessOperation.Shot ||
                commit.EmissionOutcome != UxrFirearmShotEmissionOutcome.Emitted ||
                !HasContext(out UxrGrabber hand)) return;
            UxrFirearmReadinessState current = _weapon.GetReadinessState(_triggerIndex);
            if (current == null || commit.StateAfter == null || !current.Equals(commit.StateAfter) ||
                current.Revision != commit.NextRevision || current.CurrentMagazine != commit.ReferencedMagazine ||
                _weapon.GetMagazineRounds(_triggerIndex) != commit.MagazineRoundsAfter) return;
            CaptureAutomationCorrelation(current, hand);
        }

        private bool ValidateAutomationCapability(int trigger) =>
            trigger == _triggerIndex && StateEventAuthority.IsWorldAuthority &&
            _hasReserved && ValidateReserved(ChamberCompletionKind.Automation, trigger,
                _reserved.Revision, _reserved.Magazine);

        /// <summary>Подготовка авторского бота; движение не означает разрешённый выстрел.</summary>
        public bool RequestAutomationPreparation()
        {
            if (!_configured || _commandInFlight || !StateEventAuthority.IsWorldAuthority ||
                !HasContext(out _)) { ClearAutomationCorrelation(); return false; }
            // Retained C не требует rest у косметического Fire или наличия магазина.
            if (_weapon.IsReadyToFire(_triggerIndex)) return true;
            UxrFirearmReadinessState state = _weapon.GetReadinessState(_triggerIndex);
            if (!IsAutomationCorrelationCurrent(state, out _) || state.ActionOpen ||
                state.ChamberCyclePending || _cycleActive || state.ChamberRound)
            { ClearAutomationCorrelation(); return false; }
            UxrGrabbableObject magazine = CurrentMagazine(out UxrGrabbableObjectAnchor anchor);
            if (magazine == null || magazine.GetComponent<UxrFirearmMag>().Capacity <= 0) return false;
            if (state.PostShotEmptyAction)
            {
                if (_physical == WeaponPhysicalCapability.ActionTravel)
                {
                    if (_visuals.IsSourceEmptyInProgress || _visuals.IsEmptyActionApplicationDeferred ||
                        !_visuals.HasEmptyPresentationFor(state.ShotSequence) || _feedback.IsActionHeld) return false;
                    if (!IsPhysicallyClosed())
                    {
                        if (!_emptyReturn)
                        {
                            BeginReturnDriver();
                            _emptyReturn = _driver.Active;
                        }
                        return false;
                    }
                }
                if (!HasServicedEmptyRest(state) || !CompleteOwnedCycle(ChamberCompletionKind.EmptyRest)) return false;
                state = _weapon.GetReadinessState(_triggerIndex);
            }
            if (!IsAutomationCorrelationCurrent(state, out _) || !IsPhysicallyClosed() ||
                state.ActionOpen || state.ChamberCyclePending) return false;
            _reserved = new ChamberCompletionEvidence(_triggerIndex, state.Revision, state.CycleSequence,
                magazine, anchor, ChamberCompletionKind.Automation);
            _hasReserved = true; _commandInFlight = true;
            try { return _weapon.TryRefillAndPrepareForAutomation(_triggerIndex); }
            finally { _hasReserved = false; _reserved = default; _commandInFlight = false; }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static ChamberActionBinding[] CopyBindings(ChamberActionBinding[] source)
        {
            if (source == null) return Array.Empty<ChamberActionBinding>();
            var result = new ChamberActionBinding[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                ChamberActionBinding value = source[index];
                result[index] = new ChamberActionBinding { Target = value.Target, RestPosition = value.RestPosition,
                    RestRotation = value.RestRotation.normalized, RearPosition = value.RearPosition, RearRotation = value.RearRotation.normalized,
                    AnimateRotation = value.AnimateRotation, CapturedParent = value.Target.parent };
            }
            return result;
        }

    }
}

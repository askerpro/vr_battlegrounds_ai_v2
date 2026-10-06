using System;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Один визуальный цикл на выстрел; ручная тяга имеет приоритет. Косметика не вызывает Reload/Shoot.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class WeaponMechanismVisuals : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string Part;
            public Transform Target;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
            public bool Rotate;
            public bool InMagazine;
            [NonSerialized] public Vector3 FrozenPosition;
            [NonSerialized] public Quaternion FrozenRotation;
            [NonSerialized] public Vector3 FrozenBodyPosition;
            [NonSerialized] public Quaternion FrozenBodyRotation;
            [NonSerialized] public Quaternion RotationAnchorBody;
            [NonSerialized] public float RotationAnchorProgress, RotationLastProgress;
            [NonSerialized] public bool RotationOpening;
        }
        [SerializeField] private WeaponMechanismMotion _motion;
        [SerializeField] private Transform _body;
        [SerializeField] private UxrGrabbableObject _slide;
        [SerializeField] private Transform _contactPart;
        [SerializeField] private UxrGrabbableObjectAnchor _magazineAnchor;
        [SerializeField] private Binding[] _bindings;
        [SerializeField] private float _originalSlideLength;
        private UxrFirearmWeapon _weapon;
        private AutomaticWeaponSlideFeedback _feedback;
        private WeaponMechanismMotion.Cycle _cycle;
        private float _started;
        private bool _manual;
        private bool _emptyHeld;
        private bool _catchRequired;
        private float _catchProgress;
        private float _manualRotationRearProgress;
        private UxrGrabbableObject _currentMagazine;

        public WeaponMechanismMotion Motion => _motion;
        public Binding[] Bindings => _bindings;
        public Transform Body => _body;
        public Transform ContactPart => _contactPart;
        public bool IsHeldEmpty => _emptyHeld && (!HasLedgerAdapter || _emptyShot);
        public bool HasSourceEmptyPresentation => _emptyShot && (_cycle != null || _emptyHeld);
        private bool _ownedChamberReturn;
        private bool _suppressActionCycle;
        private bool _emptyShot;
        // Только локальная работа одного pose owner. Это не копия SDK origin/Ready.
        private enum EmptyPresentationWork { None, Source, Deferred, HoldSettled, RestServiced, ReturnDriver }
        private EmptyPresentationWork _emptyWork;
        private uint _emptyWorkShot;
        public bool IsSourceEmptyInProgress => _emptyWork == EmptyPresentationWork.Source;
        public bool IsEmptyActionApplicationDeferred => _emptyWork == EmptyPresentationWork.Deferred;
        public bool HasEmptyPresentationFor(uint shot) => _emptyWork != EmptyPresentationWork.None && _emptyWorkShot == shot;
        public bool HasServicedEmptyRest(uint shot) => _emptyWork == EmptyPresentationWork.RestServiced && _emptyWorkShot == shot && !_ownedChamberReturn;

        public void CompleteOwnedEmptyReturn(uint shot, bool actualRest)
        {
            if (_emptyWork == EmptyPresentationWork.ReturnDriver && _emptyWorkShot == shot && actualRest)
                _emptyWork = EmptyPresentationWork.RestServiced;
        }
        private WeaponReadinessController ReadinessAdapter => GetComponent<WeaponReadinessController>();
        private bool HasLedgerAdapter => _weapon != null && ReadinessAdapter != null && ReadinessAdapter.IsConfigured && _weapon.UsesReadinessLedger(0);

        public Transform[] GetRequiredActionTargets()
        {
            var result = new System.Collections.Generic.List<Transform>();
            if (_slide != null) result.Add(_slide.transform);
            if (_bindings != null) foreach (Binding binding in _bindings)
                if (IsAction(binding) && !result.Contains(binding.Target)) result.Add(binding.Target);
            return result.ToArray();
        }

        public bool RequiresActionRotation(Transform target)
        {
            Binding binding = _bindings == null ? null : Array.Find(_bindings, candidate => candidate.Target == target && IsAction(candidate));
            return binding != null && binding.Rotate;
        }

        public bool TryGetSourceLocalRestPose(Transform target, out Pose pose)
        {
            pose = Pose.identity;
            if (target == null) return false;
            if (_slide != null && target == _slide.transform && _feedback != null)
            { pose = new Pose(_feedback.RestLocalPosition, _slide.InitialLocalRotation); return true; }
            Binding binding = _bindings == null ? null : Array.Find(_bindings, candidate => candidate.Target == target && IsAction(candidate));
            if (binding == null) return false;
            pose = new Pose(binding.RestPosition, binding.RestRotation);
            return true;
        }

        public bool TryGetSourceRestBodyPose(Transform target, out Pose pose)
        {
            pose = Pose.identity;
            if (_body == null || target == null || !TryGetRestMatrixToRoot(target, out Matrix4x4 targetMatrix) ||
                !TryGetRestMatrixToRoot(_body, out Matrix4x4 bodyMatrix)) return false;
            // Body mesh и Slide могут быть siblings. Сравниваем через общий weapon root,
            // без временного перемещения сцены и без world-coordinate cancellation.
            Matrix4x4 matrix = bodyMatrix.inverse * targetMatrix;
            pose = new Pose(matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation);
            return true;
        }

        private bool TryGetRestMatrixToRoot(Transform target, out Matrix4x4 matrix, Pose? preparedSlideRest = null)
        {
            matrix = Matrix4x4.identity;
            if (target == null || !target.IsChildOf(transform)) return false;
            for (Transform current = target; current != transform; current = current.parent)
            {
                if (current == null) return false;
                Binding binding = _bindings == null ? null : Array.Find(_bindings, candidate => candidate.Target == current);
                Vector3 position = binding != null ? binding.RestPosition : current.localPosition;
                Quaternion rotation = binding != null ? binding.RestRotation : current.localRotation;
                if (_slide != null && current == _slide.transform)
                {
                    if (preparedSlideRest.HasValue)
                    { position = preparedSlideRest.Value.position; rotation = preparedSlideRest.Value.rotation; }
                    else
                    {
                        if (_feedback == null) return false;
                        position = _feedback.RestLocalPosition; rotation = _slide.InitialLocalRotation;
                    }
                }
                matrix = Matrix4x4.TRS(position, rotation, current.localScale) * matrix;
            }
            return true;
        }

        public void BeginOwnedChamberReturn()
        {
            _ownedChamberReturn = true; _suppressActionCycle = true; _manual = false;
            if (_emptyShot && _emptyWork != EmptyPresentationWork.None) _emptyWork = EmptyPresentationWork.ReturnDriver;
        }

        public bool TryValidateEmptyRearPose(float time, ChamberActionBinding[] required, float epsilon)
            => TryGetValidatedEmptyRearPoses(time, required, epsilon, out _);

        private bool TryGetValidatedEmptyRearPoses(float time, ChamberActionBinding[] required, float epsilon,
            out System.Collections.Generic.Dictionary<Transform, Pose> poses)
        {
            poses = null;
            if (_feedback == null || _body == null ||
                !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 direction, out float length)) return false;
            Transform parent = _slide.transform.parent;
            Matrix4x4 parentFromBody = (parent != null ? parent.worldToLocalMatrix : Matrix4x4.identity) * _body.localToWorldMatrix;
            Func<Transform, Pose?> rest = target => TryGetSourceRestBodyPose(target, out Pose value) ? value : (Pose?)null;
            if (!TryCalculateEmptyRearPoses(_motion != null ? _motion.Empty : null, time, direction,
                length, epsilon, parentFromBody, rest, out poses)) return false;
            if (required == null) return false;
            foreach (ChamberActionBinding binding in required)
                if (binding == null || !poses.TryGetValue(binding.Target, out Pose pose) ||
                    !binding.TryGetPoseProgress(pose, epsilon, out float progress) || progress <= 0f) return false;
            return true;
        }

        // Один pose-math kernel: runtime даёт действующие frames, producer — fresh source TRS.
        // Данные rest/rear принадлежат source, здесь нет transform/SDK/cache writes.
        private bool TryCalculateEmptyRearPoses(WeaponMechanismMotion.Cycle empty, float time,
            Vector3 direction, float length, float epsilon, Matrix4x4 parentFromBody,
            Func<Transform, Pose?> sourceRest, out System.Collections.Generic.Dictionary<Transform, Pose> poses, bool fullNativeProjection = false)
        {
            poses = new System.Collections.Generic.Dictionary<Transform, Pose>();
            if (empty == null || empty.Tracks == null || _bindings == null ||
                !FiniteSource(time) || time < 0f || time > empty.Duration ||
                !FiniteSource(length) || length <= 0f || !FiniteSource(epsilon) || epsilon <= 0f) return false;
            float rear = 0f;
            foreach (Binding binding in _bindings)
            {
                if (binding == null || !IsAction(binding)) continue;
                WeaponMechanismMotion.Track track = Array.Find(empty.Tracks, candidate => candidate != null && candidate.Part == binding.Part);
                Pose? rest = sourceRest(binding.Target);
                if (track == null || track.Keys == null || track.Keys.Length == 0 || !rest.HasValue) return false;
                Pose pose = track.Evaluate(time);
                if (!FiniteSource(pose.position) || !FiniteSource(pose.rotation) || poses.ContainsKey(binding.Target)) return false;
                poses.Add(binding.Target, pose);
                float along = Vector3.Dot(parentFromBody.MultiplyVector(pose.position - rest.Value.position), direction);
                if (!FiniteSource(along) || along > length + epsilon || along < -epsilon) return false;
                rear = Mathf.Max(rear, along);
            }
            Pose? slideRest = sourceRest(_slide.transform), contactRest = sourceRest(_contactPart);
            if (rear <= epsilon || !slideRest.HasValue || !contactRest.HasValue) return false;
            Vector3 bodyOffset = parentFromBody.inverse.MultiplyVector(direction * (fullNativeProjection ? length : rear));
            poses[_slide.transform] = new Pose(slideRest.Value.position + bodyOffset, slideRest.Value.rotation);
            if (!poses.TryGetValue(_contactPart, out Pose contact)) return false;
            poses[_contactPart] = new Pose(contactRest.Value.position + bodyOffset, contact.rotation);
            return true;
        }

        /// <summary>
        /// Pure mapping подготовленного authored root. Вызывать до изменения Action pose;
        /// runtime moved object не является source. Awake/InitialLocalRotation/feedback caches не читаются.
        /// </summary>
        public bool TryBuildPreparedActionMapping(WeaponReadinessProfile profile, out ChamberActionBinding[] required,
            out float emptyRearTime, out string error)
        {
            required = Array.Empty<ChamberActionBinding>(); emptyRearTime = -1f; error = null;
            if (profile == null || !profile.TryValidate(out error) ||
                profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo ||
                profile.PhysicalCapability != WeaponPhysicalCapability.ActionTravel)
            { error = error ?? "Source factory требует detachable ActionTravel profile."; return false; }
            if (_body == null || _slide == null || _contactPart == null ||
                !_body.IsChildOf(transform) || !_slide.transform.IsChildOf(transform) ||
                !_contactPart.IsChildOf(_slide.transform) ||
                !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 direction, out float length))
            { error = "Нет authored body/Slide/contact/native travel."; return false; }
            if (_body.IsChildOf(_slide.transform))
            { error = "Source Body находится внутри движущегося Slide."; return false; }
            var sourceTargets = new System.Collections.Generic.HashSet<Transform>();
            var sourceParts = new System.Collections.Generic.HashSet<string>();
            foreach (Binding binding in _bindings ?? Array.Empty<Binding>())
            {
                if (binding == null || binding.Target == null || string.IsNullOrEmpty(binding.Part) ||
                    !binding.Target.IsChildOf(transform) || !sourceTargets.Add(binding.Target) || !sourceParts.Add(binding.Part) ||
                    !FiniteSource(binding.RestPosition) || !FiniteSource(binding.RestRotation))
                { error = "Невалидный/повторный source binding/Part/target."; return false; }
            }
            Pose slideLocalRest = new Pose(_slide.transform.localPosition, _slide.transform.localRotation);
            if (!FiniteSource(slideLocalRest.position) || !FiniteSource(slideLocalRest.rotation))
            { error = "Невалидный source Slide TRS."; return false; }
            float coordinate = Mathf.Max(Mathf.Abs(slideLocalRest.position.x), Mathf.Abs(slideLocalRest.position.y),
                Mathf.Abs(slideLocalRest.position.z), length);
            float epsilon = 8f * 1.192092896e-7f * coordinate;
            if (!FiniteSource(epsilon) || epsilon <= 0f || epsilon >= length || epsilon * epsilon <= 0f)
            { error = "Численный source диапазон не различает front и rear."; return false; }
            if (!TryGetRestMatrixToRoot(_body, out Matrix4x4 bodyMatrix, slideLocalRest) ||
                !TryGetRestMatrixToRoot(_slide.transform.parent, out Matrix4x4 parentMatrix, slideLocalRest))
            { error = "Source frames не принадлежат одному root."; return false; }
            Matrix4x4 parentFromBody = parentMatrix.inverse * bodyMatrix;
            Func<Transform, Pose?> rest = target =>
            {
                if (target == null || !TryGetRestMatrixToRoot(target, out Matrix4x4 matrix, slideLocalRest)) return null;
                matrix = bodyMatrix.inverse * matrix;
                var result = new Pose(matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation);
                return FiniteSource(result.position) && FiniteSource(result.rotation) ? result : (Pose?)null;
            };
            Transform[] targets = GetRequiredActionTargets();
            if (targets.Length == 0) { error = "Нет required Action targets."; return false; }
            foreach (Transform target in targets)
                if (!ChamberActionBinding.HasSupportedLocalFrames(target, _body))
                { error = "Unsupported source Action frames."; return false; }
            System.Collections.Generic.Dictionary<Transform, Pose> selected = null;
            if (_motion == null)
            {
                if (profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn || profile.EmptyPose != WeaponEmptyPose.ReturnToRest ||
                    targets.Length != 1 || targets[0] != _slide.transform || _contactPart != _slide.transform ||
                    (_bindings != null && _bindings.Length != 0) ||
                    !(_slide.RotationConstraint == UxrRotationConstraintMode.Locked ||
                      (_slide.RotationConstraint == UxrRotationConstraintMode.RestrictLocalRotation &&
                       _slide.RotationAngleLimitsMin.Equals(Vector3.zero) && _slide.RotationAngleLimitsMax.Equals(Vector3.zero))))
                { error = "Scalar source допускает только sole locked Slide / ManualReturn / ReturnToRest."; return false; }
                foreach (UxrGrabbableObject part in GetComponentsInChildren<UxrGrabbableObject>(true))
                    if (part.gameObject != gameObject && part != _slide && part.GetComponentInParent<UxrFirearmMag>() == null)
                    { error = "Scalar source содержит скрытый secondary Action/grabbable."; return false; }
                Pose? value = rest(_slide.transform);
                if (!value.HasValue) { error = "Нет scalar source rest."; return false; }
                selected = new System.Collections.Generic.Dictionary<Transform, Pose> {
                    [_slide.transform] = new Pose(value.Value.position + parentFromBody.inverse.MultiplyVector(direction * length), value.Value.rotation) };
            }
            else
            {
                WeaponMechanismMotion.Cycle empty = _motion.Empty ?? _motion.Manual;
                bool noEmpty = _motion.Empty == null;
                if (noEmpty)
                {
                    if (profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn || profile.EmptyPose != WeaponEmptyPose.ReturnToRest ||
                        !(_slide.RotationConstraint == UxrRotationConstraintMode.Locked ||
                          (_slide.RotationConstraint == UxrRotationConstraintMode.RestrictLocalRotation &&
                           _slide.RotationAngleLimitsMin.Equals(Vector3.zero) && _slide.RotationAngleLimitsMax.Equals(Vector3.zero))) ||
                        _contactPart == _slide.transform || !sourceTargets.Contains(_contactPart))
                    { error = "No-Empty source требует ManualReturn/ReturnToRest, locked Slide и declared contact."; return false; }
                    foreach (Transform target in targets)
                    {
                        if (target == _slide.transform) continue;
                        if (target.parent != _slide.transform)
                        { error = "No-Empty source поддерживает только плоский declared Action graph."; return false; }
                    }
                    foreach (UxrGrabbableObject part in GetComponentsInChildren<UxrGrabbableObject>(true))
                        if (part.gameObject != gameObject && part != _slide && part.GetComponentInParent<UxrFirearmMag>() == null)
                        { error = "No-Empty source содержит неизвестный Action/grabbable."; return false; }
                    if (empty == null)
                    {
                        Vector3 bodyOffset = parentFromBody.inverse.MultiplyVector(direction * length);
                        selected = new System.Collections.Generic.Dictionary<Transform, Pose>();
                        foreach (Transform target in targets)
                        {
                            Pose? atRest = rest(target);
                            if (!atRest.HasValue) { error = "Нет declared rigid rest."; return false; }
                            // Absolute Body endpoint: parent и child не получают два local offsets.
                            selected[target] = new Pose(atRest.Value.position + bodyOffset, atRest.Value.rotation);
                        }
                    }
                }
                if (selected == null && (empty == null || !ValidateSourceCycle(empty, targets, out error))) return false;
                var times = new System.Collections.Generic.SortedSet<float>();
                if (empty != null) times.Add(empty.Duration);
                if (empty != null && (!empty.HoldEnd || noEmpty)) foreach (WeaponMechanismMotion.Track track in empty.Tracks)
                    if (track != null && track.Keys != null) foreach (WeaponMechanismMotion.Key key in track.Keys)
                        if (FiniteSource(key.Time) && key.Time >= 0f && key.Time <= empty.Duration) times.Add(key.Time);
                float best = -1f;
                foreach (float time in times)
                {
                    if (!TryCalculateEmptyRearPoses(empty, time, direction, length, epsilon, parentFromBody, rest, out var candidate, noEmpty)) continue;
                    bool complete = true;
                    foreach (Transform target in targets)
                    {
                        Pose? atRest = rest(target);
                        if (!atRest.HasValue || !candidate.TryGetValue(target, out Pose atRear) ||
                            ((atRear.position - atRest.Value.position).sqrMagnitude <= epsilon * epsilon &&
                             (!RequiresActionRotation(target) || ChamberActionBinding.RotationNear(atRest.Value.rotation, atRear.rotation))))
                        { complete = false; break; }
                    }
                    if (!complete) continue;
                    float progress = Vector3.Dot(parentFromBody.MultiplyVector(candidate[_slide.transform].position - rest(_slide.transform).Value.position), direction);
                    if (noEmpty)
                    {
                        Binding contactBinding = Array.Find(_bindings, value => value.Target == _contactPart);
                        WeaponMechanismMotion.Track contactTrack = Array.Find(empty.Tracks, value => value.Part == contactBinding.Part);
                        progress = Vector3.Dot(parentFromBody.MultiplyVector(contactTrack.Evaluate(time).position - rest(_contactPart).Value.position), direction);
                    }
                    if (progress > best) { best = progress; selected = candidate; if (!noEmpty) emptyRearTime = time; }
                }
                if (selected == null) { error = "Нет общей проверяемой rear-позы всех Action parts."; return false; }
            }
            required = new ChamberActionBinding[targets.Length];
            for (int index = 0; index < targets.Length; index++)
            {
                Transform target = targets[index]; Pose atRest = rest(target).Value, atRear = selected[target];
                required[index] = new ChamberActionBinding { Target = target, RestPosition = atRest.position,
                    RestRotation = atRest.rotation, RearPosition = atRear.position,
                    RearRotation = RequiresActionRotation(target) ? atRear.rotation : atRest.rotation,
                    AnimateRotation = RequiresActionRotation(target), CapturedParent = target.parent };
            }
            return true;
        }

        private bool ValidateSourceCycle(WeaponMechanismMotion.Cycle cycle, Transform[] targets, out string error)
        {
            error = null;
            if (cycle.Tracks == null || !FiniteSource(cycle.Duration) || cycle.Duration <= 0f)
            { error = "Source cycle не содержит валидной duration/tracks."; return false; }
            var parts = new System.Collections.Generic.HashSet<string>();
            foreach (WeaponMechanismMotion.Track track in cycle.Tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.Part) || !parts.Add(track.Part) || track.Keys == null || track.Keys.Length == 0)
                { error = "Source cycle содержит null/duplicate/empty track."; return false; }
                float previous = -1f;
                foreach (WeaponMechanismMotion.Key key in track.Keys)
                {
                    if (!FiniteSource(key.Time) || key.Time < 0f || key.Time > cycle.Duration || key.Time < previous ||
                        !FiniteSource(key.Position) || !FiniteSource(key.Rotation))
                    { error = "Source cycle содержит невалидный key."; return false; }
                    previous = key.Time;
                }
            }
            foreach (Transform target in targets)
            {
                if (target == _slide.transform) continue;
                Binding binding = Array.Find(_bindings, value => value.Target == target);
                if (binding == null || !parts.Contains(binding.Part))
                { error = "Source cycle пропускает required Action track."; return false; }
            }
            return true;
        }

        private static bool FiniteSource(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FiniteSource(Vector3 value) => FiniteSource(value.x) && FiniteSource(value.y) && FiniteSource(value.z);
        private static bool FiniteSource(Quaternion value) => FiniteSource(value.x) && FiniteSource(value.y) &&
            FiniteSource(value.z) && FiniteSource(value.w) && Mathf.Abs(Quaternion.Dot(value, value) - 1f) <= 0.00001f;

        public bool IsValidatedEmptyRearPose(float time, ChamberActionBinding[] required, float epsilon)
        {
            if (!TryGetValidatedEmptyRearPoses(time, required, epsilon, out var poses)) return false;
            foreach (ChamberActionBinding binding in required)
                if (!ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out Pose actual) ||
                    (actual.position - poses[binding.Target].position).sqrMagnitude > epsilon * epsilon ||
                    !ChamberActionBinding.RotationNear(actual.rotation, poses[binding.Target].rotation)) return false;
            return true;
        }

        public void ClearConsumedEmptyPresentation()
        {
            if (!_emptyShot || _ownedChamberReturn || _manual) return;
            // Action ownership закончено; independent Source каналы доигрывают свой
            // цикл. Отмена origin не гасит чужой firing/FX канал и не меняет pose.
            _emptyShot = false; _emptyHeld = false; _suppressActionCycle = _cycle != null;
            _emptyWork = EmptyPresentationWork.None;
            _feedback?.ClearLedgerVisualHold();
            // Не ResetVisuals: canceled/partial actual pose не телепортируется в rest.
        }

        public bool RestoreSavedEmptyPresentation(bool holdOpen, float time, ChamberActionBinding[] required, float epsilon)
        {
            if (!HasLedgerAdapter || _ownedChamberReturn || _slide == null ||
                (UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide))) return false;
            var state = _weapon.GetReadinessState(0);
            if (state == null || !state.PostShotEmptyAction || state.ChamberRound || state.ActionOpen || state.ChamberCyclePending) return false;
            // Авторская source phase не перескакивает к snapshot endpoint от polling origin.
            if (IsSourceEmptyInProgress && _emptyWorkShot == state.ShotSequence) return false;
            System.Collections.Generic.Dictionary<Transform, Pose> rear = null;
            if (holdOpen && !TryGetValidatedEmptyRearPoses(time, required, epsilon, out rear)) return false;
            var sorted = (ChamberActionBinding[])required.Clone();
            Array.Sort(sorted, (left, right) => HierarchyDepth(left.Target).CompareTo(HierarchyDepth(right.Target)));
            foreach (ChamberActionBinding binding in sorted)
            {
                if (holdOpen)
                {
                    if (!ChamberActionBinding.TrySetBodyPose(binding.Target, _body, rear[binding.Target], true)) return false;
                }
                else
                {
                    if (!TryGetSourceLocalRestPose(binding.Target, out Pose rest)) return false;
                    binding.Target.SetLocalPositionAndRotation(rest.position, rest.rotation);
                }
            }
            // Snapshot settle меняет только Action presentation. Старый source cycle,
            // уже переведённый в independent-only, не обрывается этим pose projection.
            if (!(_suppressActionCycle && _emptyWork != EmptyPresentationWork.Source && _cycle != null)) _cycle = null;
            _emptyShot = true; _emptyHeld = true; _manual = false; _catchRequired = false;
            bool endpoint = holdOpen ? IsValidatedEmptyRearPose(time, required, epsilon) : true;
            if (!holdOpen) foreach (ChamberActionBinding binding in required) endpoint &= binding.IsAtRest(_body, epsilon);
            if (!endpoint) { _emptyWork = EmptyPresentationWork.Deferred; return false; }
            _emptyWorkShot = state.ShotSequence;
            _emptyWork = holdOpen ? EmptyPresentationWork.HoldSettled : EmptyPresentationWork.RestServiced;
            // Никаких Source/Reload/feed/extraction/audio callbacks при snapshot projection.
            return true;
        }

        private static int HierarchyDepth(Transform target)
        {
            int depth = 0;
            for (Transform current = target; current != null; current = current.parent) depth++;
            return depth;
        }

        public void EndOwnedChamberReturn()
        {
            _ownedChamberReturn = false;
            if (_emptyWork == EmptyPresentationWork.ReturnDriver) _emptyWork = EmptyPresentationWork.Deferred;
            _manual = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            _emptyHeld = false;
            // Не ResetVisuals: actual pose сохраняется, SDK сам проверяет физическое закрытие.
        }

        public void InvalidateActionPresentationAfterStateLoad()
        {
            // Pure local invalidation: не читать partly loaded grab state, не писать
            // transforms/SDK. Independent source channels продолжают свой цикл.
            _manual = false; _ownedChamberReturn = false; _catchRequired = false;
            _manualRotationRearProgress = 0f;
            _emptyShot = _emptyHeld = false; _emptyWork = EmptyPresentationWork.None;
            _suppressActionCycle = _cycle != null;
        }

        public bool CaptureCurrentManualContact()
        {
            if (!HasLedgerAdapter || _feedback == null || _bindings == null ||
                !UxrGrabManager.HasInstance || !UxrGrabManager.Instance.IsBeingGrabbed(_slide)) return false;
            var poses = new Pose[_bindings.Length];
            for (int index = 0; index < _bindings.Length; index++)
                if (IsAction(_bindings[index]) &&
                    !ChamberActionBinding.TryGetBodyPose(_bindings[index].Target, _body, out poses[index])) return false;
            float progress = _feedback.SignedSlideProgress;
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return false;
            _manual = true; _suppressActionCycle = true; _catchRequired = false;
            _manualRotationRearProgress = Mathf.Max(0f, progress);
            for (int index = 0; index < _bindings.Length; index++)
            {
                Binding binding = _bindings[index];
                if (!IsAction(binding)) continue;
                binding.FrozenPosition = binding.Target.localPosition; binding.FrozenRotation = binding.Target.localRotation;
                binding.FrozenBodyPosition = poses[index].position; binding.FrozenBodyRotation = poses[index].rotation;
                binding.RotationAnchorBody = poses[index].rotation;
                binding.RotationAnchorProgress = binding.RotationLastProgress = progress;
                binding.RotationOpening = true;
            }
            return true; // Только capture текущей позы, без handoff/нового хвата/жеста.
        }

        public void RefreshOwnedManualPose()
        {
            if (!HasLedgerAdapter || !_manual || _ownedChamberReturn || _bindings == null) return;
            bool held = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            float progress = _feedback.SignedSlideProgress;
            float epsilon = _feedback.PhysicalPositionEpsilon / _feedback.SlideTravelLength;
            if (!held || (_catchRequired && progress + epsilon >= _catchProgress)) _catchRequired = false;
            if (_manualRotationRearProgress <= 0f && progress > epsilon) _manualRotationRearProgress = progress;
            foreach (Binding binding in _bindings)
            {
                if (!IsAction(binding)) continue;
                if (_catchRequired && binding.Target != _contactPart)
                    ChamberActionBinding.TrySetBodyPose(binding.Target, _body,
                        new Pose(binding.FrozenBodyPosition, binding.FrozenBodyRotation), binding.Rotate);
                else if (held && binding.Target == _contactPart)
                {
                    binding.Target.localPosition = binding.FrozenPosition;
                    if (binding.Rotate) ApplyOwnedManualRotation(binding, progress);
                }
                else if (held)
                {
                    if (ReadinessAdapter.TryGetManualPositionMapping(binding.Target, out Vector3 rest, out Vector3 rear) &&
                        ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out Pose current))
                        ChamberActionBinding.TrySetBodyPose(binding.Target, _body,
                            new Pose(Vector3.Lerp(rest, rear, Mathf.Clamp01(progress)), current.rotation), false);
                    if (binding.Rotate) ApplyOwnedManualRotation(binding, progress);
                }
                else
                {
                    binding.Target.localPosition = Vector3.MoveTowards(binding.Target.localPosition, binding.RestPosition, _feedback.AutoReturnSpeed * Time.deltaTime);
                    if (binding.Rotate) binding.Target.localRotation = Quaternion.RotateTowards(binding.Target.localRotation, binding.RestRotation, 720f * Time.deltaTime);
                }
            }
            // Не ResetVisuals по широкому расстоянию: endpoint читает все actual channels.
            if (!held && ReadinessAdapter.IsActionAtRest) _manual = false;
        }

        private void ApplyOwnedManualRotation(Binding binding, float progress)
        {
            if (!ReadinessAdapter.TryGetManualRotationMapping(binding.Target, out Quaternion rest, out Quaternion rear)) return;
            float epsilon = _feedback.PhysicalPositionEpsilon / _feedback.SlideTravelLength;
            float delta = progress - binding.RotationLastProgress;
            bool opening = delta > epsilon ? true : delta < -epsilon ? false : binding.RotationOpening;
            if (opening != binding.RotationOpening)
            {
                if (!ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out Pose captured)) return;
                binding.RotationAnchorBody = captured.rotation;
                binding.RotationAnchorProgress = binding.RotationLastProgress;
                binding.RotationOpening = opening;
            }
            Quaternion value = binding.RotationAnchorBody;
            if (opening)
            {
                float distance = 1f - binding.RotationAnchorProgress;
                if (distance > epsilon)
                    value = Quaternion.Slerp(binding.RotationAnchorBody, rear, Mathf.Clamp01((progress - binding.RotationAnchorProgress) / distance));
            }
            else if (binding.RotationAnchorProgress > epsilon)
                value = Quaternion.Slerp(rest, binding.RotationAnchorBody, Mathf.Clamp01(progress / binding.RotationAnchorProgress));
            if (ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out Pose actual))
                ChamberActionBinding.TrySetBodyPose(binding.Target, _body, new Pose(actual.position, value), true);
            binding.RotationLastProgress = progress;
        }

        private void OnEnable()
        {
            _weapon = GetComponent<UxrFirearmWeapon>();
            _feedback = GetComponent<AutomaticWeaponSlideFeedback>();
            if (_weapon != null) { _weapon.ProjectileShot += HandleShot; _weapon.ProjectileShotReplayed += HandleShot; }
            if (_slide != null) _slide.Grabbing += HandleGrabbing;
            if (_feedback != null) _feedback.ManualCycleCompleted += HandleManualCompleted;
        }

        private void OnDisable()
        {
            if (_weapon != null) { _weapon.ProjectileShot -= HandleShot; _weapon.ProjectileShotReplayed -= HandleShot; }
            if (_slide != null) _slide.Grabbing -= HandleGrabbing;
            if (_feedback != null) _feedback.ManualCycleCompleted -= HandleManualCompleted;
            if (_feedback != null) _feedback.CancelVisualHold();
            ResetVisuals();
        }

        private void HandleShot(int trigger) => PlayShot(trigger, (_weapon.UsesReadinessLedger(trigger) ? _weapon.GetTotalAmmoLeft(trigger) : _weapon.GetAmmoLeft(trigger)) == 0);

        public void PlayShot(int triggerIndex, bool emptyAfterShot)
        {
            bool held = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            if (triggerIndex != 0 || _motion == null || (!HasLedgerAdapter && (_manual || held))) return;
            _cycle = emptyAfterShot && _motion.Empty != null ? _motion.Empty : _motion.Fire;
            // Если отдельного Empty clip нет, существующий Fire остаётся владельцем
            // последней source phase. Политика ReturnToRest не обрывает этот канал.
            _emptyShot = emptyAfterShot && (_motion.Empty != null || HasLedgerAdapter);
            _emptyWork = HasLedgerAdapter && _emptyShot ? EmptyPresentationWork.Source : EmptyPresentationWork.None;
            if (_emptyWork == EmptyPresentationWork.Source) _emptyWorkShot = _weapon.GetReadinessState(triggerIndex).ShotSequence;
            _suppressActionCycle = HasLedgerAdapter && (_manual || held || _ownedChamberReturn);
            _emptyHeld = false;
            _catchRequired = false;
            _started = Time.time;
        }

        private void LateUpdate()
        {
            UpdateMagazineBinding();
            bool held = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            if (HasLedgerAdapter && _manual)
            {
                RefreshOwnedManualPose();
                UpdateIndependentSourceChannels();
                return;
            }
            if (_manual)
            {
                float progress = _feedback != null ? _feedback.GetSlideProgress() : 0f;
                if (_catchRequired && progress >= _catchProgress - 0.0005f)
                {
                    _catchRequired = false;
                    _emptyHeld = false;
                }
                foreach (Binding binding in _bindings)
                {
                    if (!IsAction(binding) || binding.Target == null) continue;
                    if (_catchRequired && binding.Target != _contactPart)
                    {
                        binding.Target.SetPositionAndRotation(_body.TransformPoint(binding.FrozenBodyPosition), _body.rotation * binding.FrozenBodyRotation);
                    }
                    else
                    {
                        // Контактная система UXR не меняется под держащей рукой.
                        if (held && binding.Target == _contactPart)
                        {
                            binding.Target.localPosition = binding.FrozenPosition;
                            if (binding.Rotate)
                            {
                                // Собственный поворот контактной детали возвращается вместе с реальным
                                // ходом ручки. Handoff непрерывен; в переднем упоре rotation точно rest.
                                float remaining = _manualRotationRearProgress > 0f
                                    ? Mathf.Clamp01(progress / _manualRotationRearProgress) : 1f;
                                binding.Target.localRotation = Quaternion.Slerp(binding.RestRotation, binding.FrozenRotation, remaining);
                            }
                        }
                        else if (held)
                        {
                            binding.Target.localPosition = binding.RestPosition;
                            if (binding.Rotate) binding.Target.localRotation = binding.RestRotation;
                        }
                        else
                        {
                            binding.Target.localPosition = Vector3.MoveTowards(binding.Target.localPosition, binding.RestPosition, 1.5f * Time.deltaTime);
                            if (binding.Rotate) binding.Target.localRotation = Quaternion.RotateTowards(binding.Target.localRotation, binding.RestRotation, 720f * Time.deltaTime);
                        }
                    }
                }
                if (!held && (_slide == null || (_slide.transform.localPosition - _slide.InitialLocalPosition).sqrMagnitude < 1e-8f))
                {
                    if (_catchRequired)
                    {
                        // Отпускание до зацепления не закрывает открытый внутренний затвор.
                        _manual = false;
                        _emptyHeld = true;
                    }
                    else ResetVisuals();
                }
                return;
            }
            if (HasLedgerAdapter && _suppressActionCycle && _emptyWork != EmptyPresentationWork.Source)
            { UpdateIndependentSourceChannels(); return; }
            if (_cycle == null) return;
            float endTime = HasLedgerAdapter && _emptyShot && ReadinessAdapter.EmptyPose == WeaponEmptyPose.HoldOpen
                ? ReadinessAdapter.EmptyRearTime : _cycle.Duration;
            bool holdEnd = _cycle.HoldEnd || (HasLedgerAdapter && _emptyShot && ReadinessAdapter.EmptyPose == WeaponEmptyPose.HoldOpen);
            float time = Mathf.Min(Time.time - _started, endTime);
            foreach (Binding binding in _bindings) ApplyTrack(binding, _cycle, time);
            if (holdEnd && !_ownedChamberReturn && !_suppressActionCycle) CoupleHeldEmptyContact();
            if (time < endTime) return;
            if (HasLedgerAdapter && _emptyShot)
            {
                FinishLedgerEmptySource(holdEnd);
                return;
            }
            if (holdEnd)
            {
                if (!_emptyHeld) FinishEmpty();
            }
            else ResetVisuals();
        }

        /// <summary>Общий игровой профиль Empty: доступная руке деталь остаётся сзади вместе с механизмом.</summary>
        private void CoupleHeldEmptyContact()
        {
            if (_contactPart == null || _slide == null || _bindings == null ||
                !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 direction, out float length)) return;
            Binding contact = Array.Find(_bindings, binding => binding.Target == _contactPart);
            if (contact == null) return;
            Transform slideParent = _slide.transform.parent;
            float rear = 0f;
            foreach (Binding binding in _bindings)
            {
                if (!IsAction(binding)) continue;
                Vector3 worldOffset = binding.Target.parent.TransformVector(binding.Target.localPosition - binding.RestPosition);
                Vector3 localOffset = slideParent != null ? slideParent.InverseTransformVector(worldOffset) : worldOffset;
                rear = Mathf.Max(rear, Vector3.Dot(localOffset, direction));
            }
            // Клип остаётся источником движения. Ручка следует его реальной задней позе
            // только в Empty, внутри уже объявленных ограничений ручного механизма.
            Vector3 offset = direction * Mathf.Clamp(rear, 0f, length);
            Vector3 worldOffsetToRear = slideParent != null ? slideParent.TransformVector(offset) : offset;
            _contactPart.position = _contactPart.parent.TransformPoint(contact.RestPosition) + worldOffsetToRear;
        }

        private void FinishEmpty()
        {
            _emptyHeld = true;
            // Общая Empty-поза передаётся ручному механизму, сохраняя мировые позы деталей.
            if (_feedback == null || _contactPart == null || !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 dir, out float length)) return;
            Binding contact = Array.Find(_bindings, b => b.Target == _contactPart);
            if (contact == null) return;
            Vector3 delta = _slide.transform.InverseTransformVector(_contactPart.parent.TransformVector(_contactPart.localPosition - contact.RestPosition));
            float along = Vector3.Dot(delta, dir);
            if (Mathf.Abs(along) < 0.001f) return;
            Vector3[] positions = new Vector3[_bindings.Length];
            Quaternion[] rotations = new Quaternion[_bindings.Length];
            for (int i = 0; i < _bindings.Length; i++) if (_bindings[i].Target != null) { positions[i] = _bindings[i].Target.position; rotations[i] = _bindings[i].Target.rotation; }
            _feedback.HoldVisualPose(dir * along);
            // Мировая поза каждого независимого канала и контакта остаётся прежней.
            for (int i = 0; i < _bindings.Length; i++) if (IsAction(_bindings[i]) && _bindings[i].Target != null)
                _bindings[i].Target.SetPositionAndRotation(positions[i], rotations[i]);
            _cycle = null;
            if (HasLedgerAdapter && ReadinessAdapter.EmptyPose == WeaponEmptyPose.ReturnToRest) ReadinessAdapter.RequestEmptyReturn();
        }

        private void FinishLedgerEmptySource(bool holdEnd)
        {
            // Source phase завершается один раз даже при held Action. Применение позы
            // может оставаться deferred; source time не разрешает двигать удерживаемую деталь.
            _cycle = null;
            _emptyWork = EmptyPresentationWork.Deferred;
            _emptyHeld = false;
            if (!holdEnd) foreach (Binding binding in _bindings)
            {
                if (binding.Target == null || IsAction(binding) || binding.InMagazine) continue;
                binding.Target.SetLocalPositionAndRotation(binding.RestPosition, binding.Rotate ? binding.RestRotation : binding.Target.localRotation);
            }
            bool held = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            if (held || _ownedChamberReturn || _suppressActionCycle) return;
            ReadinessAdapter.ServiceFinishedEmptyPresentation(holdEnd);
        }

        private void HandleGrabbing(object sender, UxrManipulationEventArgs e)
        {
            Binding contact = Array.Find(_bindings, b => b.Target == _contactPart);
            if (_feedback != null && contact != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 direction, out _))
            {
                Vector3 residual = _slide.transform.InverseTransformVector(_contactPart.parent.TransformVector(_contactPart.localPosition - contact.RestPosition));
                var poses = new Pose[_bindings.Length];
                for (int i = 0; i < _bindings.Length; i++) if (_bindings[i].Target != null)
                {
                    if (HasLedgerAdapter) ChamberActionBinding.TryGetBodyPose(_bindings[i].Target, _body, out poses[i]);
                    else poses[i] = new Pose(_bindings[i].Target.position, _bindings[i].Target.rotation);
                }
                Vector3 slideBeforeHandoff = _slide.transform.localPosition;
                _feedback.BeginVisualHandoff(direction * Vector3.Dot(residual, direction));
                for (int i = 0; i < _bindings.Length; i++) if (IsAction(_bindings[i]) && _bindings[i].Target != null)
                {
                    if (HasLedgerAdapter)
                    {
                        if ((_slide.transform.localPosition - slideBeforeHandoff).sqrMagnitude > 0f)
                            ChamberActionBinding.TrySetBodyPose(_bindings[i].Target, _body, poses[i], _bindings[i].Rotate);
                    }
                    else _bindings[i].Target.SetPositionAndRotation(poses[i].position, poses[i].rotation);
                }
                if ((_slide.transform.localPosition - slideBeforeHandoff).sqrMagnitude > 0f)
                    UxrGrabManager.CaptureGrabbingObjectPose(e);
            }
            _manual = true;
            float rearProgress = _feedback != null ? _feedback.GetSlideProgress() : 0f;
            _manualRotationRearProgress = float.IsNaN(rearProgress) || float.IsInfinity(rearProgress) || rearProgress <= 0f ? 0f : rearProgress;
            if (HasLedgerAdapter) _suppressActionCycle = true;
            else _cycle = null;
            _catchRequired = false;
            foreach (Binding b in _bindings) if (b.Target != null)
            {
                b.FrozenPosition = b.Target.localPosition; b.FrozenRotation = b.Target.localRotation;
                if (HasLedgerAdapter && ChamberActionBinding.TryGetBodyPose(b.Target, _body, out Pose captured))
                { b.FrozenBodyPosition = captured.position; b.FrozenBodyRotation = captured.rotation; }
                else
                { b.FrozenBodyPosition = _body.InverseTransformPoint(b.Target.position); b.FrozenBodyRotation = Quaternion.Inverse(_body.rotation) * b.Target.rotation; }
                b.RotationAnchorBody = b.FrozenBodyRotation;
                b.RotationAnchorProgress = b.RotationLastProgress = _feedback != null ? _feedback.SignedSlideProgress : 0f;
                b.RotationOpening = true;
            }
            // Открытый внутренний затвор возможен уже в первом кадре Empty и в середине обычного Fire.
            if (_feedback != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 dir, out float length))
            {
                float far = 0f;
                foreach (Binding b in _bindings) if (IsAction(b) && b.Target != _contactPart)
                    far = Mathf.Max(far, Vector3.Dot(_slide.transform.InverseTransformVector(b.Target.parent.TransformVector(b.Target.localPosition - b.RestPosition)), dir));
                if (!HasLedgerAdapter && far > 0.001f)
                {
                    _catchRequired = true;
                    _catchProgress = _feedback.GetSlideProgress() + far / length;
                    _feedback.RequireManualCatch(_catchProgress);
                }
            }
        }

        /// <summary>
        /// Физический передний упор Action-каналов, независимо от Loaded/события досылания.
        /// Открытый internalBolt при передней Charger не является закрытым механизмом.
        /// </summary>
        public bool IsManualActionPhysicallyClosed(float positionEpsilon)
        {
            if (_bindings == null) return false;
            foreach (Binding binding in _bindings)
            {
                if (!IsAction(binding)) continue;
                if ((binding.Target.localPosition - binding.RestPosition).sqrMagnitude > positionEpsilon * positionEpsilon)
                    return false;
                // Dot quaternion содержит погрешность float даже у совпадающих rotations.
                if (binding.Rotate && 1f - Mathf.Abs(Quaternion.Dot(binding.Target.localRotation, binding.RestRotation)) > 8f * 1.192092896e-7f)
                    return false;
            }
            return true;
        }

        private void HandleManualCompleted() { _emptyHeld = false; }

        private bool IsAction(Binding b) => b.Target != null && _slide != null && b.Target.IsChildOf(_slide.transform);

        private void ApplyTrack(Binding binding, WeaponMechanismMotion.Cycle cycle, float time)
        {
            if (HasLedgerAdapter && IsAction(binding) && (_ownedChamberReturn || _suppressActionCycle)) return;
            if (binding.Target == null || cycle == null || (binding.InMagazine && (_currentMagazine == null || _currentMagazine.CurrentAnchor != _magazineAnchor))) return;
            WeaponMechanismMotion.Track track = Array.Find(cycle.Tracks, t => t.Part == binding.Part);
            if (track == null) return;
            Pose pose = track.Evaluate(time);
            if (HasLedgerAdapter && IsAction(binding))
                ChamberActionBinding.TrySetBodyPose(binding.Target, _body, pose, track.AnimateRotation && binding.Rotate);
            else
            {
                binding.Target.position = _body.TransformPoint(pose.position);
                if (track.AnimateRotation && binding.Rotate) binding.Target.rotation = _body.rotation * pose.rotation;
            }
        }

        private void UpdateMagazineBinding()
        {
            var magazine = _magazineAnchor != null ? _magazineAnchor.CurrentPlacedObject : null;
            if (magazine == _currentMagazine) return;
            _currentMagazine = magazine;
            foreach (Binding b in _bindings) if (b.InMagazine)
            {
                b.Target = null;
                if (magazine == null) continue;
                foreach (MeshFilter filter in magazine.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name == (b.Part == "Cylinder" ? "MechanismVisual" : b.Part)) { b.Target = filter.transform; break; }
            }
        }

        private void UpdateIndependentSourceChannels()
        {
            if (_cycle == null || _bindings == null) return;
            float time = Mathf.Min(Time.time - _started, _cycle.Duration);
            foreach (Binding binding in _bindings) if (!IsAction(binding)) ApplyTrack(binding, _cycle, time);
            if (HasLedgerAdapter && _emptyShot && _emptyWork == EmptyPresentationWork.Source)
            {
                float end = ReadinessAdapter.EmptyPose == WeaponEmptyPose.HoldOpen ? ReadinessAdapter.EmptyRearTime : _cycle.Duration;
                if (Time.time - _started >= end) FinishLedgerEmptySource(_cycle.HoldEnd);
                return;
            }
            if (time < _cycle.Duration || (_cycle.HoldEnd && !(HasLedgerAdapter && _suppressActionCycle))) return;
            _cycle = null;
            foreach (Binding binding in _bindings)
            {
                if (binding.Target == null || IsAction(binding) || binding.InMagazine) continue;
                binding.Target.localPosition = binding.RestPosition;
                if (binding.Rotate) binding.Target.localRotation = binding.RestRotation;
            }
        }

        public void ResetVisuals()
        {
            _cycle = null;
            if (!HasLedgerAdapter)
            { _manual = false; _emptyHeld = false; _catchRequired = false; _manualRotationRearProgress = 0f; }
            if (_bindings == null) return;
            foreach (Binding binding in _bindings)
            {
                if (HasLedgerAdapter && IsAction(binding) && (_ownedChamberReturn || _suppressActionCycle)) continue;
                if (binding.Target == null || (binding.InMagazine && (_currentMagazine == null || _currentMagazine.CurrentAnchor != _magazineAnchor))) continue;
                binding.Target.localPosition = binding.RestPosition;
                if (binding.Rotate) binding.Target.localRotation = binding.RestRotation;
            }
        }
    }
}

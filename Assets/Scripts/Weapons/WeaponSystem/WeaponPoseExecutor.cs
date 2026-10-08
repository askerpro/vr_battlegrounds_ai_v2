using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный писатель позы Action и деталей клипов (этап D, план п. 3.6, 4.1). Получает цель от машины
    /// (<see cref="PoseTarget"/>) и переводит её в трансформы <see cref="WeaponMechanismRig"/>. Не читает учёт и не решает,
    /// куда двигать детали: HoldOpen, пружина, Stay (S4), клипы Fire/Empty (S1) — всё это цели машины.
    ///
    /// <para>
    /// <b>Что хранит.</b> Только кинематическую непрерывность руки (поза контактной детали и опорный поворот на момент хвата,
    /// логика прежнего <c>WeaponMechanismVisuals.ApplyOwnedManualRotation</c>) и признак «нерычажные детали сдвинуты
    /// клипом» — чтобы вернуть их в покой один раз, а не писать трансформы каждый кадр. Фазы показа здесь нет (класс 4).
    /// </para>
    /// <para>
    /// <b>Перехват ручки.</b> Хват ручки, когда клип увёл контактную деталь (затвор посреди Fire-клипа), сдвигает ручку на
    /// эту деталь до того, как SDK посчитает хват (<c>Grabbing</c>), — рука берёт текущую позу Action без рывка.
    /// </para>
    /// </summary>
    internal sealed class WeaponPoseExecutor
    {
        private const float RotateDegreesPerSecond = 720f;

        private readonly WeaponMechanismRig _rig;
        private readonly bool _holdsOpen;
        private PosePresentation _lastAction = PosePresentation.Rest;
        private bool _auxDirty, _following, _attached;

        // Непрерывность руки (только пока ручку держат).
        private Vector3[] _frozenLocal;
        private Quaternion[] _anchorRotation;
        private float[] _anchorProgress, _lastProgress;
        private bool[] _opening;

        public WeaponPoseExecutor(WeaponMechanismRig rig, bool holdsOpen)
        {
            _rig = rig;
            _holdsOpen = holdsOpen;
        }

        /// <summary>Последняя применённая цель Action (для панели и проб).</summary>
        public PosePresentation LastAction => _lastAction;

        public void Attach()
        {
            if (_attached || _rig.Handle == null) return;
            _rig.Handle.Grabbing += OnHandleGrabbing;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            if (_rig.Handle != null) _rig.Handle.Grabbing -= OnHandleGrabbing;
            _attached = false;
            _following = false;
        }

        /// <summary>Применить цель кадра. Хост вызывает один раз за кадр с последней целью машины.</summary>
        public void Apply(in PoseTarget target, float deltaTime)
        {
            if (!_rig.IsPrepared) return;
            ApplyAuxiliary(target);
            if (!_rig.HasAction || !_rig.IsCurrent()) { _lastAction = target.Action; return; }
            if (target.Action != PosePresentation.FollowHand) _following = false;
            switch (target.Action)
            {
                case PosePresentation.FollowHand: FollowHand(); break;
                case PosePresentation.FireClip: ApplyActionClip(_rig.Motion != null ? _rig.Motion.Fire : null, target.ClipTime, false); break;
                case PosePresentation.EmptyClip: ApplyActionClip(_rig.Motion != null ? _rig.Motion.Empty : null, target.ClipTime, _holdsOpen); break;
                case PosePresentation.HoldRear: SetRear(); break;
                case PosePresentation.ReturnToRest: Return(target.ReturnSpeed * Mathf.Max(0f, deltaTime), deltaTime); break;
                // Rest машина выдаёт только для Action уже в покое (ComputePose); Stay — Action остаётся, где его отпустили.
            }
            _lastAction = target.Action;
        }

        // ── Action ──────────────────────────────────────────────────────────────────────────

        private void FollowHand()
        {
            ChamberActionBinding[] bindings = _rig.RuntimeBindings;
            float progress = _rig.HandleProgress;
            if (!_following) BeginFollow(bindings, progress);
            Transform handle = _rig.Handle.transform;
            for (int index = 0; index < bindings.Length; index++)
            {
                ChamberActionBinding binding = bindings[index];
                if (binding.Target == handle) continue; // ручку двигает рука (SDK / PumpGrabFollow)
                if (binding.Target == _rig.ContactPart)
                    binding.Target.localPosition = _frozenLocal[index]; // контактная деталь под рукой не смещается
                else if (ChamberActionBinding.TryGetBodyPose(binding.Target, _rig.Body, out Pose current))
                    ChamberActionBinding.TrySetBodyPose(binding.Target, _rig.Body,
                        new Pose(Vector3.Lerp(binding.RestPosition, binding.RearPosition, Mathf.Clamp01(progress)), current.rotation), false);
                if (binding.AnimateRotation) RotateWithHand(index, binding, progress);
            }
        }

        private void BeginFollow(ChamberActionBinding[] bindings, float progress)
        {
            int count = bindings.Length;
            if (_frozenLocal == null || _frozenLocal.Length != count)
            {
                _frozenLocal = new Vector3[count]; _anchorRotation = new Quaternion[count];
                _anchorProgress = new float[count]; _lastProgress = new float[count]; _opening = new bool[count];
            }
            for (int index = 0; index < count; index++)
            {
                Transform target = bindings[index].Target;
                _frozenLocal[index] = target.localPosition;
                _anchorRotation[index] = ChamberActionBinding.TryGetBodyPose(target, _rig.Body, out Pose pose) ? pose.rotation : bindings[index].RestRotation;
                _anchorProgress[index] = _lastProgress[index] = progress;
                _opening[index] = true;
            }
            _following = true;
        }

        /// <summary>Непрерывный поворот детали с ходом руки (прежний <c>ApplyOwnedManualRotation</c>): без скачка в момент хвата.</summary>
        private void RotateWithHand(int index, ChamberActionBinding binding, float progress)
        {
            float epsilon = _rig.Epsilon / _rig.TravelLength;
            float delta = progress - _lastProgress[index];
            bool opening = delta > epsilon || (!(delta < -epsilon) && _opening[index]);
            if (opening != _opening[index])
            {
                if (!ChamberActionBinding.TryGetBodyPose(binding.Target, _rig.Body, out Pose captured)) return;
                _anchorRotation[index] = captured.rotation;
                _anchorProgress[index] = _lastProgress[index];
                _opening[index] = opening;
            }
            Quaternion value = _anchorRotation[index];
            if (opening)
            {
                float distance = 1f - _anchorProgress[index];
                if (distance > epsilon)
                    value = Quaternion.Slerp(_anchorRotation[index], binding.RearRotation, Mathf.Clamp01((progress - _anchorProgress[index]) / distance));
            }
            else if (_anchorProgress[index] > epsilon)
                value = Quaternion.Slerp(binding.RestRotation, _anchorRotation[index], Mathf.Clamp01(progress / _anchorProgress[index]));
            if (ChamberActionBinding.TryGetBodyPose(binding.Target, _rig.Body, out Pose actual))
                ChamberActionBinding.TrySetBodyPose(binding.Target, _rig.Body, new Pose(actual.position, value), true);
            _lastProgress[index] = progress;
        }

        /// <summary>HoldOpen: проверенная задняя поза (родители раньше детей).</summary>
        private void SetRear()
        {
            ChamberActionBinding[] bindings = _rig.RuntimeBindings;
            foreach (int index in _rig.ParentFirstOrder)
            {
                ChamberActionBinding binding = bindings[index];
                ChamberActionBinding.TrySetBodyPose(binding.Target, _rig.Body, new Pose(binding.RearPosition, binding.RearRotation), binding.AnimateRotation);
            }
        }

        /// <summary>Возврат в покой со скоростью (пружина или подготовка); в конце — точная поза покоя.</summary>
        private void Return(float step, float deltaTime)
        {
            ChamberActionBinding[] bindings = _rig.RuntimeBindings;
            WeaponMechanismRig.LocalPose[] rest = _rig.ActionLocalRest;
            float angle = RotateDegreesPerSecond * Mathf.Max(0f, deltaTime);
            for (int index = 0; index < bindings.Length; index++)
            {
                Transform target = bindings[index].Target;
                target.localPosition = Vector3.MoveTowards(target.localPosition, rest[index].Position, step);
                if (Quaternion.Angle(target.localRotation, rest[index].Rotation) <= angle) target.localRotation = rest[index].Rotation;
                else target.localRotation = Quaternion.RotateTowards(target.localRotation, rest[index].Rotation, angle);
            }
        }

        private void ApplyActionClip(WeaponMechanismMotion.Cycle cycle, float time, bool coupleContact)
        {
            if (cycle == null || cycle.Tracks == null) return;
            foreach (WeaponMechanismRig.Part part in _rig.Parts)
            {
                if (!_rig.IsActionPart(part)) continue;
                WeaponMechanismMotion.Track track = Find(cycle, part.Name);
                if (track == null) continue;
                ChamberActionBinding.TrySetBodyPose(part.Target, _rig.Body, track.Evaluate(time), track.AnimateRotation && part.Rotate);
            }
            if (coupleContact) CoupleContact();
        }

        /// <summary>Empty-клип HoldOpen: контактная деталь идёт за реальной задней позой механизма вдоль оси хода.</summary>
        private void CoupleContact()
        {
            Transform contact = _rig.ContactPart;
            WeaponMechanismRig.Part contactPart = FindPart(contact);
            if (contact == null || contactPart == null) return;
            Transform parent = _rig.Handle.transform.parent;
            Vector3 direction = _rig.TravelDirection;
            float rear = 0f;
            foreach (WeaponMechanismRig.Part part in _rig.Parts)
            {
                if (!_rig.IsActionPart(part)) continue;
                Vector3 worldOffset = part.Target.parent.TransformVector(part.Target.localPosition - part.RestPosition);
                Vector3 localOffset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
                rear = Mathf.Max(rear, Vector3.Dot(localOffset, direction));
            }
            Vector3 offset = direction * Mathf.Clamp(rear, 0f, _rig.TravelLength);
            Vector3 worldToRear = parent != null ? parent.TransformVector(offset) : offset;
            contact.position = contact.parent.TransformPoint(contactPart.RestPosition) + worldToRear;
        }

        // ── Нерычажные детали ───────────────────────────────────────────────────────────────

        private void ApplyAuxiliary(in PoseTarget target)
        {
            WeaponMechanismMotion motion = _rig.Motion;
            WeaponMechanismMotion.Cycle cycle = motion == null ? null :
                target.Auxiliary == AuxiliaryPose.FireClip ? motion.Fire :
                target.Auxiliary == AuxiliaryPose.EmptyClip ? (motion.Empty ?? motion.Fire) : null;
            if (cycle != null && cycle.Tracks != null)
            {
                foreach (WeaponMechanismRig.Part part in _rig.Parts)
                {
                    if (part == null || _rig.IsActionPart(part)) continue;
                    Transform partTarget = _rig.TargetOf(part);
                    WeaponMechanismMotion.Track track = partTarget != null ? Find(cycle, part.Name) : null;
                    if (track == null) continue;
                    Pose pose = track.Evaluate(Mathf.Min(target.ClipTime, cycle.Duration));
                    partTarget.position = _rig.Body.TransformPoint(pose.position);
                    if (track.AnimateRotation && part.Rotate) partTarget.rotation = _rig.Body.rotation * pose.rotation;
                }
                _auxDirty = true;
                return;
            }
            if (!_auxDirty) return;
            _auxDirty = false;
            foreach (WeaponMechanismRig.Part part in _rig.Parts)
            {
                if (part == null || _rig.IsActionPart(part)) continue;
                Transform partTarget = _rig.TargetOf(part);
                if (partTarget == null) continue;
                partTarget.localPosition = part.RestPosition;
                if (part.Rotate) partTarget.localRotation = part.RestRotation;
            }
        }

        // ── Перехват ручки ──────────────────────────────────────────────────────────────────

        private void OnHandleGrabbing(object sender, UxrManipulationEventArgs args)
        {
            if (!_rig.IsPrepared || !_rig.HasAction || !_rig.IsCurrent()) return;
            Transform contact = _rig.ContactPart, handle = _rig.Handle.transform;
            WeaponMechanismRig.Part contactPart = FindPart(contact);
            if (contact == null || contactPart == null || contact == handle) return;
            Vector3 residual = handle.InverseTransformVector(contact.parent.TransformVector(contact.localPosition - contactPart.RestPosition));
            Vector3 offset = _rig.TravelDirection * Vector3.Dot(residual, _rig.TravelDirection);
            if (offset.sqrMagnitude < 1e-10f) return;
            ChamberActionBinding[] bindings = _rig.RuntimeBindings;
            var poses = new Pose[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
                ChamberActionBinding.TryGetBodyPose(bindings[index].Target, _rig.Body, out poses[index]);
            handle.localPosition += offset;
            // Детали сохраняют позу относительно корпуса: ручка «подхватывает» затвор там, где он есть.
            foreach (int index in _rig.ParentFirstOrder)
                if (bindings[index].Target != handle)
                    ChamberActionBinding.TrySetBodyPose(bindings[index].Target, _rig.Body, poses[index], bindings[index].AnimateRotation);
            UxrGrabManager.CaptureGrabbingObjectPose(args);
        }

        private WeaponMechanismRig.Part FindPart(Transform target)
        {
            if (target == null) return null;
            foreach (WeaponMechanismRig.Part part in _rig.Parts) if (part != null && part.Target == target) return part;
            return null;
        }

        private static WeaponMechanismMotion.Track Find(WeaponMechanismMotion.Cycle cycle, string part)
        {
            foreach (WeaponMechanismMotion.Track track in cycle.Tracks)
                if (track != null && track.Part == part) return track;
            return null;
        }
    }
}

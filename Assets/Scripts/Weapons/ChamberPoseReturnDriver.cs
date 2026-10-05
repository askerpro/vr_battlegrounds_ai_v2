using System;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Возврат captured Action-позы к исходному упору; боезапасом и эпизодом спуска владеют другие системы.</summary>
    internal sealed class ChamberPoseReturnDriver
    {
        private Transform _body;
        private UxrGrabbableObject _slide;
        private Vector3 _restSlide;
        private ChamberActionBinding[] _bindings;
        private Pose[] _captured;
        private Vector3 _capturedSlide;
        private float _distance;
        private float _remaining;
        private bool _active;
        private bool _recapture;
        private bool _completed;
        private WeaponMechanismVisuals _visuals;

        public bool Active => _active;
        public bool Completed => _completed;

        public bool Begin(Transform body, UxrGrabbableObject slide, Vector3 restSlide,
            ChamberActionBinding[] bindings, float epsilon, WeaponMechanismVisuals visuals)
        {
            Cancel();
            if (body == null || slide == null || bindings == null || visuals == null ||
                !AutomaticWeaponSlideFeedback.TryGetSlideTravel(slide, out _, out float length)) return false;
            _body = body; _slide = slide; _restSlide = restSlide;
            _visuals = visuals;
            _bindings = (ChamberActionBinding[])bindings.Clone();
            // Parent перед child: установка мировой позы дочерней детали не должна
            // быть сдвинута последующим обновлением собственного parent binding.
            Array.Sort(_bindings, (left, right) => Depth(left.Target).CompareTo(Depth(right.Target)));
            _captured = new Pose[_bindings.Length];
            Capture(length, epsilon);
            _active = _distance > epsilon;
            return _active;
        }

        private static int Depth(Transform target)
        {
            int result = 0;
            for (Transform parent = target; parent != null; parent = parent.parent) result++;
            return result;
        }

        private void Capture(float length, float epsilon)
        {
            _capturedSlide = _slide.transform.localPosition;
            _distance = (_capturedSlide - _restSlide).magnitude;
            for (int index = 0; index < _bindings.Length; index++)
            {
                ChamberActionBinding binding = _bindings[index];
                if (!ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out _captured[index]))
                { _distance = 0f; _remaining = 0f; return; }
                Vector3 worldDelta = _body.TransformVector(_captured[index].position - binding.RestPosition);
                Transform parent = _slide.transform.parent;
                _distance = Mathf.Max(_distance, (parent != null ? parent.InverseTransformVector(worldDelta) : worldDelta).magnitude);
                if (binding.AnimateRotation)
                {
                    float rearAngle = ChamberActionBinding.RotationDistance(binding.RestRotation, binding.RearRotation);
                    if (!ChamberActionBinding.RotationNear(binding.RestRotation, binding.RearRotation))
                        _distance = Mathf.Max(_distance, length * ChamberActionBinding.RotationDistance(binding.RestRotation, _captured[index].rotation) / rearAngle);
                }
            }
            if (float.IsNaN(_distance) || float.IsInfinity(_distance) || _distance <= epsilon) _distance = 0f;
            _remaining = 1f; _recapture = false;
        }

        public bool Advance(float deltaTime, float localSpeed, bool held, float epsilon)
        {
            if (!_active) return false;
            if (_body == null || _slide == null || _visuals == null || _bindings == null ||
                float.IsNaN(epsilon) || float.IsInfinity(epsilon) || epsilon <= 0f)
            { Cancel(); return false; }
            foreach (ChamberActionBinding binding in _bindings)
                if (binding == null || binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body))
                { Cancel(); return false; }
            if (held) { _recapture = true; return false; }
            if (_recapture && AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out _, out float length))
                Capture(length, epsilon);
            if (_distance == 0f)
            {
                bool rest = true;
                foreach (ChamberActionBinding binding in _bindings) rest &= binding.IsAtRest(_body, epsilon);
                Cancel(); _completed = rest;
                return rest;
            }
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                localSpeed <= 0f || float.IsNaN(localSpeed) || float.IsInfinity(localSpeed)) return false;
            _remaining = Mathf.Max(0f, _remaining - localSpeed * deltaTime / _distance);
            _slide.transform.localPosition = Vector3.LerpUnclamped(_restSlide, _capturedSlide, _remaining);
            for (int index = 0; index < _bindings.Length; index++)
            {
                ChamberActionBinding binding = _bindings[index];
                Vector3 position = Vector3.LerpUnclamped(binding.RestPosition, _captured[index].position, _remaining);
                Quaternion rotation = Quaternion.Slerp(binding.RestRotation, _captured[index].rotation, _remaining);
                if (_remaining == 0f && _visuals.TryGetSourceLocalRestPose(binding.Target, out Pose localRest))
                {
                    binding.Target.localPosition = localRest.position;
                    if (binding.AnimateRotation) binding.Target.localRotation = localRest.rotation;
                }
                else if (!ChamberActionBinding.TrySetBodyPose(binding.Target, _body, new Pose(position, rotation), binding.AnimateRotation))
                { Cancel(); return false; }
            }
            if (_remaining > 0f) return false;
            Cancel();
            _completed = true;
            return true;
        }

        public void Cancel()
        {
            _active = false; _recapture = false; _completed = false;
            // Ни pose teleport, ни bool readiness: owner отдельно проверяет actual rest.
        }
    }
}

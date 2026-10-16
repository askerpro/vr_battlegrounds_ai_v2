using System;
using System.Collections.Generic;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>
    /// Применение q только к preview-копии. Создаётся после переноса исходного кадра: текущая поза становится q0.
    /// Local positions/scales костей и ориентация запястья фиксированы; v2 может перемещать только явно заданную внешнюю рамку.
    /// </summary>
    public sealed class ContactIkRigDriver : IDisposable
    {
        private sealed class BoneState
        {
            public Transform Bone;
            public Quaternion Rotation, ExpectedRotation;
            public Vector3 Position, Scale;
        }

        private readonly GripTargetRig _rig;
        private readonly UxrHandSide _side;
        private readonly Transform _wrist;
        private readonly UnityEngine.SceneManagement.Scene _previewScene;
        private readonly Vector3 _wristPosition, _wristScale;
        private readonly Quaternion _wristRotation;
        private readonly List<BoneState> _prior = new List<BoneState>();
        private readonly ContactIkDof[] _dofs;
        private readonly Transform _placementRoot;
        private readonly Vector3 _placementPosition, _placementLocalPosition, _placementScale;
        private readonly Quaternion _placementRotation;
        private readonly float _maximumTranslation;
        private Vector3 _translationWorld;
        private bool _applied, _disposed;

        public ContactIkRigDriver(GripTargetRig rig, UxrHandSide side, IReadOnlyList<ContactIkDof> dofs,
            Transform palmPlacementRoot = null, float? maxPalmTranslationMeters = null)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            if (dofs == null) throw new ArgumentNullException(nameof(dofs));
            _side = side;
            _wrist = rig.Hand(side).Wrist;
            _previewScene = _wrist ? _wrist.gameObject.scene : default;
            CheckTemporary();
            if (palmPlacementRoot != null || maxPalmTranslationMeters.HasValue)
            {
                if (!palmPlacementRoot || !maxPalmTranslationMeters.HasValue || !Finite(maxPalmTranslationMeters.Value) || maxPalmTranslationMeters.Value < 0 ||
                    palmPlacementRoot == _wrist || !_wrist.IsChildOf(palmPlacementRoot) || EditorUtility.IsPersistent(palmPlacementRoot) ||
                    palmPlacementRoot.gameObject.scene != _wrist.gameObject.scene || new HashSet<Transform>(rig.Avatar.AvatarRig.Transforms).Contains(palmPlacementRoot))
                    throw new ArgumentException("Нужен явный preview placement root выше запястья и конечный max translation >= 0.");
                _placementRoot = palmPlacementRoot; _maximumTranslation = maxPalmTranslationMeters.Value;
                _placementPosition = palmPlacementRoot.position; _placementLocalPosition = palmPlacementRoot.localPosition;
                _placementRotation = palmPlacementRoot.rotation; _placementScale = palmPlacementRoot.lossyScale;
            }
            _wristPosition = _wrist.position;
            _wristRotation = _wrist.rotation;
            _wristScale = _wrist.lossyScale;
            foreach (Transform bone in _wrist.GetComponentsInChildren<Transform>(true))
                _prior.Add(new BoneState { Bone = bone, Rotation = bone.localRotation, ExpectedRotation = bone.localRotation, Position = bone.localPosition, Scale = bone.localScale });
            UxrAvatarHand hand = rig.Hand(side);
            var allowed = new HashSet<Transform>();
            foreach (UxrAvatarFinger finger in new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little })
                foreach (Transform bone in new[] { finger.Proximal, finger.Intermediate, finger.Distal })
                    if (bone != null) allowed.Add(bone);
            _dofs = new ContactIkDof[dofs.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dofs.Count; i++)
            {
                ContactIkDof d = dofs[i];
                if (d == null || string.IsNullOrEmpty(d.Id) || !ids.Add(d.Id) || !d.Bone || !allowed.Contains(d.Bone) ||
                    !Finite(d.LocalAxis) || d.LocalAxis.sqrMagnitude < 1e-10f || !Finite(d.MinDegrees) || !Finite(d.MaxDegrees) ||
                    d.MinDegrees > d.MaxDegrees || !d.Bone.IsChildOf(_wrist))
                    throw new ArgumentException("DOF должен принадлежать фаланге preview-копии и иметь явные ось/упорядоченные границы.");
                _dofs[i] = new ContactIkDof
                {
                    Id = d.Id, Bone = d.Bone, LocalAxis = d.LocalAxis.normalized,
                    MinDegrees = d.MinDegrees, MaxDegrees = d.MaxDegrees,
                    PriorScaleDegrees = d.PriorScaleDegrees, PriorWeight = d.PriorWeight
                };
            }
        }

        /// <summary>Абсолютный q от q0; несколько осей одной кости применяются в порядке входного профиля.</summary>
        public void Apply(IReadOnlyList<float> degrees)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContactIkRigDriver));
            if (degrees == null || degrees.Count != _dofs.Length) throw new ArgumentException("Размер q не совпадает с профилем DOF.");
            CheckTemporary();
            CheckFixedGeometry();
            for (int i = 0; i < _dofs.Length; i++)
                if (!Finite(degrees[i]) || degrees[i] < _dofs[i].MinDegrees || degrees[i] > _dofs[i].MaxDegrees)
                    throw new ArgumentException("Угол вне явных ограничений: " + _dofs[i].Id);
            RestorePlacement();
            RestoreRotations();
            for (int i = 0; i < _dofs.Length; i++)
                _dofs[i].Bone.localRotation *= Quaternion.AngleAxis(degrees[i], _dofs[i].LocalAxis);
            foreach (BoneState state in _prior) state.ExpectedRotation = state.Bone.localRotation;
            _applied = true;
        }

        /// <summary>Одна аналитическая коррекция рамки после q. Bone local positions/scales и ориентация не записываются.</summary>
        public Vector3 ApplyPalmTranslation(Vector3 requestedObjectMeters, Matrix4x4 objectToWorld)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContactIkRigDriver));
            if (!_applied || !_placementRoot) throw new InvalidOperationException("Palm placement требует явный root и предварительный Apply(q).");
            CheckTemporary(); CheckFixedGeometry();
            ContactIkSurfaceAnchor.ValidateUniformFrame(objectToWorld, true);
            if (!Finite(requestedObjectMeters)) throw new ArgumentException("Нечисловая palm correction.");
            Vector3 bounded = Vector3.ClampMagnitude(requestedObjectMeters, _maximumTranslation);
            _translationWorld = objectToWorld.MultiplyVector(bounded);
            _placementRoot.position = _placementPosition + _translationWorld;
            return bounded;
        }

        /// <summary>Дескриптор уже применённого q. Запись ассета остаётся у существующего конвейера.</summary>
        public UxrHandDescriptor CaptureDescriptor()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContactIkRigDriver));
            if (!_applied) throw new InvalidOperationException("Перед CaptureDescriptor необходимо явно Apply итогового q.");
            CheckTemporary();
            CheckFixedGeometry();
            return new UxrHandDescriptor(_rig.Avatar, _side);
        }

        public void Dispose()
        {
            if (_disposed) return;
            CheckTemporary(true);
            RestoreRotations();
            RestorePlacement();
            _disposed = true;
        }

        /// <summary>Отмена кооперативного поиска возвращает исходную preview-позу до следующего внешнего placement.</summary>
        public void RestorePrior()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContactIkRigDriver));
            CheckTemporary(true); CheckFixedGeometry();
            RestoreRotations(); RestorePlacement(); _applied = false;
        }

        private void RestoreRotations()
        {
            foreach (BoneState state in _prior)
            {
                if (!state.Bone) throw new InvalidOperationException("Кость preview-копии удалена во время поиска.");
                state.Bone.localRotation = state.Rotation;
                state.ExpectedRotation = state.Rotation;
            }
        }

        private void CheckTemporary(bool cleanup = false)
        {
            // Отмена на ExitingEditMode должна восстановить только ту же private preview-копию, сохранив запрет нового solve.
            if (!_wrist || EditorUtility.IsPersistent(_wrist) || _wrist.gameObject.scene != _previewScene ||
                !EditorSceneManager.IsPreviewScene(_wrist.gameObject.scene) || !_rig.Avatar || EditorUtility.IsPersistent(_rig.Avatar) ||
                _rig.Avatar.gameObject.scene != _previewScene || _rig.Hand(_side).Wrist != _wrist)
                throw new InvalidOperationException("Изменена идентичность private preview-копии Contact IK.");
            if (!ReferenceEquals(_placementRoot, null) && (!_placementRoot || EditorUtility.IsPersistent(_placementRoot) ||
                _placementRoot.gameObject.scene != _previewScene || !_wrist.IsChildOf(_placementRoot)))
                throw new InvalidOperationException("Placement root больше не принадлежит исходной preview-копии.");
            foreach (BoneState state in _prior)
                if (!state.Bone || EditorUtility.IsPersistent(state.Bone) || state.Bone.gameObject.scene != _previewScene ||
                    (state.Bone != _wrist && !state.Bone.IsChildOf(_wrist)))
                    throw new InvalidOperationException("Кость больше не принадлежит исходной preview-кисти.");
            if (!cleanup && EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Contact IK разрешён только на непостоянной копии в editor preview scene вне Play.");
        }

        private void CheckFixedGeometry()
        {
            if (_placementRoot && ((_placementRoot.position - _placementPosition - _translationWorld).sqrMagnitude > 1e-12f ||
                Quaternion.Angle(_placementRoot.rotation, _placementRotation) > .001f || (_placementRoot.lossyScale - _placementScale).sqrMagnitude > 1e-12f))
                throw new InvalidOperationException("Посторонний writer изменил placement frame.");
            if ((_wrist.position - _wristPosition - _translationWorld).sqrMagnitude > 1e-10f || Quaternion.Angle(_wrist.rotation, _wristRotation) > .001f ||
                (_wrist.lossyScale - _wristScale).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Нарушена фиксированная рамка запястья.");
            foreach (BoneState state in _prior)
                if (!state.Bone || (state.Bone.localPosition - state.Position).sqrMagnitude > 1e-12f ||
                    (state.Bone.localScale - state.Scale).sqrMagnitude > 1e-12f)
                    throw new InvalidOperationException("Во время IK изменились позиции/масштабы костей.");
                else if (Quaternion.Angle(state.Bone.localRotation, state.ExpectedRotation) > .001f)
                    throw new InvalidOperationException("Посторонний writer изменил поворот кости во время IK.");
        }

        private void RestorePlacement()
        {
            if (_placementRoot) _placementRoot.localPosition = _placementLocalPosition;
            _translationWorld = Vector3.zero;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}

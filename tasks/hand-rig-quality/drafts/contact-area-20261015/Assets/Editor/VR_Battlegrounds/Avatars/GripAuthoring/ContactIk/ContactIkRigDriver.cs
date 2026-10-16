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
    /// Запястье и пястные фиксированы; позиции и масштабы костей не записываются.
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
        private readonly Vector3 _wristPosition, _wristScale;
        private readonly Quaternion _wristRotation;
        private readonly List<BoneState> _prior = new List<BoneState>();
        private readonly ContactIkDof[] _dofs;
        private bool _applied, _disposed;

        public ContactIkRigDriver(GripTargetRig rig, UxrHandSide side, IReadOnlyList<ContactIkDof> dofs)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            if (dofs == null) throw new ArgumentNullException(nameof(dofs));
            _side = side;
            _wrist = rig.Hand(side).Wrist;
            CheckTemporary();
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
            RestoreRotations();
            for (int i = 0; i < _dofs.Length; i++)
                _dofs[i].Bone.localRotation *= Quaternion.AngleAxis(degrees[i], _dofs[i].LocalAxis);
            foreach (BoneState state in _prior) state.ExpectedRotation = state.Bone.localRotation;
            _applied = true;
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
            CheckTemporary();
            RestoreRotations();
            _disposed = true;
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

        private void CheckTemporary()
        {
            if (!_wrist || EditorUtility.IsPersistent(_wrist) || !EditorSceneManager.IsPreviewScene(_wrist.gameObject.scene) ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Contact IK разрешён только на непостоянной копии в editor preview scene вне Play.");
        }

        private void CheckFixedGeometry()
        {
            if ((_wrist.position - _wristPosition).sqrMagnitude > 1e-12f || Quaternion.Angle(_wrist.rotation, _wristRotation) > .001f ||
                (_wrist.lossyScale - _wristScale).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Нарушена фиксированная рамка запястья.");
            foreach (BoneState state in _prior)
                if (!state.Bone || (state.Bone.localPosition - state.Position).sqrMagnitude > 1e-12f ||
                    (state.Bone.localScale - state.Scale).sqrMagnitude > 1e-12f)
                    throw new InvalidOperationException("Во время IK изменились позиции/масштабы костей.");
                else if (Quaternion.Angle(state.Bone.localRotation, state.ExpectedRotation) > .001f)
                    throw new InvalidOperationException("Посторонний writer изменил поворот кости во время IK.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}

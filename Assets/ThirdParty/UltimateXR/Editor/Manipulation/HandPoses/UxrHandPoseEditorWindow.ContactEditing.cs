using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace UltimateXR.Editor.Manipulation.HandPoses
{
    // VR Battlegrounds patch SDK83: типизированная граница контактного редактирования; SDK — единственный live writer.
    public enum UxrHandPoseContactEditMode { Manual, Targets, ForwardWrap }

    public sealed class UxrHandPoseContactEditContext
    {
        sealed class BoneState
        {
            public Transform Bone;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }
        readonly UxrHandDescriptor _working;
        readonly BoneState[] _bones;
        readonly Matrix4x4 _avatarFrame, _wristFrame;
        readonly int _poseDirtyCount;
        public UxrHandPoseEditorWindow Window { get; }
        public UxrAvatar Avatar { get; }
        public UxrHandPoseAsset Pose { get; }
        public UxrHandSide Side { get; }
        public UxrHandPoseType PoseType { get; }
        public UxrBlendPoseType BlendType { get; }
        public float Blend { get; }
        public bool AutoSave { get; }
        public bool ReadOnly { get; }
        public bool GestureActive { get; }
        public UxrHandDescriptor WorkingDescriptor => Copy(_working);

        internal UxrHandPoseContactEditContext(UxrHandPoseEditorWindow window, UxrAvatar avatar, UxrHandPoseAsset pose,
            UxrHandSide side, UxrHandPoseType poseType, UxrBlendPoseType blendType, float blend,
            bool autoSave, bool readOnly, bool gestureActive)
        {
            Window = window; Avatar = avatar; Pose = pose; Side = side; PoseType = poseType;
            BlendType = blendType; Blend = blend; AutoSave = autoSave; ReadOnly = readOnly; GestureActive = gestureActive;
            _working = new UxrHandDescriptor(avatar, side); _poseDirtyCount = EditorUtility.GetDirtyCount(pose);
            var wrist = avatar.GetHand(side).Wrist;
            _avatarFrame = avatar.transform.localToWorldMatrix; _wristFrame = wrist.localToWorldMatrix;
            _bones = wrist.GetComponentsInChildren<Transform>(true).Select(t => new BoneState
            { Bone = t, Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale }).ToArray();
        }

        internal bool MatchesWorkingState()
        {
            if (!Window || !Avatar || !Pose || EditorUtility.GetDirtyCount(Pose) != _poseDirtyCount ||
                !MatrixEqual(Avatar.transform.localToWorldMatrix, _avatarFrame) ||
                !MatrixEqual(Avatar.GetHand(Side).Wrist.localToWorldMatrix, _wristFrame)) return false;
            foreach (var state in _bones)
                if (!state.Bone || !state.Bone.localPosition.Equals(state.Position) || !state.Bone.localRotation.Equals(state.Rotation) || !state.Bone.localScale.Equals(state.Scale)) return false;
            return _working.Equals(new UxrHandDescriptor(Avatar, Side));
        }
        internal bool ValidAppliedFingers()
        {
            if (!MatrixEqual(Avatar.transform.localToWorldMatrix, _avatarFrame) || !MatrixEqual(Avatar.GetHand(Side).Wrist.localToWorldMatrix, _wristFrame)) return false;
            foreach (var state in _bones)
            {
                if (!state.Bone || !state.Bone.localPosition.Equals(state.Position) || !state.Bone.localScale.Equals(state.Scale)) return false;
                var q = state.Bone.localRotation;
                double norm = (double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w;
                if (double.IsNaN(norm) || double.IsInfinity(norm) || Math.Abs(norm - 1) > .01) return false;
            }
            return true;
        }
        static bool MatrixEqual(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; i++) if (a[i] != b[i]) return false; return true; }
        internal static UxrHandDescriptor Copy(UxrHandDescriptor descriptor)
        { var copy = new UxrHandDescriptor(); copy.CopyFrom(descriptor); return copy; }
    }

    public partial class UxrHandPoseEditorWindow
    {
        [SerializeField] UxrHandPoseContactEditMode _contactEditMode;
        public static event Action<UxrHandPoseEditorWindow, SceneView> ContactEditingSceneGUI;
        public UxrHandPoseContactEditMode ContactEditMode
        {
            get => _contactEditMode;
            set
            {
                if (!Enum.IsDefined(typeof(UxrHandPoseContactEditMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_selectedFingerSpinner != null && value != UxrHandPoseContactEditMode.Manual)
                    throw new InvalidOperationException("Сначала завершите ручное сгибание пальца.");
                _contactEditMode = value;
            }
        }

        public bool TryCaptureContactEditContext(UxrHandSide side, out UxrHandPoseContactEditContext context, out string reason)
        {
            context = null; reason = null;
            if (side != UxrHandSide.Left && side != UxrHandSide.Right) { reason = "Неизвестная сторона кисти."; return false; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { reason = "Редактор занят Play/compile/import."; return false; }
            if (!_avatar || !_currentHandPose || !_avatar.GetHand(side).Wrist || _updateHandTransforms)
            { reason = "Нет синхронизированного SDK-контекста кисти."; return false; }
            // VR Battlegrounds patch 91: та же проверка, что у окна; галочка открывает данные унаследованной позы.
            bool readOnly = IsPoseDataReadOnly(_currentHandPose);
            context = new UxrHandPoseContactEditContext(this, _avatar, _currentHandPose, side, CurrentPoseType,
                CurrentBlendPoseType, _blendValue, _autoSave, readOnly, _selectedFingerSpinner != null);
            return true;
        }

        public bool TryCommitContactEdit(UxrHandPoseContactEditContext expected, UxrHandDescriptor descriptor, out string reason)
            => TryCommitContactEdit(expected, descriptor, null, 0, Vector3.zero, out reason);

        /// <summary>Одна явная SDK Undo-команда: пальцы и отдельное положение существующего snap, без записи prefab.</summary>
        public bool TryCommitContactEdit(UxrHandPoseContactEditContext expected, UxrHandDescriptor descriptor,
            UxrGrabbableObject grabbable, int grabPoint, Vector3 worldPalmOffset, out string reason)
        {
            reason = null;
            if (expected == null || descriptor == null || expected.Window != this)
            { reason = "Нет исходного контекста/descriptor этого SDK окна."; return false; }
            if (!TryCaptureContactEditContext(expected.Side, out var current, out reason)) return false;
            if (current.Avatar != expected.Avatar || current.Pose != expected.Pose || current.PoseType != expected.PoseType ||
                current.BlendType != expected.BlendType || current.Blend != expected.Blend || current.AutoSave != expected.AutoSave ||
                current.ReadOnly || current.GestureActive || !expected.MatchesWorkingState())
            { reason = "Контекст устарел, поза read-only или ручной gesture ещё активен."; return false; }
            if (current.PoseType != UxrHandPoseType.Fixed &&
                (current.PoseType != UxrHandPoseType.Blend || (!Mathf.Approximately(current.Blend, 0) && !Mathf.Approximately(current.Blend, 1))))
            { reason = "Правка разрешена только для Fixed или endpoint Blend-позы."; return false; }
            if (!FiniteContactOffset(worldPalmOffset) || worldPalmOffset.magnitude > .030001f)
            { reason = "Palm offset должен быть конечным и не превышать 30 мм."; return false; }
            Transform alignment = null;
            if (worldPalmOffset.sqrMagnitude > 0)
            {
                if (!grabbable || EditorUtility.IsPersistent(grabbable) || !grabbable.gameObject.scene.IsValid() || !grabbable.gameObject.scene.isLoaded ||
                    grabbable.gameObject.scene != current.Avatar.gameObject.scene ||
                    grabbable.transform.IsChildOf(current.Avatar.transform) || current.Avatar.transform.IsChildOf(grabbable.transform) ||
                    grabPoint < 0 || grabPoint >= grabbable.GrabPointCount)
                { reason = "Palm placement требует существующий сценовый grabbable вне аватара и его GrabPoint."; return false; }
                var point = grabbable.GetGrabPoint(grabPoint);
                if (point.SnapMode != UxrSnapToHandMode.PositionAndRotation || (!point.BothHandsCompatible && point.HandSide != expected.Side))
                { reason = "GrabPoint несовместим со стороной или не использует PositionAndRotation."; return false; }
                alignment = grabbable.Editor_GetGrabPointGrabAlignTransform(current.Avatar, grabPoint, expected.Side);
                if (!alignment || EditorUtility.IsPersistent(alignment) || alignment.gameObject.scene != grabbable.gameObject.scene ||
                    alignment == grabbable.transform || !alignment.IsChildOf(grabbable.transform) ||
                    alignment.IsChildOf(current.Avatar.transform) || current.Avatar.transform.IsChildOf(alignment))
                { reason = "Для palm placement нужен отдельный существующий snap-child предмета; перемещение самого предмета не заменяет размещение хвата."; return false; }
                if (!FiniteContactOffset(alignment.position + worldPalmOffset))
                { reason = "Новое положение snap нечисловое."; return false; }
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Контактное редактирование кисти");
            try
            {
                RegisterHandsUndo("Контактное редактирование кисти");
                Undo.RecordObject(_currentHandPose, "Контактное редактирование кисти");
                if (alignment)
                {
                    Undo.RecordObject(alignment, "Контактная постановка ладони");
                    alignment.position += worldPalmOffset;
                    if (PrefabUtility.IsPartOfPrefabInstance(alignment)) PrefabUtility.RecordPrefabInstancePropertyModifications(alignment);
                }
                UxrAvatarRig.UpdateHandUsingDescriptor(_avatar, expected.Side, UxrHandPoseContactEditContext.Copy(descriptor));
                if (!expected.ValidAppliedFingers()) throw new InvalidOperationException("Descriptor изменяет placement/scale либо даёт nonfinite rotations.");
                if (_autoSave) SavePose(_currentHandPose, expected.Side, CurrentPoseType, CurrentBlendPoseType, SaveHandPoseFlags.HandDescriptors);
                _updateHandTransforms = false; BuildFingerSpinners();
                UxrGrabbableObjectEditor.RefreshGrabPoseMeshes(_avatar, _currentHandPose);
                UxrGrabbableObjectSnapTransformEditor.RefreshGrabPoseMeshes(_avatar, _currentHandPose);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Repaint(); SceneView.RepaintAll();
                DiagnosticsContextUpdated?.Invoke(this, _avatar, _currentHandPose, _blendValue);
                return true;
            }
            catch (Exception error)
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group); reason = "Правка отменена: " + error.Message; return false;
            }
        }
        static bool FiniteContactOffset(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}

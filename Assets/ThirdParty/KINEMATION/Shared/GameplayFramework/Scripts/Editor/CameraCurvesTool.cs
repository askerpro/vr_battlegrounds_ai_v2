// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using System.IO;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Editor.CurveImporter;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.Shared.GameplayFramework.Scripts.Editor
{
    public class CameraCurvesTool : IEditorTool
    {
        private AnimationClip _cameraClip;
        private AnimationClip _refCameraClip;
        private AnimationClip _characterClip;
        private string _sourceCameraBone = "root";
        private string _targetCameraCurve = "camera_bone";

        private string _statusMessage = string.Empty;
        private MessageType _statusType = MessageType.None;

        private static string TransformTypeName = typeof(Transform).AssemblyQualifiedName;

        private struct RotationCurves
        {
            public AnimationCurve x;
            public AnimationCurve y;
            public AnimationCurve z;
            public AnimationCurve w;

            public bool IsValid => x != null && y != null && z != null && w != null;
        }

        public void Init()
        {
        }

        public void Render()
        {
            if (!EditorGUIUtility.wideMode) EditorGUIUtility.wideMode = true;
            
            _cameraClip = DrawAnimationField("Camera Clip", _cameraClip);
            _refCameraClip = DrawAnimationField("Ref Camera Clip", _refCameraClip);
            _characterClip = DrawAnimationField("Character Clip", _characterClip);

            _sourceCameraBone = EditorGUILayout.TextField("Camera Bone", _sourceCameraBone);
            _targetCameraCurve = EditorGUILayout.TextField("Character Curve", _targetCameraCurve);

            if (!string.IsNullOrWhiteSpace(_targetCameraCurve))
            {
                EditorGUILayout.HelpBox(
                    $"Creates curves: {_targetCameraCurve}.x, {_targetCameraCurve}.y, {_targetCameraCurve}.z, {_targetCameraCurve}.w",
                    MessageType.Info);
            }

            bool isValid = _cameraClip != null && _characterClip != null &&
                           !string.IsNullOrWhiteSpace(_sourceCameraBone) &&
                           !string.IsNullOrWhiteSpace(_targetCameraCurve);

            if (!isValid)
            {
                EditorGUILayout.HelpBox("Assign camera and character animations plus curve settings.", MessageType.Warning);
            }

            GUI.enabled = isValid;
            if (GUILayout.Button("Apply"))
            {
                BakeCameraCurves();
            }

            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        public string GetToolCategory() => "Gameplay/General";
        public string GetToolName() => "Camera Curves";
        public string GetDocsURL() => string.Empty;

        public string GetToolDescription()
        {
            return "Extracts camera-bone quaternion curves, optionally offsets them by a reference camera pose, and stores the result as gameplay custom curves.";
        }

        private static AnimationClip DrawAnimationField(string label, AnimationClip clip)
        {
            Rect fieldRect = EditorGUILayout.GetControlRect();
            AnimationClip selectedClip = EditorGUI.ObjectField(fieldRect, label, clip, typeof(AnimationClip), true) as AnimationClip;

            TryHandleAnimationFieldDrop(fieldRect, ref selectedClip);
            return selectedClip;
        }

        private static bool TryHandleAnimationFieldDrop(Rect fieldRect, ref AnimationClip clip)
        {
            Event currentEvent = Event.current;
            if (!fieldRect.Contains(currentEvent.mousePosition))
            {
                return false;
            }

            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return false;
            }

            if (!TryResolveAnimationClip(DragAndDrop.objectReferences, out AnimationClip droppedClip))
            {
                return false;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                clip = droppedClip;
                GUI.changed = true;
            }

            currentEvent.Use();
            return true;
        }

        private static bool TryResolveAnimationClip(Object[] objects, out AnimationClip clip)
        {
            clip = null;

            if (objects == null)
            {
                return false;
            }

            foreach (Object draggedObject in objects)
            {
                if (TryResolveAnimationClip(draggedObject, out clip))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveAnimationClip(Object animationObject, out AnimationClip clip)
        {
            clip = animationObject as AnimationClip;
            if (clip != null)
            {
                return true;
            }

            if (animationObject == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(animationObject);
            if (string.IsNullOrEmpty(assetPath) ||
                !string.Equals(Path.GetExtension(assetPath), ".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                clip = asset as AnimationClip;
                if (clip != null && (clip.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    return true;
                }
            }

            clip = null;
            return false;
        }

        private void BakeCameraCurves()
        {
            if (!TryExtractRotationCurves(_cameraClip, _sourceCameraBone, out RotationCurves sourceCurves,
                    out string sourceError))
            {
                SetStatus(sourceError, MessageType.Error);
                return;
            }

            Quaternion refCameraRotation = Quaternion.identity;
            if (_refCameraClip != null)
            {
                if (!TryExtractRotationCurves(_refCameraClip, _sourceCameraBone, out RotationCurves referenceCurves,
                        out string referenceError))
                {
                    SetStatus(referenceError, MessageType.Error);
                    return;
                }

                refCameraRotation = EvaluateRotation(referenceCurves, 0f);
            }

            AnimationCurve curveX = new AnimationCurve();
            AnimationCurve curveY = new AnimationCurve();
            AnimationCurve curveZ = new AnimationCurve();
            AnimationCurve curveW = new AnimationCurve();

            float timeStep = _cameraClip.frameRate > 0f ? 1f / _cameraClip.frameRate : 1f / 30f;
            float playback = 0f;
            float length = _characterClip.length;
            
            while (playback <= length + 0.0001f)
            {
                float time = Mathf.Min(playback, length);
                Quaternion cameraRotation = EvaluateRotation(sourceCurves, time);
                Quaternion outputRotation = Quaternion.Inverse(refCameraRotation) * cameraRotation;

                curveX.AddKey(time, outputRotation.x);
                curveY.AddKey(time, outputRotation.y);
                curveZ.AddKey(time, outputRotation.z);
                curveW.AddKey(time, outputRotation.w);

                playback += timeStep;
            }

            List<CustomEditorCurveData> newCurves = new List<CustomEditorCurveData>
            {
                new (_targetCameraCurve, "localRotation.x", TransformTypeName, curveX),
                new (_targetCameraCurve, "localRotation.y", TransformTypeName, curveY),
                new (_targetCameraCurve, "localRotation.z", TransformTypeName, curveZ),
                new (_targetCameraCurve, "localRotation.w", TransformTypeName, curveW),
            };

            SaveCurves(_characterClip, newCurves);
            SetStatus($"Baked camera curves to {_characterClip.name}.", MessageType.Info);
        }

        private void SaveCurves(AnimationClip clip, List<CustomEditorCurveData> newCurves)
        {
            if (KEditorUtility.IsSubAsset(clip))
            {
                CurveProcessorUtility.TrySavingToFBX(clip, newCurves);
                return;
            }

            Undo.RecordObject(clip, "Bake Camera Curves");

            foreach (CustomEditorCurveData curveData in newCurves)
            {
                clip.SetCurve(curveData.relativePath, typeof(Transform), curveData.propertyName, curveData.curve);
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
        }

        private static Quaternion EvaluateRotation(RotationCurves rotationCurves, float time)
        {
            return new Quaternion(rotationCurves.x.Evaluate(time), rotationCurves.y.Evaluate(time),
                rotationCurves.z.Evaluate(time), rotationCurves.w.Evaluate(time));
        }

        private static bool TryExtractRotationCurves(AnimationClip clip, string boneName,
            out RotationCurves rotationCurves, out string errorMessage)
        {
            rotationCurves = default;
            errorMessage = string.Empty;

            if (clip == null)
            {
                errorMessage = "Animation clip reference is missing.";
                return false;
            }

            string targetPath = null;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                bool pathMatches = string.Equals(binding.path, boneName, System.StringComparison.Ordinal) ||
                                   binding.path.EndsWith("/" + boneName, System.StringComparison.Ordinal);
                if (!pathMatches)
                {
                    continue;
                }

                if (targetPath != null && !string.Equals(targetPath, binding.path, System.StringComparison.Ordinal))
                {
                    errorMessage =
                        $"Multiple paths in '{clip.name}' match '{boneName}'. Use the full relative bone path instead.";
                    return false;
                }

                targetPath = binding.path;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                if (binding.propertyName == "m_LocalRotation.x" || binding.propertyName == "localRotation.x")
                {
                    rotationCurves.x = curve;
                    continue;
                }

                if (binding.propertyName == "m_LocalRotation.y" || binding.propertyName == "localRotation.y")
                {
                    rotationCurves.y = curve;
                    continue;
                }

                if (binding.propertyName == "m_LocalRotation.z" || binding.propertyName == "localRotation.z")
                {
                    rotationCurves.z = curve;
                    continue;
                }

                if (binding.propertyName == "m_LocalRotation.w" || binding.propertyName == "localRotation.w")
                {
                    rotationCurves.w = curve;
                }
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                errorMessage = $"Could not find quaternion rotation curves for '{boneName}' in '{clip.name}'.";
                return false;
            }

            if (rotationCurves.IsValid)
            {
                return true;
            }

            errorMessage = $"Incomplete quaternion rotation curves for '{targetPath}' in '{clip.name}'.";
            return false;
        }

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
        }
    }
}
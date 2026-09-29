// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KINEMATION.Shared.KAnimationCore.Editor.Tools
{
    public class AnimationClipSamplerTool : IEditorTool
    {
        private GameObject _characterGameObject;
        private AnimationClip _clip;
        private float _sampleTime;
        private string _statusMessage = string.Empty;

        public void Init()
        {
        }

        public void Render()
        {
            if (!EditorGUIUtility.wideMode) EditorGUIUtility.wideMode = true;

            EditorGUI.BeginChangeCheck();

            _characterGameObject = EditorGUILayout.ObjectField(
                new GUIContent("Character Game Object", "Scene character GameObject to sample the clip on."),
                _characterGameObject, typeof(GameObject), true) as GameObject;

            _clip = EditorGUILayout.ObjectField(
                new GUIContent("Animation Clip", "Animation clip to sample."),
                _clip, typeof(AnimationClip), false) as AnimationClip;

            if (EditorGUI.EndChangeCheck())
            {
                ClampSampleTime();
                _statusMessage = string.Empty;
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Use Selected Character"))
            {
                _characterGameObject = Selection.activeGameObject;
                _statusMessage = string.Empty;
            }

            if (GUILayout.Button("Use Selected Clip"))
            {
                AnimationClip selectedClip = KEditorUtility.GetAnimationClipFromSelection();
                if (selectedClip != null)
                {
                    _clip = selectedClip;
                    ClampSampleTime();
                    _statusMessage = string.Empty;
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            DrawSampleTimeField();

            bool isValid = IsInputValid(out string validationMessage);
            if (!isValid)
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }

            GUI.enabled = isValid;
            if (GUILayout.Button("Sample Animation"))
            {
                SampleAnimation();
            }

            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private void DrawSampleTimeField()
        {
            float clipLength = _clip == null ? 0f : Mathf.Max(0f, _clip.length);

            _sampleTime = EditorGUILayout.FloatField(
                new GUIContent("Sample Time", "Time in seconds to sample the clip at."),
                _sampleTime);

            if (_clip != null)
            {
                _sampleTime = Mathf.Clamp(_sampleTime, 0f, clipLength);

                GUI.enabled = clipLength > Mathf.Epsilon;
                _sampleTime = EditorGUILayout.Slider("Time", _sampleTime, 0f, clipLength);
                GUI.enabled = true;

                EditorGUILayout.LabelField("Clip Length", $"{clipLength:0.###}s");
            }
            else
            {
                _sampleTime = Mathf.Max(0f, _sampleTime);
            }
        }

        private void SampleAnimation()
        {
            _statusMessage = string.Empty;

            if (!IsInputValid(out string validationMessage))
            {
                _statusMessage = validationMessage;
                return;
            }

            ClampSampleTime();

            try
            {
                Undo.RegisterFullObjectHierarchyUndo(_characterGameObject, "Sample Animation Clip");
                _clip.SampleAnimation(_characterGameObject, _sampleTime);

                RecordPrefabInstanceModifications(_characterGameObject);
                MarkSceneDirty(_characterGameObject);
                SceneView.RepaintAll();

                _statusMessage =
                    $"Sampled '{_clip.name}' at {_sampleTime:0.###}s on '{_characterGameObject.name}'.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _statusMessage = "Sampling failed. Check Console for details.";
            }
        }

        private bool IsInputValid(out string validationMessage)
        {
            if (_characterGameObject == null)
            {
                validationMessage = "Assign a scene character GameObject.";
                return false;
            }

            if (EditorUtility.IsPersistent(_characterGameObject))
            {
                validationMessage = "Character Game Object must be a scene object, not a prefab or model asset.";
                return false;
            }

            if (_clip == null)
            {
                validationMessage = "Assign an Animation Clip.";
                return false;
            }

            if ((_clip.hideFlags & HideFlags.HideInHierarchy) != 0)
            {
                validationMessage = "Assign a visible Animation Clip, not a hidden preview clip.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private void ClampSampleTime()
        {
            if (_clip == null)
            {
                _sampleTime = Mathf.Max(0f, _sampleTime);
                return;
            }

            _sampleTime = Mathf.Clamp(_sampleTime, 0f, Mathf.Max(0f, _clip.length));
        }

        private static void MarkSceneDirty(GameObject gameObject)
        {
            Scene scene = gameObject.scene;
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void RecordPrefabInstanceModifications(GameObject root)
        {
            if (root == null || !PrefabUtility.IsPartOfPrefabInstance(root)) return;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
            }

            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        public string GetToolCategory()
        {
            return "Animation";
        }

        public string GetToolName()
        {
            return "Animation Clip Sampler";
        }

        public string GetDocsURL()
        {
            return string.Empty;
        }

        public string GetToolDescription()
        {
            return "Samples an Animation Clip on a scene character GameObject at the selected time.";
        }
    }
}
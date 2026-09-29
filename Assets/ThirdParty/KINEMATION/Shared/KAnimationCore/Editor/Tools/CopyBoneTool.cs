// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.Shared.KAnimationCore.Editor.CurveImporter;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.Shared.KAnimationCore.Editor.Tools
{
    public class CopyBoneTool : IEditorTool
    {
        private enum ClipStatus
        {
            Pending,
            Processed,
            Skipped
        }

        private class ClipQueueItem
        {
            public AnimationClip clip;
            public string assetPath;
            public string key;
            public ClipStatus status;
            public string statusMessage;
        }

        private Transform _root;
        private Transform _extractFrom;
        private Transform _extractTo;

        private readonly List<ClipQueueItem> _clipQueue = new List<ClipQueueItem>();
        private readonly HashSet<string> _queuedClipKeys = new HashSet<string>();
        private bool _showClipQueue;
        private string _queueMessage;

        private AnimationClip _refClip;

        private Vector3 _rotationOffset;
        private bool _isAdditive;

        private GUIStyle _foldoutStyle;

        private struct TransformData
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public TransformData(Transform t)
            {
                localPosition = t.localPosition;
                localRotation = t.localRotation;
                localScale = t.localScale;
            }

            public void Restore(Transform t)
            {
                t.localPosition = localPosition;
                t.localRotation = localRotation;
                t.localScale = localScale;
            }
        }

        private string GetBonePath(Transform targetBone, Transform root)
        {
            if (targetBone == null || root == null) return "";

            string path = targetBone.name;
            Transform current = targetBone.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return current == root ? path : null;
        }

        private List<CustomEditorCurveData> ExtractAnimationData(AnimationClip clip)
        {
            Transform[] allBones = _root.GetComponentsInChildren<Transform>(true);
            TransformData[] cache = new TransformData[allBones.Length];

            for (int i = 0; i < allBones.Length; i++)
            {
                cache[i] = new TransformData(allBones[i]);
            }

            AnimationCurve tX = new AnimationCurve();
            AnimationCurve tY = new AnimationCurve();
            AnimationCurve tZ = new AnimationCurve();

            AnimationCurve rX = new AnimationCurve();
            AnimationCurve rY = new AnimationCurve();
            AnimationCurve rZ = new AnimationCurve();
            AnimationCurve rW = new AnimationCurve();

            try
            {
                Vector3 refTranslation = Vector3.zero;
                Quaternion refRotation = Quaternion.identity;

                if (_isAdditive)
                {
                    _refClip.SampleAnimation(_root.gameObject, 0f);
                    refTranslation = _root.InverseTransformPoint(_extractFrom.position);
                    refRotation = Quaternion.Inverse(_root.rotation) * _extractFrom.rotation *
                                  Quaternion.Euler(_rotationOffset);
                }

                float playLength = clip.length;
                float frameRate = clip.frameRate > 0 ? 1f / clip.frameRate : 1f / 30f;
                float playBack = 0f;

                while (playBack <= playLength)
                {
                    clip.SampleAnimation(_root.gameObject, playBack);

                    Quaternion newBasis = Quaternion.Euler(_rotationOffset);

                    Vector3 position = newBasis * _extractFrom.position;
                    Quaternion rotation = newBasis * _extractFrom.rotation * Quaternion.Inverse(newBasis);

                    if (_isAdditive)
                    {
                        position = _root.InverseTransformPoint(position);
                        rotation = Quaternion.Inverse(_root.rotation) * rotation;

                        position -= refTranslation;
                        rotation *= Quaternion.Inverse(refRotation);

                        position = _root.TransformPoint(position);
                        rotation = _root.rotation * rotation;
                    }

                    position = _extractTo.parent.InverseTransformPoint(position);
                    rotation = Quaternion.Inverse(_extractTo.parent.rotation) * rotation;

                    tX.AddKey(playBack, position.x);
                    tY.AddKey(playBack, position.y);
                    tZ.AddKey(playBack, position.z);

                    rX.AddKey(playBack, rotation.x);
                    rY.AddKey(playBack, rotation.y);
                    rZ.AddKey(playBack, rotation.z);
                    rW.AddKey(playBack, rotation.w);

                    playBack += frameRate;
                }

                string path = GetBonePath(_extractTo, _root);
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException("Copy To must be a child of the character model.");
                }

                string transformType = typeof(Transform).AssemblyQualifiedName;
                return new List<CustomEditorCurveData>
                {
                    new CustomEditorCurveData(path, "localPosition.x", transformType, tX),
                    new CustomEditorCurveData(path, "localPosition.y", transformType, tY),
                    new CustomEditorCurveData(path, "localPosition.z", transformType, tZ),
                    new CustomEditorCurveData(path, "localRotation.x", transformType, rX),
                    new CustomEditorCurveData(path, "localRotation.y", transformType, rY),
                    new CustomEditorCurveData(path, "localRotation.z", transformType, rZ),
                    new CustomEditorCurveData(path, "localRotation.w", transformType, rW)
                };
            }
            finally
            {
                for (int i = 0; i < allBones.Length; i++)
                {
                    if (allBones[i] != null) cache[i].Restore(allBones[i]);
                }
            }
        }

        private static void WriteCurvesToClip(AnimationClip clip, List<CustomEditorCurveData> curves)
        {
            foreach (CustomEditorCurveData curveData in curves)
            {
                clip.SetCurve(curveData.relativePath, typeof(Transform), curveData.propertyName, curveData.curve);
            }

            EditorUtility.SetDirty(clip);
        }

        private void ProcessQueue()
        {
            Dictionary<ModelImporter, List<ClipQueueItem>> importerItems =
                new Dictionary<ModelImporter, List<ClipQueueItem>>();

            foreach (ClipQueueItem item in _clipQueue)
            {
                item.status = ClipStatus.Pending;
                item.statusMessage = string.Empty;

                if (item.clip == null)
                {
                    SetSkipped(item, "Animation clip is no longer available.");
                    continue;
                }

                try
                {
                    List<CustomEditorCurveData> curves = ExtractAnimationData(item.clip);

                    if (IsFbxPath(item.assetPath))
                    {
                        if (!CurveProcessorUtility.TryGetModelImporter(item.clip, out ModelImporter importer))
                        {
                            throw new InvalidOperationException("Could not resolve the clip's ModelImporter.");
                        }

                        CurveProcessorUtility.WriteCurvesToImporter(item.clip, importer, curves);

                        if (!importerItems.TryGetValue(importer, out List<ClipQueueItem> items))
                        {
                            items = new List<ClipQueueItem>();
                            importerItems.Add(importer, items);
                        }

                        items.Add(item);
                        item.statusMessage = "Waiting for FBX reimport.";
                    }
                    else
                    {
                        WriteCurvesToClip(item.clip, curves);
                        item.status = ClipStatus.Processed;
                    }
                }
                catch (Exception exception)
                {
                    SetSkipped(item, exception.Message);
                    Debug.LogException(exception);
                }
            }

            foreach (KeyValuePair<ModelImporter, List<ClipQueueItem>> pair in importerItems)
            {
                try
                {
                    pair.Key.SaveAndReimport();
                    foreach (ClipQueueItem item in pair.Value)
                    {
                        item.status = ClipStatus.Processed;
                        item.statusMessage = string.Empty;
                    }
                }
                catch (Exception exception)
                {
                    foreach (ClipQueueItem item in pair.Value)
                    {
                        SetSkipped(item, $"FBX reimport failed: {exception.Message}");
                    }

                    Debug.LogException(exception);
                }
            }

            int processed = 0;
            int skipped = 0;
            foreach (ClipQueueItem item in _clipQueue)
            {
                if (item.status == ClipStatus.Processed) processed++;
                if (item.status == ClipStatus.Skipped) skipped++;
            }

            _queueMessage = $"Processed {processed} clip(s). Skipped {skipped}.";
        }

        private static void SetSkipped(ClipQueueItem item, string message)
        {
            item.status = ClipStatus.Skipped;
            item.statusMessage = message;
        }

        private void DrawClipQueue()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));
            GUIStyle dropAreaStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Box(dropArea, "Drop .anim clips or FBX assets here", dropAreaStyle);
            HandleDragAndDrop(dropArea);

            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldout);
                Color foldoutTextColor = EditorStyles.label.normal.textColor;
                _foldoutStyle.normal.textColor = foldoutTextColor;
                _foldoutStyle.hover.textColor = foldoutTextColor;
                _foldoutStyle.active.textColor = foldoutTextColor;
                _foldoutStyle.focused.textColor = foldoutTextColor;
                _foldoutStyle.onNormal.textColor = foldoutTextColor;
                _foldoutStyle.onHover.textColor = foldoutTextColor;
                _foldoutStyle.onActive.textColor = foldoutTextColor;
                _foldoutStyle.onFocused.textColor = foldoutTextColor;
            }

            EditorGUILayout.BeginHorizontal();
            _showClipQueue = EditorGUILayout.Foldout(_showClipQueue, $"Queued Clips: {_clipQueue.Count}", 
                true, _foldoutStyle);
            GUI.enabled = _clipQueue.Count > 0;
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(52f)))
            {
                _clipQueue.Clear();
                _queuedClipKeys.Clear();
                _queueMessage = string.Empty;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_showClipQueue)
            {
                for (int i = 0; i < _clipQueue.Count; i++)
                {
                    ClipQueueItem item = _clipQueue[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(item.clip, typeof(AnimationClip), false);
                    GUILayout.Label(new GUIContent(item.status.ToString(), item.statusMessage), EditorStyles.miniLabel,
                        GUILayout.Width(62f));

                    bool remove = GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f));
                    EditorGUILayout.EndHorizontal();

                    if (!remove) continue;

                    _queuedClipKeys.Remove(item.key);
                    _clipQueue.RemoveAt(i);
                    i--;
                }
            }

            if (!string.IsNullOrEmpty(_queueMessage))
            {
                EditorGUILayout.HelpBox(_queueMessage, MessageType.Info);
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition)) return;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform) return;

            bool hasSupportedAsset = HasSupportedAsset(DragAndDrop.objectReferences);
            DragAndDrop.visualMode = hasSupportedAsset ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform && hasSupportedAsset)
            {
                DragAndDrop.AcceptDrag();
                AddDroppedAssets(DragAndDrop.objectReferences);
            }

            currentEvent.Use();
        }

        private static bool HasSupportedAsset(Object[] assets)
        {
            foreach (Object asset in assets)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (IsFbxPath(path) || string.Equals(Path.GetExtension(path), ".anim",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddDroppedAssets(Object[] assets)
        {
            int added = 0;
            int ignored = 0;
            HashSet<string> processedPaths = new HashSet<string>();

            foreach (Object asset in assets)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) || !processedPaths.Add(path))
                {
                    ignored++;
                    continue;
                }

                if (IsFbxPath(path))
                {
                    foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        AnimationClip clip = subAsset as AnimationClip;
                        if (clip == null) continue;
                        if (!IsValidFbxClip(clip))
                        {
                            ignored++;
                            continue;
                        }

                        if (TryAddClip(clip, path)) added++;
                        else ignored++;
                    }
                }
                else if (string.Equals(Path.GetExtension(path), ".anim", StringComparison.OrdinalIgnoreCase))
                {
                    AnimationClip clip = asset as AnimationClip ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (TryAddClip(clip, path)) added++;
                    else ignored++;
                }
                else
                {
                    ignored++;
                }
            }

            _queueMessage = $"Added {added} clip(s). Ignored {ignored}.";
        }

        private bool TryAddClip(AnimationClip clip, string assetPath)
        {
            if (clip == null) return false;

            string key = GetClipKey(clip);
            if (!_queuedClipKeys.Add(key)) return false;

            _clipQueue.Add(new ClipQueueItem
            {
                clip = clip,
                assetPath = assetPath,
                key = key,
                status = ClipStatus.Pending,
                statusMessage = string.Empty
            });

            return true;
        }

        private static string GetClipKey(AnimationClip clip)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId))
            {
                return $"{guid}:{localId}";
            }

            return $"{AssetDatabase.GetAssetPath(clip)}:{clip.name}";
        }

        private static bool IsValidFbxClip(AnimationClip clip)
        {
            if (clip == null || (clip.hideFlags & HideFlags.HideInHierarchy) != 0) return false;
            return clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsFbxPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase);
        }

        public void Init()
        {
        }

        public void Render()
        {
            if (!EditorGUIUtility.wideMode) EditorGUIUtility.wideMode = true;

            DrawClipQueue();

            GUIContent content = new GUIContent("Character Model", "Model Game Object.");
            _root = EditorGUILayout.ObjectField(content, _root, typeof(Transform), true) as Transform;

            content = new GUIContent("Copy From", "Bone to copy pose from.");
            _extractFrom = EditorGUILayout.ObjectField(content, _extractFrom, typeof(Transform), true) as Transform;

            content = new GUIContent("Copy To", "Bone to copy pose to.");
            _extractTo = EditorGUILayout.ObjectField(content, _extractTo, typeof(Transform), true) as Transform;
            _rotationOffset = EditorGUILayout.Vector3Field("Rotation Offset", _rotationOffset);

            EditorGUILayout.Space();

            content = new GUIContent("Is Additive", "If true, pose will be copied relative to the reference animation.");
            _isAdditive = EditorGUILayout.Toggle(content, _isAdditive);

            GUI.enabled = _isAdditive;
            _refClip = EditorGUILayout.ObjectField("Reference Animation", _refClip, typeof(AnimationClip), true) as
                AnimationClip;
            GUI.enabled = true;

            bool valid = _clipQueue.Count > 0 && _root != null && _extractFrom != null && _extractTo != null &&
                         _extractTo.parent != null;
            if (_isAdditive && _refClip == null) valid = false;

            if (!valid)
            {
                EditorGUILayout.HelpBox("Queue clips and assign all references.", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Process Queue"))
            {
                ProcessQueue();
            }
        }

        public string GetToolCategory() => "Animation";
        public string GetToolName() => "Copy Bone";
        public string GetDocsURL() => string.Empty;
        public string GetToolDescription() => "Samples and bakes animation from one bone to another.";
    }
}
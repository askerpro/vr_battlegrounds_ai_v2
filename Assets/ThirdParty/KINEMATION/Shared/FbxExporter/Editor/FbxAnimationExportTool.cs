// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.Shared.FbxExporter.Editor
{
    public class FbxAnimationExportTool : IEditorTool
    {
        private enum ClipStatus
        {
            Pending,
            Exported,
            Failed
        }

        private class ClipQueueItem
        {
            public AnimationClip clip;
            public string key;
            public ClipStatus status;
            public string statusMessage;
        }

        private GameObject _model;
        private readonly List<ClipQueueItem> _clipQueue = new List<ClipQueueItem>();
        private readonly HashSet<string> _queuedClipKeys = new HashSet<string>();
        private bool _showClipQueue = true;
        private GUIStyle _foldoutStyle;

        private string _outputAssetDirectory = "Assets/";
        private string _lastExportPath;
        private string _statusMessage = "Drop animation clips or FBX assets into the queue.";
        private MessageType _statusType = MessageType.Info;

        private float _startTime;
        private float _endTime;
        private float _sampleRate = 30f;
        private bool _includeScaleCurves = true;
        private bool _optimizeConstantCurves = true;

        public void Init()
        {
        }

        public void Render()
        {
            _model = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), true);
            DrawClipQueue();
            DrawExportOptions();
            DrawOutputDirectory();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            using (new EditorGUI.DisabledScope(!CanExport()))
            {
                if (GUILayout.Button("Export Queue", GUILayout.Height(28f)))
                {
                    ExportQueue();
                }
            }

            DrawLastExportInfo();
        }

        public string GetToolCategory() => "Animation";
        public string GetToolName() => "FBX Exporter";
        public string GetDocsURL() => string.Empty;
        public string GetToolDescription() => "Exports Animation Clips to ASCII FBX assets.";

        private void DrawClipQueue()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));
            var dropAreaStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Box(dropArea, "Drop .anim clips or FBX assets here", dropAreaStyle);
            HandleDragAndDrop(dropArea);

            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldout);
                Color textColor = EditorStyles.label.normal.textColor;
                _foldoutStyle.normal.textColor = textColor;
                _foldoutStyle.hover.textColor = textColor;
                _foldoutStyle.active.textColor = textColor;
                _foldoutStyle.focused.textColor = textColor;
                _foldoutStyle.onNormal.textColor = textColor;
                _foldoutStyle.onHover.textColor = textColor;
                _foldoutStyle.onActive.textColor = textColor;
                _foldoutStyle.onFocused.textColor = textColor;
            }

            EditorGUILayout.BeginHorizontal();
            _showClipQueue = EditorGUILayout.Foldout(_showClipQueue, $"Queued Clips: {_clipQueue.Count}", true,
                _foldoutStyle);

            using (new EditorGUI.DisabledScope(_clipQueue.Count == 0))
            {
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(52f)))
                {
                    _clipQueue.Clear();
                    _queuedClipKeys.Clear();
                    _statusMessage = "Drop animation clips or FBX assets into the queue.";
                    _statusType = MessageType.Info;
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!_showClipQueue)
            {
                return;
            }

            for (int i = 0; i < _clipQueue.Count; i++)
            {
                ClipQueueItem item = _clipQueue[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(item.clip, typeof(AnimationClip), false);
                GUILayout.Label(new GUIContent(item.status.ToString(), item.statusMessage), EditorStyles.miniLabel,
                    GUILayout.Width(62f));
                bool remove = GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();

                if (!remove)
                {
                    continue;
                }

                _queuedClipKeys.Remove(item.key);
                _clipQueue.RemoveAt(i);
                i--;
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

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
                string extension = Path.GetExtension(AssetDatabase.GetAssetPath(asset));
                if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".anim", StringComparison.OrdinalIgnoreCase))
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
            var processedPaths = new HashSet<string>();

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
                        var clip = subAsset as AnimationClip;
                        if (clip == null)
                        {
                            continue;
                        }

                        if (!IsValidFbxClip(clip))
                        {
                            ignored++;
                            continue;
                        }

                        if (TryAddClip(clip)) added++;
                        else ignored++;
                    }
                }
                else if (string.Equals(Path.GetExtension(path), ".anim", StringComparison.OrdinalIgnoreCase))
                {
                    var clip = asset as AnimationClip ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (TryAddClip(clip)) added++;
                    else ignored++;
                }
                else
                {
                    ignored++;
                }
            }

            _statusMessage = $"Added {added} clip(s). Ignored {ignored}.";
            _statusType = MessageType.Info;
        }

        private bool TryAddClip(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            string key = GetClipKey(clip);
            if (!_queuedClipKeys.Add(key))
            {
                return false;
            }

            _clipQueue.Add(new ClipQueueItem
            {
                clip = clip,
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
            if (clip == null || (clip.hideFlags & HideFlags.HideInHierarchy) != 0)
            {
                return false;
            }

            return clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsFbxPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private void DrawExportOptions()
        {
            _startTime = Mathf.Max(0f, EditorGUILayout.FloatField("Start Time", _startTime));
            _endTime = Mathf.Max(0f, EditorGUILayout.FloatField("End Time", _endTime));
            _sampleRate = Mathf.Max(0f, EditorGUILayout.FloatField("Base Sample Rate", _sampleRate));
            _includeScaleCurves = EditorGUILayout.Toggle("Include Scale Curves", _includeScaleCurves);
            _optimizeConstantCurves = EditorGUILayout.Toggle("Optimize Constant Curves", _optimizeConstantCurves);
        }

        private void DrawOutputDirectory()
        {
            EditorGUILayout.BeginHorizontal();
            _outputAssetDirectory = EditorGUILayout.TextField("Output Asset Folder", _outputAssetDirectory);
            if (GUILayout.Button("Choose", GUILayout.Width(72f)))
            {
                string initialDirectory = GetAbsoluteProjectPath(NormalizeAssetDirectory(_outputAssetDirectory));
                if (!Directory.Exists(initialDirectory))
                {
                    initialDirectory = Application.dataPath;
                }

                string selectedPath = EditorUtility.OpenFolderPanel("Choose FBX Export Folder", initialDirectory,
                    string.Empty);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    string selectedAssetPath = FileUtil.GetProjectRelativePath(selectedPath);
                    if (string.IsNullOrEmpty(selectedAssetPath) ||
                        (!selectedAssetPath.StartsWith("Assets/") && selectedAssetPath != "Assets"))
                    {
                        _statusMessage = "Output directory must be inside the project's Assets folder.";
                        _statusType = MessageType.Error;
                    }
                    else
                    {
                        _outputAssetDirectory = NormalizeAssetDirectory(selectedAssetPath);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLastExportInfo()
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Export", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_lastExportPath, EditorStyles.wordWrappedLabel);

            Object exportedAsset = AssetDatabase.LoadMainAssetAtPath(_lastExportPath);
            int importedClipCount = AssetDatabase.LoadAllAssetsAtPath(_lastExportPath).OfType<AnimationClip>().Count();
            EditorGUILayout.LabelField("Imported Clips", importedClipCount.ToString());

            using (new EditorGUI.DisabledScope(exportedAsset == null))
            {
                if (GUILayout.Button("Ping Exported Asset"))
                {
                    Selection.activeObject = exportedAsset;
                    EditorGUIUtility.PingObject(exportedAsset);
                }
            }
        }

        private bool CanExport()
        {
            return _model != null && _clipQueue.Count > 0 && !string.IsNullOrWhiteSpace(_outputAssetDirectory);
        }

        private void ExportQueue()
        {
            int exported = 0;
            int failed = 0;
            var pendingImports = new List<KeyValuePair<ClipQueueItem, string>>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (ClipQueueItem item in _clipQueue)
                {
                    item.status = ClipStatus.Pending;
                    item.statusMessage = string.Empty;

                    if (item.clip == null)
                    {
                        item.status = ClipStatus.Failed;
                        item.statusMessage = "Animation clip is no longer available.";
                        failed++;
                        continue;
                    }

                    string outputAssetPath = GetOutputAssetPath(item.clip);
                    var options = new AsciiFbxExporter.ExportOptions
                    {
                        model = _model,
                        clip = item.clip,
                        outputAssetPath = outputAssetPath,
                        startTime = _startTime,
                        endTime = _endTime,
                        sampleRate = _sampleRate,
                        stripRootNode = true,
                        includeScaleCurves = _includeScaleCurves,
                        optimizeConstantCurves = _optimizeConstantCurves
                    };

                    if (AsciiFbxExporter.ExportFile(options, out string writtenAssetPath, out string error))
                    {
                        item.statusMessage = writtenAssetPath;
                        pendingImports.Add(new KeyValuePair<ClipQueueItem, string>(item, writtenAssetPath));
                        AssetDatabase.ImportAsset(writtenAssetPath, ImportAssetOptions.ForceUpdate);
                        continue;
                    }

                    item.status = ClipStatus.Failed;
                    item.statusMessage = error;
                    failed++;

                    if (string.Equals(error, "Export cancelled.", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var correctiveImports = new List<string>();
            foreach (KeyValuePair<ClipQueueItem, string> pendingImport in pendingImports)
            {
                ModelImporter importer = AssetImporter.GetAtPath(pendingImport.Value) as ModelImporter;
                if (importer == null)
                {
                    pendingImport.Key.status = ClipStatus.Failed;
                    pendingImport.Key.statusMessage =
                        "Unity did not create a ModelImporter for the exported FBX.";
                    failed++;
                    continue;
                }

                if (AsciiFbxExporter.ApplyImporterSettings(importer))
                {
                    EditorUtility.SetDirty(importer);
                    if (AssetDatabase.WriteImportSettingsIfDirty(pendingImport.Value))
                    {
                        correctiveImports.Add(pendingImport.Value);
                    }
                }

                pendingImport.Key.status = ClipStatus.Exported;
                pendingImport.Key.statusMessage = pendingImport.Value;
                _lastExportPath = pendingImport.Value;
                exported++;
            }

            if (correctiveImports.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string assetPath in correctiveImports)
                    {
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            _statusMessage = $"Exported {exported} clip(s). Failed {failed}.";
            _statusType = failed > 0 ? MessageType.Warning : MessageType.Info;
        }

        private string GetOutputAssetPath(AnimationClip clip)
        {
            string outputAssetPath = $"{NormalizeAssetDirectory(_outputAssetDirectory)}/{GetOutputFileName(clip)}";
            return AssetDatabase.GenerateUniqueAssetPath(outputAssetPath);
        }

        private static string GetOutputFileName(AnimationClip clip)
        {
            string fileName = clip != null ? clip.name : "FbxExport";
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidCharacter.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(fileName) ? "FbxExport.fbx" : $"{fileName}.fbx";
        }

        private static string NormalizeAssetDirectory(string assetDirectory)
        {
            return (assetDirectory ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }
    }
}

// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.Shared.FbxExporter.Editor
{
    public static class AsciiFbxExporter
    {
        public const long FbxTicksPerSecond = 46186158000L;

        private const float MinSampleRate = 1f;
        private const float MaxSampleRate = 1000f;
        private const float CurveEpsilon = 0.0001f;
        private const double FrameSnapEpsilon = 0.001d;
        private const float PositionCurveTolerance = 0.0005f;
        private const float RotationCurveTolerance = 0.05f;
        private const float ScaleCurveTolerance = 0.0005f;
        private const int MaxAdaptiveCurveDepth = 8;
        private const string ExporterVersionTag = "v2026-08-10-streaming-batch";
        private const string ExporterDisplayName = "KINEMATION ASCII FBX Exporter";
        private const string LegacyExporterDisplayName = "Retarget Pro ASCII FBX Exporter";
        private const int WeightedUserKeyAttrFlag = 50334728;
        private const float DefaultTangentWeight = 1f / 3f;
        private const float TerminalTangentWeight = 0.00010001f;
        private const float FbxTranslationScale = 100f;
        private const float FbxFileUnitScaleFactor = 1f;
        private const int FbxFallbackTimeMode = 14;
        private const int Fbx60FpsTimeMode = 3;
        private const int TransformChannelCount = 9;
        private const int TranslationX = 0;
        private const int TranslationY = 1;
        private const int TranslationZ = 2;
        private const int RotationX = 3;
        private const int RotationY = 4;
        private const int RotationZ = 5;
        private const int ScaleX = 6;
        private const int ScaleY = 7;
        private const int ScaleZ = 8;
        private static readonly string[] AnimatorRootPositionPropertyNames = { "RootT.x", "RootT.y", "RootT.z" };
        private static readonly string[] AnimatorRootRotationPropertyNames = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

        [Serializable]
        public sealed class ExportOptions
        {
            public GameObject model;
            public AnimationClip clip;
            public string outputAssetPath = "Assets/RetargetedAnimations/AsciiExport.fbx";
            public float sampleRate;
            public float startTime;
            public float endTime;
            public bool stripRootNode = true;
            public bool includeScaleCurves = true;
            public bool optimizeConstantCurves = true;
            public string[] excludedTransformPaths;
        }

        public sealed class DirectPoseExportOptions
        {
            public GameObject model;
            public IEnumerable<Transform> animatedTransforms;
            public string outputAssetPath = "Assets/RetargetedAnimations/DirectPoseExport.fbx";
            public float sampleRate;
            public int expectedSampleCount;
            public bool stripRootNode = true;
            public bool normalizeRootScale;
            public bool includeScaleCurves = true;
            public bool optimizeConstantCurves = true;
        }

        public sealed class DirectPoseContextOptions
        {
            public GameObject model;
            public IEnumerable<Transform> animatedTransforms;
            public bool stripRootNode = true;
            public bool normalizeRootScale;
            public bool includeScaleCurves = true;
            public bool optimizeConstantCurves = true;
        }

        public sealed class DirectPoseClipOptions
        {
            public string outputAssetPath = "Assets/RetargetedAnimations/DirectPoseExport.fbx";
            public float sampleRate;
            public int expectedSampleCount;
        }

        private sealed class ExportRootTrack
        {
            public int trackIndex;
            public string exportName;
            public Vector3 defaultLocalPosition;
            public Vector3 defaultLocalRotation;
            public Vector3 defaultLocalScale;
            public Vector3 lastSampledEuler;
            public bool hasRotationSample;

            public FbxCurveNode translationNode;
            public FbxCurveNode rotationNode;
            public FbxCurveNode scaleNode;
        }

        private sealed class ExportNode
        {
            public int trackIndex;
            public int parentIndex;
            public string exportName;
            public string sourcePath;
            public Transform transform;
            public bool isSkeletonNode;
            public Vector3 defaultLocalPosition;
            public Vector3 defaultLocalRotation;
            public Vector3 defaultLocalScale;
            public Vector3 baselineLocalPosition;
            public Quaternion baselineLocalRotation;
            public Vector3 baselineLocalScale;
            public Vector3 lastSampledEuler;
            public bool hasRotationSample;

            public AnimationCurve sourcePositionX;
            public AnimationCurve sourcePositionY;
            public AnimationCurve sourcePositionZ;
            public AnimationCurve sourceScaleX;
            public AnimationCurve sourceScaleY;
            public AnimationCurve sourceScaleZ;
            public AnimationCurve sourceEulerX;
            public AnimationCurve sourceEulerY;
            public AnimationCurve sourceEulerZ;

            public long modelId;
            public long nodeAttributeId;
            public FbxCurveNode translationNode;
            public FbxCurveNode rotationNode;
            public FbxCurveNode scaleNode;
        }

        private sealed class FbxCurveNode
        {
            public long id;
            public string label;
            public Vector3 defaultValue;
            public FbxCurve xCurve;
            public FbxCurve yCurve;
            public FbxCurve zCurve;
        }

        private sealed class FbxCurve
        {
            public long id;
            public float defaultValue;
            public int trackIndex;
            public int channelIndex;
            public float valueScale;
        }

        private sealed class ExportDocument
        {
            public string sceneName;
            public long sceneRootModelId;
            public long animationStackId;
            public long animationLayerId;
            public long stopTime;
            public float frameRate;
            public bool includeScaleCurves;
            public bool optimizeConstantCurves;
            public ExportRootTrack rootTrack;
            public List<ExportNode> nodes;
            public IReadOnlyList<long> sampleTimes;
            public PoseBuffer poseBuffer;
        }

        private sealed class PoseBuffer
        {
            private float[] _values = Array.Empty<float>();
            private int _trackCount;
            private int _sampleCapacity;

            public int SampleCount { get; private set; }

            public void Prepare(int trackCount, int sampleCount)
            {
                if (trackCount <= 0) throw new ArgumentOutOfRangeException(nameof(trackCount));
                if (sampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));

                int requiredLength = checked(trackCount * TransformChannelCount * sampleCount);
                if (_trackCount != trackCount || _values.Length < requiredLength)
                {
                    _values = new float[requiredLength];
                    _sampleCapacity = sampleCount;
                }

                _trackCount = trackCount;
                SampleCount = sampleCount;
            }

            public void Set(int trackIndex, int channelIndex, int sampleIndex, float value)
            {
                _values[GetIndex(trackIndex, channelIndex, sampleIndex)] = value;
            }

            public float Get(int trackIndex, int channelIndex, int sampleIndex)
            {
                return _values[GetIndex(trackIndex, channelIndex, sampleIndex)];
            }

            private int GetIndex(int trackIndex, int channelIndex, int sampleIndex)
            {
                if ((uint)trackIndex >= (uint)_trackCount ||
                    (uint)channelIndex >= TransformChannelCount ||
                    (uint)sampleIndex >= (uint)SampleCount)
                {
                    throw new IndexOutOfRangeException("The pose buffer index is outside the prepared clip slice.");
                }

                return (trackIndex * TransformChannelCount + channelIndex) * _sampleCapacity + sampleIndex;
            }
        }

        private sealed class FbxAsciiWriter : IDisposable
        {
            private const int BufferSize = 64 * 1024;
            private readonly FileStream _stream;
            private readonly UTF8Encoding _encoding = new UTF8Encoding(false);
            private readonly byte[] _buffer = new byte[BufferSize];
            private int _bufferCount;

            public FbxAsciiWriter(string path)
            {
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.SequentialScan);
            }

            public void Append(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                int maxByteCount = _encoding.GetMaxByteCount(value.Length);
                if (maxByteCount > _buffer.Length)
                {
                    Flush();
                    byte[] bytes = _encoding.GetBytes(value);
                    _stream.Write(bytes, 0, bytes.Length);
                    return;
                }

                EnsureCapacity(maxByteCount);
                _bufferCount += _encoding.GetBytes(value, 0, value.Length, _buffer, _bufferCount);
            }

            public void Append(char value)
            {
                EnsureCapacity(1);
                _buffer[_bufferCount++] = (byte)value;
            }

            public void Append(int value)
            {
                EnsureCapacity(32);
                if (!Utf8Formatter.TryFormat(value, _buffer.AsSpan(_bufferCount),
                        out int written))
                {
                    throw new InvalidOperationException("Failed to format an FBX integer.");
                }

                _bufferCount += written;
            }

            public void Append(long value)
            {
                EnsureCapacity(32);
                if (!Utf8Formatter.TryFormat(value, _buffer.AsSpan(_bufferCount),
                        out int written))
                {
                    throw new InvalidOperationException("Failed to format an FBX time value.");
                }

                _bufferCount += written;
            }

            public void Append(float value)
            {
                Span<char> formattedValue = stackalloc char[32];
                if (!value.TryFormat(formattedValue, out int written, "R", CultureInfo.InvariantCulture))
                {
                    throw new InvalidOperationException("Failed to format an FBX floating-point value.");
                }

                EnsureCapacity(written);
                for (int i = 0; i < written; i++)
                {
                    _buffer[_bufferCount++] = (byte)formattedValue[i];
                }
            }

            public void AppendLine()
            {
                Append(Environment.NewLine);
            }

            public void AppendLine(string value)
            {
                Append(value);
                AppendLine();
            }

            public void Dispose()
            {
                Flush();
                _stream.Dispose();
            }

            private void EnsureCapacity(int requiredBytes)
            {
                if (_buffer.Length - _bufferCount < requiredBytes) Flush();
            }

            private void Flush()
            {
                if (_bufferCount == 0) return;
                _stream.Write(_buffer, 0, _bufferCount);
                _bufferCount = 0;
            }
        }

        private sealed class RootMotionCurves
        {
            public AnimationCurve[] positionCurves;
            public AnimationCurve[] rotationCurves;

            public bool HasPosition => positionCurves != null && positionCurves.Length == 3;
            public bool HasRotation => rotationCurves != null && rotationCurves.Length == 4;
            public bool HasAny => HasPosition || HasRotation;
        }

        private sealed class ClipCurveLookup
        {
            public readonly Dictionary<string, AnimationCurve> transformCurves =
                new Dictionary<string, AnimationCurve>(StringComparer.Ordinal);
        }

        private sealed class IdGenerator
        {
            private long _nextId = 1000000L;

            public long Next()
            {
                _nextId += 16L;
                return _nextId;
            }
        }

        public sealed class DirectPoseExportContext : IDisposable
        {
            private readonly GameObject _model;
            private readonly HashSet<Transform> _animatedTransforms;
            private readonly bool _includeScaleCurves;
            private readonly bool _optimizeConstantCurves;
            private readonly ExportRootTrack _rootTrack;
            private readonly List<ExportNode> _nodes = new List<ExportNode>();
            private readonly PoseBuffer _poseBuffer = new PoseBuffer();
            private readonly Vector3 _rootPosition;
            private readonly Quaternion _rootRotation;
            private readonly Vector3 _rootScale;
            private ExportDocument _document;
            private bool _hasActiveRecorder;
            private bool _disposed;

            internal DirectPoseExportContext(DirectPoseContextOptions options,
                HashSet<Transform> animatedTransforms)
            {
                _model = options.model;
                _animatedTransforms = animatedTransforms;
                _includeScaleCurves = options.includeScaleCurves;
                _optimizeConstantCurves = options.optimizeConstantCurves;

                Transform modelRoot = _model.transform;
                bool stripRootNode = options.stripRootNode && modelRoot.childCount > 0;
                _rootPosition = modelRoot.localPosition;
                _rootRotation = modelRoot.localRotation;
                _rootScale = options.normalizeRootScale ? Vector3.one : modelRoot.localScale;
                _rootTrack = stripRootNode ? CreateRootTrack(modelRoot) : null;

                HashSet<Transform> skeletonTransforms = BuildSkeletonTransformSet(_model);
                if (stripRootNode)
                {
                    for (int i = 0; i < modelRoot.childCount; i++)
                    {
                        CollectExportNodes(modelRoot.GetChild(i), modelRoot, -1, _nodes, skeletonTransforms);
                    }
                }
                else
                {
                    CollectExportNodes(modelRoot, modelRoot, -1, _nodes, skeletonTransforms);
                }

                AssignTrackIndices(_rootTrack, _nodes);
                ResetCaptureState();
            }

            public bool TryCreateRecorder(DirectPoseClipOptions options,
                out DirectPoseRecorder recorder, out string error)
            {
                return TryCreateRecorder(options, false, out recorder, out error);
            }

            internal bool TryCreateRecorder(DirectPoseClipOptions options, bool disposeContext,
                out DirectPoseRecorder recorder, out string error)
            {
                recorder = null;
                error = string.Empty;
                if (_disposed)
                {
                    error = "The direct pose export context has been disposed.";
                    return false;
                }

                if (_hasActiveRecorder)
                {
                    error = "Finish the active direct pose recording before starting another clip.";
                    return false;
                }

                if (options == null || options.expectedSampleCount <= 0)
                {
                    error = "The direct pose clip must declare at least one expected sample.";
                    return false;
                }

                if (!TryNormalizeOutputAssetPath(options.outputAssetPath,
                        out string outputAssetPath, out error))
                {
                    return false;
                }

                ResetCaptureState();
                _poseBuffer.Prepare(_nodes.Count + (_rootTrack != null ? 1 : 0),
                    options.expectedSampleCount);
                _hasActiveRecorder = true;
                recorder = new DirectPoseRecorder(this, outputAssetPath,
                    ResolveSampleRate(options.sampleRate, null), options.expectedSampleCount,
                    disposeContext);
                return true;
            }

            internal void Capture(int sampleIndex)
            {
                CaptureCurrentPose(_model, _rootTrack, _nodes, _rootPosition, _rootRotation,
                    _rootScale, sampleIndex, _animatedTransforms, false, _poseBuffer);
            }

            internal void Write(string outputAssetPath, float sampleRate,
                IReadOnlyList<float> sampleTimes)
            {
                List<long> fbxSampleTimes = ConvertSampleTimesToFbxTicks(sampleTimes, sampleRate);
                if (_document == null)
                {
                    _document = BuildDocument(outputAssetPath, _includeScaleCurves,
                        _optimizeConstantCurves, _rootTrack, _nodes, fbxSampleTimes, sampleRate,
                        _poseBuffer);
                }
                else
                {
                    UpdateDocument(_document, outputAssetPath, fbxSampleTimes, sampleRate);
                }

                WriteAsciiDocument(GetAbsoluteProjectPath(outputAssetPath), _document);
            }

            internal void ReleaseRecorder()
            {
                _hasActiveRecorder = false;
            }

            public void Dispose()
            {
                _disposed = true;
                _hasActiveRecorder = false;
            }

            private void ResetCaptureState()
            {
                if (_rootTrack != null)
                {
                    _rootTrack.defaultLocalPosition = _rootPosition;
                    _rootTrack.defaultLocalRotation = ToSignedEuler(_rootRotation.eulerAngles);
                    _rootTrack.defaultLocalScale = _rootScale;
                    _rootTrack.lastSampledEuler = default;
                    _rootTrack.hasRotationSample = false;
                }

                for (int i = 0; i < _nodes.Count; i++)
                {
                    ExportNode node = _nodes[i];
                    node.defaultLocalPosition = node.baselineLocalPosition;
                    node.defaultLocalRotation = ToSignedEuler(node.baselineLocalRotation.eulerAngles);
                    node.defaultLocalScale = node.baselineLocalScale;
                    node.lastSampledEuler = default;
                    node.hasRotationSample = false;
                }
            }
        }

        public sealed class DirectPoseRecorder : IDisposable
        {
            private readonly DirectPoseExportContext _context;
            private readonly string _outputAssetPath;
            private readonly float _sampleRate;
            private readonly float[] _sampleTimes;
            private readonly bool _disposeContext;
            private int _sampleCount;
            private bool _written;
            private bool _released;

            internal DirectPoseRecorder(DirectPoseExportContext context, string outputAssetPath,
                float sampleRate, int expectedSampleCount, bool disposeContext)
            {
                _context = context;
                _outputAssetPath = outputAssetPath;
                _sampleRate = sampleRate;
                _sampleTimes = new float[expectedSampleCount];
                _disposeContext = disposeContext;
            }

            public bool Capture(float sampleTime, out string error)
            {
                error = string.Empty;
                if (_written || _released)
                {
                    error = "The direct pose export is already closed.";
                    return false;
                }

                if (float.IsNaN(sampleTime) || float.IsInfinity(sampleTime) || sampleTime < 0f)
                {
                    error = "The direct pose sample time is invalid.";
                    return false;
                }

                if (_sampleCount >= _sampleTimes.Length)
                {
                    error = "The direct pose recording contains more samples than declared.";
                    return false;
                }

                if (_sampleCount > 0 && sampleTime <= _sampleTimes[_sampleCount - 1])
                {
                    error = "Direct pose sample times must be strictly increasing.";
                    return false;
                }

                int sampleIndex = _sampleCount++;
                _sampleTimes[sampleIndex] = sampleTime;
                _context.Capture(sampleIndex);
                return true;
            }

            public bool Write(out string error)
            {
                error = string.Empty;
                if (_written || _released)
                {
                    error = "The direct pose export is already closed.";
                    return false;
                }

                if (_sampleCount != _sampleTimes.Length)
                {
                    error = $"Expected {_sampleTimes.Length} direct pose samples but captured {_sampleCount}.";
                    return false;
                }

                try
                {
                    _context.Write(_outputAssetPath, _sampleRate, _sampleTimes);
                    _written = true;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
                finally
                {
                    Release();
                }
            }

            public void Dispose()
            {
                Release();
            }

            private void Release()
            {
                if (_released) return;
                _released = true;
                _context.ReleaseRecorder();
                if (_disposeContext) _context.Dispose();
            }
        }

        public static bool TryCreateDirectPoseRecorder(DirectPoseExportOptions options,
            out DirectPoseRecorder recorder, out string error)
        {
            recorder = null;
            error = string.Empty;
            if (options == null)
            {
                error = "Assign a model root to export.";
                return false;
            }

            var contextOptions = new DirectPoseContextOptions
            {
                model = options.model,
                animatedTransforms = options.animatedTransforms,
                stripRootNode = options.stripRootNode,
                normalizeRootScale = options.normalizeRootScale,
                includeScaleCurves = options.includeScaleCurves,
                optimizeConstantCurves = options.optimizeConstantCurves
            };
            if (!TryCreateDirectPoseContext(contextOptions,
                    out DirectPoseExportContext context, out error)) return false;

            var clipOptions = new DirectPoseClipOptions
            {
                outputAssetPath = options.outputAssetPath,
                sampleRate = options.sampleRate,
                expectedSampleCount = options.expectedSampleCount
            };
            if (context.TryCreateRecorder(clipOptions, true, out recorder, out error)) return true;

            context.Dispose();
            return false;
        }

        public static bool TryCreateDirectPoseContext(DirectPoseContextOptions options,
            out DirectPoseExportContext context, out string error)
        {
            context = null;
            error = string.Empty;
            if (options == null || options.model == null)
            {
                error = "Assign a model root to export.";
                return false;
            }

            var animatedTransforms = new HashSet<Transform>();
            if (options.animatedTransforms != null)
            {
                foreach (Transform transform in options.animatedTransforms)
                {
                    if (transform != null && transform != options.model.transform &&
                        transform.IsChildOf(options.model.transform))
                    {
                        animatedTransforms.Add(transform);
                    }
                }
            }

            if (animatedTransforms.Count == 0)
            {
                error = "The direct pose export has no animated transforms.";
                return false;
            }

            context = new DirectPoseExportContext(options, animatedTransforms);
            return true;
        }

        public static bool Export(ExportOptions options, out string error)
        {
            if (!ExportFile(options, out string outputAssetPath, out error)) return false;

            AssetDatabase.ImportAsset(outputAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ModelImporter importer = AssetImporter.GetAtPath(outputAssetPath) as ModelImporter;
            if (importer == null)
            {
                error = "Unity did not create a ModelImporter for the exported FBX.";
                return false;
            }

            ConfigureImporter(importer);
            return true;
        }

        internal static bool ExportFile(ExportOptions options, out string exportedAssetPath,
            out string error)
        {
            exportedAssetPath = string.Empty;
            error = string.Empty;

            if (!ValidateOptions(options, out float normalizedStartTime, out float normalizedEndTime,
                    out float sampleRate, out error))
            {
                return false;
            }

            string outputAssetPath = NormalizeAssetPath(options.outputAssetPath);
            exportedAssetPath = outputAssetPath;
            string absoluteOutputPath = GetAbsoluteProjectPath(outputAssetPath);
            string directoryPath = Path.GetDirectoryName(absoluteOutputPath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                error = "Unable to resolve the output directory.";
                return false;
            }

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            GameObject exportInstance = null;

            try
            {
                exportInstance = Object.Instantiate(options.model);
                if (exportInstance == null)
                {
                    error = "Failed to instantiate the export model.";
                    return false;
                }

                exportInstance.hideFlags = HideFlags.HideAndDontSave;
                exportInstance.name = options.model.name;

                RootMotionCurves rootMotionCurves = ExtractRootMotionCurves(options.clip);

                // Imported animation paths must start at the actual skeleton root, not the model container.
                bool stripRootNode = options.stripRootNode && exportInstance.transform.childCount > 0;
                ExportRootTrack rootTrack = stripRootNode ? CreateRootTrack(exportInstance.transform) : null;
                HashSet<Transform> skeletonTransforms = BuildSkeletonTransformSet(exportInstance);
                HashSet<string> excludedTransformPaths = null;
                if (options.excludedTransformPaths != null && options.excludedTransformPaths.Length > 0)
                {
                    excludedTransformPaths = new HashSet<string>(options.excludedTransformPaths, StringComparer.Ordinal);
                    excludedTransformPaths.Remove(null);
                    excludedTransformPaths.Remove(string.Empty);
                }

                var exportNodes = new List<ExportNode>();
                if (stripRootNode)
                {
                    for (int i = 0; i < exportInstance.transform.childCount; i++)
                    {
                        CollectExportNodes(exportInstance.transform.GetChild(i), exportInstance.transform, -1,
                            exportNodes, skeletonTransforms, excludedTransformPaths);
                    }
                }
                else
                {
                    CollectExportNodes(exportInstance.transform, exportInstance.transform, -1, exportNodes,
                        skeletonTransforms, excludedTransformPaths);
                }
                AssignTrackIndices(rootTrack, exportNodes);

                ClipCurveLookup clipCurveLookup = BuildClipCurveLookup(options.clip);
                AttachSourceCurves(exportNodes, clipCurveLookup);

                if (!stripRootNode)
                {
                    ApplyExportRootDefaults(exportInstance.transform, exportNodes, rootMotionCurves, normalizedStartTime);
                }

                List<float> sampleTimes = BuildSampleTimes(normalizedStartTime, normalizedEndTime, sampleRate,
                    exportNodes, rootMotionCurves);
                if (sampleTimes.Count == 0)
                {
                    sampleTimes.Add(0f);
                }

                var poseBuffer = new PoseBuffer();
                poseBuffer.Prepare(exportNodes.Count + (rootTrack != null ? 1 : 0), sampleTimes.Count);
                if (!CaptureSamples(options.clip, exportInstance, rootTrack, rootMotionCurves, exportNodes, sampleTimes,
                        normalizedStartTime, poseBuffer, out error))
                {
                    return false;
                }

                List<long> fbxSampleTimes = ConvertSampleTimesToFbxTicks(sampleTimes, sampleRate);
                ExportDocument document = BuildDocument(outputAssetPath, options.includeScaleCurves,
                    options.optimizeConstantCurves, rootTrack, exportNodes, fbxSampleTimes, sampleRate, poseBuffer);

                WriteAsciiDocument(absoluteOutputPath, document);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (exportInstance != null)
                {
                    Object.DestroyImmediate(exportInstance);
                }
            }
        }

        private static ExportDocument BuildDocument(string outputAssetPath, bool includeScaleCurves,
            bool optimizeConstantCurves, ExportRootTrack rootTrack, List<ExportNode> exportNodes,
            IReadOnlyList<long> sampleTimes, float resolvedFrameRate, PoseBuffer poseBuffer)
        {
            var idGenerator = new IdGenerator();

            var document = new ExportDocument
            {
                sceneName = Path.GetFileNameWithoutExtension(outputAssetPath),
                sceneRootModelId = idGenerator.Next(),
                animationStackId = idGenerator.Next(),
                animationLayerId = idGenerator.Next(),
                stopTime = sampleTimes.Count > 0 ? sampleTimes[sampleTimes.Count - 1] : 0L,
                frameRate = resolvedFrameRate,
                includeScaleCurves = includeScaleCurves,
                optimizeConstantCurves = optimizeConstantCurves,
                rootTrack = rootTrack,
                nodes = exportNodes,
                sampleTimes = sampleTimes,
                poseBuffer = poseBuffer
            };

            if (rootTrack != null)
            {
                rootTrack.translationNode = BuildCurveNode(idGenerator, "T", rootTrack.defaultLocalPosition,
                    rootTrack.trackIndex, TranslationX, FbxTranslationScale);
                rootTrack.rotationNode = BuildCurveNode(idGenerator, "R", rootTrack.defaultLocalRotation,
                    rootTrack.trackIndex, RotationX);

                if (includeScaleCurves)
                {
                    rootTrack.scaleNode = BuildCurveNode(idGenerator, "S", rootTrack.defaultLocalScale,
                        rootTrack.trackIndex, ScaleX);
                }
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                node.modelId = idGenerator.Next();
                if (node.isSkeletonNode)
                {
                    node.nodeAttributeId = idGenerator.Next();
                }

                node.translationNode = BuildCurveNode(idGenerator, "T", node.defaultLocalPosition,
                    node.trackIndex, TranslationX, FbxTranslationScale);
                node.rotationNode = BuildCurveNode(idGenerator, "R", node.defaultLocalRotation,
                    node.trackIndex, RotationX);

                if (includeScaleCurves)
                {
                    node.scaleNode = BuildCurveNode(idGenerator, "S", node.defaultLocalScale,
                        node.trackIndex, ScaleX);
                }
            }

            return document;
        }

        private static void UpdateDocument(ExportDocument document, string outputAssetPath,
            IReadOnlyList<long> sampleTimes, float frameRate)
        {
            document.sceneName = Path.GetFileNameWithoutExtension(outputAssetPath);
            document.stopTime = sampleTimes.Count > 0 ? sampleTimes[sampleTimes.Count - 1] : 0L;
            document.frameRate = frameRate;
            document.sampleTimes = sampleTimes;

            if (document.rootTrack != null)
            {
                UpdateCurveNodeDefaults(document.rootTrack.translationNode,
                    document.rootTrack.defaultLocalPosition, FbxTranslationScale);
                UpdateCurveNodeDefaults(document.rootTrack.rotationNode,
                    document.rootTrack.defaultLocalRotation, 1f);
                if (document.includeScaleCurves)
                {
                    UpdateCurveNodeDefaults(document.rootTrack.scaleNode,
                        document.rootTrack.defaultLocalScale, 1f);
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                UpdateCurveNodeDefaults(node.translationNode, node.defaultLocalPosition, FbxTranslationScale);
                UpdateCurveNodeDefaults(node.rotationNode, node.defaultLocalRotation, 1f);
                if (document.includeScaleCurves)
                {
                    UpdateCurveNodeDefaults(node.scaleNode, node.defaultLocalScale, 1f);
                }
            }
        }

        private static void UpdateCurveNodeDefaults(FbxCurveNode curveNode,
            Vector3 defaultValue, float valueScale)
        {
            Vector3 scaledValue = ScaleVector3(defaultValue, valueScale);
            curveNode.defaultValue = scaledValue;
            curveNode.xCurve.defaultValue = scaledValue.x;
            curveNode.yCurve.defaultValue = scaledValue.y;
            curveNode.zCurve.defaultValue = scaledValue.z;
        }

        private static FbxCurveNode BuildCurveNode(IdGenerator idGenerator, string label,
            Vector3 defaultValue, int trackIndex, int firstChannelIndex, float valueScale = 1f)
        {
            return new FbxCurveNode
            {
                id = idGenerator.Next(),
                label = label,
                defaultValue = ScaleVector3(defaultValue, valueScale),
                xCurve = BuildCurve(idGenerator, defaultValue.x, trackIndex, firstChannelIndex, valueScale),
                yCurve = BuildCurve(idGenerator, defaultValue.y, trackIndex, firstChannelIndex + 1, valueScale),
                zCurve = BuildCurve(idGenerator, defaultValue.z, trackIndex, firstChannelIndex + 2, valueScale)
            };
        }

        private static FbxCurve BuildCurve(IdGenerator idGenerator, float defaultValue,
            int trackIndex, int channelIndex, float valueScale)
        {
            return new FbxCurve
            {
                id = idGenerator.Next(),
                defaultValue = ScaleFloat(defaultValue, valueScale),
                trackIndex = trackIndex,
                channelIndex = channelIndex,
                valueScale = valueScale
            };
        }

        private static List<float> BuildSampleTimes(float startTime, float endTime, float sampleRate,
            IReadOnlyList<ExportNode> exportNodes, RootMotionCurves rootMotionCurves)
        {
            float duration = Mathf.Max(0f, endTime - startTime);
            if (duration <= Mathf.Epsilon)
            {
                return new List<float> { 0f };
            }

            var absoluteTimes = new List<float>(256);
            var curveAnchors = new List<float>(64);
            AddUniformSampleTimes(startTime, endTime, sampleRate, absoluteTimes);
            AddRootMotionSampleTimes(rootMotionCurves, startTime, endTime, absoluteTimes, curveAnchors);
            AddNodeCurveSampleTimes(exportNodes, startTime, endTime, absoluteTimes, curveAnchors);

            absoluteTimes.Sort();

            var result = new List<float>(absoluteTimes.Count);
            float lastAbsoluteTime = float.NegativeInfinity;
            for (int i = 0; i < absoluteTimes.Count; i++)
            {
                float absoluteTime = Mathf.Clamp(absoluteTimes[i], startTime, endTime);
                if (result.Count > 0 && Mathf.Abs(absoluteTime - lastAbsoluteTime) <= CurveEpsilon)
                {
                    continue;
                }

                result.Add(Mathf.Max(0f, absoluteTime - startTime));
                lastAbsoluteTime = absoluteTime;
            }

            if (result.Count == 0 || result[0] > CurveEpsilon)
            {
                result.Insert(0, 0f);
            }

            if (Mathf.Abs(result[result.Count - 1] - duration) > CurveEpsilon)
            {
                result.Add(duration);
            }

            return result;
        }

        private static void AddUniformSampleTimes(float startTime, float endTime, float sampleRate,
            ICollection<float> absoluteTimes)
        {
            if (absoluteTimes == null)
            {
                return;
            }

            float duration = Mathf.Max(0f, endTime - startTime);
            if (duration <= Mathf.Epsilon)
            {
                absoluteTimes.Add(startTime);
                return;
            }

            float delta = 1f / Mathf.Max(sampleRate, MinSampleRate);
            float time = startTime;
            while (time < endTime)
            {
                absoluteTimes.Add(time);
                time += delta;
            }

            absoluteTimes.Add(endTime);
        }

        private static void AddRootMotionSampleTimes(RootMotionCurves rootMotionCurves, float startTime, float endTime,
            ICollection<float> absoluteTimes, List<float> curveAnchors)
        {
            if (rootMotionCurves == null)
            {
                return;
            }

            AddCurveGroupSampleTimes(rootMotionCurves.positionCurves, startTime, endTime, PositionCurveTolerance,
                absoluteTimes, curveAnchors);
            AddCurveGroupSampleTimes(rootMotionCurves.rotationCurves, startTime, endTime, RotationCurveTolerance,
                absoluteTimes, curveAnchors);
        }

        private static void AddNodeCurveSampleTimes(IReadOnlyList<ExportNode> exportNodes, float startTime, float endTime,
            ICollection<float> absoluteTimes, List<float> curveAnchors)
        {
            if (exportNodes == null)
            {
                return;
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                AddCurveSampleTimes(node.sourcePositionX, startTime, endTime, PositionCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourcePositionY, startTime, endTime, PositionCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourcePositionZ, startTime, endTime, PositionCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceEulerX, startTime, endTime, RotationCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceEulerY, startTime, endTime, RotationCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceEulerZ, startTime, endTime, RotationCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceScaleX, startTime, endTime, ScaleCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceScaleY, startTime, endTime, ScaleCurveTolerance,
                    absoluteTimes, curveAnchors);
                AddCurveSampleTimes(node.sourceScaleZ, startTime, endTime, ScaleCurveTolerance,
                    absoluteTimes, curveAnchors);
            }
        }

        private static void AddCurveGroupSampleTimes(AnimationCurve[] curves, float startTime, float endTime,
            float tolerance, ICollection<float> absoluteTimes, List<float> curveAnchors)
        {
            if (curves == null)
            {
                return;
            }

            for (int i = 0; i < curves.Length; i++)
            {
                AddCurveSampleTimes(curves[i], startTime, endTime, tolerance, absoluteTimes, curveAnchors);
            }
        }

        private static void AddCurveSampleTimes(AnimationCurve curve, float startTime, float endTime, float tolerance,
            ICollection<float> absoluteTimes, List<float> anchors)
        {
            if (curve == null || curve.length == 0 || absoluteTimes == null || anchors == null)
            {
                return;
            }

            anchors.Clear();
            anchors.Add(startTime);
            anchors.Add(endTime);

            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                float keyTime = keys[i].time;
                if (keyTime < startTime - CurveEpsilon || keyTime > endTime + CurveEpsilon)
                {
                    continue;
                }

                anchors.Add(Mathf.Clamp(keyTime, startTime, endTime));
            }

            anchors.Sort();

            float previousAnchor = float.NaN;
            for (int i = 0; i < anchors.Count; i++)
            {
                float anchor = anchors[i];
                if (i > 0 && Mathf.Abs(anchor - previousAnchor) <= CurveEpsilon)
                {
                    continue;
                }

                absoluteTimes.Add(anchor);
                if (i > 0 && anchor - previousAnchor > CurveEpsilon)
                {
                    AddAdaptiveCurveSampleTimes(curve, previousAnchor, anchor, tolerance, 0, absoluteTimes);
                }

                previousAnchor = anchor;
            }
        }

        private static void AddAdaptiveCurveSampleTimes(AnimationCurve curve, float startTime, float endTime,
            float tolerance, int depth, ICollection<float> absoluteTimes)
        {
            if (curve == null || absoluteTimes == null || depth >= MaxAdaptiveCurveDepth)
            {
                return;
            }

            if (endTime - startTime <= CurveEpsilon ||
                !NeedsAdaptiveSubdivision(curve, startTime, endTime, tolerance))
            {
                return;
            }

            float midTime = (startTime + endTime) * 0.5f;
            absoluteTimes.Add(midTime);
            AddAdaptiveCurveSampleTimes(curve, startTime, midTime, tolerance, depth + 1, absoluteTimes);
            AddAdaptiveCurveSampleTimes(curve, midTime, endTime, tolerance, depth + 1, absoluteTimes);
        }

        private static bool NeedsAdaptiveSubdivision(AnimationCurve curve, float startTime, float endTime,
            float tolerance)
        {
            float startValue = curve.Evaluate(startTime);
            float endValue = curve.Evaluate(endTime);

            const int probeCount = 3;
            for (int i = 1; i <= probeCount; i++)
            {
                float alpha = i / (probeCount + 1f);
                float probeTime = Mathf.Lerp(startTime, endTime, alpha);
                float expectedValue = Mathf.Lerp(startValue, endValue, alpha);
                float actualValue = curve.Evaluate(probeTime);
                if (Mathf.Abs(actualValue - expectedValue) > tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CaptureSamples(AnimationClip clip, GameObject exportInstance, ExportRootTrack rootTrack,
            RootMotionCurves rootMotionCurves, List<ExportNode> exportNodes, IReadOnlyList<float> sampleTimes, float startTime,
            PoseBuffer poseBuffer, out string error)
        {
            error = string.Empty;
            int sampleCount = sampleTimes.Count;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float relativeTime = sampleTimes[sampleIndex];
                float clipTime = startTime + relativeTime;
                if (clip != null)
                {
                    clipTime = Mathf.Clamp(clipTime, 0f, clip.length);
                }

                if (EditorUtility.DisplayCancelableProgressBar("ASCII FBX Exporter",
                        $"Sampling {clip.name} ({sampleIndex + 1}/{sampleCount})",
                        sampleCount <= 1 ? 1f : sampleIndex / (float)(sampleCount - 1)))
                {
                    error = "Export cancelled.";
                    return false;
                }

                clip.SampleAnimation(exportInstance, clipTime);
                ResolveRootPose(exportInstance.transform, rootMotionCurves, clipTime, out Vector3 rootPosition,
                    out Quaternion rootRotation, out Vector3 rootScale);
                CaptureCurrentPose(exportInstance, rootTrack, exportNodes, rootPosition, rootRotation, rootScale,
                    sampleIndex, null, rootMotionCurves != null && rootMotionCurves.HasAny, poseBuffer);
            }

            return true;
        }

        private static void CaptureCurrentPose(GameObject exportInstance, ExportRootTrack rootTrack,
            IReadOnlyList<ExportNode> exportNodes, Vector3 rootPosition, Quaternion rootRotation,
            Vector3 rootScale, int sampleIndex, ISet<Transform> animatedTransforms, bool useRootMotion,
            PoseBuffer poseBuffer)
        {
            if (rootTrack != null)
            {
                AddRootTrackSample(rootTrack, rootPosition, rootRotation, rootScale, sampleIndex, poseBuffer);
            }

            for (int nodeIndex = 0; nodeIndex < exportNodes.Count; nodeIndex++)
            {
                ExportNode node = exportNodes[nodeIndex];
                Transform transform = node.transform;
                Vector3 unityFallbackPosition = sampleIndex == 0
                    ? node.defaultLocalPosition
                    : ConvertFbxLocalPositionToUnity(node.defaultLocalPosition);

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;

                if (transform == exportInstance.transform && useRootMotion)
                {
                    position = SanitizeVector3(rootPosition, unityFallbackPosition);
                    rotation = rootRotation;
                    scale = SanitizeVector3(rootScale, node.defaultLocalScale);
                }
                else if (animatedTransforms == null || animatedTransforms.Contains(transform))
                {
                    position = SanitizeVector3(transform.localPosition, unityFallbackPosition);
                    rotation = transform.localRotation;
                    scale = SanitizeVector3(transform.localScale, node.defaultLocalScale);
                }
                else
                {
                    position = SanitizeVector3(node.baselineLocalPosition, unityFallbackPosition);
                    rotation = node.baselineLocalRotation;
                    scale = SanitizeVector3(node.baselineLocalScale, node.defaultLocalScale);
                }

                if (rootTrack != null && node.parentIndex < 0 && node.isSkeletonNode)
                {
                    position = rootPosition + rootRotation * Vector3.Scale(rootScale, position);
                    rotation = rootRotation * rotation;
                    scale = Vector3.Scale(rootScale, scale);
                }

                position = ConvertUnityLocalPositionToFbx(position);
                rotation = ConvertUnityLocalRotationToFbx(rotation);

                if (sampleIndex == 0)
                {
                    node.defaultLocalPosition = position;
                    node.defaultLocalRotation = ToSignedEuler(rotation.eulerAngles);
                    node.defaultLocalScale = scale;
                }

                Vector3 rawEuler = ToSignedEuler(rotation.eulerAngles);
                if (node.hasRotationSample)
                {
                    rawEuler = UnwrapEuler(node.lastSampledEuler, rawEuler);
                }
                else
                {
                    node.hasRotationSample = true;
                }

                node.lastSampledEuler = rawEuler;
                poseBuffer.Set(node.trackIndex, TranslationX, sampleIndex, position.x);
                poseBuffer.Set(node.trackIndex, TranslationY, sampleIndex, position.y);
                poseBuffer.Set(node.trackIndex, TranslationZ, sampleIndex, position.z);
                poseBuffer.Set(node.trackIndex, RotationX, sampleIndex, rawEuler.x);
                poseBuffer.Set(node.trackIndex, RotationY, sampleIndex, rawEuler.y);
                poseBuffer.Set(node.trackIndex, RotationZ, sampleIndex, rawEuler.z);
                poseBuffer.Set(node.trackIndex, ScaleX, sampleIndex, scale.x);
                poseBuffer.Set(node.trackIndex, ScaleY, sampleIndex, scale.y);
                poseBuffer.Set(node.trackIndex, ScaleZ, sampleIndex, scale.z);
            }
        }

        private static void AssignTrackIndices(ExportRootTrack rootTrack, IReadOnlyList<ExportNode> nodes)
        {
            int trackIndex = 0;
            if (rootTrack != null)
            {
                rootTrack.trackIndex = trackIndex++;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].trackIndex = trackIndex++;
            }
        }

        private static void AddRootTrackSample(ExportRootTrack rootTrack, Vector3 position, Quaternion rotation,
            Vector3 scale, int sampleIndex, PoseBuffer poseBuffer)
        {
            Vector3 unityFallbackPosition = sampleIndex == 0
                ? rootTrack.defaultLocalPosition
                : ConvertFbxLocalPositionToUnity(rootTrack.defaultLocalPosition);

            position = ConvertUnityLocalPositionToFbx(SanitizeVector3(position, unityFallbackPosition));
            rotation = ConvertUnityLocalRotationToFbx(rotation);
            scale = SanitizeVector3(scale, rootTrack.defaultLocalScale);

            Vector3 rawEuler = ToSignedEuler(rotation.eulerAngles);
            if (sampleIndex == 0)
            {
                rootTrack.defaultLocalPosition = position;
                rootTrack.defaultLocalRotation = rawEuler;
                rootTrack.defaultLocalScale = scale;
            }

            if (rootTrack.hasRotationSample)
            {
                rawEuler = UnwrapEuler(rootTrack.lastSampledEuler, rawEuler);
            }
            else
            {
                rootTrack.hasRotationSample = true;
            }

            rootTrack.lastSampledEuler = rawEuler;

            poseBuffer.Set(rootTrack.trackIndex, TranslationX, sampleIndex, position.x);
            poseBuffer.Set(rootTrack.trackIndex, TranslationY, sampleIndex, position.y);
            poseBuffer.Set(rootTrack.trackIndex, TranslationZ, sampleIndex, position.z);
            poseBuffer.Set(rootTrack.trackIndex, RotationX, sampleIndex, rawEuler.x);
            poseBuffer.Set(rootTrack.trackIndex, RotationY, sampleIndex, rawEuler.y);
            poseBuffer.Set(rootTrack.trackIndex, RotationZ, sampleIndex, rawEuler.z);
            poseBuffer.Set(rootTrack.trackIndex, ScaleX, sampleIndex, scale.x);
            poseBuffer.Set(rootTrack.trackIndex, ScaleY, sampleIndex, scale.y);
            poseBuffer.Set(rootTrack.trackIndex, ScaleZ, sampleIndex, scale.z);
        }

        private static ExportRootTrack CreateRootTrack(Transform rootTransform)
        {
            return new ExportRootTrack
            {
                exportName = rootTransform != null && !string.IsNullOrEmpty(rootTransform.name)
                    ? rootTransform.name
                    : "SceneRoot",
                defaultLocalPosition = rootTransform.localPosition,
                defaultLocalRotation = ToSignedEuler(rootTransform.localRotation.eulerAngles),
                defaultLocalScale = rootTransform.localScale
            };
        }

        private static void CollectExportNodes(Transform transform, Transform clipRoot, int parentIndex,
            ICollection<ExportNode> exportNodes, HashSet<Transform> skeletonTransforms,
            ISet<string> excludedTransformPaths = null)
        {
            if (transform == null)
            {
                return;
            }

            string sourcePath = clipRoot != null
                ? AnimationUtility.CalculateTransformPath(transform, clipRoot)
                : string.Empty;
            if (excludedTransformPaths != null && excludedTransformPaths.Contains(sourcePath))
            {
                return;
            }

            string exportName = string.IsNullOrEmpty(transform.name) ? "Node" : transform.name;

            var node = new ExportNode
            {
                parentIndex = parentIndex,
                exportName = exportName,
                sourcePath = sourcePath,
                transform = transform,
                isSkeletonNode = skeletonTransforms == null || skeletonTransforms.Count == 0 ||
                                 skeletonTransforms.Contains(transform),
                defaultLocalPosition = transform.localPosition,
                defaultLocalRotation = ToSignedEuler(transform.localRotation.eulerAngles),
                defaultLocalScale = transform.localScale,
                baselineLocalPosition = transform.localPosition,
                baselineLocalRotation = transform.localRotation,
                baselineLocalScale = transform.localScale
            };

            int nodeIndex = exportNodes.Count;
            exportNodes.Add(node);

            for (int i = 0; i < transform.childCount; i++)
            {
                CollectExportNodes(transform.GetChild(i), clipRoot, nodeIndex, exportNodes, skeletonTransforms,
                    excludedTransformPaths);
            }
        }

        private static HashSet<Transform> BuildSkeletonTransformSet(GameObject root)
        {
            var result = new HashSet<Transform>();
            if (root == null)
            {
                return result;
            }

            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.rootBone != null)
                {
                    AddTransformTree(GetTopmostChildUnder(renderer.rootBone, root.transform), result);
                    continue;
                }

                Transform[] bones = renderer.bones;
                if (bones == null)
                {
                    continue;
                }

                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    AddTransformAndAncestors(bones[boneIndex], root.transform, result);
                }
            }

            if (result.Count == 0)
            {
                Transform fallbackRoot = FindDirectChild(root.transform, "root");
                if (fallbackRoot != null)
                {
                    AddTransformTree(fallbackRoot, result);
                }
            }

            return result;
        }

        private static void AddTransformTree(Transform transform, ISet<Transform> result)
        {
            if (transform == null || result == null || result.Contains(transform))
            {
                return;
            }

            result.Add(transform);
            for (int i = 0; i < transform.childCount; i++)
            {
                AddTransformTree(transform.GetChild(i), result);
            }
        }

        private static void AddTransformAndAncestors(Transform transform, Transform stopExclusive,
            ISet<Transform> result)
        {
            while (transform != null && transform != stopExclusive)
            {
                result.Add(transform);
                transform = transform.parent;
            }
        }

        private static Transform GetTopmostChildUnder(Transform transform, Transform stopExclusive)
        {
            if (transform == null)
            {
                return null;
            }

            Transform current = transform;
            while (current.parent != null && current.parent != stopExclusive)
            {
                current = current.parent;
            }

            return current;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static void ApplyExportRootDefaults(Transform exportRoot, List<ExportNode> exportNodes,
            RootMotionCurves rootMotionCurves, float clipStartTime)
        {
            if (exportRoot == null || exportNodes == null || exportNodes.Count == 0)
            {
                return;
            }

            ExportNode rootNode = exportNodes[0];
            if (rootNode.transform != exportRoot)
            {
                return;
            }

            ResolveRootPose(exportRoot, rootMotionCurves, clipStartTime, out Vector3 position, out _,
                out Vector3 scale);
            rootNode.defaultLocalPosition = SanitizeVector3(position, rootNode.defaultLocalPosition);
            rootNode.defaultLocalScale = SanitizeVector3(scale, rootNode.defaultLocalScale);
        }

        private static RootMotionCurves ExtractRootMotionCurves(AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            AnimationCurve[] positionCurves = TryGetEditorCurveGroup(clip, AnimatorRootPositionPropertyNames);
            AnimationCurve[] rotationCurves = TryGetEditorCurveGroup(clip, AnimatorRootRotationPropertyNames);
            if (positionCurves == null && rotationCurves == null)
            {
                return null;
            }

            return new RootMotionCurves
            {
                positionCurves = positionCurves,
                rotationCurves = rotationCurves
            };
        }

        private static AnimationCurve[] TryGetEditorCurveGroup(AnimationClip clip, string[] propertyNames)
        {
            if (clip == null || propertyNames == null || propertyNames.Length == 0)
            {
                return null;
            }

            var curves = new AnimationCurve[propertyNames.Length];
            for (int i = 0; i < propertyNames.Length; i++)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), propertyNames[i]);
                curves[i] = AnimationUtility.GetEditorCurve(clip, binding);
                if (curves[i] == null)
                {
                    return null;
                }
            }

            return curves;
        }

        private static ClipCurveLookup BuildClipCurveLookup(AnimationClip clip)
        {
            var lookup = new ClipCurveLookup();
            if (clip == null)
            {
                return lookup;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                lookup.transformCurves[MakeCurveLookupKey(binding.path, binding.propertyName)] = curve;
            }

            return lookup;
        }

        private static void AttachSourceCurves(List<ExportNode> exportNodes, ClipCurveLookup clipCurveLookup)
        {
            if (exportNodes == null || clipCurveLookup == null)
            {
                return;
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                string path = node.sourcePath ?? string.Empty;

                node.sourcePositionX = FindTransformCurve(clipCurveLookup, path, "localPosition.x", "m_LocalPosition.x");
                node.sourcePositionY = FindTransformCurve(clipCurveLookup, path, "localPosition.y", "m_LocalPosition.y");
                node.sourcePositionZ = FindTransformCurve(clipCurveLookup, path, "localPosition.z", "m_LocalPosition.z");

                node.sourceScaleX = FindTransformCurve(clipCurveLookup, path, "localScale.x", "m_LocalScale.x");
                node.sourceScaleY = FindTransformCurve(clipCurveLookup, path, "localScale.y", "m_LocalScale.y");
                node.sourceScaleZ = FindTransformCurve(clipCurveLookup, path, "localScale.z", "m_LocalScale.z");

                node.sourceEulerX = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.x",
                    "localEulerAnglesBaked.x", "localEulerAngles.x");
                node.sourceEulerY = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.y",
                    "localEulerAnglesBaked.y", "localEulerAngles.y");
                node.sourceEulerZ = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.z",
                    "localEulerAnglesBaked.z", "localEulerAngles.z");
            }
        }

        private static AnimationCurve FindTransformCurve(ClipCurveLookup lookup, string path, params string[] propertyNames)
        {
            if (lookup == null || propertyNames == null)
            {
                return null;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (lookup.transformCurves.TryGetValue(MakeCurveLookupKey(path, propertyNames[i]), out AnimationCurve curve))
                {
                    return curve;
                }
            }

            return null;
        }

        private static string MakeCurveLookupKey(string path, string propertyName)
        {
            return $"{path ?? string.Empty}|{propertyName ?? string.Empty}";
        }

        private static bool ValidateOptions(ExportOptions options, out float normalizedStartTime,
            out float normalizedEndTime, out float sampleRate, out string error)
        {
            normalizedStartTime = 0f;
            normalizedEndTime = 0f;
            sampleRate = 30f;
            error = string.Empty;

            if (options == null)
            {
                error = "Export options are missing.";
                return false;
            }

            if (options.model == null)
            {
                error = "Assign a model root to export.";
                return false;
            }

            if (options.clip == null)
            {
                error = "Assign an animation clip to export.";
                return false;
            }

            if (!TryNormalizeOutputAssetPath(options.outputAssetPath, out _, out error))
            {
                return false;
            }

            sampleRate = ResolveSampleRate(options.sampleRate, options.clip);
            NormalizeClipRange(options.clip, options.startTime, options.endTime, out normalizedStartTime,
                out normalizedEndTime);
            normalizedEndTime = AlignEndTimeToFrame(normalizedStartTime, normalizedEndTime, sampleRate);

            return true;
        }

        private static bool TryNormalizeOutputAssetPath(string path, out string outputAssetPath, out string error)
        {
            outputAssetPath = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Select an output path inside Assets.";
                return false;
            }

            outputAssetPath = NormalizeAssetPath(path);
            if (!outputAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !outputAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                error = "Output path must be an FBX file inside the project's Assets folder.";
                return false;
            }

            return true;
        }

        private static void NormalizeClipRange(AnimationClip clip, float startTime, float endTime,
            out float normalizedStartTime, out float normalizedEndTime)
        {
            float clipLength = clip != null ? Mathf.Max(0f, clip.length) : 0f;
            normalizedStartTime = Mathf.Clamp(startTime, 0f, clipLength);

            float resolvedEndTime = endTime;
            if (clip != null && resolvedEndTime <= 0f)
            {
                resolvedEndTime = clip.length;
            }

            normalizedEndTime = Mathf.Clamp(resolvedEndTime, normalizedStartTime, clipLength);
        }

        private static float AlignEndTimeToFrame(float startTime, float endTime, float sampleRate)
        {
            float duration = Mathf.Max(0f, endTime - startTime);
            if (duration <= Mathf.Epsilon)
            {
                return startTime;
            }

            float frameRate = Mathf.Max(sampleRate, MinSampleRate);
            int frameCount = Mathf.Max(1, Mathf.RoundToInt(duration * frameRate));
            return startTime + frameCount / frameRate;
        }

        private static float ResolveSampleRate(float requestedSampleRate, AnimationClip clip)
        {
            float resolved = requestedSampleRate;
            if (resolved <= 0f && clip != null)
            {
                resolved = clip.frameRate;
            }

            if (resolved <= 0f)
            {
                resolved = 30f;
            }

            return Mathf.Clamp(resolved, MinSampleRate, MaxSampleRate);
        }

        private static List<long> ConvertSampleTimesToFbxTicks(IReadOnlyList<float> sampleTimes, float frameRate)
        {
            var result = new List<long>(sampleTimes.Count);
            double safeFrameRate = Math.Max(frameRate, MinSampleRate);
            for (int i = 0; i < sampleTimes.Count; i++)
            {
                double seconds = sampleTimes[i];
                double frame = seconds * safeFrameRate;
                double roundedFrame = Math.Round(frame);
                if (Math.Abs(frame - roundedFrame) <= FrameSnapEpsilon)
                {
                    seconds = roundedFrame / safeFrameRate;
                }

                result.Add(SecondsToFbxTicks(seconds));
            }

            return result;
        }

        private static long SecondsToFbxTicks(double seconds)
        {
            return (long)Math.Round(seconds * FbxTicksPerSecond, MidpointRounding.AwayFromZero);
        }

        private static Vector3 SanitizeVector3(Vector3 value, Vector3 fallback)
        {
            value.x = SanitizeFloat(value.x, fallback.x);
            value.y = SanitizeFloat(value.y, fallback.y);
            value.z = SanitizeFloat(value.z, fallback.z);
            return value;
        }

        private static Vector3 ScaleVector3(Vector3 value, float scale)
        {
            return Mathf.Approximately(scale, 1f) ? value : value * scale;
        }

        private static float ScaleFloat(float value, float scale)
        {
            return Mathf.Approximately(scale, 1f) ? value : value * scale;
        }

        private static int FloatToIntBits(float value)
        {
            return BitConverter.SingleToInt32Bits(value);
        }

        private static void ResolveRootPose(Transform rootTransform, RootMotionCurves rootMotionCurves, float clipTime,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = rootTransform != null ? rootTransform.localPosition : Vector3.zero;
            rotation = rootTransform != null ? rootTransform.localRotation : Quaternion.identity;
            scale = rootTransform != null ? rootTransform.localScale : Vector3.one;

            if (rootMotionCurves == null)
            {
                return;
            }

            if (rootMotionCurves.HasPosition)
            {
                position = new Vector3(
                    rootMotionCurves.positionCurves[0].Evaluate(clipTime),
                    rootMotionCurves.positionCurves[1].Evaluate(clipTime),
                    rootMotionCurves.positionCurves[2].Evaluate(clipTime));
            }

            if (rootMotionCurves.HasRotation)
            {
                rotation = new Quaternion(
                    rootMotionCurves.rotationCurves[0].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[1].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[2].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[3].Evaluate(clipTime));

                if (Quaternion.Dot(rotation, rotation) > CurveEpsilon)
                {
                    rotation.Normalize();
                }
                else
                {
                    rotation = Quaternion.identity;
                }
            }
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value;
        }

        private static Vector3 ToSignedEuler(Vector3 euler)
        {
            return new Vector3(ToSignedAngle(euler.x), ToSignedAngle(euler.y), ToSignedAngle(euler.z));
        }

        private static Vector3 ConvertUnityLocalPositionToFbx(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        private static Vector3 ConvertFbxLocalPositionToUnity(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        private static Quaternion ConvertUnityLocalRotationToFbx(Quaternion rotation)
        {
            Quaternion converted = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
            if (Quaternion.Dot(converted, converted) > CurveEpsilon)
            {
                converted.Normalize();
                return converted;
            }

            return Quaternion.identity;
        }

        private static Vector3 UnwrapEuler(Vector3 previous, Vector3 current)
        {
            // ZXY Euler rotations have two equivalent solutions. Keep the branch continuous with the prior sample.
            Vector3 alternate = new Vector3(180f - current.x, current.y + 180f, current.z + 180f);

            current.x = previous.x + Mathf.DeltaAngle(previous.x, current.x);
            current.y = previous.y + Mathf.DeltaAngle(previous.y, current.y);
            current.z = previous.z + Mathf.DeltaAngle(previous.z, current.z);

            alternate.x = previous.x + Mathf.DeltaAngle(previous.x, alternate.x);
            alternate.y = previous.y + Mathf.DeltaAngle(previous.y, alternate.y);
            alternate.z = previous.z + Mathf.DeltaAngle(previous.z, alternate.z);

            return (current - previous).sqrMagnitude <= (alternate - previous).sqrMagnitude ? current : alternate;
        }

        private static float ToSignedAngle(float angle)
        {
            while (angle > 180f)
            {
                angle -= 360f;
            }

            while (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/').Trim();
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), NormalizeAssetPath(assetPath)));
        }

        private static void WriteAsciiDocument(string absoluteOutputPath, ExportDocument document)
        {
            string outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException("Unable to resolve the FBX output directory.");
            }

            Directory.CreateDirectory(outputDirectory);
            string tempDirectory = Path.Combine(Directory.GetCurrentDirectory(),
                "Library", "KINEMATION", "FbxExporter");
            Directory.CreateDirectory(tempDirectory);
            string tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");

            try
            {
                using (var builder = new FbxAsciiWriter(tempPath))
                {
                    AppendHeader(builder);
                    AppendGlobalSettings(builder, document.stopTime, document.frameRate);
                    AppendDocuments(builder);
                    AppendReferences(builder);
                    AppendDefinitions(builder, document);
                    AppendObjects(builder, document);
                    AppendConnections(builder, document);
                    AppendTakes(builder, document);
                }

                if (File.Exists(absoluteOutputPath))
                {
                    File.Replace(tempPath, absoluteOutputPath, null);
                }
                else
                {
                    File.Move(tempPath, absoluteOutputPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static void AppendHeader(FbxAsciiWriter builder)
        {
            DateTime now = DateTime.Now;

            builder.AppendLine("; FBX 7.7.0 project file");
            builder.AppendLine("; ----------------------------------------------------");
            builder.AppendLine($"; ExporterVersion: {ExporterVersionTag}");
            builder.AppendLine();
            builder.AppendLine("FBXHeaderExtension:  {");
            builder.AppendLine("\tFBXHeaderVersion: 1003");
            builder.AppendLine("\tFBXVersion: 7700");
            builder.AppendLine("\tCreationTimeStamp:  {");
            builder.AppendLine("\t\tVersion: 1000");
            builder.AppendLine($"\t\tYear: {now.Year}");
            builder.AppendLine($"\t\tMonth: {now.Month}");
            builder.AppendLine($"\t\tDay: {now.Day}");
            builder.AppendLine($"\t\tHour: {now.Hour}");
            builder.AppendLine($"\t\tMinute: {now.Minute}");
            builder.AppendLine($"\t\tSecond: {now.Second}");
            builder.AppendLine($"\t\tMillisecond: {now.Millisecond}");
            builder.AppendLine("\t}");
            builder.AppendLine($"\tCreator: \"{ExporterDisplayName} {ExporterVersionTag}\"");
            builder.AppendLine("}");
        }

        private static void AppendGlobalSettings(FbxAsciiWriter builder, long stopTime, float frameRate)
        {
            frameRate = Mathf.Clamp(frameRate, MinSampleRate, MaxSampleRate);
            int FbxCustomTimeMode = Mathf.Approximately(frameRate, 60f) ? Fbx60FpsTimeMode :
                FbxFallbackTimeMode;

            builder.AppendLine("GlobalSettings:  {");
            builder.AppendLine("\tVersion: 1000");
            builder.AppendLine("\tProperties70:  {");
            builder.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            builder.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            builder.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",-1");
            builder.AppendLine("\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine($"\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{FormatFloat(FbxFileUnitScaleFactor)}");
            builder.AppendLine($"\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",{FormatFloat(FbxFileUnitScaleFactor)}");
            builder.AppendLine("\t\tP: \"AmbientColor\", \"ColorRGB\", \"Color\", \"\",0,0,0");
            builder.AppendLine("\t\tP: \"DefaultCamera\", \"KString\", \"\", \"\", \"Producer Perspective\"");
            builder.AppendLine($"\t\tP: \"TimeMode\", \"enum\", \"\", \"\",{FbxCustomTimeMode}");
            builder.AppendLine("\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine($"\t\tP: \"CustomFrameRate\", \"double\", \"Number\", \"\",{FormatFloat(frameRate)}");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendDocuments(FbxAsciiWriter builder)
        {
            builder.AppendLine("Documents:  {");
            builder.AppendLine("\tCount: 1");
            builder.AppendLine("\tDocument: 1, \"Scene\", \"Scene\" {");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
            builder.AppendLine("\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tRootNode: 0");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendReferences(FbxAsciiWriter builder)
        {
            builder.AppendLine("References:  {");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static int CountSkeletonNodes(IReadOnlyList<ExportNode> nodes)
        {
            if (nodes == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].isSkeletonNode)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsSkeletonRoot(IReadOnlyList<ExportNode> nodes, int nodeIndex)
        {
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count || !nodes[nodeIndex].isSkeletonNode)
            {
                return false;
            }

            int parentIndex = nodes[nodeIndex].parentIndex;
            return parentIndex < 0 || parentIndex >= nodes.Count || !nodes[parentIndex].isSkeletonNode;
        }

        private static string GetModelType(IReadOnlyList<ExportNode> nodes, int nodeIndex)
        {
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count || !nodes[nodeIndex].isSkeletonNode)
            {
                return "Null";
            }

            return IsSkeletonRoot(nodes, nodeIndex) ? "Root" : "LimbNode";
        }

        private static void AppendDefinitions(FbxAsciiWriter builder, ExportDocument document)
        {
            int modelCount = document.nodes.Count + (document.rootTrack != null ? 1 : 0);
            int skeletonNodeCount = CountSkeletonNodes(document.nodes);
            int rootCurveNodeCount = document.rootTrack != null ? (document.includeScaleCurves ? 3 : 2) : 0;
            int rootCurveCount = document.rootTrack != null ? (document.includeScaleCurves ? 9 : 6) : 0;
            int curveNodeCount = rootCurveNodeCount + document.nodes.Count * (document.includeScaleCurves ? 3 : 2);
            int curveCount = rootCurveCount + document.nodes.Count * (document.includeScaleCurves ? 9 : 6);
            int totalCount = 1 + modelCount + (skeletonNodeCount > 0 ? 1 : 0) + 1 + 1 + curveNodeCount + curveCount;

            builder.AppendLine("Definitions:  {");
            builder.AppendLine("\tVersion: 100");
            builder.AppendLine($"\tCount: {totalCount}");
            builder.AppendLine("\tObjectType: \"GlobalSettings\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"Model\" {");
            builder.AppendLine($"\t\tCount: {modelCount}");
            builder.AppendLine("\t\tPropertyTemplate: \"FbxNode\" {");
            builder.AppendLine("\t\t\tProperties70:  {");
            builder.AppendLine("\t\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
            builder.AppendLine("\t\t\t\tP: \"Visibility\", \"Visibility\", \"\", \"A\",1");
            builder.AppendLine("\t\t\t}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
            if (skeletonNodeCount > 0)
            {
                builder.AppendLine("\tObjectType: \"NodeAttribute\" {");
                builder.AppendLine($"\t\tCount: {skeletonNodeCount}");
                builder.AppendLine("\t\tPropertyTemplate: \"FbxSkeleton\" {");
                builder.AppendLine("\t\t\tProperties70:  {");
                builder.AppendLine("\t\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\",0.8,0.8,0.8");
                builder.AppendLine("\t\t\t\tP: \"Size\", \"double\", \"Number\", \"\",100");
                builder.AppendLine("\t\t\t\tP: \"LimbLength\", \"double\", \"Number\", \"H\",1");
                builder.AppendLine("\t\t\t}");
                builder.AppendLine("\t\t}");
                builder.AppendLine("\t}");
            }
            builder.AppendLine("\tObjectType: \"AnimationStack\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationLayer\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationCurveNode\" {");
            builder.AppendLine($"\t\tCount: {curveNodeCount}");
            builder.AppendLine("\t\tPropertyTemplate: \"FbxAnimCurveNode\" {");
            builder.AppendLine("\t\t\tProperties70:  {");
            builder.AppendLine("\t\t\t\tP: \"d\", \"Compound\", \"\", \"\"");
            builder.AppendLine("\t\t\t}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationCurve\" {");
            builder.AppendLine($"\t\tCount: {curveCount}");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendObjects(FbxAsciiWriter builder, ExportDocument document)
        {
            builder.AppendLine("Objects:  {");

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                if (node.isSkeletonNode)
                {
                    AppendNodeAttribute(builder, node, IsSkeletonRoot(document.nodes, i));
                }
            }

            if (document.rootTrack != null)
            {
                AppendSceneRootModel(builder, document.sceneRootModelId, document.sceneName, document.rootTrack);
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                AppendModel(builder, node, GetModelType(document.nodes, i));
            }

            AppendAnimationStack(builder, document.animationStackId, document.sceneName, document.stopTime);
            AppendAnimationLayer(builder, document.animationLayerId);

            if (document.rootTrack != null)
            {
                AppendCurveNode(builder, document.rootTrack.translationNode);
                AppendCurveNode(builder, document.rootTrack.rotationNode);
                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    AppendCurveNode(builder, document.rootTrack.scaleNode);
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                AppendCurveNode(builder, node.translationNode);
                AppendCurveNode(builder, node.rotationNode);
                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    AppendCurveNode(builder, node.scaleNode);
                }
            }

            if (document.rootTrack != null)
            {
                AppendCurve(builder, document, document.rootTrack.translationNode.xCurve);
                AppendCurve(builder, document, document.rootTrack.translationNode.yCurve);
                AppendCurve(builder, document, document.rootTrack.translationNode.zCurve);
                AppendCurve(builder, document, document.rootTrack.rotationNode.xCurve);
                AppendCurve(builder, document, document.rootTrack.rotationNode.yCurve);
                AppendCurve(builder, document, document.rootTrack.rotationNode.zCurve);

                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    AppendCurve(builder, document, document.rootTrack.scaleNode.xCurve);
                    AppendCurve(builder, document, document.rootTrack.scaleNode.yCurve);
                    AppendCurve(builder, document, document.rootTrack.scaleNode.zCurve);
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                AppendCurve(builder, document, node.translationNode.xCurve);
                AppendCurve(builder, document, node.translationNode.yCurve);
                AppendCurve(builder, document, node.translationNode.zCurve);
                AppendCurve(builder, document, node.rotationNode.xCurve);
                AppendCurve(builder, document, node.rotationNode.yCurve);
                AppendCurve(builder, document, node.rotationNode.zCurve);

                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    AppendCurve(builder, document, node.scaleNode.xCurve);
                    AppendCurve(builder, document, node.scaleNode.yCurve);
                    AppendCurve(builder, document, node.scaleNode.zCurve);
                }
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendSceneRootModel(FbxAsciiWriter builder, long modelId, string sceneName,
            ExportRootTrack rootTrack)
        {
            string modelName = rootTrack != null && !string.IsNullOrEmpty(rootTrack.exportName)
                ? rootTrack.exportName
                : sceneName;
            builder.AppendLine($"\tModel: {modelId}, \"Model::{EscapeFbxString(modelName)}\", \"Null\" {{");
            builder.AppendLine("\t\tVersion: 232");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            Vector3 translation = rootTrack != null ? rootTrack.defaultLocalPosition : Vector3.zero;
            Vector3 rotation = rootTrack != null ? rootTrack.defaultLocalRotation : Vector3.zero;
            Vector3 scale = rootTrack != null ? rootTrack.defaultLocalScale : Vector3.one;
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A+\",{FormatVector3(ScaleVector3(translation, FbxTranslationScale))}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A+\",{FormatVector3(rotation)}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A+\",{FormatVector3(scale)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tShading: Y");
            builder.AppendLine("\t\tCulling: \"CullingOff\"");
            builder.AppendLine("\t}");
        }

        private static void AppendNodeAttribute(FbxAsciiWriter builder, ExportNode node, bool isRoot)
        {
            string attributeType = isRoot ? "Root" : "LimbNode";
            builder.AppendLine(
                $"\tNodeAttribute: {node.nodeAttributeId}, \"NodeAttribute::{EscapeFbxString(node.exportName)}_Skel\", \"{attributeType}\" {{");
            builder.AppendLine(isRoot
                ? "\t\tTypeFlags: \"Null\", \"Skeleton\", \"Root\""
                : "\t\tTypeFlags: \"Skeleton\"");
            builder.AppendLine("\t}");
        }

        private static void AppendModel(FbxAsciiWriter builder, ExportNode node, string modelType)
        {
            builder.AppendLine(
                $"\tModel: {node.modelId}, \"Model::{EscapeFbxString(node.exportName)}\", \"{modelType}\" {{");
            builder.AppendLine("\t\tVersion: 232");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            builder.AppendLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A+\",{FormatVector3(ScaleVector3(node.defaultLocalPosition, FbxTranslationScale))}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A+\",{FormatVector3(node.defaultLocalRotation)}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A+\",{FormatVector3(node.defaultLocalScale)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tShading: Y");
            builder.AppendLine("\t\tCulling: \"CullingOff\"");
            builder.AppendLine("\t}");
        }

        private static void AppendAnimationStack(FbxAsciiWriter builder, long animationStackId, string sceneName,
            long stopTime)
        {
            builder.AppendLine(
                $"\tAnimationStack: {animationStackId}, \"AnimStack::{EscapeFbxString(sceneName)}\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine(
                $"\t\t\tP: \"Description\", \"KString\", \"\", \"\", \"Animation Take: {EscapeFbxString(sceneName)}\"");
            builder.AppendLine("\t\t\tP: \"LocalStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\t\tP: \"LocalStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine("\t\t\tP: \"ReferenceStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\t\tP: \"ReferenceStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendAnimationLayer(FbxAsciiWriter builder, long animationLayerId)
        {
            builder.AppendLine($"\tAnimationLayer: {animationLayerId}, \"AnimLayer::Animation Base Layer\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"Weight\", \"Number\", \"\", \"A\",100");
            builder.AppendLine("\t\t\tP: \"Mute\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"Solo\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"Lock\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"BlendMode\", \"enum\", \"\", \"\",0");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendCurveNode(FbxAsciiWriter builder, FbxCurveNode curveNode)
        {
            builder.AppendLine(
                $"\tAnimationCurveNode: {curveNode.id}, \"AnimCurveNode::{curveNode.label}\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine($"\t\t\tP: \"d|X\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.x)}");
            builder.AppendLine($"\t\t\tP: \"d|Y\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.y)}");
            builder.AppendLine($"\t\t\tP: \"d|Z\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.z)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendCurve(FbxAsciiWriter builder, ExportDocument document, FbxCurve curve)
        {
            bool useConstantEndpoints = document.optimizeConstantCurves &&
                                        IsConstantCurve(document.poseBuffer, curve);
            int keyCount = GetCurveKeyCount(document.sampleTimes, useConstantEndpoints);
            int keyAttrFlagsCount = keyCount;
            int keyAttrDataCount = keyCount * 4;
            int keyAttrRefCount = keyCount;
            builder.AppendLine($"\tAnimationCurve: {curve.id}, \"AnimCurve::\", \"\" {{");
            builder.AppendLine($"\t\tDefault: {FormatFloat(curve.defaultValue)}");
            builder.AppendLine("\t\tKeyVer: 4009");

            builder.AppendLine($"\t\tKeyTime: *{keyCount} {{");
            builder.Append("\t\t\ta: ");
            AppendCurveTimes(builder, document.sampleTimes, keyCount, useConstantEndpoints);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyValueFloat: *{keyCount} {{");
            builder.Append("\t\t\ta: ");
            AppendCurveValues(builder, document, curve, keyCount, useConstantEndpoints);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrFlags: *{keyAttrFlagsCount} {{");
            builder.Append("\t\t\ta: ");
            AppendRepeated(builder, WeightedUserKeyAttrFlag, keyCount);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrDataFloat: *{keyAttrDataCount} {{");
            builder.Append("\t\t\ta: ");
            AppendCurveTangentData(builder, document, curve, keyCount, useConstantEndpoints);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrRefCount: *{keyAttrRefCount} {{");
            builder.Append("\t\t\ta: ");
            AppendRepeated(builder, 1, keyCount);
            builder.AppendLine();
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendConnections(FbxAsciiWriter builder, ExportDocument document)
        {
            builder.AppendLine("Connections:  {");
            builder.AppendLine($"\tC: \"OO\",{document.animationLayerId},{document.animationStackId}");

            if (document.rootTrack != null)
            {
                builder.AppendLine($"\tC: \"OO\",{document.sceneRootModelId},0");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.id},{document.sceneRootModelId},\"Lcl Translation\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.id},{document.sceneRootModelId},\"Lcl Rotation\"");
                builder.AppendLine($"\tC: \"OO\",{document.rootTrack.translationNode.id},{document.animationLayerId}");
                builder.AppendLine($"\tC: \"OO\",{document.rootTrack.rotationNode.id},{document.animationLayerId}");

                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.xCurve.id},{document.rootTrack.translationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.yCurve.id},{document.rootTrack.translationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.zCurve.id},{document.rootTrack.translationNode.id},\"d|Z\"");

                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.xCurve.id},{document.rootTrack.rotationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.yCurve.id},{document.rootTrack.rotationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.zCurve.id},{document.rootTrack.rotationNode.id},\"d|Z\"");

                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.id},{document.sceneRootModelId},\"Lcl Scaling\"");
                    builder.AppendLine($"\tC: \"OO\",{document.rootTrack.scaleNode.id},{document.animationLayerId}");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.xCurve.id},{document.rootTrack.scaleNode.id},\"d|X\"");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.yCurve.id},{document.rootTrack.scaleNode.id},\"d|Y\"");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.zCurve.id},{document.rootTrack.scaleNode.id},\"d|Z\"");
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                long parentModelId = node.parentIndex >= 0
                    ? document.nodes[node.parentIndex].modelId
                    : 0;

                builder.AppendLine($"\tC: \"OO\",{node.modelId},{parentModelId}");
                if (node.isSkeletonNode)
                {
                    builder.AppendLine($"\tC: \"OO\",{node.nodeAttributeId},{node.modelId}");
                }

                builder.AppendLine($"\tC: \"OP\",{node.translationNode.id},{node.modelId},\"Lcl Translation\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.id},{node.modelId},\"Lcl Rotation\"");
                builder.AppendLine($"\tC: \"OO\",{node.translationNode.id},{document.animationLayerId}");
                builder.AppendLine($"\tC: \"OO\",{node.rotationNode.id},{document.animationLayerId}");

                builder.AppendLine($"\tC: \"OP\",{node.translationNode.xCurve.id},{node.translationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{node.translationNode.yCurve.id},{node.translationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{node.translationNode.zCurve.id},{node.translationNode.id},\"d|Z\"");

                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.xCurve.id},{node.rotationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.yCurve.id},{node.rotationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.zCurve.id},{node.rotationNode.id},\"d|Z\"");

                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.id},{node.modelId},\"Lcl Scaling\"");
                    builder.AppendLine($"\tC: \"OO\",{node.scaleNode.id},{document.animationLayerId}");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.xCurve.id},{node.scaleNode.id},\"d|X\"");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.yCurve.id},{node.scaleNode.id},\"d|Y\"");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.zCurve.id},{node.scaleNode.id},\"d|Z\"");
                }
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendTakes(FbxAsciiWriter builder, ExportDocument document)
        {
            builder.AppendLine("Takes:  {");
            builder.AppendLine("\tCurrent: \"\"");
            builder.AppendLine($"\tTake: \"{EscapeFbxString(document.sceneName)}\" {{");
            builder.AppendLine($"\t\tFileName: \"{EscapeFbxString(document.sceneName)}.tak\"");
            builder.AppendLine($"\t\tLocalTime: 0,{document.stopTime}");
            builder.AppendLine($"\t\tReferenceTime: 0,{document.stopTime}");
            builder.AppendLine($"\t\tComments: \"Animation Take: {EscapeFbxString(document.sceneName)}\"");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
        }

        private static int GetCurveKeyCount(IReadOnlyList<long> sampleTimes, bool useConstantEndpoints)
        {
            if (sampleTimes == null || sampleTimes.Count == 0) return 0;
            if (!useConstantEndpoints) return sampleTimes.Count;
            return sampleTimes.Count > 1 && sampleTimes[0] != sampleTimes[sampleTimes.Count - 1] ? 2 : 1;
        }

        private static int GetCurveSampleIndex(int keyIndex, int keyCount,
            int sampleCount, bool useConstantEndpoints)
        {
            return useConstantEndpoints && keyCount > 1 && keyIndex == keyCount - 1
                ? sampleCount - 1
                : keyIndex;
        }

        private static bool IsConstantCurve(PoseBuffer poseBuffer, FbxCurve curve)
        {
            if (poseBuffer == null || poseBuffer.SampleCount == 0) return false;
            float reference = poseBuffer.Get(curve.trackIndex, curve.channelIndex, 0);
            for (int i = 1; i < poseBuffer.SampleCount; i++)
            {
                float value = poseBuffer.Get(curve.trackIndex, curve.channelIndex, i);
                if (!Mathf.Approximately(reference, value) &&
                    Mathf.Abs(reference - value) > CurveEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        private static float GetCurveValue(ExportDocument document, FbxCurve curve, int sampleIndex)
        {
            return ScaleFloat(document.poseBuffer.Get(curve.trackIndex, curve.channelIndex, sampleIndex),
                curve.valueScale);
        }

        private static void AppendCurveTimes(FbxAsciiWriter builder, IReadOnlyList<long> sampleTimes,
            int keyCount, bool useConstantEndpoints)
        {
            for (int i = 0; i < keyCount; i++)
            {
                if (i > 0) builder.Append(',');
                int sampleIndex = GetCurveSampleIndex(i, keyCount, sampleTimes.Count, useConstantEndpoints);
                builder.Append(sampleTimes[sampleIndex]);
            }
        }

        private static void AppendCurveValues(FbxAsciiWriter builder, ExportDocument document,
            FbxCurve curve, int keyCount, bool useConstantEndpoints)
        {
            for (int i = 0; i < keyCount; i++)
            {
                if (i > 0) builder.Append(',');
                int sampleIndex = GetCurveSampleIndex(i, keyCount,
                    document.sampleTimes.Count, useConstantEndpoints);
                builder.Append(GetCurveValue(document, curve, sampleIndex));
            }
        }

        private static void AppendRepeated(FbxAsciiWriter builder, int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(value);
            }
        }

        private static void AppendCurveTangentData(FbxAsciiWriter builder, ExportDocument document,
            FbxCurve curve, int keyCount, bool useConstantEndpoints)
        {
            for (int i = 0; i < keyCount; i++)
            {
                int sampleIndex = GetCurveSampleIndex(i, keyCount,
                    document.sampleTimes.Count, useConstantEndpoints);
                float rightSlope = 0f;
                if (i < keyCount - 1)
                {
                    int nextSampleIndex = GetCurveSampleIndex(i + 1, keyCount,
                        document.sampleTimes.Count, useConstantEndpoints);
                    float deltaTime = (document.sampleTimes[nextSampleIndex] -
                                       document.sampleTimes[sampleIndex]) / (float)FbxTicksPerSecond;
                    if (deltaTime > Mathf.Epsilon)
                    {
                        rightSlope = (GetCurveValue(document, curve, nextSampleIndex) -
                                      GetCurveValue(document, curve, sampleIndex)) / deltaTime;
                    }
                }

                AppendTangentValue(builder, FloatToIntBits(rightSlope), i, 0);
                AppendTangentValue(builder,
                    FloatToIntBits(i < keyCount - 1 ? rightSlope : -0f), i, 1);
                AppendTangentValue(builder, FloatToIntBits(DefaultTangentWeight), i, 2);
                AppendTangentValue(builder,
                    FloatToIntBits(i < keyCount - 1 ? DefaultTangentWeight : TerminalTangentWeight),
                    i, 3);
            }
        }

        private static void AppendTangentValue(FbxAsciiWriter builder, int value,
            int keyIndex, int valueIndex)
        {
            if (keyIndex > 0 || valueIndex > 0) builder.Append(',');
            builder.Append(value);
        }

        private static string EscapeFbxString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)}";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static bool IsAsciiFbxExportAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                using var reader = new StreamReader(absolutePath);
                for (int i = 0; i < 24 && !reader.EndOfStream; i++)
                {
                    string line = reader.ReadLine();
                    if (line != null && (line.IndexOf(ExporterDisplayName, StringComparison.Ordinal) >= 0 || line.IndexOf(LegacyExporterDisplayName, StringComparison.Ordinal) >= 0))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static void ConfigureImporter(ModelImporter importer)
        {
            if (importer == null)
            {
                return;
            }

            if (ApplyImporterSettings(importer))
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        public static bool ApplyImporterSettings(ModelImporter importer)
        {
            if (importer == null)
            {
                return false;
            }

            bool changed = false;

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (!importer.useFileUnits)
            {
                importer.useFileUnits = true;
                changed = true;
            }

            if (!importer.useFileScale)
            {
                importer.useFileScale = true;
                changed = true;
            }

            if (!Mathf.Approximately(importer.globalScale, 1f))
            {
                importer.globalScale = 1f;
                changed = true;
            }

            if (!importer.preserveHierarchy)
            {
                importer.preserveHierarchy = true;
                changed = true;
            }

            if (importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = false;
                changed = true;
            }

            if (importer.resampleCurves)
            {
                importer.resampleCurves = false;
                changed = true;
            }

            if (importer.animationCompression != ModelImporterAnimationCompression.Off)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                changed = true;
            }

            return changed;
        }

    }
}

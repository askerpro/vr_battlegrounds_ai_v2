using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.Avatars.ContactIk;
using VrBattlegrounds.Editor.GripAuthoring.ContactPoints;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    public enum HandPoseSdkWrapProfile { NoPenetration, MoreContacts }

    /// <summary>Заимствованный контекст root: массивы view заморожены, snapshot/dispose остаются у общей сессии.</summary>
    public sealed class HandPoseSdkContactContext
    {
        public UxrHandPoseEditorWindow Window { get; }
        public UxrHandPoseContactEditContext SdkContext { get; }
        public UxrHandSide Side { get; }
        public GameObject ObjectRoot { get; }
        public UxrGrabbableObject Grabbable { get; }
        public int GrabPoint { get; }
        public string InputKey { get; }
        public HandContactIkPreviewView View { get; }
        public Func<HandPoseSdkContactRequest, HandPoseSdkContactJob> BeginJob { get; }
        public Func<bool> IsCurrent { get; }
        public Action<Vector3> CommitPalmPlacement { get; }
        public Matrix4x4 ObjectToWorld => Matrix4x4.TRS(ObjectRoot.transform.position, ObjectRoot.transform.rotation, Vector3.one);

        public HandPoseSdkContactContext(UxrHandPoseEditorWindow window, UxrHandPoseContactEditContext sdkContext,
            UxrHandSide side, GameObject objectRoot, UxrGrabbableObject grabbable, int grabPoint, string inputKey,
            HandContactIkPreviewView view, Func<HandPoseSdkContactRequest, HandPoseSdkContactJob> beginJob,
            Func<bool> isCurrent, Action<Vector3> commitPalmPlacement = null)
        {
            Window = window;
            SdkContext = sdkContext;
            Side = side;
            ObjectRoot = objectRoot;
            Grabbable = grabbable;
            GrabPoint = grabPoint;
            InputKey = inputKey;
            View = view;
            BeginJob = beginJob;
            IsCurrent = isCurrent;
            CommitPalmPlacement = commitPalmPlacement;
        }
    }

    public sealed class HandPoseSdkContactRequest
    {
        readonly HandContactIkPreviewTarget[] _targets;
        public long Generation { get; }
        public UxrHandPoseContactEditContext SdkContext { get; }
        public HandPoseSdkContactContext Context { get; }
        public UxrHandPoseContactEditMode Mode { get; }
        public float DistalPriority { get; }
        public HandPoseSdkWrapProfile WrapProfile { get; }
        public float MaximumPenetrationMm { get; }
        public HandContactIkPreviewTarget[] Targets => _targets.Select(t => t.Copy()).ToArray();

        public HandPoseSdkContactRequest(long generation, UxrHandPoseContactEditContext sdkContext,
            HandPoseSdkContactContext context, UxrHandPoseContactEditMode mode, float distalPriority,
            HandPoseSdkWrapProfile wrapProfile, float maximumPenetrationMm, HandContactIkPreviewTarget[] targets)
        {
            Generation = generation;
            SdkContext = sdkContext;
            Context = context;
            Mode = mode;
            DistalPriority = distalPriority;
            WrapProfile = wrapProfile;
            MaximumPenetrationMm = maximumPenetrationMm;
            _targets = targets.Select(t => t.Copy()).ToArray();
        }
    }

    public sealed class HandPoseSdkContactResult
    {
        readonly UxrHandDescriptor _descriptor;
        public UxrHandDescriptor Descriptor
        {
            get
            {
                var copy = new UxrHandDescriptor();
                copy.CopyFrom(_descriptor);
                return copy;
            }
        }
        public HandContactIkPreviewResult Preview { get; }

        public HandPoseSdkContactResult(UxrHandDescriptor descriptor, HandContactIkPreviewResult preview)
        {
            _descriptor = new UxrHandDescriptor();
            _descriptor.CopyFrom(descriptor);
            Preview = preview;
        }
    }

    /// <summary>Адаптер cooperative backend. Нативные ресурсы освобождаются после копирования завершённого результата.</summary>
    public sealed class HandPoseSdkContactJob : IDisposable
    {
        readonly IContactIkCooperativeJob _job;
        readonly Func<HandPoseSdkContactResult> _complete;
        readonly Action _cleanup;
        HandPoseSdkContactResult _result;
        bool _disposed;

        public long Generation => _job.Generation;
        public bool IsCompleted => _job.IsCompleted;
        public bool IsCancelled => _job.IsCancelled;
        public int Evaluations => _job.Evaluations;
        public string Progress => _job.Progress;
        public HandPoseSdkContactResult Result => !_disposed && IsCompleted && !IsCancelled
            ? _result ?? (_result = _complete())
            : throw new InvalidOperationException("Результат job ещё не доступен.");

        public HandPoseSdkContactJob(IContactIkCooperativeJob job, Func<HandPoseSdkContactResult> complete, Action cleanup)
        {
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _complete = complete ?? throw new ArgumentNullException(nameof(complete));
            _cleanup = cleanup;
        }

        public bool Step(int maxEvaluations)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandPoseSdkContactJob));
            return _job.Step(maxEvaluations);
        }

        public void Cancel()
        {
            if (!_disposed) _job.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _job.Dispose();
            }
            finally
            {
                _cleanup?.Invoke();
            }
        }
    }

    /// <summary>
    /// Встроенное в <see cref="UxrHandPoseEditorWindow"/> управление хватом от текущего <see cref="UxrGrabbableObject"/>:
    /// 1) Кнопка «Обхватить» — автоснап фаланг по текущему snapTransform с мгновенной фиксацией позы;
    /// 2) Режим IK (конечные фаланги) — прямое перемещение конечной фаланги в SceneView с автоматическим решением промежуточного IK;
    /// 3) Выключение IK — возврат к штатным ползункам (spinners) Hand Pose Editor.
    /// </summary>
    [InitializeOnLoad]
    public static class HandPoseSdkContactEditing
    {
        static readonly string[] FingerNamesRu = { "Большой", "Указательный", "Средний", "Безымянный", "Мизинец" };
        static readonly UxrFingerType[] FingerTypes =
        {
            UxrFingerType.Thumb,
            UxrFingerType.Index,
            UxrFingerType.Middle,
            UxrFingerType.Ring,
            UxrFingerType.Little
        };

        static UxrHandPoseEditorWindow _window;
        static UxrAvatar _avatar;
        static UxrHandPoseAsset _pose;
        static float _blend;

        static UxrGrabbableObject _grabbable;
        static int _grabPoint;
        static UxrHandSide _side = UxrHandSide.Right;

        static int _activeFingerIndex = 1; // По умолчанию указательный палец
        static bool _showAllFingerHandles;
        static bool _ikModeRequested;
        static bool _autoSyncWithGrabbable = true;
        static string _status = "";

        static int _lastSyncedGrabbableId;
        static int _lastSyncedGrabPoint = -1;
        static UxrHandSide _lastSyncedSide = UxrHandSide.Right;
        static string _lastSyncedAvatarPrefabGuid = "";
        static int _lastSyncedPoseId;
        static bool _isSyncingWindow;

        static HandPoseSdkContactContext _legacyContext;
        public static HandPoseSdkContactContext CurrentContext => _legacyContext;
        public static HandContactIkPreviewResult CurrentPreview => null;

        struct TriangleAvatarSpace
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public Vector3 Normal;
        }

        static HandPoseSdkContactEditing()
        {
            UxrHandPoseEditorWindow.DiagnosticsGUI += DrawGUI;
            UxrHandPoseEditorWindow.DiagnosticsContextUpdated += OnWindowContextUpdated;
            UxrHandPoseEditorWindow.DiagnosticsClosed += ClearContext;
            UxrHandPoseEditorWindow.ContactEditingSceneGUI += DrawScene;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload += () => ClearContext();
            EditorApplication.quitting += () => ClearContext();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode)
                {
                    ClearContext();
                }
            };
        }

        public static void UpdateContext(HandPoseSdkContactContext context)
        {
            _legacyContext = context;
        }

        public static void ClearContext(UxrHandPoseEditorWindow window = null)
        {
            if (window != null && _window != null && _window != window)
            {
                return;
            }

            if (_window != null)
            {
                try
                {
                    _window.ContactEditMode = UxrHandPoseContactEditMode.Manual;
                }
                catch
                {
                    // Ignore when window is closing
                }
            }

            _legacyContext = null;
            if (window != null)
            {
                _ikModeRequested = false;
                _window = null;
                _avatar = null;
                _pose = null;
            }
        }

        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (_window != null && _autoSyncWithGrabbable && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += () =>
                {
                    if (_window != null && _autoSyncWithGrabbable)
                    {
                        SyncFromSelection();
                        CheckAutoSyncWithGrabbable();
                    }
                };
            }

            return modifications;
        }

        static void OnWindowContextUpdated(UxrHandPoseEditorWindow window, UxrAvatar avatar, UxrHandPoseAsset pose, float blend)
        {
            _window = window;
            _avatar = avatar;
            _pose = pose;
            _blend = blend;

            if (_avatar != null)
            {
                EnsureValidAvatarRigAxes(_avatar);
            }

            if (_ikModeRequested && _window != null && _avatar != null && _pose != null)
            {
                _window.ContactEditMode = UxrHandPoseContactEditMode.Targets;
            }

            SyncFromSelection();
        }

        static void OnSelectionChanged()
        {
            SyncFromSelection();
            if (_autoSyncWithGrabbable)
            {
                CheckAutoSyncWithGrabbable();
            }

            if (_window != null)
            {
                _window.Repaint();
                SceneView.RepaintAll();
            }
        }

        static void SyncFromSelection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            GameObject activeGo = Selection.activeGameObject;
            if (activeGo != null && !EditorUtility.IsPersistent(activeGo))
            {
                // 1. Проверяем выбор самого SnapTransform или превью-меша (Grip Align Left/Right Hand)
                var snapComponent = activeGo.GetComponentInParent<UxrGrabbableObjectSnapTransform>();
                var parentGrabbable = activeGo.GetComponentInParent<UxrGrabbableObject>();

                if (parentGrabbable == null)
                {
                    var previewProxy = activeGo.GetComponentInParent<UxrGrabbableObjectPreviewMeshProxy>();
                    if (previewProxy != null && previewProxy.PreviewMeshComponent != null)
                    {
                        parentGrabbable = previewProxy.PreviewMeshComponent.GetComponentInParent<UxrGrabbableObject>();
                        snapComponent = previewProxy.PreviewMeshComponent.GetComponentInParent<UxrGrabbableObjectSnapTransform>();
                    }
                }

                if (parentGrabbable != null)
                {
                    _grabbable = parentGrabbable;
                    _grabPoint = Mathf.Clamp(_grabPoint, 0, Mathf.Max(0, _grabbable.GrabPointCount - 1));

                    Transform targetSnap = snapComponent != null ? snapComponent.transform : activeGo.transform;
                    if (TryMatchSnapTransform(_grabbable, _avatar, targetSnap, out int matchedGrabPoint, out UxrHandSide matchedSide))
                    {
                        _grabPoint = matchedGrabPoint;
                        _side = matchedSide;
                    }
                    else if (targetSnap.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _side = UxrHandSide.Left;
                    }
                    else if (targetSnap.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _side = UxrHandSide.Right;
                    }

                    return;
                }

                // 2. Если выбран корневой объект оружия с дочерним UxrGrabbableObject (но не аватар)
                if (activeGo.GetComponentInParent<UxrAvatar>() == null)
                {
                    var childGrabbable = activeGo.GetComponentInChildren<UxrGrabbableObject>(true);
                    if (childGrabbable != null)
                    {
                        _grabbable = childGrabbable;
                        _grabPoint = Mathf.Clamp(_grabPoint, 0, Mathf.Max(0, _grabbable.GrabPointCount - 1));
                        return;
                    }
                }
            }

            // 3. Если кэш пуст или объект удалён — ищем активный UxrGrabbableObject в сцене
            if (_grabbable == null || EditorUtility.IsPersistent(_grabbable.gameObject))
            {
                _grabbable = FindBestSceneGrabbable(_avatar);
                if (_grabbable != null)
                {
                    _grabPoint = Mathf.Clamp(_grabPoint, 0, Mathf.Max(0, _grabbable.GrabPointCount - 1));
                }
            }
        }

        static void CheckAutoSyncWithGrabbable()
        {
            if (_isSyncingWindow || _window == null || _grabbable == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!TryGetGrabbablePoseAndAvatar(_grabbable, _grabPoint, out UxrHandPoseAsset configuredPose, out _, out string avatarPrefabGuid, out UxrHandSide? restrictedSide))
            {
                return;
            }

            if (restrictedSide.HasValue)
            {
                _side = restrictedSide.Value;
            }

            int grabbableId = _grabbable.GetInstanceID();
            int poseId = configuredPose != null ? configuredPose.GetInstanceID() : 0;

            bool signatureChanged =
                grabbableId != _lastSyncedGrabbableId ||
                _grabPoint != _lastSyncedGrabPoint ||
                _side != _lastSyncedSide ||
                !string.Equals(avatarPrefabGuid, _lastSyncedAvatarPrefabGuid, StringComparison.Ordinal) ||
                poseId != _lastSyncedPoseId;

            if (signatureChanged)
            {
                SyncWithGrabbableSettings(force: false);
            }
        }

        /// <summary>
        /// Синхронизирует окно <see cref="UxrHandPoseEditorWindow"/> (целевой аватар, позу, точку хвата и сторону руки)
        /// с текущими настройками выбранного <see cref="UxrGrabbableObject"/>.
        /// </summary>
        public static bool SyncWithGrabbableSettings(bool force = true)
        {
            if (_isSyncingWindow || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            SyncFromSelection();
            if (_grabbable == null)
            {
                if (force)
                {
                    _status = "Не найден выбранный объект с компонентом UxrGrabbableObject в сцене.";
                }

                return false;
            }

            if (!TryGetGrabbablePoseAndAvatar(
                    _grabbable,
                    _grabPoint,
                    out UxrHandPoseAsset configuredPose,
                    out UxrAvatar targetSceneAvatar,
                    out string avatarPrefabGuid,
                    out UxrHandSide? restrictedSide))
            {
                if (force)
                {
                    _status = $"У {_grabbable.name} не удалось прочитать настройки точки хвата.";
                }

                return false;
            }

            if (restrictedSide.HasValue)
            {
                _side = restrictedSide.Value;
            }
            else
            {
                // Если для текущей стороны нет SnapTransform, а для противоположной есть — переключаемся на неё
                Transform curSnap = ResolveSnapTransform(_grabbable, targetSceneAvatar ?? _avatar, _grabPoint, _side);
                if (curSnap == null)
                {
                    UxrHandSide otherSide = _side == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left;
                    Transform otherSnap = ResolveSnapTransform(_grabbable, targetSceneAvatar ?? _avatar, _grabPoint, otherSide);
                    if (otherSnap != null)
                    {
                        _side = otherSide;
                    }
                }
            }

            _lastSyncedGrabbableId = _grabbable.GetInstanceID();
            _lastSyncedGrabPoint = _grabPoint;
            _lastSyncedSide = _side;
            _lastSyncedAvatarPrefabGuid = avatarPrefabGuid ?? "";
            _lastSyncedPoseId = configuredPose != null ? configuredPose.GetInstanceID() : 0;

            bool changedWindowContext = false;
            if (_window != null && targetSceneAvatar != null && configuredPose != null)
            {
                if (force || _avatar != targetSceneAvatar || _pose != configuredPose)
                {
                    _isSyncingWindow = true;
                    try
                    {
                        UxrHandPoseEditorWindow.Open(targetSceneAvatar, configuredPose);
                        if (_ikModeRequested && _window != null)
                        {
                            _window.ContactEditMode = UxrHandPoseContactEditMode.Targets;
                        }

                        changedWindowContext = true;
                    }
                    finally
                    {
                        _isSyncingWindow = false;
                    }
                }
            }

            Transform snapTransform = ResolveSnapTransform(_grabbable, _avatar, _grabPoint, _side);
            string snapName = snapTransform != null ? snapTransform.name : "без SnapTransform";
            string poseName = configuredPose != null ? configuredPose.name : (_pose != null ? _pose.name : "не задана");

            if (force || changedWindowContext)
            {
                _status = $"Синхронизировано с {_grabbable.name}: поза «{poseName}», снап «{snapName}» ({(_side == UxrHandSide.Left ? "левая" : "правая")} кисть).";
                if (_window != null)
                {
                    _window.Repaint();
                }

                SceneView.RepaintAll();
            }

            return true;
        }

        static bool TryGetGrabbablePoseAndAvatar(
            UxrGrabbableObject grabbable,
            int grabPoint,
            out UxrHandPoseAsset configuredPose,
            out UxrAvatar editableSceneAvatar,
            out string avatarPrefabGuid,
            out UxrHandSide? restrictedSide)
        {
            configuredPose = null;
            editableSceneAvatar = _avatar;
            avatarPrefabGuid = "";
            restrictedSide = null;

            if (grabbable == null || grabbable.GrabPointCount == 0)
            {
                return false;
            }

            int gp = Mathf.Clamp(grabPoint, 0, grabbable.GrabPointCount - 1);
            UxrGrabPointInfo grabPointInfo = grabbable.GetGrabPoint(gp);
            if (grabPointInfo == null)
            {
                return false;
            }

            if (!grabPointInfo.BothHandsCompatible)
            {
                restrictedSide = grabPointInfo.HandSide;
            }

            GameObject selectedAvatarPrefab = grabbable.Editor_GetSelectedAvatarPrefabForGrips();
            UxrAvatar selectedPrefabAvatar = selectedAvatarPrefab != null ? selectedAvatarPrefab.GetComponent<UxrAvatar>() : null;
            if (selectedPrefabAvatar != null)
            {
                avatarPrefabGuid = selectedPrefabAvatar.PrefabGuid ?? "";
            }

            UxrGripPoseInfo gripPoseInfo = null;
            if (selectedPrefabAvatar != null)
            {
                gripPoseInfo = grabPointInfo.GetGripPoseInfo(selectedPrefabAvatar);
            }
            else if (_avatar != null)
            {
                gripPoseInfo = grabPointInfo.GetGripPoseInfo(_avatar);
            }

            if (gripPoseInfo == null)
            {
                gripPoseInfo = grabPointInfo.DefaultGripPoseInfo;
            }

            if (gripPoseInfo != null)
            {
                configuredPose = gripPoseInfo.HandPose;
            }

            editableSceneAvatar = ResolveBestEditableSceneAvatar(selectedPrefabAvatar, configuredPose);
            if (configuredPose != null && editableSceneAvatar != null)
            {
                configuredPose = editableSceneAvatar.GetHandPose(configuredPose.name, true) ?? configuredPose;
            }

            return true;
        }

        static UxrAvatar ResolveBestEditableSceneAvatar(UxrAvatar selectedPrefabAvatar, UxrHandPoseAsset targetPose)
        {
            // 1. Если текущий аватар в окне уже напрямую владеет этой позой (не read-only), оставляем его
            if (_avatar != null && !EditorUtility.IsPersistent(_avatar.gameObject))
            {
                if (targetPose == null || _avatar.GetHandPose(targetPose.name, false) != null)
                {
                    return _avatar;
                }
            }

            UxrAvatar[] sceneAvatars = Object.FindObjectsByType<UxrAvatar>(FindObjectsInactive.Include)
                .Where(a => a != null && !EditorUtility.IsPersistent(a.gameObject))
                .ToArray();

            if (targetPose != null)
            {
                // 2. Ищем аватар на сцене, который является непосредственным владельцем позы (например, CyborgHandsBase),
                // чтобы поза открывалась в режиме редактирования, а не как read-only наследник.
                UxrAvatar directOwner = sceneAvatars
                    .OrderByDescending(a => a.gameObject.activeInHierarchy)
                    .FirstOrDefault(a => a.GetHandPose(targetPose.name, false) != null);
                if (directOwner != null)
                {
                    return directOwner;
                }

                UxrAvatar inheritedOwner = sceneAvatars
                    .OrderByDescending(a => a.gameObject.activeInHierarchy)
                    .FirstOrDefault(a => a.GetHandPose(targetPose.name, true) != null);
                if (inheritedOwner != null)
                {
                    return inheritedOwner;
                }
            }

            if (selectedPrefabAvatar != null)
            {
                UxrAvatar compatible = sceneAvatars
                    .OrderByDescending(a => a.gameObject.activeInHierarchy)
                    .FirstOrDefault(a => a.IsPrefabCompatibleWith(selectedPrefabAvatar) || selectedPrefabAvatar.IsPrefabCompatibleWith(a));
                if (compatible != null)
                {
                    return compatible;
                }
            }

            return _avatar != null ? _avatar : sceneAvatars.FirstOrDefault(a => a.gameObject.activeInHierarchy);
        }

        static bool EnsureEditableOwnerAvatarForCurrentPose()
        {
            if (_window == null || _avatar == null || _pose == null)
            {
                return false;
            }

            if (_avatar.GetHandPose(_pose.name, false) != null)
            {
                return true;
            }

            UxrAvatar ownerAvatar = ResolveBestEditableSceneAvatar(null, _pose);
            if (ownerAvatar != null && ownerAvatar != _avatar && ownerAvatar.GetHandPose(_pose.name, false) != null)
            {
                _isSyncingWindow = true;
                try
                {
                    UxrHandPoseEditorWindow.Open(ownerAvatar, _pose);
                    if (_ikModeRequested && _window != null)
                    {
                        _window.ContactEditMode = UxrHandPoseContactEditMode.Targets;
                    }
                }
                finally
                {
                    _isSyncingWindow = false;
                }

                return _avatar != null && _avatar.GetHandPose(_pose.name, false) != null;
            }

            return false;
        }

        static bool TryMatchSnapTransform(UxrGrabbableObject grabbable, UxrAvatar avatar, Transform candidate, out int grabPoint, out UxrHandSide side)
        {
            grabPoint = 0;
            side = _side;
            if (grabbable == null || candidate == null)
            {
                return false;
            }

            UxrAvatar lookupAvatar = avatar;
            if (lookupAvatar == null)
            {
                GameObject selectedPrefab = grabbable.Editor_GetSelectedAvatarPrefabForGrips();
                if (selectedPrefab != null)
                {
                    lookupAvatar = selectedPrefab.GetComponent<UxrAvatar>();
                }
            }

            if (lookupAvatar == null)
            {
                return false;
            }

            for (int gp = 0; gp < grabbable.GrabPointCount; gp++)
            {
                foreach (UxrHandSide checkSide in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    Transform align = grabbable.Editor_GetGrabPointGrabAlignTransform(lookupAvatar, gp, checkSide);
                    if (align != null && (candidate == align || candidate.IsChildOf(align)))
                    {
                        grabPoint = gp;
                        side = checkSide;
                        return true;
                    }
                }
            }

            return false;
        }

        static UxrGrabbableObject FindBestSceneGrabbable(UxrAvatar avatar)
        {
            var all = Object.FindObjectsByType<UxrGrabbableObject>(FindObjectsInactive.Exclude);
            if (all == null || all.Length == 0)
            {
                return null;
            }

            if (avatar != null)
            {
                foreach (var g in all)
                {
                    if (g == null || EditorUtility.IsPersistent(g.gameObject) || g.GrabPointCount == 0) continue;
                    if (g.Editor_GetGrabPointGrabAlignTransform(avatar, 0, UxrHandSide.Right) != null ||
                        g.Editor_GetGrabPointGrabAlignTransform(avatar, 0, UxrHandSide.Left) != null)
                    {
                        return g;
                    }
                }
            }

            return all.FirstOrDefault(g => g != null && !EditorUtility.IsPersistent(g.gameObject) && g.GrabPointCount > 0);
        }

        static Transform ResolveSnapTransform(UxrGrabbableObject grabbable, UxrAvatar avatar, int grabPoint, UxrHandSide side)
        {
            if (grabbable == null || grabbable.GrabPointCount == 0)
            {
                return null;
            }

            int gp = Mathf.Clamp(grabPoint, 0, grabbable.GrabPointCount - 1);
            if (avatar != null)
            {
                Transform align = grabbable.Editor_GetGrabPointGrabAlignTransform(avatar, gp, side);
                if (align != null)
                {
                    return align;
                }
            }

            GameObject selectedAvatarGo = grabbable.Editor_GetSelectedAvatarPrefabForGrips();
            if (selectedAvatarGo != null)
            {
                var selectedAvatar = selectedAvatarGo.GetComponent<UxrAvatar>();
                if (selectedAvatar != null)
                {
                    Transform align = grabbable.Editor_GetGrabPointGrabAlignTransform(selectedAvatar, gp, side);
                    if (align != null)
                    {
                        return align;
                    }
                }
            }

            return null;
        }

        static UxrGrabber ResolveGrabber(UxrAvatar avatar, UxrHandSide side)
        {
            if (avatar == null)
            {
                return null;
            }

            return avatar.GetComponentsInChildren<UxrGrabber>(true).FirstOrDefault(g => g != null && g.Side == side);
        }

        static void EnsureValidAvatarRigAxes(UxrAvatar avatar)
        {
            if (avatar == null || avatar.AvatarRigInfo == null)
            {
                return;
            }

            UxrAvatar bindAvatar = null;
            bool repairedAny = false;

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrAvatarArmInfo armInfo = avatar.AvatarRigInfo.GetArmInfo(side);
                if (armInfo == null)
                {
                    continue;
                }

                UxrUniversalLocalAxes fingerAxes = armInfo.FingerUniversalLocalAxes;
                UxrUniversalLocalAxes wristAxes = armInfo.HandUniversalLocalAxes;

                bool fingerAxesInvalid =
                    fingerAxes == null ||
                    fingerAxes.LocalRight.sqrMagnitude < 0.5f ||
                    fingerAxes.LocalUp.sqrMagnitude < 0.5f ||
                    fingerAxes.LocalForward.sqrMagnitude < 0.5f ||
                    Mathf.Abs(Vector3.Dot(fingerAxes.LocalRight.normalized, fingerAxes.LocalForward.normalized)) > 0.25f ||
                    Mathf.Abs(Vector3.Dot(fingerAxes.LocalUp.normalized, fingerAxes.LocalForward.normalized)) > 0.25f;

                bool wristAxesInvalid =
                    wristAxes == null ||
                    wristAxes.LocalRight.sqrMagnitude < 0.5f ||
                    wristAxes.LocalUp.sqrMagnitude < 0.5f ||
                    wristAxes.LocalForward.sqrMagnitude < 0.5f;

                if (!fingerAxesInvalid && !wristAxesInvalid)
                {
                    continue;
                }

                if (bindAvatar == null)
                {
                    bindAvatar = GetBindPoseAvatar(avatar);
                }

                UxrAvatarArmInfo bindArmInfo = bindAvatar != null && bindAvatar.AvatarRigInfo != null
                    ? bindAvatar.AvatarRigInfo.GetArmInfo(side)
                    : null;

                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo handAxesField = typeof(UxrAvatarArmInfo).GetField("_handUniversalLocalAxes", flags);
                FieldInfo fingerAxesField = typeof(UxrAvatarArmInfo).GetField("_fingerUniversalLocalAxes", flags);

                Transform wristTransform = avatar.GetHand(side)?.Wrist ?? avatar.transform;

                if (bindArmInfo != null &&
                    bindArmInfo.HandUniversalLocalAxes != null &&
                    bindArmInfo.FingerUniversalLocalAxes != null &&
                    bindArmInfo.FingerUniversalLocalAxes.LocalUp.sqrMagnitude > 0.5f)
                {
                    UxrUniversalLocalAxes srcWrist = bindArmInfo.HandUniversalLocalAxes;
                    UxrUniversalLocalAxes srcFinger = bindArmInfo.FingerUniversalLocalAxes;

                    handAxesField?.SetValue(
                        armInfo,
                        UxrUniversalLocalAxes.FromAxes(wristTransform, srcWrist.LocalRight, srcWrist.LocalUp, srcWrist.LocalForward));
                    fingerAxesField?.SetValue(
                        armInfo,
                        UxrUniversalLocalAxes.FromAxes(wristTransform, srcFinger.LocalRight, srcFinger.LocalUp, srcFinger.LocalForward));
                    repairedAny = true;
                }
                else
                {
                    Vector3 fRight = side == UxrHandSide.Left ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);
                    Vector3 fUp = side == UxrHandSide.Left ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 1f, 0f);
                    Vector3 fForward = new Vector3(-1f, 0f, 0f);
                    fingerAxesField?.SetValue(armInfo, UxrUniversalLocalAxes.FromAxes(wristTransform, fRight, fUp, fForward));
                    repairedAny = true;
                }
            }

            if (repairedAny)
            {
                EditorUtility.SetDirty(avatar);
                if (_window != null)
                {
                    MethodInfo buildSpinners = typeof(UxrHandPoseEditorWindow).GetMethod(
                        "BuildFingerSpinners",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    buildSpinners?.Invoke(_window, null);
                }
            }
        }

        static UxrAvatar GetBindPoseAvatar(UxrAvatar sceneAvatar)
        {
            if (sceneAvatar == null)
            {
                return null;
            }

            if (EditorUtility.IsPersistent(sceneAvatar.gameObject))
            {
                return sceneAvatar;
            }

            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(sceneAvatar.gameObject);
            if (prefabSource != null)
            {
                UxrAvatar sourceAvatar = prefabSource.GetComponent<UxrAvatar>();
                if (sourceAvatar != null)
                {
                    return sourceAvatar;
                }
            }

            if (sceneAvatar.ParentAvatarPrefab != null)
            {
                return sceneAvatar.ParentAvatarPrefab;
            }

            return null;
        }

        static void DrawGUI(UxrHandPoseEditorWindow window)
        {
            if (window == null)
            {
                return;
            }

            _window = window;
            if (_avatar != null)
            {
                EnsureValidAvatarRigAxes(_avatar);
            }

            SyncFromSelection();
            if (_autoSyncWithGrabbable)
            {
                CheckAutoSyncWithGrabbable();
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Автоснап и IK конечных фаланг", EditorStyles.boldLabel);

                Transform snapTransform = ResolveSnapTransform(_grabbable, _avatar, _grabPoint, _side);
                UxrHandPoseAsset grabbablePose = null;
                if (_grabbable != null)
                {
                    TryGetGrabbablePoseAndAvatar(_grabbable, _grabPoint, out grabbablePose, out _, out _, out _);
                }

                string grabbableLabel = _grabbable != null
                    ? $"{_grabbable.name} · {(snapTransform != null ? snapTransform.name : "без SnapTransform")} · поза: {(grabbablePose != null ? grabbablePose.name : "не задана")}"
                    : "Выберите объект с UxrGrabbableObject в сцене";
                EditorGUILayout.LabelField("Текущий Grabbable:", grabbableLabel, EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool newAutoSync = EditorGUILayout.ToggleLeft(
                        "Автосинхронизация с выбранным Grabbable",
                        _autoSyncWithGrabbable,
                        GUILayout.Width(250));
                    if (newAutoSync != _autoSyncWithGrabbable)
                    {
                        _autoSyncWithGrabbable = newAutoSync;
                        if (_autoSyncWithGrabbable)
                        {
                            SyncWithGrabbableSettings(force: true);
                        }
                    }

                    using (new EditorGUI.DisabledScope(_grabbable == null))
                    {
                        if (GUILayout.Button("Синхронизировать с Grabbable", GUILayout.Height(20)))
                        {
                            SyncWithGrabbableSettings(force: true);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isLeft = _side == UxrHandSide.Left;
                    if (GUILayout.Toggle(isLeft, "Левая кисть", "Button") && !isLeft)
                    {
                        _side = UxrHandSide.Left;
                        _lastSyncedSide = _side;
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Toggle(!isLeft, "Правая кисть", "Button") && isLeft)
                    {
                        _side = UxrHandSide.Right;
                        _lastSyncedSide = _side;
                        SceneView.RepaintAll();
                    }

                    if (_grabbable != null && _grabbable.GrabPointCount > 1)
                    {
                        string[] grabPointNames = Enumerable.Range(0, _grabbable.GrabPointCount)
                            .Select(i => UxrGrabPointIndex.GetIndexDisplayName(_grabbable, i))
                            .ToArray();
                        int newPoint = EditorGUILayout.Popup(Mathf.Clamp(_grabPoint, 0, grabPointNames.Length - 1), grabPointNames, GUILayout.MaxWidth(140));
                        if (newPoint != _grabPoint)
                        {
                            _grabPoint = newPoint;
                            if (_autoSyncWithGrabbable)
                            {
                                SyncWithGrabbableSettings(force: true);
                            }
                            else
                            {
                                SceneView.RepaintAll();
                            }
                        }
                    }
                }

                bool canAutoWrap = _avatar != null && _pose != null && _grabbable != null && snapTransform != null &&
                                   !EditorApplication.isPlayingOrWillChangePlaymode;
                bool canUseIk = _avatar != null && _pose != null && !EditorApplication.isPlayingOrWillChangePlaymode;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canAutoWrap))
                    {
                        if (GUILayout.Button("Обхватить", GUILayout.Height(26)))
                        {
                            AutoWrapAndCommit();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!canUseIk))
                    {
                        bool ikActive = window.ContactEditMode != UxrHandPoseContactEditMode.Manual;
                        Color prevColor = GUI.backgroundColor;
                        if (ikActive)
                        {
                            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.55f, 1f);
                        }

                        bool newIkActive = GUILayout.Toggle(
                            ikActive,
                            ikActive ? "Режим IK (конечные фаланги): ВКЛ" : "Режим IK (конечные фаланги): ВЫКЛ",
                            "Button",
                            GUILayout.Height(26));
                        GUI.backgroundColor = prevColor;

                        if (newIkActive != ikActive)
                        {
                            _ikModeRequested = newIkActive;
                            window.ContactEditMode = newIkActive
                                ? UxrHandPoseContactEditMode.Targets
                                : UxrHandPoseContactEditMode.Manual;
                            _status = newIkActive
                                ? "Режим IK включён: тяните конечную фалангу в SceneView (промежуточный IK решается автоматически)."
                                : "Режим IK выключен: активны ручные штатные ползунки Hand Pose Editor.";
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (window.ContactEditMode != UxrHandPoseContactEditMode.Manual && canUseIk)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _activeFingerIndex = GUILayout.Toolbar(
                            Mathf.Clamp(_activeFingerIndex, 0, FingerNamesRu.Length - 1),
                            FingerNamesRu);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool showAll = EditorGUILayout.ToggleLeft("Показать гизмо всех 5 пальцев сразу", _showAllFingerHandles);
                        if (showAll != _showAllFingerHandles)
                        {
                            _showAllFingerHandles = showAll;
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_status))
                {
                    EditorGUILayout.HelpBox(_status, MessageType.None);
                }
            }
        }

        static void AutoWrapAndCommit()
        {
            if (_window == null || _avatar == null || _pose == null || _grabbable == null)
            {
                _status = "Для автоснапа выберите аватар с открытой позой и предмет UxrGrabbableObject.";
                return;
            }

            EnsureEditableOwnerAvatarForCurrentPose();
            EnsureValidAvatarRigAxes(_avatar);

            if (!_window.TryCaptureContactEditContext(_side, out var sdkCtx, out string reason))
            {
                _status = reason;
                return;
            }

            if (sdkCtx.ReadOnly)
            {
                _status = "Поза унаследована от родительского префаба (read-only). Выберите корневой префаб CyborgHandsBase в качестве Target Avatar.";
                return;
            }

            Transform snapTransform = ResolveSnapTransform(_grabbable, _avatar, _grabPoint, _side);
            UxrGrabber grabber = ResolveGrabber(_avatar, _side);
            if (snapTransform == null || grabber == null)
            {
                _status = "Не найден SnapTransform (Grip Align) или UxrGrabber для выбранной кисти.";
                return;
            }

            // 1. Собираем треугольники внешней поверхности предмета вокруг точки хвата и переводим их в мировое пространство кисти аватара
            List<TriangleAvatarSpace> weaponTriangles = CollectWeaponTrianglesInAvatarSpace(_grabbable.gameObject, snapTransform, grabber.transform);
            if (weaponTriangles.Count == 0)
            {
                _status = "На предмете не найдены меши или коллайдеры рядом с точкой хвата.";
                return;
            }

            // 2. Создаём изолированную теневую копию костей кисти (HideAndDontSave), чтобы живые кости _avatar
            // вообще не модифицировались до вызова TryCommitContactEdit (соблюдение контракта SDK83 MatchesWorkingState).
            UxrAvatarHand sourceHand = _avatar.GetHand(_side);
            UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;

            GameObject shadowRootGo = null;
            UxrHandDescriptor wrappedDescriptor = null;
            int contactedSegments = 0;

            try
            {
                UxrAvatarArm shadowArm = CreateShadowHand(sourceHand, out shadowRootGo);
                UxrAvatarHand shadowHand = shadowArm.Hand;

                const float PadRadiusProximal = 0.0068f;
                const float PadRadiusIntermediate = 0.0058f;
                const float PadRadiusDistal = 0.0050f;

                // 3. Последовательно сгибаем каждый палец на теневой копии строго вдоль оси сгиба фаланги (1-DOF для средних/концевых)
                foreach (UxrFingerType fingerType in new[]
                {
                    UxrFingerType.Index,
                    UxrFingerType.Middle,
                    UxrFingerType.Ring,
                    UxrFingerType.Little,
                    UxrFingerType.Thumb
                })
                {
                    UxrAvatarFinger finger = shadowHand.GetFinger(fingerType);
                    if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                    {
                        continue;
                    }

                    bool isThumb = fingerType == UxrFingerType.Thumb;
                    BindPoseFingerState bindState = CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes);
                    GetFingerAngleBounds(_side, isThumb, bindState.BindSpread,
                        out float minSpread, out float maxSpread,
                        out float minCurl1, out float maxCurl1,
                        out float minCurl2, out float maxCurl2,
                        out float minCurl3, out float maxCurl3);

                    float spread = bindState.BindSpread;
                    float curl1 = isThumb ? -4f : 0f;
                    float curl2 = 2f;
                    float curl3 = 2f;

                    ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl1, curl2, curl3);

                    if (isThumb)
                    {
                        float bestSpread = spread;
                        float bestDist = float.PositiveInfinity;
                        for (float candidateSpread = minSpread; candidateSpread <= maxSpread; candidateSpread += 3f)
                        {
                            ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, true, candidateSpread, curl1, curl2, curl3);
                            Vector3 tipPos = GetDistalTipWorld(finger, fingerAxes);
                            float dist = DistanceToWeaponSurface(tipPos, weaponTriangles, out _);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestSpread = candidateSpread;
                            }
                        }

                        spread = bestSpread;
                        ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, true, spread, curl1, curl2, curl3);
                    }

                    // Сгибаем проксимальную фалангу (Curl1)
                    if (SweepJointCurlUntilContact(
                        finger, bindState, fingerAxes, isThumb,
                        1, spread, ref curl1, curl2, curl3,
                        minCurl1, isThumb ? maxCurl1 : Mathf.Min(maxCurl1, 85f),
                        PadRadiusProximal, weaponTriangles))
                    {
                        contactedSegments++;
                    }

                    // Сгибаем среднюю фалангу (Curl2 — строго вокруг оси сгиба, 0° бокового отклонения)
                    if (SweepJointCurlUntilContact(
                        finger, bindState, fingerAxes, isThumb,
                        2, spread, ref curl1, ref curl2, curl3,
                        minCurl2, Mathf.Min(maxCurl2, 95f),
                        PadRadiusIntermediate, weaponTriangles))
                    {
                        contactedSegments++;
                    }

                    // Сгибаем конечную фалангу (Curl3 — строго вокруг оси сгиба, 0° бокового отклонения)
                    if (SweepDistalCurlUntilContact(
                        finger, bindState, fingerAxes, isThumb,
                        spread, curl1, curl2, ref curl3,
                        minCurl3, Mathf.Min(maxCurl3, 85f),
                        PadRadiusDistal, weaponTriangles))
                    {
                        contactedSegments++;
                    }
                }

                wrappedDescriptor = new UxrHandDescriptor(shadowArm, wristAxes, fingerAxes);
            }
            catch (Exception ex)
            {
                _status = "Ошибка автоснапа: " + ex.Message;
                return;
            }
            finally
            {
                if (shadowRootGo != null)
                {
                    Object.DestroyImmediate(shadowRootGo);
                }
            }

            // 4. Фиксируем полученную позу через штатный конвейер SDK (Undo + SaveHandPose + RefreshGrabPoseMeshes)
            if (_window.TryCommitContactEdit(sdkCtx, wrappedDescriptor, out string commitReason))
            {
                _status = $"Обхват выполнен и зафиксирован ({contactedSegments}/15 фаланг в контакте с рукоятью).";
                _window.Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                _status = commitReason;
            }
        }

        static bool SweepJointCurlUntilContact(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            int jointIndex,
            float spread,
            ref float curl1,
            float curl2,
            float curl3,
            float minCurl,
            float maxCurl,
            float padRadius,
            List<TriangleAvatarSpace> weaponTriangles)
        {
            float dummyCurl2 = curl2;
            return SweepJointCurlUntilContact(
                finger, bindState, fingerAxes, isThumb,
                jointIndex, spread, ref curl1, ref dummyCurl2, curl3,
                minCurl, maxCurl, padRadius, weaponTriangles);
        }

        static bool SweepJointCurlUntilContact(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            int jointIndex,
            float spread,
            ref float curl1,
            ref float curl2,
            float curl3,
            float minCurl,
            float maxCurl,
            float padRadius,
            List<TriangleAvatarSpace> weaponTriangles)
        {
            Transform bone = jointIndex == 1 ? finger.Proximal : finger.Intermediate;
            Transform childBone = jointIndex == 1 ? finger.Intermediate : finger.Distal;

            float startCurl = Mathf.Clamp(jointIndex == 1 ? curl1 : curl2, minCurl, maxCurl);
            ApplyAnatomicalFingerAngles(
                finger, bindState, fingerAxes, isThumb,
                spread,
                jointIndex == 1 ? startCurl : curl1,
                jointIndex == 2 ? startCurl : curl2,
                curl3);

            float prevMinClearance = EvaluateSegmentClearance(bone.position, childBone.position, padRadius, weaponTriangles);
            float chosenCurl = startCurl;
            bool contacted = false;

            const float StepDeg = 1.5f;
            for (float angle = startCurl + StepDeg; angle <= maxCurl; angle += StepDeg)
            {
                ApplyAnatomicalFingerAngles(
                    finger, bindState, fingerAxes, isThumb,
                    spread,
                    jointIndex == 1 ? angle : curl1,
                    jointIndex == 2 ? angle : curl2,
                    curl3);

                float clearance = EvaluateSegmentClearance(bone.position, childBone.position, padRadius, weaponTriangles);
                if (clearance <= 0.0004f)
                {
                    chosenCurl = clearance < -0.0012f && prevMinClearance > clearance ? angle - StepDeg : angle;
                    contacted = true;
                    break;
                }

                prevMinClearance = clearance;
                chosenCurl = angle;
            }

            if (jointIndex == 1)
            {
                curl1 = chosenCurl;
            }
            else
            {
                curl2 = chosenCurl;
            }

            ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl1, curl2, curl3);
            return contacted;
        }

        static bool SweepDistalCurlUntilContact(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            float spread,
            float curl1,
            float curl2,
            ref float curl3,
            float minCurl,
            float maxCurl,
            float padRadius,
            List<TriangleAvatarSpace> weaponTriangles)
        {
            Transform bone = finger.Distal;
            float startCurl = Mathf.Clamp(curl3, minCurl, maxCurl);
            ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl1, curl2, startCurl);

            float prevClearance = EvaluateSegmentClearance(bone.position, GetDistalTipWorld(finger, fingerAxes), padRadius, weaponTriangles);
            float chosenCurl = startCurl;
            bool contacted = false;

            const float StepDeg = 1.5f;
            for (float angle = startCurl + StepDeg; angle <= maxCurl; angle += StepDeg)
            {
                ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl1, curl2, angle);
                float clearance = EvaluateSegmentClearance(bone.position, GetDistalTipWorld(finger, fingerAxes), padRadius, weaponTriangles);

                if (clearance <= 0.0004f)
                {
                    chosenCurl = clearance < -0.0012f && prevClearance > clearance ? angle - StepDeg : angle;
                    contacted = true;
                    break;
                }

                prevClearance = clearance;
                chosenCurl = angle;
            }

            curl3 = chosenCurl;
            ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl1, curl2, curl3);
            return contacted;
        }

        static float EvaluateSegmentClearance(Vector3 segA, Vector3 segB, float padRadius, List<TriangleAvatarSpace> weaponTriangles)
        {
            Vector3 pMid = Vector3.Lerp(segA, segB, 0.55f);
            Vector3 pEnd = Vector3.Lerp(segA, segB, 0.90f);
            float dMid = SignedDistanceToWeaponSurface(pMid, weaponTriangles);
            float dEnd = SignedDistanceToWeaponSurface(pEnd, weaponTriangles);
            return Mathf.Min(dMid, dEnd) - padRadius;
        }

        static void DrawScene(UxrHandPoseEditorWindow window, SceneView sceneView)
        {
            if (window == null || window != _window || _avatar == null || _pose == null)
            {
                return;
            }

            if (window.ContactEditMode == UxrHandPoseContactEditMode.Manual)
            {
                return;
            }

            EnsureValidAvatarRigAxes(_avatar);

            UxrAvatarHand hand = _avatar.GetHand(_side);
            if (hand == null || hand.Wrist == null)
            {
                return;
            }

            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;

            Transform snapTransform = ResolveSnapTransform(_grabbable, _avatar, _grabPoint, _side);
            UxrGrabber grabber = ResolveGrabber(_avatar, _side);
            bool usePreviewSpace = snapTransform != null && grabber != null;

            Quaternion rotAvatarToPreview = usePreviewSpace
                ? snapTransform.rotation * Quaternion.Inverse(grabber.transform.rotation)
                : Quaternion.identity;
            Quaternion rotPreviewToAvatar = Quaternion.Inverse(rotAvatarToPreview);

            Vector3 ToPreview(Vector3 avatarWorldPt) => usePreviewSpace
                ? snapTransform.position + rotAvatarToPreview * (avatarWorldPt - grabber.transform.position)
                : avatarWorldPt;

            Vector3 ToAvatar(Vector3 previewWorldPt) => usePreviewSpace
                ? grabber.transform.position + rotPreviewToAvatar * (previewWorldPt - snapTransform.position)
                : previewWorldPt;

            Quaternion ToPreviewRot(Quaternion avatarWorldRot) => usePreviewSpace
                ? rotAvatarToPreview * avatarWorldRot
                : avatarWorldRot;

            var oldColor = Handles.color;
            var oldZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;

                Vector3 wristPos = ToPreview(hand.Wrist.position);

                for (int i = 0; i < FingerTypes.Length; i++)
                {
                    UxrFingerType fingerType = FingerTypes[i];
                    UxrAvatarFinger finger = hand.GetFinger(fingerType);
                    if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                    {
                        continue;
                    }

                    bool isSelected = i == _activeFingerIndex;
                    Vector3 pProximal = ToPreview(finger.Proximal.position);
                    Vector3 pIntermediate = ToPreview(finger.Intermediate.position);
                    Vector3 pDistal = ToPreview(finger.Distal.position);
                    Vector3 pTip = ToPreview(GetDistalTipWorld(finger, fingerAxes));

                    // Рисуем цепочку костей пальца поверх превью кисти
                    Handles.color = isSelected
                        ? new Color(0.25f, 0.95f, 0.55f, 0.95f)
                        : new Color(0.85f, 0.90f, 0.98f, 0.65f);

                    Handles.DrawAAPolyLine(isSelected ? 3.5f : 2.0f, wristPos, pProximal, pIntermediate, pDistal, pTip);

                    float jointRadius = HandleUtility.GetHandleSize(pDistal) * 0.025f;
                    Handles.SphereHandleCap(0, pProximal, Quaternion.identity, jointRadius, EventType.Repaint);
                    Handles.SphereHandleCap(0, pIntermediate, Quaternion.identity, jointRadius, EventType.Repaint);
                    Handles.SphereHandleCap(0, pDistal, Quaternion.identity, jointRadius * 1.15f, EventType.Repaint);

                    float tipHandleSize = HandleUtility.GetHandleSize(pTip) * (isSelected ? 0.065f : 0.055f);
                    Handles.color = isSelected
                        ? new Color(1.0f, 0.85f, 0.2f, 1.0f)
                        : new Color(0.3f, 0.85f, 1.0f, 0.9f);

                    if (isSelected || _showAllFingerHandles)
                    {
                        // Для активного пальца рисуем визуальную сферу и осевой PositionHandle без перекрытия ControlID
                        Handles.SphereHandleCap(0, pTip, Quaternion.identity, tipHandleSize, EventType.Repaint);

                        Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local
                            ? ToPreviewRot(finger.Distal.rotation)
                            : Quaternion.identity;

                        EditorGUI.BeginChangeCheck();
                        Vector3 newTipPreview = Handles.PositionHandle(pTip, handleRotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _activeFingerIndex = i;
                            Vector3 targetTipAvatarWorld = ToAvatar(newTipPreview);
                            SolveFingerIkAndCommit(fingerType, targetTipAvatarWorld);
                        }
                    }
                    else
                    {
                        // Для невыбранных пальцев клик по сфере на кончике пальца сразу делает палец активным
                        if (Handles.Button(pTip, Quaternion.identity, tipHandleSize, tipHandleSize * 1.25f, Handles.SphereHandleCap))
                        {
                            _activeFingerIndex = i;
                            _status = $"Выбран палец: {FingerNamesRu[i]}";
                            _window.Repaint();
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.zTest = oldZTest;
            }
        }

        static void SolveFingerIkAndCommit(
            UxrFingerType fingerType,
            Vector3 targetTipAvatarWorld)
        {
            if (_window == null || _avatar == null || _pose == null)
            {
                return;
            }

            EnsureEditableOwnerAvatarForCurrentPose();
            EnsureValidAvatarRigAxes(_avatar);

            if (!_window.TryCaptureContactEditContext(_side, out var sdkCtx, out string reason))
            {
                _status = reason;
                return;
            }

            if (sdkCtx.ReadOnly)
            {
                _status = "Поза унаследована от родительского префаба (read-only). Выберите корневой префаб CyborgHandsBase в качестве Target Avatar.";
                return;
            }

            UxrAvatarHand sourceHand = _avatar.GetHand(_side);
            UxrAvatarFinger sourceFinger = sourceHand?.GetFinger(fingerType);
            if (sourceFinger == null || sourceFinger.Proximal == null || sourceFinger.Intermediate == null || sourceFinger.Distal == null)
            {
                return;
            }

            UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;

            GameObject shadowRootGo = null;
            UxrHandDescriptor solvedDescriptor = null;

            try
            {
                UxrAvatarArm shadowArm = CreateShadowHand(sourceHand, out shadowRootGo);
                UxrAvatarHand shadowHand = shadowArm.Hand;
                Transform wrist = shadowHand.Wrist;
                UxrAvatarFinger finger = shadowHand.GetFinger(fingerType);

                bool isThumb = fingerType == UxrFingerType.Thumb;
                BindPoseFingerState bindState = CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes);
                GetFingerAngleBounds(_side, isThumb, bindState.BindSpread,
                    out float minSpread, out float maxSpread,
                    out float minCurl1, out float maxCurl1,
                    out float minCurl2, out float maxCurl2,
                    out float minCurl3, out float maxCurl3);

                float spread = Mathf.Clamp(
                    GetFingerAngle(wrist, finger.Proximal, wristAxes, fingerAxes, _side, isThumb, UxrFingerAngleType.Spread),
                    minSpread, maxSpread);
                float curl1 = Mathf.Clamp(
                    GetFingerAngle(wrist, finger.Proximal, wristAxes, fingerAxes, _side, isThumb, UxrFingerAngleType.Curl),
                    minCurl1, maxCurl1);
                float curl2 = Mathf.Clamp(
                    GetFingerAngle(finger.Proximal, finger.Intermediate, fingerAxes, fingerAxes, _side, false, UxrFingerAngleType.Curl),
                    minCurl2, maxCurl2);
                float curl3 = Mathf.Clamp(
                    GetFingerAngle(finger.Intermediate, finger.Distal, fingerAxes, fingerAxes, _side, false, UxrFingerAngleType.Curl),
                    minCurl3, maxCurl3);

                float initSpread = spread;
                float initCurl1 = curl1;
                float initCurl2 = curl2;
                float initCurl3 = curl3;

                float EvaluateObjective(float s, float c1, float c2, float c3)
                {
                    ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, s, c1, c2, c3);

                    Vector3 tip = GetDistalTipWorld(finger, fingerAxes);
                    float posErrSq = (tip - targetTipAvatarWorld).sqrMagnitude;

                    // Плавная анатомическая связка между средним и дистальным суставами + штраф за лишнее боковое отклонение корня
                    float couplingErr = (c3 - 0.68f * c2) * 0.00018f;
                    float spreadReg = (s - initSpread) * (s - initSpread) * 2.5e-7f;
                    float curlReg =
                        ((c1 - initCurl1) * (c1 - initCurl1) +
                         (c2 - initCurl2) * (c2 - initCurl2) +
                         (c3 - initCurl3) * (c3 - initCurl3)) * 1e-8f;

                    return posErrSq + couplingErr * couplingErr + spreadReg + curlReg;
                }

                float bestCost = EvaluateObjective(spread, curl1, curl2, curl3);
                float[] stepSizes = { 8.0f, 3.0f, 1.0f, 0.35f };

                foreach (float step in stepSizes)
                {
                    for (int iter = 0; iter < 5; iter++)
                    {
                        // 1. Сначала оптимизируем чистое сгибание суставов (Curl1, Curl2, Curl3) вдоль одной оси
                        float c1Plus = Mathf.Clamp(curl1 + step, minCurl1, maxCurl1);
                        float c1Minus = Mathf.Clamp(curl1 - step, minCurl1, maxCurl1);
                        float costPlus = EvaluateObjective(spread, c1Plus, curl2, curl3);
                        float costMinus = EvaluateObjective(spread, c1Minus, curl2, curl3);
                        if (costPlus < bestCost && costPlus <= costMinus)
                        {
                            curl1 = c1Plus;
                            bestCost = costPlus;
                        }
                        else if (costMinus < bestCost)
                        {
                            curl1 = c1Minus;
                            bestCost = costMinus;
                        }

                        // 2. Intermediate Curl (строго 1-DOF сгибание вокруг LocalRight)
                        float c2Plus = Mathf.Clamp(curl2 + step, minCurl2, maxCurl2);
                        float c2Minus = Mathf.Clamp(curl2 - step, minCurl2, maxCurl2);
                        costPlus = EvaluateObjective(spread, curl1, c2Plus, curl3);
                        costMinus = EvaluateObjective(spread, curl1, c2Minus, curl3);
                        if (costPlus < bestCost && costPlus <= costMinus)
                        {
                            curl2 = c2Plus;
                            bestCost = costPlus;
                        }
                        else if (costMinus < bestCost)
                        {
                            curl2 = c2Minus;
                            bestCost = costMinus;
                        }

                        // 3. Distal Curl (строго 1-DOF сгибание вокруг LocalRight)
                        float c3Plus = Mathf.Clamp(curl3 + step, minCurl3, maxCurl3);
                        float c3Minus = Mathf.Clamp(curl3 - step, minCurl3, maxCurl3);
                        costPlus = EvaluateObjective(spread, curl1, curl2, c3Plus);
                        costMinus = EvaluateObjective(spread, curl1, curl2, c3Minus);
                        if (costPlus < bestCost && costPlus <= costMinus)
                        {
                            curl3 = c3Plus;
                            bestCost = costPlus;
                        }
                        else if (costMinus < bestCost)
                        {
                            curl3 = c3Minus;
                            bestCost = costMinus;
                        }

                        // 4. В последнюю очередь — ограниченное боковое отведение корневой фаланги (Proximal Spread ±16°)
                        float spreadStep = step * 0.5f;
                        float sPlus = Mathf.Clamp(spread + spreadStep, minSpread, maxSpread);
                        float sMinus = Mathf.Clamp(spread - spreadStep, minSpread, maxSpread);
                        costPlus = EvaluateObjective(sPlus, curl1, curl2, curl3);
                        costMinus = EvaluateObjective(sMinus, curl1, curl2, curl3);
                        if (costPlus < bestCost && costPlus <= costMinus)
                        {
                            spread = sPlus;
                            bestCost = costPlus;
                        }
                        else if (costMinus < bestCost)
                        {
                            spread = sMinus;
                            bestCost = costMinus;
                        }
                    }
                }

                EvaluateObjective(spread, curl1, curl2, curl3);
                solvedDescriptor = new UxrHandDescriptor(shadowArm, wristAxes, fingerAxes);
            }
            catch (Exception ex)
            {
                _status = "Ошибка IK пальца: " + ex.Message;
                return;
            }
            finally
            {
                if (shadowRootGo != null)
                {
                    Object.DestroyImmediate(shadowRootGo);
                }
            }

            if (_window.TryCommitContactEdit(sdkCtx, solvedDescriptor, out string commitReason))
            {
                _status = $"IK ({FingerNamesRu[(int)fingerType]}): поза обновлена.";
                _window.Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                _status = commitReason;
            }
        }

        struct BindPoseFingerState
        {
            public Quaternion ProximalLocalRot;
            public Quaternion IntermediateLocalRot;
            public Quaternion DistalLocalRot;
            public float BindSpread;
            public float BindCurl1;
            public float BindCurl2;
            public float BindCurl3;
        }

        static BindPoseFingerState CaptureBindPoseFingerState(
            UxrAvatar avatar,
            UxrHandSide side,
            UxrFingerType fingerType,
            UxrUniversalLocalAxes wristAxes,
            UxrUniversalLocalAxes fingerAxes)
        {
            UxrAvatar bindAvatar = GetBindPoseAvatar(avatar) ?? avatar;
            UxrAvatarHand bindHand = bindAvatar != null ? bindAvatar.GetHand(side) : null;
            UxrAvatarFinger bindFinger = bindHand != null ? bindHand.GetFinger(fingerType) : null;

            if (bindFinger == null || bindFinger.Proximal == null || bindFinger.Intermediate == null || bindFinger.Distal == null)
            {
                UxrAvatarHand fallbackHand = avatar.GetHand(side);
                bindHand = fallbackHand;
                bindFinger = fallbackHand.GetFinger(fingerType);
            }

            bool isThumb = fingerType == UxrFingerType.Thumb;
            Transform wrist = bindHand.Wrist;

            return new BindPoseFingerState
            {
                ProximalLocalRot = bindFinger.Proximal.localRotation,
                IntermediateLocalRot = bindFinger.Intermediate.localRotation,
                DistalLocalRot = bindFinger.Distal.localRotation,
                BindSpread = GetFingerAngle(wrist, bindFinger.Proximal, wristAxes, fingerAxes, side, isThumb, UxrFingerAngleType.Spread),
                BindCurl1 = GetFingerAngle(wrist, bindFinger.Proximal, wristAxes, fingerAxes, side, isThumb, UxrFingerAngleType.Curl),
                BindCurl2 = GetFingerAngle(bindFinger.Proximal, bindFinger.Intermediate, fingerAxes, fingerAxes, side, false, UxrFingerAngleType.Curl),
                BindCurl3 = GetFingerAngle(bindFinger.Intermediate, bindFinger.Distal, fingerAxes, fingerAxes, side, false, UxrFingerAngleType.Curl)
            };
        }

        /// <summary>
        /// Применяет углы суставов пальца аналитически от нейтральной (bind) ориентации без накопления паразитного кручения (roll):
        /// - Корневая фаланга (Proximal) имеет ограниченное отведение вбок (Spread вокруг LocalUp) и сгибание (Curl1 вокруг LocalRight).
        /// - Средняя (Intermediate) и конечная (Distal) фаланги сгибаются строго вдоль одной оси (LocalRight), без бокового изгиба и без осевого скручивания.
        /// </summary>
        static void ApplyAnatomicalFingerAngles(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            float spread,
            float curl1,
            float curl2,
            float curl3)
        {
            Quaternion deltaSpread = Quaternion.AngleAxis(spread - bindState.BindSpread, fingerAxes.LocalUp);
            Quaternion deltaCurl1 = Quaternion.AngleAxis(curl1 - bindState.BindCurl1, fingerAxes.LocalRight);
            finger.Proximal.localRotation = bindState.ProximalLocalRot * deltaSpread * deltaCurl1;

            if (isThumb)
            {
                finger.Intermediate.localRotation = bindState.IntermediateLocalRot * Quaternion.AngleAxis(curl2 - bindState.BindCurl2, fingerAxes.LocalRight);
                finger.Distal.localRotation = bindState.DistalLocalRot * Quaternion.AngleAxis(curl3 - bindState.BindCurl3, fingerAxes.LocalRight);
            }
            else
            {
                // Для 4 основных пальцев средняя и дистальная фаланги являются чистыми 1-осевыми шарнирами (0° Spread, 0° Roll)
                finger.Intermediate.localRotation = Quaternion.AngleAxis(curl2, fingerAxes.LocalRight);
                finger.Distal.localRotation = Quaternion.AngleAxis(curl3, fingerAxes.LocalRight);
            }
        }

        static UxrAvatarArm CreateShadowHand(UxrAvatarHand sourceHand, out GameObject shadowRootGo)
        {
            shadowRootGo = new GameObject("__UxrHandPoseShadowRoot")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Transform sourceWrist = sourceHand.Wrist;
            if (sourceWrist.parent != null)
            {
                shadowRootGo.transform.position = sourceWrist.parent.position;
                shadowRootGo.transform.rotation = sourceWrist.parent.rotation;
                shadowRootGo.transform.localScale = sourceWrist.parent.lossyScale;
            }

            var boneMap = new Dictionary<Transform, Transform>(32);
            CloneTransformSubtree(sourceWrist, shadowRootGo.transform, boneMap);

            Transform MapBone(Transform src) => src != null && boneMap.TryGetValue(src, out Transform dst) ? dst : null;

            UxrAvatarFinger MapFinger(UxrAvatarFinger srcFinger)
            {
                if (srcFinger == null)
                {
                    return new UxrAvatarFinger();
                }

                return new UxrAvatarFinger
                {
                    Metacarpal = MapBone(srcFinger.Metacarpal),
                    Proximal = MapBone(srcFinger.Proximal),
                    Intermediate = MapBone(srcFinger.Intermediate),
                    Distal = MapBone(srcFinger.Distal)
                };
            }

            var shadowHand = new UxrAvatarHand
            {
                Wrist = MapBone(sourceWrist),
                Thumb = MapFinger(sourceHand.Thumb),
                Index = MapFinger(sourceHand.Index),
                Middle = MapFinger(sourceHand.Middle),
                Ring = MapFinger(sourceHand.Ring),
                Little = MapFinger(sourceHand.Little)
            };

            return new UxrAvatarArm
            {
                Hand = shadowHand
            };
        }

        static void CloneTransformSubtree(Transform source, Transform targetParent, Dictionary<Transform, Transform> boneMap)
        {
            var cloneGo = new GameObject(source.name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Transform cloneTransform = cloneGo.transform;
            cloneTransform.SetParent(targetParent, false);
            cloneTransform.localPosition = source.localPosition;
            cloneTransform.localRotation = source.localRotation;
            cloneTransform.localScale = source.localScale;

            boneMap[source] = cloneTransform;

            int childCount = source.childCount;
            for (int i = 0; i < childCount; i++)
            {
                CloneTransformSubtree(source.GetChild(i), cloneTransform, boneMap);
            }
        }

        static Vector3 GetDistalTipWorld(UxrAvatarFinger finger, UxrUniversalLocalAxes fingerAxes)
        {
            float midLen = Vector3.Distance(finger.Intermediate.position, finger.Distal.position);
            float distalLen = Mathf.Max(0.016f, midLen * 0.82f);
            Vector3 worldDir = finger.Distal.TransformDirection(fingerAxes.LocalForward).normalized;
            return finger.Distal.position + worldDir * distalLen;
        }

        static void GetFingerAngleBounds(
            UxrHandSide side,
            bool isThumb,
            float bindSpread,
            out float minSpread,
            out float maxSpread,
            out float minCurl1,
            out float maxCurl1,
            out float minCurl2,
            out float maxCurl2,
            out float minCurl3,
            out float maxCurl3)
        {
            if (isThumb)
            {
                // Корневой сустав большого пальца: умеренное отведение и сгибание в пределах Hand Pose Editor
                float hardMinSpread = side == UxrHandSide.Left ? -50f : -5f;
                float hardMaxSpread = side == UxrHandSide.Left ? 5f : 50f;
                minSpread = Mathf.Max(hardMinSpread, bindSpread - 18f);
                maxSpread = Mathf.Min(hardMaxSpread, bindSpread + 18f);
                minCurl1 = -10f;
                maxCurl1 = 40f;
                minCurl2 = -15f;
                maxCurl2 = 85f;
                minCurl3 = -10f;
                maxCurl3 = 80f;
            }
            else
            {
                // Боковое отведение (Spread) разрешено ТОЛЬКО корневой фаланге и строго ограничено ±16° от нейтрали
                const float MaxLateralSpreadDeg = 16f;
                minSpread = Mathf.Clamp(bindSpread - MaxLateralSpreadDeg, -20f, 20f);
                maxSpread = Mathf.Clamp(bindSpread + MaxLateralSpreadDeg, -20f, 20f);
                minCurl1 = -10f;
                maxCurl1 = 90f;
                minCurl2 = 0f;
                maxCurl2 = 100f;
                minCurl3 = 0f;
                maxCurl3 = 85f;
            }
        }

        /// <summary>
        /// Точный расчёт угла Spread/Curl по формуле <see cref="UxrFingerSpinner"/>.
        /// Гарантирует 100% совпадение углов при переключении между IK и ручным режимом Hand Pose Editor.
        /// </summary>
        static float GetFingerAngle(
            Transform parent,
            Transform target,
            UxrUniversalLocalAxes parentLocalAxes,
            UxrUniversalLocalAxes targetLocalAxes,
            UxrHandSide handSide,
            bool isThumbProximal,
            UxrFingerAngleType angleType)
        {
            Vector3 parentRight = isThumbProximal
                ? handSide == UxrHandSide.Left
                    ? parent.TransformDirection(-parentLocalAxes.LocalForward)
                    : parent.TransformDirection(parentLocalAxes.LocalForward)
                : parent.TransformDirection(parentLocalAxes.LocalRight);

            Vector3 parentUp = parent.TransformDirection(parentLocalAxes.LocalUp);

            Vector3 parentForward = isThumbProximal
                ? handSide == UxrHandSide.Left
                    ? parent.TransformDirection(parentLocalAxes.LocalRight)
                    : parent.TransformDirection(-parentLocalAxes.LocalRight)
                : parent.TransformDirection(parentLocalAxes.LocalForward);

            Vector3 targetRight = target.TransformDirection(targetLocalAxes.LocalRight);
            Vector3 targetUp = target.TransformDirection(targetLocalAxes.LocalUp);
            Vector3 targetForward = target.TransformDirection(targetLocalAxes.LocalForward);

            Quaternion toForward = Quaternion.FromToRotation(targetForward, parentForward);
            Vector3 rightOnlyRoll = toForward * targetRight;
            float roll = Vector3.SignedAngle(parentRight, rightOnlyRoll, parentForward);

            Quaternion removeRoll = Quaternion.AngleAxis(-roll, targetForward);

            if (angleType == UxrFingerAngleType.Spread)
            {
                Vector3 spreadAxis = removeRoll * targetUp;
                Vector3 planeRightNormal = parentRight;
                Vector3 spreadVectorOnPlane = Vector3.Cross(planeRightNormal, Vector3.ProjectOnPlane(spreadAxis, planeRightNormal));
                return Vector3.SignedAngle(spreadVectorOnPlane, targetForward, spreadAxis);
            }

            if (angleType == UxrFingerAngleType.Curl)
            {
                Vector3 curlAxis = removeRoll * targetRight;
                Vector3 planeUpNormal = parentUp;
                Vector3 curlVectorOnPlane = Vector3.Cross(Vector3.ProjectOnPlane(curlAxis, planeUpNormal), planeUpNormal);
                return Vector3.SignedAngle(curlVectorOnPlane, targetForward, curlAxis);
            }

            return 0f;
        }

        static List<TriangleAvatarSpace> CollectWeaponTrianglesInAvatarSpace(
            GameObject weaponRoot,
            Transform snapTransform,
            Transform grabberTransform)
        {
            var result = new List<TriangleAvatarSpace>(1024);
            if (weaponRoot == null || snapTransform == null || grabberTransform == null)
            {
                return result;
            }

            Quaternion rotPreviewToAvatar = grabberTransform.rotation * Quaternion.Inverse(snapTransform.rotation);
            Vector3 snapPos = snapTransform.position;
            Vector3 grabberPos = grabberTransform.position;
            const float MaxRadiusSq = 0.22f * 0.22f;

            Vector3 ToAvatar(Vector3 weaponWorldPt) => grabberPos + rotPreviewToAvatar * (weaponWorldPt - snapPos);

            foreach (MeshFilter mf in weaponRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                if (mf.GetComponent<UxrGrabbableObjectPreviewMesh>() != null ||
                    mf.GetComponentInParent<UxrGrabbableObjectPreviewMesh>() != null)
                {
                    continue;
                }

                var renderer = mf.GetComponent<MeshRenderer>();
                if (renderer != null && !renderer.enabled) continue;

                AppendMeshTriangles(mf.sharedMesh, mf.transform.localToWorldMatrix, snapPos, MaxRadiusSq, ToAvatar, result);
            }

            return result;
        }

        static void AppendMeshTriangles(
            Mesh mesh,
            Matrix4x4 localToWorld,
            Vector3 snapWorldPos,
            float maxRadiusSq,
            Func<Vector3, Vector3> toAvatar,
            List<TriangleAvatarSpace> output)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            if (vertices == null || indices == null || indices.Length < 3)
            {
                return;
            }

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 wA = localToWorld.MultiplyPoint3x4(vertices[indices[i]]);
                Vector3 wB = localToWorld.MultiplyPoint3x4(vertices[indices[i + 1]]);
                Vector3 wC = localToWorld.MultiplyPoint3x4(vertices[indices[i + 2]]);

                Vector3 center = (wA + wB + wC) * (1f / 3f);
                if ((center - snapWorldPos).sqrMagnitude > maxRadiusSq)
                {
                    continue;
                }

                Vector3 a = toAvatar(wA);
                Vector3 b = toAvatar(wB);
                Vector3 c = toAvatar(wC);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float areaSq = cross.sqrMagnitude;
                if (areaSq < 1e-12f)
                {
                    continue;
                }

                output.Add(new TriangleAvatarSpace
                {
                    A = a,
                    B = b,
                    C = c,
                    Normal = cross / Mathf.Sqrt(areaSq)
                });
            }
        }

        static float DistanceToWeaponSurface(Vector3 point, List<TriangleAvatarSpace> triangles, out Vector3 closestNormal)
        {
            float minSq = float.PositiveInfinity;
            closestNormal = Vector3.up;

            for (int i = 0; i < triangles.Count; i++)
            {
                TriangleAvatarSpace tri = triangles[i];
                Vector3 closest = ClosestPointOnTriangle(point, tri.A, tri.B, tri.C);
                Vector3 diff = point - closest;
                float sq = diff.sqrMagnitude;
                if (sq < minSq)
                {
                    minSq = sq;
                    closestNormal = tri.Normal;
                }
            }

            return Mathf.Sqrt(minSq);
        }

        static float SignedDistanceToWeaponSurface(Vector3 point, List<TriangleAvatarSpace> triangles)
        {
            float minSq = float.PositiveInfinity;
            Vector3 bestDiff = Vector3.zero;
            Vector3 bestNormal = Vector3.up;

            for (int i = 0; i < triangles.Count; i++)
            {
                TriangleAvatarSpace tri = triangles[i];
                Vector3 closest = ClosestPointOnTriangle(point, tri.A, tri.B, tri.C);
                Vector3 diff = point - closest;
                float sq = diff.sqrMagnitude;
                if (sq < minSq)
                {
                    minSq = sq;
                    bestDiff = diff;
                    bestNormal = tri.Normal;
                }
            }

            float dist = Mathf.Sqrt(minSq);
            float sign = Vector3.Dot(bestDiff, bestNormal) < -1e-5f ? -1f : 1f;
            return dist * sign;
        }

        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }
    }
}

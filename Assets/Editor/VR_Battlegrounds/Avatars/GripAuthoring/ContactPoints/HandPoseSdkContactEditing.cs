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
using VrBattlegrounds.Editor.GripAuthoring.Analysis;
using static VrBattlegrounds.Editor.GripAuthoring.Analysis.GripAuthoringGeometry;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
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

        static HandPoseSdkContactEditing()
        {
            UxrHandPoseEditorWindow.DiagnosticsGUI += DrawGUI;
            UxrHandPoseEditorWindow.DiagnosticsContextUpdated += OnWindowContextUpdated;
            UxrHandPoseEditorWindow.DiagnosticsClosed += ClearContext;
            UxrHandPoseEditorWindow.ContactEditingSceneGUI += DrawScene;
            GripAnalysisPanel.Bind(GetAnalysisHost, ResolveSnapTransform, ResolveGrabber);
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

        /// <summary>Текущий контекст панели для режима «Анализ» (только чтение).</summary>
        static GripAnalysisHost GetAnalysisHost()
        {
            return new GripAnalysisHost(_window, _avatar, _pose, _side, _grabbable, _grabPoint);
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
            // VR Battlegrounds patch 91: при галочке «Править позы базы через этот аватар» позу базы правим через
            // сценовый аватар-потомок с ригом, а не ищем владельца позы (у CyborgHandsBase нет рига и меша).
            if (UxrHandPoseEditorWindow.EditInheritedPosesThroughVariant)
            {
                UxrAvatar riggedVariant = ResolveRiggedSceneAvatarForPose(targetPose);
                if (riggedVariant != null)
                {
                    return riggedVariant;
                }
            }

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

        /// <summary>
        /// VR Battlegrounds patch 91: сценовый аватар с данными пальцев, у которого поза <paramref name="targetPose"/>
        /// разрешается по цепочке префабов в тот же ассет. Текущий аватар окна — первым, затем активные.
        /// </summary>
        static UxrAvatar ResolveRiggedSceneAvatarForPose(UxrHandPoseAsset targetPose)
        {
            bool Suitable(UxrAvatar a) =>
                a != null && !EditorUtility.IsPersistent(a.gameObject) &&
                a.AvatarRig != null && a.AvatarRig.HasFingerData() &&
                (targetPose == null || a.GetHandPose(targetPose.name, true) == targetPose);

            if (Suitable(_avatar))
            {
                return _avatar;
            }

            return Object.FindObjectsByType<UxrAvatar>(FindObjectsInactive.Include)
                .Where(Suitable)
                .OrderByDescending(a => a.gameObject.activeInHierarchy)
                .FirstOrDefault();
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

            // VR Battlegrounds patch 91: с галочкой унаследованная поза правится в текущем аватаре-потомке с ригом,
            // окно не переоткрывается на владельце без рига.
            if (_window.IsInheritedPoseEditableThroughVariant(_pose) && _avatar.AvatarRig != null && _avatar.AvatarRig.HasFingerData())
            {
                return true;
            }

            UxrAvatar ownerAvatar = ResolveBestEditableSceneAvatar(null, _pose);
            // VR Battlegrounds patch 91: с галочкой ownerAvatar — сценовый потомок с ригом, а не владелец позы.
            bool editThroughVariant = UxrHandPoseEditorWindow.EditInheritedPosesThroughVariant;
            if (ownerAvatar != null && ownerAvatar != _avatar && (ownerAvatar.GetHandPose(_pose.name, false) != null || editThroughVariant))
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

                return _avatar != null && (_avatar.GetHandPose(_pose.name, false) != null ||
                                           (_window != null && _window.IsInheritedPoseEditableThroughVariant(_pose)));
            }

            return false;
        }

        /// <summary>
        /// VR Battlegrounds patch 91: причина отказа для унаследованной позы — подсказка про галочку окна,
        /// а не про выбор CyborgHandsBase (у базы нет рига и меша).
        /// </summary>
        static string InheritedPoseReadOnlyStatus()
        {
            UxrAvatar owner = _window != null ? _window.GetInheritedPoseOwner(_pose) : null;
            string ownerName = owner != null ? owner.name : "родительского префаба";
            if (UxrHandPoseEditorWindow.IsSdkSharedPose(_pose))
            {
                return $"Поза {(_pose != null ? _pose.name : string.Empty)} — общая поза SDK (Assets/ThirdParty): правка запрещена.";
            }

            return $"Поза унаследована от {ownerName} (read-only). Включите в Hand Pose Editor галочку " +
                   "«Править позы базы через этот аватар»: правка пойдёт в ассет позы базы через риг этого аватара-потомка.";
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

        static void DrawGUI(UxrHandPoseEditorWindow window)
        {
            if (window == null)
            {
                return;
            }

            DrawContactPanel(window);

            // Режим «Анализ»: блок результатов под панелью автоснапа, на ту же ширину
            if (GripAnalysisPanel.IsEnabled)
            {
                GripAnalysisPanel.DrawColumn(window);
            }
        }

        static void DrawContactPanel(UxrHandPoseEditorWindow window)
        {
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

                using (new EditorGUI.DisabledScope(!canAutoWrap))
                {
                    DrawWrapSettings();
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
                        // Явное снятие осевого скручивания корневой фаланги без перетаскивания (IK снимает его и сам на первом шаге)
                        int fingerIndex = Mathf.Clamp(_activeFingerIndex, 0, FingerNamesRu.Length - 1);
                        bool activeIsThumb = FingerTypes[fingerIndex] == UxrFingerType.Thumb;
                        using (new EditorGUI.DisabledScope(activeIsThumb))
                        {
                            var removeOneContent = new GUIContent(
                                activeIsThumb ? "у большого пальца скручивание допустимо" : $"Убрать скручивание: {FingerNamesRu[fingerIndex]}",
                                "Обнулить осевое скручивание проксимальной фаланги и тем же IK вернуть кончик на место (Spread и Curl в пределах).");
                            if (GUILayout.Button(removeOneContent))
                            {
                                RemoveProximalTwistAndCommit(false);
                            }
                        }

                        var removeAllContent = new GUIContent(
                            "Убрать скручивание: все пальцы",
                            "То же для указательного, среднего, безымянного и мизинца; большой палец исключён (у сустава CMC скручивание естественно).");
                        if (GUILayout.Button(removeAllContent))
                        {
                            RemoveProximalTwistAndCommit(true);
                        }
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

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool showBones = EditorGUILayout.ToggleLeft(
                            new GUIContent("Показать кости пальца (позиция и вращение)",
                                "Для активного пальца: localPosition/localRotation каждой кости, оси анатомической рамки в SceneView и bind для сравнения."),
                            _showFingerBones);
                        if (showBones != _showFingerBones)
                        {
                            _showFingerBones = showBones;
                            EditorPrefs.SetBool(ShowFingerBonesPrefKey, showBones);
                            SceneView.RepaintAll();
                        }
                    }

                    if (_showFingerBones)
                    {
                        DrawFingerBonesTable();
                    }
                }

                GripAnalysisPanel.DrawToggle(window);

                if (!string.IsNullOrEmpty(_status))
                {
                    EditorGUILayout.HelpBox(_status, MessageType.None);
                }
            }
        }

        // ---------- «Обхватить»: равномерное сворачивание суставов до касания по пробам анализа ----------

        const string WrapWeightsPrefKey = "VrBattlegrounds.HandPoseContact.WrapWeights";
        const string WrapToleranceMmPrefKey = "VrBattlegrounds.HandPoseContact.WrapToleranceMm";
        // Шаг сворачивания одного сустава за ход, °
        const float WrapStepDeg = 0.75f;
        // Откат к минимуму расстояния (точка прошла сквозь дыру или край меша) считается касанием только ближе этого, мм
        const float WrapCaptureMm = 3f;
        // Рост расстояния после спада, с которого считается, что минимум пройден, мм
        const float WrapRiseMm = 0.1f;
        const int WrapMaxMoves = 4000;
        static readonly string[] SegmentNamesShortRu = { "прокс.", "средн.", "дист." };

        static Vector3 _wrapWeights = LoadWrapWeights();
        static float _wrapToleranceMm = EditorPrefs.GetFloat(WrapToleranceMmPrefKey, 0.5f);

        static Vector3 LoadWrapWeights()
        {
            string raw = EditorPrefs.GetString(WrapWeightsPrefKey, "1;1;1");
            string[] parts = raw.Split(';');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float a) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float c))
            {
                return new Vector3(Mathf.Max(0f, a), Mathf.Max(0f, b), Mathf.Max(0f, c));
            }

            return Vector3.one;
        }

        static void SaveWrapWeights(Vector3 weights)
        {
            _wrapWeights = new Vector3(Mathf.Max(0f, weights.x), Mathf.Max(0f, weights.y), Mathf.Max(0f, weights.z));
            EditorPrefs.SetString(WrapWeightsPrefKey, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0};{1};{2}",
                _wrapWeights.x, _wrapWeights.y, _wrapWeights.z));
        }

        /// <summary>Строка параметров «Обхватить» под кнопкой: веса сгиба суставов и допуск касания.</summary>
        static void DrawWrapSettings()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Веса сгиба П/С/Д",
                    "Доля угла, которую получает сустав за ход «Обхватить»: проксимальный / средний / дистальный. 1:1:1 — равномерно, как спиннеры SDK; 0 — сустав не сгибается."),
                    GUILayout.Width(110));
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 14f;
                float wp = EditorGUILayout.FloatField("П", _wrapWeights.x, GUILayout.MinWidth(40));
                float wm = EditorGUILayout.FloatField("С", _wrapWeights.y, GUILayout.MinWidth(40));
                float wd = EditorGUILayout.FloatField("Д", _wrapWeights.z, GUILayout.MinWidth(40));
                EditorGUIUtility.labelWidth = 70f;
                float tol = EditorGUILayout.FloatField(new GUIContent("Допуск, мм", "Касание: расстояние пробы до поверхности не больше допуска."),
                    _wrapToleranceMm, GUILayout.MinWidth(110));
                EditorGUIUtility.labelWidth = labelWidth;

                var weights = new Vector3(wp, wm, wd);
                if (weights != _wrapWeights)
                {
                    SaveWrapWeights(weights);
                }

                tol = Mathf.Clamp(tol, 0f, 10f);
                if (!Mathf.Approximately(tol, _wrapToleranceMm))
                {
                    _wrapToleranceMm = tol;
                    EditorPrefs.SetFloat(WrapToleranceMmPrefKey, tol);
                }
            }
        }

        /// <summary>Пробы одной фаланги в системе предмета: те же, что у анализа (кольцо в доле t из профиля кисти, K1 — его ладонная точка, у конечной ещё кончик X2).</summary>
        struct WrapSegmentProbe
        {
            public float MinDistance;
            public bool Penetration;
        }

        sealed class WrapContext
        {
            public GripSurfaceCache Cache;
            public Func<Vector3, Vector3> ToObject;
            public Func<Vector3, Vector3> DirToObject;
            public float WindingNear;
            public float WindingRadius;
            public float[] RingT;
        }

        /// <summary>
        /// Радиус (м) и доля t кольца фаланги для проб «Обхватить» — единственная точка выбора.
        /// Радиус — из профиля кисти (умолчание 6,8/5,8/5,0 мм), t — из профиля с учётом черновика колец.
        /// </summary>
        static void GetWrapRing(WrapContext ctx, int fingerIndex, int segment, out float radius, out float t)
        {
            radius = _avatar != null
                ? GripHandProbeProfiles.GetRingRadiusMeters(_avatar, _side, FingerTypes[fingerIndex], segment)
                : GetPadRadius(segment);
            int ringIndex = fingerIndex * 3 + segment;
            t = GripHandProbeSide.ClampRingT(ctx.RingT != null && ringIndex < ctx.RingT.Length ? ctx.RingT[ringIndex] : GripHandProbeSide.DefaultRingT);
        }

        static WrapSegmentProbe EvaluateWrapSegment(WrapContext ctx, UxrAvatarFinger finger, int fingerIndex, int segment, UxrUniversalLocalAxes fingerAxes)
        {
            var result = new WrapSegmentProbe { MinDistance = float.PositiveInfinity };
            Transform[] bones = { finger.Proximal, finger.Intermediate, finger.Distal };
            Vector3 tipWorld = GetDistalTipWorld(finger, fingerAxes);
            Vector3 startWorld = bones[segment].position;
            Vector3 endWorld = segment < 2 ? bones[segment + 1].position : tipWorld;

            Vector3 a = ctx.ToObject(startWorld);
            Vector3 b = ctx.ToObject(endWorld);
            Vector3 axis = b - a;
            float length = axis.magnitude;
            if (length < 1e-5f)
            {
                return result;
            }

            axis /= length;
            // Ладонная сторона подушечки — −LocalUp кости, как у проб анализа
            Vector3 radial = Vector3.ProjectOnPlane(ctx.DirToObject(-bones[segment].TransformVector(fingerAxes.LocalUp)), axis);
            if (radial.sqrMagnitude < 1e-8f)
            {
                return result;
            }

            radial.Normalize();
            Vector3 binormal = Vector3.Cross(axis, radial);
            GetWrapRing(ctx, fingerIndex, segment, out float radius, out float t);
            Vector3 center = Vector3.Lerp(a, b, t);

            // Рамка кольца из профиля кисти (t, радиус, наклон, азимут точки касания) — та же, что у анализа
            if (_avatar != null &&
                GripHandProbeProfiles.GetRingFrame(_avatar, _side, FingerTypes[fingerIndex], segment, bones[segment], startWorld, endWorld,
                                                   out Vector3 ringCenterWorld, out Vector3 ringNormalWorld, out Vector3 contactDirWorld, out float ringRadius))
            {
                Vector3 ringNormal = ctx.DirToObject(ringNormalWorld).normalized;
                Vector3 contactDir = Vector3.ProjectOnPlane(ctx.DirToObject(contactDirWorld), ringNormal);
                if (contactDir.sqrMagnitude > 1e-8f)
                {
                    radial = contactDir.normalized;
                    binormal = Vector3.Cross(ringNormal, radial);
                    center = ctx.ToObject(ringCenterWorld);
                    radius = ringRadius;
                }
            }

            void Probe(Vector3 point)
            {
                GripSurfaceHit hit = ctx.Cache.Query(point, ctx.WindingNear, ctx.WindingRadius);
                if (!hit.Valid)
                {
                    return;
                }

                result.MinDistance = Mathf.Min(result.MinDistance, hit.Distance);
                // Знак берём только надёжный (winding): у меша с дырами знак по нормали может врать
                if (hit.Signed < 0f && hit.Method == GripSignMethod.Winding)
                {
                    result.Penetration = true;
                }
            }

            // Кольцо из 6 точек; 0° — ладонная точка K1
            for (int k = 0; k < 6; k++)
            {
                float rad = k * 60f * Mathf.Deg2Rad;
                Probe(center + (radial * Mathf.Cos(rad) + binormal * Mathf.Sin(rad)) * radius);
            }

            if (segment == 2)
            {
                Probe(ctx.ToObject(tipWorld)); // кончик X2
            }

            return result;
        }

        sealed class WrapFingerResult
        {
            public readonly bool[] Contact = new bool[3];
            public readonly bool[] AtLimit = new bool[3];
            public readonly bool[] ByMinimum = new bool[3];
            public float Curl1;
            public float Curl2;
            public float Curl3;
        }

        /// <summary>
        /// Сворачивает суставы пальца по кругу (прокс. → средн. → дист. → …) шагом WrapStepDeg·вес от открытого положения.
        /// Касание фаланги: беззнаковое расстояние любой её пробы ≤ допуска или надёжный (winding) отрицательный знак; если
        /// расстояние после спада начало расти ближе WrapCaptureMm (точка прошла сквозь дыру или край меша) — откат к шагу
        /// с минимумом, тоже касание. Коснувшаяся фаланга блокирует свой сустав и все проксимальнее, дистальные продолжают.
        /// Сустав, упёршийся в предел GetFingerAngleBounds, фиксируется. Spread — из текущей позы; Twist у четырёх пальцев 0,
        /// у большого — текущий (CMC). Пястная не меняется.
        /// </summary>
        static WrapFingerResult WrapFinger(WrapContext ctx, UxrAvatarFinger finger, int fingerIndex, UxrFingerType fingerType,
            UxrUniversalLocalAxes wristAxes, UxrUniversalLocalAxes fingerAxes)
        {
            bool isThumb = fingerType == UxrFingerType.Thumb;
            BindPoseFingerState bindState = CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes);
            GetFingerAngleBounds(_side, isThumb, bindState.BindSpread,
                out float minSpread, out float maxSpread,
                out float minCurl1, out float maxCurl1,
                out float minCurl2, out float maxCurl2,
                out float minCurl3, out float maxCurl3);

            MeasureAnatomicalFingerAngles(finger, bindState, fingerAxes, out float measuredSpread, out _, out _, out _);
            float spread = Mathf.Clamp(measuredSpread, minSpread, maxSpread);
            float twist = isThumb ? MeasureProximalTwist(finger, bindState, fingerAxes) : 0f;

            float[] minCurl = { minCurl1, minCurl2, minCurl3 };
            float[] maxCurl = { maxCurl1, maxCurl2, maxCurl3 };
            float[] weights = { _wrapWeights.x, _wrapWeights.y, _wrapWeights.z };
            // Открытое положение — bind-углы в пределах
            float[] curl =
            {
                Mathf.Clamp(bindState.BindCurl1, minCurl1, maxCurl1),
                Mathf.Clamp(bindState.BindCurl2, minCurl2, maxCurl2),
                Mathf.Clamp(bindState.BindCurl3, minCurl3, maxCurl3)
            };

            var result = new WrapFingerResult();
            bool[] locked = new bool[3];
            float[] minDistance = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            var curlAtMinimum = new float[3][];
            float tolerance = _wrapToleranceMm * 0.001f;
            float capture = WrapCaptureMm * 0.001f;
            float rise = WrapRiseMm * 0.001f;

            void Apply() => ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, spread, curl[0], curl[1], curl[2], twist);

            void LockThrough(int segment)
            {
                for (int j = 0; j <= segment; j++)
                {
                    locked[j] = true;
                }
            }

            for (int j = 0; j < 3; j++)
            {
                if (weights[j] <= 0f)
                {
                    locked[j] = true;
                }
            }

            // Проверка касаний после хода; true — был откат к минимуму (поза изменилась)
            bool CheckContacts()
            {
                for (int s = 0; s < 3; s++)
                {
                    if (result.Contact[s])
                    {
                        continue;
                    }

                    WrapSegmentProbe probe = EvaluateWrapSegment(ctx, finger, fingerIndex, s, fingerAxes);
                    if (probe.MinDistance <= tolerance || probe.Penetration)
                    {
                        result.Contact[s] = true;
                        LockThrough(s);
                        continue;
                    }

                    if (probe.MinDistance < minDistance[s])
                    {
                        minDistance[s] = probe.MinDistance;
                        curlAtMinimum[s] = (float[])curl.Clone();
                    }
                    else if (probe.MinDistance > minDistance[s] + rise && minDistance[s] <= capture && curlAtMinimum[s] != null)
                    {
                        // Минимум пройден у самой поверхности: откат к нему, это касание (дыра или край меша)
                        Array.Copy(curlAtMinimum[s], curl, 3);
                        result.Contact[s] = true;
                        result.ByMinimum[s] = true;
                        LockThrough(s);
                        Apply();
                        return true;
                    }
                }

                return false;
            }

            Apply();
            CheckContacts();

            int joint = 0;
            for (int move = 0; move < WrapMaxMoves; move++)
            {
                int j = -1;
                for (int k = 0; k < 3; k++)
                {
                    int candidate = (joint + k) % 3;
                    if (!locked[candidate])
                    {
                        j = candidate;
                        break;
                    }
                }

                if (j < 0)
                {
                    break;
                }

                joint = (j + 1) % 3;
                float next = curl[j] + WrapStepDeg * weights[j];
                if (next >= maxCurl[j])
                {
                    next = maxCurl[j];
                    locked[j] = true;
                    result.AtLimit[j] = true;
                }

                curl[j] = next;
                Apply();
                CheckContacts();
            }

            Apply();
            result.Curl1 = curl[0];
            result.Curl2 = curl[1];
            result.Curl3 = curl[2];
            return result;
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
                _status = InheritedPoseReadOnlyStatus(); // VR Battlegrounds patch 91
                return;
            }

            Transform snapTransform = ResolveSnapTransform(_grabbable, _avatar, _grabPoint, _side);
            UxrGrabber grabber = ResolveGrabber(_avatar, _side);
            if (snapTransform == null || grabber == null)
            {
                _status = "Не найден SnapTransform (Grip Align) или UxrGrabber для выбранной кисти.";
                return;
            }

            // 1. Поверхность предмета — тот же набор и тот же сеточный кэш, что у анализа (система корня предмета без масштаба)
            Transform root = _grabbable.transform;
            Matrix4x4 objectToWorld = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
            Matrix4x4 worldToObject = objectToWorld.inverse;
            var cache = new GripSurfaceCache();
            cache.Rebuild(CollectWeaponTrianglesInObjectSpace(_grabbable.gameObject, snapTransform, worldToObject), 0UL);
            if (cache.Count == 0)
            {
                _status = "На предмете не найдены меши рядом с точкой хвата.";
                return;
            }

            Quaternion rotAvatarToPreview = snapTransform.rotation * Quaternion.Inverse(grabber.transform.rotation);
            Vector3 snapPos = snapTransform.position;
            Vector3 grabberPos = grabber.transform.position;
            var ctx = new WrapContext
            {
                Cache = cache,
                ToObject = p => worldToObject.MultiplyPoint3x4(snapPos + rotAvatarToPreview * (p - grabberPos)),
                DirToObject = d => worldToObject.MultiplyVector(rotAvatarToPreview * d),
                WindingNear = GripAnalysisSettings.WindingNearMm.Value * 0.001f,
                WindingRadius = GripAnalysisSettings.WindingRadiusMm.Value * 0.001f,
                RingT = GripRingEditing.GetEffective(GetAnalysisHost(), GripHandProbeProfiles.Find(_avatar))
            };

            // 2. Теневая копия кисти: живые кости не меняются до TryCommitContactEdit
            UxrAvatarHand sourceHand = _avatar.GetHand(_side);
            UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;

            GameObject shadowRootGo = null;
            UxrHandDescriptor wrappedDescriptor = null;
            int contactedSegments = 0;
            var report = new List<string>();

            try
            {
                UxrAvatarArm shadowArm = CreateShadowHand(sourceHand, out shadowRootGo);
                UxrAvatarHand shadowHand = shadowArm.Hand;

                // 3. Каждый палец отдельно, большой — по тому же правилу со своими пределами
                for (int i = 0; i < FingerTypes.Length; i++)
                {
                    UxrFingerType fingerType = FingerTypes[i];
                    UxrAvatarFinger finger = shadowHand.GetFinger(fingerType);
                    if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                    {
                        report.Add($"{FingerNamesRu[i]}: n/a (нет костей)");
                        continue;
                    }

                    WrapFingerResult r = WrapFinger(ctx, finger, i, fingerType, wristAxes, fingerAxes);
                    var touched = new List<string>(3);
                    for (int s = 0; s < 3; s++)
                    {
                        if (r.Contact[s])
                        {
                            contactedSegments++;
                            touched.Add(SegmentNamesShortRu[s] + (r.ByMinimum[s] ? "*" : ""));
                        }
                    }

                    string limits = r.AtLimit[0] || r.AtLimit[1] || r.AtLimit[2]
                        ? " · предел: " + string.Join(", ", Enumerable.Range(0, 3).Where(s => r.AtLimit[s]).Select(s => SegmentNamesShortRu[s]))
                        : "";
                    report.Add($"{FingerNamesRu[i]}: касание {(touched.Count > 0 ? string.Join(", ", touched) : "нет")} · Curl1 {r.Curl1:0.0}° Curl2 {r.Curl2:0.0}° Curl3 {r.Curl3:0.0}°{limits}");
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

            // 4. Фиксация штатным путём SDK (одна группа Undo, сохранение позы, обновление превью)
            if (_window.TryCommitContactEdit(sdkCtx, wrappedDescriptor, out string commitReason))
            {
                _status = $"Обхват выполнен ({contactedSegments}/15 фаланг в контакте; * — касание по минимуму у дыры/края меша):\n" + string.Join("\n", report);
                _window.Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                _status = commitReason;
            }
        }

        static void DrawScene(UxrHandPoseEditorWindow window, SceneView sceneView)
        {
            if (window == null || window != _window || _avatar == null || _pose == null)
            {
                return;
            }

            // Пробы анализа рисуются в любом режиме, в том числе при ручных спиннерах
            GripAnalysisPanel.DrawScene(window, sceneView);

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

                if (_showFingerBones)
                {
                    UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
                    DrawFingerBoneIndicators(hand, FingerTypes[Mathf.Clamp(_activeFingerIndex, 0, FingerTypes.Length - 1)],
                        wristAxes, fingerAxes, ToPreview, ToPreviewRot);
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.zTest = oldZTest;
            }
        }

        // ---------- Индикаторы костей активного пальца (локальная позиция/вращение и анатомические углы) ----------

        const string ShowFingerBonesPrefKey = "VrBattlegrounds.HandPoseContact.ShowFingerBones";
        const float TwistChangeEpsilonDeg = 0.05f;
        const string HighlightColor = "#ffb020";

        static readonly string[] AnatomyNames = { "Spread", "Curl1", "Twist", "Curl2", "Curl3", "вне шарнира средн.", "вне шарнира дист.", "пястная Δbind" };

        /// <summary>Показания одной кости: как в инспекторе Unity (localPosition, localRotation) плюс анатомические углы.</summary>
        sealed class BoneReadout
        {
            public string Name;
            public Transform Bone;
            public Quaternion BindLocalRotation;
            public bool HasBind;
            public Vector3 LocalPositionMm;
            public Vector3 InspectorEuler;
            public Quaternion LocalRotation;
            public string Anatomy;
        }

        sealed class FingerReadout
        {
            public UxrFingerType FingerType;
            public readonly List<BoneReadout> Bones = new List<BoneReadout>(4);
            public float[] Anatomy;
            public Vector3 TipWorld;
            public Vector3 TipWristMm;
            public bool BindIsCurrentPose;
        }

        /// <summary>Итог последнего шага IK для активного пальца: что изменилось сильнее всего.</summary>
        sealed class IkStepReport
        {
            public UxrFingerType FingerType;
            public string AnatomySummary;
            public bool TwistChanged;
            public bool TwistRemoved;
            public float TwistBefore;
            public float TwistAfter;
            public float TwistDelta;
            public readonly Dictionary<string, int> EulerAxisByBone = new Dictionary<string, int>();
            public string EulerSummary;
        }

        static bool _showFingerBones = EditorPrefs.GetBool(ShowFingerBonesPrefKey, false);
        static IkStepReport _lastIkStep;
        static readonly Dictionary<long, (BindPoseFingerState State, bool IsCurrentPose)> BindStateCache = new Dictionary<long, (BindPoseFingerState, bool)>();
        static GUIStyle _boneLabelStyle;
        static GUIStyle _boneTableStyle;
        static Texture2D _boneLabelBackground;

        static (BindPoseFingerState State, bool IsCurrentPose) GetCachedBindState(UxrFingerType fingerType, UxrUniversalLocalAxes wristAxes, UxrUniversalLocalAxes fingerAxes)
        {
            long key = ((long)_avatar.GetInstanceID() << 8) ^ ((long)_side << 4) ^ (long)fingerType;
            if (BindStateCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (BindStateCache.Count > 64)
            {
                BindStateCache.Clear();
            }

            // Тот же bind, что у IK; если bind-аватара нет, IK тоже берёт текущую позу — показываем это явно
            bool strict = TryCaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes, out BindPoseFingerState state, out _);
            var entry = strict
                ? (state, false)
                : (CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes), true);
            BindStateCache[key] = entry;
            return entry;
        }

        static Vector3 GetInspectorEuler(Transform bone)
        {
            // Те же значения, что поле Rotation в инспекторе (с учётом подсказки Euler-углов)
            return TransformUtils.GetInspectorRotation(bone);
        }

        static FingerReadout ComputeFingerReadout(UxrAvatarHand hand, UxrFingerType fingerType, UxrUniversalLocalAxes wristAxes, UxrUniversalLocalAxes fingerAxes)
        {
            UxrAvatarFinger finger = hand?.GetFinger(fingerType);
            if (hand == null || hand.Wrist == null || finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
            {
                return null;
            }

            bool isThumb = fingerType == UxrFingerType.Thumb;
            var (bind, bindIsCurrentPose) = GetCachedBindState(fingerType, wristAxes, fingerAxes);
            GetFingerAngleBounds(_side, isThumb, bind.BindSpread,
                out float minSpread, out float maxSpread,
                out float minCurl1, out float maxCurl1,
                out float minCurl2, out float maxCurl2,
                out float minCurl3, out float maxCurl3);

            MeasureAnatomicalFingerAngles(finger, bind, fingerAxes, out float spread, out float curl1, out float curl2, out float curl3);
            float twist = MeasureProximalTwist(finger, bind, fingerAxes);
            GetHingeResidual(finger.Intermediate.localRotation, bind.IntermediateLocalRot, fingerAxes, out _, out _, out float residual2);
            GetHingeResidual(finger.Distal.localRotation, bind.DistalLocalRot, fingerAxes, out _, out _, out float residual3);
            bool hasMetacarpal = finger.Metacarpal != null;
            float metacarpalDelta = hasMetacarpal && bind.HasMetacarpal
                ? Quaternion.Angle(bind.MetacarpalLocalRot, finger.Metacarpal.localRotation)
                : float.NaN;

            var readout = new FingerReadout
            {
                FingerType = fingerType,
                Anatomy = new[] { spread, curl1, twist, curl2, curl3, residual2, residual3, metacarpalDelta },
                BindIsCurrentPose = bindIsCurrentPose
            };

            BoneReadout Make(string name, Transform bone, bool hasBind, Quaternion bindLocal, string anatomy) => new BoneReadout
            {
                Name = name,
                Bone = bone,
                HasBind = hasBind,
                BindLocalRotation = bindLocal,
                LocalPositionMm = bone.localPosition * 1000f,
                InspectorEuler = GetInspectorEuler(bone),
                LocalRotation = bone.localRotation,
                Anatomy = anatomy
            };

            if (hasMetacarpal)
            {
                readout.Bones.Add(Make("Пястная", finger.Metacarpal, bind.HasMetacarpal, bind.MetacarpalLocalRot,
                    bind.HasMetacarpal ? $"Δbind {metacarpalDelta:0.0}°" : "Δbind n/a (нет у bind)"));
            }

            string twistNote = isThumb ? " (CMC: допустимо)" : "";
            readout.Bones.Add(Make("Проксимальная", finger.Proximal, true, bind.ProximalLocalRot,
                $"Spread {spread:0.0}° (bind {bind.BindSpread:0.0}°, предел [{minSpread:0.#}..{maxSpread:0.#}]) · Curl1 {curl1:0.0}° [{minCurl1:0.#}..{maxCurl1:0.#}] · Twist {twist:+0.0;-0.0}°{twistNote}"));
            readout.Bones.Add(Make("Средняя", finger.Intermediate, true, bind.IntermediateLocalRot,
                $"Curl2 {curl2:0.0}° [{minCurl2:0.#}..{maxCurl2:0.#}] · вне шарнира {residual2:0.0}°"));
            readout.Bones.Add(Make("Конечная", finger.Distal, true, bind.DistalLocalRot,
                $"Curl3 {curl3:0.0}° [{minCurl3:0.#}..{maxCurl3:0.#}] · вне шарнира {residual3:0.0}°"));

            readout.TipWorld = GetDistalTipWorld(finger, fingerAxes);
            readout.TipWristMm = Quaternion.Inverse(hand.Wrist.rotation) * (readout.TipWorld - hand.Wrist.position) * 1000f;
            return readout;
        }

        /// <summary>Сравнивает показания до и после шага IK: какой анатомический угол и какая компонента Euler изменились сильнее всего.</summary>
        static void RecordIkStep(FingerReadout before, FingerReadout after)
        {
            if (before == null || after == null || before.FingerType != after.FingerType)
            {
                return;
            }

            var report = new IkStepReport { FingerType = after.FingerType };

            int bestAnatomy = -1;
            float bestAnatomyDelta = 0f;
            for (int i = 0; i < AnatomyNames.Length; i++)
            {
                if (float.IsNaN(before.Anatomy[i]) || float.IsNaN(after.Anatomy[i]))
                {
                    continue;
                }

                float delta = Mathf.Abs(Mathf.DeltaAngle(before.Anatomy[i], after.Anatomy[i]));
                if (delta > bestAnatomyDelta)
                {
                    bestAnatomyDelta = delta;
                    bestAnatomy = i;
                }
            }

            report.TwistDelta = Mathf.DeltaAngle(before.Anatomy[2], after.Anatomy[2]);
            bool isThumbStep = after.FingerType == UxrFingerType.Thumb;
            report.TwistBefore = before.Anatomy[2];
            report.TwistAfter = after.Anatomy[2];
            // Ошибка — если после шага IK у четырёх пальцев остался или появился Twist; снятие до 0 — штатно
            report.TwistChanged = !isThumbStep && Mathf.Abs(report.TwistAfter) > TwistChangeEpsilonDeg;
            report.TwistRemoved = !isThumbStep && !report.TwistChanged && Mathf.Abs(report.TwistBefore) > TwistChangeEpsilonDeg;
            report.AnatomySummary = bestAnatomy < 0
                ? "анатомические углы не изменились"
                : $"сильнее всего: {AnatomyNames[bestAnatomy]} {Mathf.DeltaAngle(before.Anatomy[bestAnatomy], after.Anatomy[bestAnatomy]):+0.00;-0.00}°";

            string[] axisNames = { "X", "Y", "Z" };
            string bestEulerBone = null;
            int bestEulerAxis = -1;
            float bestEulerDelta = 0f;
            for (int b = 0; b < after.Bones.Count && b < before.Bones.Count; b++)
            {
                Vector3 e0 = before.Bones[b].InspectorEuler;
                Vector3 e1 = after.Bones[b].InspectorEuler;
                int axis = -1;
                float axisDelta = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(e0[k], e1[k]));
                    if (d > axisDelta)
                    {
                        axisDelta = d;
                        axis = k;
                    }
                }

                if (axis >= 0 && axisDelta > 0.01f)
                {
                    report.EulerAxisByBone[after.Bones[b].Name] = axis;
                    if (axisDelta > bestEulerDelta)
                    {
                        bestEulerDelta = axisDelta;
                        bestEulerAxis = axis;
                        bestEulerBone = after.Bones[b].Name;
                    }
                }
            }

            report.EulerSummary = bestEulerBone == null
                ? "локальные повороты не изменились"
                : $"локально сильнее всего: {bestEulerBone} rot.{axisNames[bestEulerAxis]} на {bestEulerDelta:0.00}°";
            _lastIkStep = report;
        }

        static string FormatEuler(Vector3 euler, int highlightAxis)
        {
            string C(int k) => k == highlightAxis
                ? $"<color={HighlightColor}><b>{euler[k]:0.0}</b></color>"
                : $"{euler[k]:0.0}";
            return $"({C(0)}, {C(1)}, {C(2)})°";
        }

        static string FormatBoneText(BoneReadout bone, IkStepReport step)
        {
            int highlight = step != null && step.EulerAxisByBone.TryGetValue(bone.Name, out int axis) ? axis : -1;
            Vector3 p = bone.LocalPositionMm;
            Quaternion q = bone.LocalRotation;
            return $"<b>{bone.Name}</b>  pos ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) мм · rot {FormatEuler(bone.InspectorEuler, highlight)}\n" +
                   $"<size=10>q ({q.x:0.0000}, {q.y:0.0000}, {q.z:0.0000}, {q.w:0.0000})\n{bone.Anatomy}</size>";
        }

        static string FormatTipText(FingerReadout readout)
        {
            Vector3 t = readout.TipWristMm;
            return $"<b>Кончик</b>  от запястья ({t.x:0.0}, {t.y:0.0}, {t.z:0.0}) мм";
        }

        static void EnsureBoneStyles()
        {
            if (_boneLabelBackground == null)
            {
                _boneLabelBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _boneLabelBackground.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.82f));
                _boneLabelBackground.Apply();
            }

            if (_boneLabelStyle == null)
            {
                _boneLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    fontSize = 12,
                    wordWrap = false,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(6, 6, 3, 3)
                };
                _boneLabelStyle.normal.textColor = Color.white;
                _boneLabelStyle.normal.background = _boneLabelBackground;
            }

            if (_boneTableStyle == null)
            {
                _boneTableStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true, fontSize = 11 };
            }
        }

        /// <summary>Таблица показаний активного пальца в панели под кнопками IK.</summary>
        static void DrawFingerBonesTable()
        {
            if (_avatar == null)
            {
                return;
            }

            EnsureValidAvatarRigAxes(_avatar);
            UxrAvatarHand hand = _avatar.GetHand(_side);
            UxrFingerType fingerType = FingerTypes[Mathf.Clamp(_activeFingerIndex, 0, FingerTypes.Length - 1)];
            UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;
            FingerReadout readout = ComputeFingerReadout(hand, fingerType, wristAxes, fingerAxes);
            if (readout == null)
            {
                EditorGUILayout.HelpBox("У активного пальца нет костей.", MessageType.None);
                return;
            }

            EnsureBoneStyles();
            IkStepReport step = _lastIkStep != null && _lastIkStep.FingerType == fingerType ? _lastIkStep : null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"<b>{FingerNamesRu[Mathf.Clamp(_activeFingerIndex, 0, FingerNamesRu.Length - 1)]}</b>: localPosition (мм) / localRotation (Euler инспектора, °) относительно родителя" +
                    (readout.BindIsCurrentPose ? "  <color=#ff6060>bind-аватар не найден: bind = текущая поза</color>" : ""),
                    _boneTableStyle);
                foreach (BoneReadout bone in readout.Bones)
                {
                    EditorGUILayout.LabelField(FormatBoneText(bone, step), _boneTableStyle);
                }

                EditorGUILayout.LabelField(FormatTipText(readout), _boneTableStyle);

                if (step != null)
                {
                    string twistLine = step.TwistChanged
                        ? $"\n<color=#ff4040><b>Twist меняется! После IK {step.TwistAfter:+0.00;-0.00}° (было {step.TwistBefore:+0.00;-0.00}°) — ошибка</b></color>"
                        : step.TwistRemoved
                            ? $"\n<color={HighlightColor}>Twist меняется: IK снял скручивание {step.TwistBefore:+0.0;-0.0}° → 0</color>"
                            : "";
                    EditorGUILayout.LabelField(
                        $"Последний шаг IK: <color={HighlightColor}>{step.AnatomySummary}</color> · {step.EulerSummary}{twistLine}",
                        _boneTableStyle);
                }
            }
        }

        /// <summary>
        /// SceneView: точка и оси анатомической рамки (LocalRight — красная, ось сгиба; LocalUp — зелёная, ось развода;
        /// LocalForward — синяя, вдоль кости) каждой кости активного пальца; тонко — те же оси в bind при текущем родителе.
        /// Подписи — колонкой справа от пальца с выносками, чтобы не перекрываться.
        /// </summary>
        static void DrawFingerBoneIndicators(
            UxrAvatarHand hand,
            UxrFingerType fingerType,
            UxrUniversalLocalAxes wristAxes,
            UxrUniversalLocalAxes fingerAxes,
            Func<Vector3, Vector3> toPreview,
            Func<Quaternion, Quaternion> toPreviewRot)
        {
            FingerReadout readout = ComputeFingerReadout(hand, fingerType, wristAxes, fingerAxes);
            if (readout == null)
            {
                return;
            }

            EnsureBoneStyles();
            IkStepReport step = _lastIkStep != null && _lastIkStep.FingerType == fingerType ? _lastIkStep : null;
            var axisColors = new[] { new Color(1f, 0.25f, 0.25f, 1f), new Color(0.3f, 1f, 0.3f, 1f), new Color(0.35f, 0.55f, 1f, 1f) };
            Vector3[] localAxes = { fingerAxes.LocalRight, fingerAxes.LocalUp, fingerAxes.LocalForward };

            var anchors = new List<Vector3>(readout.Bones.Count + 1);
            var texts = new List<string>(readout.Bones.Count + 1);

            foreach (BoneReadout bone in readout.Bones)
            {
                Vector3 p = toPreview(bone.Bone.position);
                float len = HandleUtility.GetHandleSize(p) * 0.16f;
                Quaternion current = toPreviewRot(bone.Bone.rotation);
                Quaternion parentRotation = bone.Bone.parent != null ? bone.Bone.parent.rotation : Quaternion.identity;
                Quaternion bindWorld = toPreviewRot(parentRotation * bone.BindLocalRotation);

                Handles.color = Color.white;
                Handles.SphereHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.03f, EventType.Repaint);
                for (int k = 0; k < 3; k++)
                {
                    Handles.color = axisColors[k];
                    Handles.DrawAAPolyLine(4f, p, p + current * localAxes[k] * len);
                    if (bone.HasBind)
                    {
                        Handles.color = new Color(axisColors[k].r, axisColors[k].g, axisColors[k].b, 0.55f);
                        Handles.DrawAAPolyLine(1.5f, p, p + bindWorld * localAxes[k] * len * 1.15f);
                    }
                }

                anchors.Add(p);
                texts.Add(FormatBoneText(bone, step));
            }

            Vector3 tip = toPreview(readout.TipWorld);
            Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
            Handles.SphereHandleCap(0, tip, Quaternion.identity, HandleUtility.GetHandleSize(tip) * 0.03f, EventType.Repaint);
            anchors.Add(tip);
            texts.Add(FormatTipText(readout));

            if (step != null && step.TwistRemoved)
            {
                texts[texts.Count - 1] += $"\n<color={HighlightColor}>IK снял скручивание {step.TwistBefore:+0.0;-0.0}° → 0</color>";
            }

            if (step != null && step.TwistChanged)
            {
                texts[texts.Count - 1] += $"\n<color=#ff4040><b>Twist меняется! После IK {step.TwistAfter:+0.00;-0.00}° — ошибка</b></color>";
            }

            // Колонка подписей справа от пальца, по порядку экранной высоты, с выносками
            Handles.BeginGUI();
            try
            {
                var screen = anchors.Select(a => HandleUtility.WorldToGUIPoint(a)).ToList();
                float columnX = screen.Max(s => s.x) + 48f;
                var order = Enumerable.Range(0, screen.Count).OrderBy(i => screen[i].y).ToList();
                float nextTop = float.MinValue;
                foreach (int i in order)
                {
                    var content = new GUIContent(texts[i]);
                    Vector2 size = _boneLabelStyle.CalcSize(content);
                    float top = Mathf.Max(screen[i].y - size.y * 0.5f, nextTop + 4f);
                    var rect = new Rect(columnX, top, size.x, size.y);
                    nextTop = rect.yMax;

                    Handles.color = new Color(1f, 1f, 1f, 0.6f);
                    Handles.DrawAAPolyLine(1.5f, new Vector3(screen[i].x, screen[i].y, 0f), new Vector3(rect.xMin, rect.center.y, 0f));
                    GUI.Label(rect, content, _boneLabelStyle);
                }
            }
            finally
            {
                Handles.EndGUI();
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
                _status = InheritedPoseReadOnlyStatus(); // VR Battlegrounds patch 91
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

            // Показания живой кисти до шага — для подсветки «что изменилось сильнее всего»
            FingerReadout readoutBefore = _showFingerBones ? ComputeFingerReadout(sourceHand, fingerType, wristAxes, fingerAxes) : null;

            GameObject shadowRootGo = null;
            UxrHandDescriptor solvedDescriptor = null;

            try
            {
                UxrAvatarArm shadowArm = CreateShadowHand(sourceHand, out shadowRootGo);
                UxrAvatarHand shadowHand = shadowArm.Hand;
                UxrAvatarFinger finger = shadowHand.GetFinger(fingerType);

                bool isThumb = fingerType == UxrFingerType.Thumb;
                BindPoseFingerState bindState = CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes);

                // IK не скручивает корневую фалангу: у четырёх пальцев решатель всегда работает с Twist = 0. Если поза была
                // скручена, первый же шаг снимает скручивание, как «Убрать скручивание», но с целью ручки — палец сразу идёт к цели.
                // У большого пальца (сустав CMC) имеющееся скручивание сохраняется
                float proximalTwist = isThumb ? MeasureProximalTwist(finger, bindState, fingerAxes) : 0f;
                SolveFingerTipIk(finger, bindState, fingerAxes, isThumb, targetTipAvatarWorld, proximalTwist);
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
                if (readoutBefore != null)
                {
                    RecordIkStep(readoutBefore, ComputeFingerReadout(_avatar.GetHand(_side), fingerType, wristAxes, fingerAxes));
                }

                _window.Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                _status = commitReason;
            }
        }

        /// <summary>
        /// Решатель IK конечной фаланги (покоординатный спуск по Curl1, Curl2, Curl3 и Spread в пределах GetFingerAngleBounds)
        /// на теневой копии пальца. Старт — текущая поза (MeasureAnatomicalFingerAngles), осевое скручивание проксимальной
        /// фаланги фиксировано значением <paramref name="proximalTwist"/>. Пястная кость не меняется.
        /// Возвращает остаточную ошибку кончика (м).
        /// </summary>
        static float SolveFingerTipIk(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            Vector3 targetTipAvatarWorld,
            float proximalTwist)
        {
            GetFingerAngleBounds(_side, isThumb, bindState.BindSpread,
                out float minSpread, out float maxSpread,
                out float minCurl1, out float maxCurl1,
                out float minCurl2, out float maxCurl2,
                out float minCurl3, out float maxCurl3);

            // Стартовые углы — точным обращением ApplyAnatomicalFingerAngles: повторное применение воспроизводит текущую позу,
            // поэтому серия коммитов при перетаскивании ручки не накапливает боковой увод
            MeasureAnatomicalFingerAngles(finger, bindState, fingerAxes,
                out float measuredSpread, out float measuredCurl1, out float measuredCurl2, out float measuredCurl3);
            float spread = Mathf.Clamp(measuredSpread, minSpread, maxSpread);
            float curl1 = Mathf.Clamp(measuredCurl1, minCurl1, maxCurl1);
            float curl2 = Mathf.Clamp(measuredCurl2, minCurl2, maxCurl2);
            float curl3 = Mathf.Clamp(measuredCurl3, minCurl3, maxCurl3);

            float initSpread = spread;
            float initCurl1 = curl1;
            float initCurl2 = curl2;
            float initCurl3 = curl3;

            float EvaluateObjective(float s, float c1, float c2, float c3)
            {
                ApplyAnatomicalFingerAngles(finger, bindState, fingerAxes, isThumb, s, c1, c2, c3, proximalTwist);

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
            return Vector3.Distance(GetDistalTipWorld(finger, fingerAxes), targetTipAvatarWorld);
        }

        /// <summary>
        /// «Убрать скручивание»: для активного пальца или всех, кроме большого, обнуляет осевое скручивание проксимальной фаланги
        /// и тем же решателем IK возвращает условный кончик (GetDistalTipWorld) в исходную точку; Spread и Curl — в пределах
        /// GetFingerAngleBounds. Пястные не меняются. Фиксация — тем же путём, что у IK: TryCommitContactEdit (одна группа Undo,
        /// read-only и общие позы SDK защищены патчем 91).
        /// </summary>
        static void RemoveProximalTwistAndCommit(bool allFingers)
        {
            const float TipWarnMeters = 0.001f;
            const float NoTwistDeg = 0.05f;

            if (_window == null || _avatar == null || _pose == null)
            {
                return;
            }

            UxrFingerType activeType = FingerTypes[Mathf.Clamp(_activeFingerIndex, 0, FingerTypes.Length - 1)];
            if (!allFingers && activeType == UxrFingerType.Thumb)
            {
                _status = "У большого пальца скручивание допустимо (сустав CMC) — оно не снимается.";
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
                _status = InheritedPoseReadOnlyStatus(); // VR Battlegrounds patch 91
                return;
            }

            UxrAvatarHand sourceHand = _avatar.GetHand(_side);
            if (sourceHand == null || sourceHand.Wrist == null)
            {
                return;
            }

            UxrUniversalLocalAxes wristAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).HandUniversalLocalAxes;
            UxrUniversalLocalAxes fingerAxes = _avatar.AvatarRigInfo.GetArmInfo(_side).FingerUniversalLocalAxes;

            GameObject shadowRootGo = null;
            UxrHandDescriptor solvedDescriptor = null;
            var report = new List<string>();
            bool anyChanged = false;
            bool anyWarn = false;

            try
            {
                UxrAvatarArm shadowArm = CreateShadowHand(sourceHand, out shadowRootGo);
                UxrAvatarHand shadowHand = shadowArm.Hand;

                for (int i = 0; i < FingerTypes.Length; i++)
                {
                    UxrFingerType fingerType = FingerTypes[i];
                    if (fingerType == UxrFingerType.Thumb || (!allFingers && fingerType != activeType))
                    {
                        continue;
                    }

                    UxrAvatarFinger finger = shadowHand.GetFinger(fingerType);
                    if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                    {
                        report.Add($"{FingerNamesRu[i]}: n/a (нет костей)");
                        continue;
                    }

                    BindPoseFingerState bindState = CaptureBindPoseFingerState(_avatar, _side, fingerType, wristAxes, fingerAxes);
                    float twist = MeasureProximalTwist(finger, bindState, fingerAxes);
                    if (Mathf.Abs(twist) < NoTwistDeg)
                    {
                        report.Add($"{FingerNamesRu[i]}: скручивания нет");
                        continue;
                    }

                    // 1. Исходное положение кончика; 2–3. twist = 0, Spread/Curl текущей позы как старт, IK возвращает кончик
                    Vector3 tipBefore = GetDistalTipWorld(finger, fingerAxes);
                    float tipError = SolveFingerTipIk(finger, bindState, fingerAxes, false, tipBefore, 0f);
                    anyChanged = true;

                    bool warn = tipError > TipWarnMeters;
                    anyWarn |= warn;
                    report.Add($"{FingerNamesRu[i]}: снято {twist:+0.0;-0.0}°, кончик {tipError * 1000f:0.0} мм" +
                               (warn ? " — warn: упёрлись в пределы Spread/Curl" : ""));
                }

                if (anyChanged)
                {
                    solvedDescriptor = new UxrHandDescriptor(shadowArm, wristAxes, fingerAxes);
                }
            }
            catch (Exception ex)
            {
                _status = "Ошибка снятия скручивания: " + ex.Message;
                return;
            }
            finally
            {
                if (shadowRootGo != null)
                {
                    Object.DestroyImmediate(shadowRootGo);
                }
            }

            string details = string.Join("\n", report);
            if (!anyChanged)
            {
                _status = "Убрать скручивание: поза не изменена.\n" + details;
                return;
            }

            // 4. Та же фиксация, что у IK: одна SDK-группа Undo, сохранение позы и обновление превью
            if (_window.TryCommitContactEdit(sdkCtx, solvedDescriptor, out string commitReason))
            {
                _status = (anyWarn ? "Скручивание снято, есть warn:\n" : "Скручивание снято:\n") + details;
                _window.Repaint();
                SceneView.RepaintAll();
            }
            else
            {
                _status = commitReason;
            }
        }

        /// <summary>
        /// Применяет углы суставов пальца аналитически от нейтральной (bind) ориентации без накопления паразитного кручения (roll):
        /// - Корневая фаланга (Proximal): сгибание (Curl1 вокруг LocalRight), затем ограниченное отведение вбок (Spread вокруг уже
        ///   согнутой LocalUp) — порядок спиннеров SDK и UxrAvatarRig.CurlFinger, так что развод спиннером не даёт Twist.
        /// - Средняя (Intermediate) и конечная (Distal) фаланги — чистые шарниры: поворот только вокруг собственной LocalRight
        ///   в bind-рамке кости (как UxrAvatarRig.CurlFinger и спиннеры Hand Pose Editor), без бокового изгиба и без осевого скручивания.
        ///   Ось берётся в локальной рамке кости, поэтому автоматически следует за Spread/Curl родителя.
        /// - <paramref name="proximalTwist"/> — осевое скручивание проксимальной фаланги: IK и «Обхватить» у четырёх пальцев
        ///   ставят 0; у большого пальца IK передаёт имеющееся (сустав CMC).
        /// - Пястная кость (Metacarpal) не меняется.
        /// Обратная операция — GripAuthoringGeometry.MeasureAnatomicalFingerAngles и MeasureProximalTwist.
        /// </summary>
        static void ApplyAnatomicalFingerAngles(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            bool isThumb,
            float spread,
            float curl1,
            float curl2,
            float curl3,
            float proximalTwist = 0f)
        {
            Quaternion deltaSpread = Quaternion.AngleAxis(spread - bindState.BindSpread, fingerAxes.LocalUp);
            Quaternion deltaCurl1 = Quaternion.AngleAxis(curl1 - bindState.BindCurl1, fingerAxes.LocalRight);
            Quaternion twist = Quaternion.AngleAxis(proximalTwist, fingerAxes.LocalForward);
            finger.Proximal.localRotation = bindState.ProximalLocalRot * deltaCurl1 * deltaSpread * twist;

            // Одинаково для всех пальцев (isThumb здесь не нужен): прежняя абсолютная формула AngleAxis(curl, LocalRight)
            // для 4 пальцев отбрасывала bind-поворот кости (у CyborgGeo до 5,6° вбок/кручения на Middle_2/Ring_2),
            // и фаланга уходила вбок относительно меша
            finger.Intermediate.localRotation = bindState.IntermediateLocalRot * Quaternion.AngleAxis(curl2 - bindState.BindCurl2, fingerAxes.LocalRight);
            finger.Distal.localRotation = bindState.DistalLocalRot * Quaternion.AngleAxis(curl3 - bindState.BindCurl3, fingerAxes.LocalRight);
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
    }
}

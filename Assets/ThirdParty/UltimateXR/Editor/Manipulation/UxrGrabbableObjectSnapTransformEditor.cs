// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrGrabbableObjectSnapTransformEditor.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Editor.Avatar;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Extensions.Unity;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace UltimateXR.Editor.Manipulation
{
    /// <summary>
    ///     Custom editor for <see cref="UxrGrabbableObjectSnapTransform" />.
    /// </summary>
    [CustomEditor(typeof(UxrGrabbableObjectSnapTransform))]
    public class UxrGrabbableObjectSnapTransformEditor : UnityEditor.Editor
    {
        #region Public Methods

        /// <summary>
        ///     Refreshes the preview poses for a specific avatar and hand pose. Designed to be called from the Hand Pose Editor to
        ///     keep the preview meshes updated. Preview hand pose meshes that share the same prefab will be updated accordingly.
        /// </summary>
        /// <param name="avatar">The avatar to update the poses for</param>
        /// <param name="handPoseAsset">The pose asset to look for and update</param>
        public static void RefreshGrabPoseMeshes(UxrAvatar avatar, UxrHandPoseAsset handPoseAsset)
        {
            foreach (KeyValuePair<SerializedObject, UxrGrabbableObjectSnapTransformEditor> editorPair in s_openEditors)
            {
                editorPair.Value.RefreshGrabPoseMeshesInternal(avatar.GetAvatarPrefab(), handPoseAsset);
            }
        }

        /// <summary>
        ///     Creates a hand pose preview object.
        /// </summary>
        /// <param name="parent">Parent that will be assigned to the new object</param>
        /// <param name="previewMesh">The preview mesh object</param>
        /// <param name="mesh">Mesh assigned to the object</param>
        /// <param name="sharedMaterials">Shared materials to be assigned to the mesh</param>
        /// <returns>New preview GameObject</returns>
        public static GameObject CreateAndSetupPreviewMeshObject(Transform parent, UxrPreviewHandGripMesh previewMesh, Mesh mesh, Material[] sharedMaterials)
        {
            // Create child that the proxy will have to follow

            GameObject grabPose = EditorUtility.CreateGameObjectWithHideFlags("GrabPose",
                                                                              HideFlags.HideAndDontSave,
                                                                              typeof(UxrGrabbableObjectPreviewMesh));

            grabPose.transform.position = parent.position;
            grabPose.transform.rotation = parent.rotation;
            grabPose.transform.SetParent(parent);

            // Create proxy at root level that avoids non-uniform scaling problems

            GameObject grabPoseProxy = EditorUtility.CreateGameObjectWithHideFlags("GrabPoseProxy",
                                                                                   HideFlags.HideAndDontSave,
                                                                                   typeof(UxrGrabbableObjectPreviewMeshProxy),
                                                                                   typeof(MeshFilter),
                                                                                   typeof(MeshRenderer));

            grabPoseProxy.transform.position                                                      = parent.position;
            grabPoseProxy.transform.rotation                                                      = parent.rotation;
            grabPoseProxy.GetComponent<MeshFilter>().sharedMesh                                   = mesh;
            grabPoseProxy.GetComponent<MeshRenderer>().sharedMaterials                            = sharedMaterials;
            grabPoseProxy.GetComponent<UxrGrabbableObjectPreviewMeshProxy>().PreviewMesh          = previewMesh;
            grabPoseProxy.GetComponent<UxrGrabbableObjectPreviewMeshProxy>().PreviewMeshComponent = grabPose.GetComponent<UxrGrabbableObjectPreviewMesh>();

            grabPose.GetComponent<UxrGrabbableObjectPreviewMesh>().PreviewMeshProxy = grabPoseProxy.GetComponent<UxrGrabbableObjectPreviewMeshProxy>();

            return grabPose;
        }

        #endregion

        #region Unity

        /// <summary>
        ///     Creates temporal grab pose editor objects.
        /// </summary>
        private void OnEnable()
        {
            for (int i = 0; i < serializedObject.targetObjects.Length; ++i)
            {
                UxrGrabbableObjectSnapTransform snapTransform = serializedObject.targetObject as UxrGrabbableObjectSnapTransform;
                snapTransform.hideFlags = HideFlags.DontSaveInBuild;
            }

            Undo.undoRedoPerformed += OnUndoRedo;

            CreateGrabPoseTempEditorObjects();

            // Add a static reference so that we can update the grip preview meshes from the Hand Pose Editor

            if (s_openEditors.ContainsKey(serializedObject) == false)
            {
                s_openEditors.Add(serializedObject, this);
            }

            // Destroy objects that don't belong to any UxrGrabbableObject (duplicated in editor or moved from hierarchy)

            foreach (Object targetObject in serializedObject.targetObjects)
            {
                CheckUnusedTransform(targetObject as UxrGrabbableObjectSnapTransform);
            }
        }

        /// <summary>
        ///     Destroys temporal grab pose editor objects.
        /// </summary>
        private void OnDisable()
        {
            DestroyGrabPoseTempEditorObjects();

            Undo.undoRedoPerformed -= OnUndoRedo;

            if (s_openEditors.ContainsKey(serializedObject))
            {
                s_openEditors.Remove(serializedObject);
            }
        }

        /// <summary>
        ///     Draws the custom inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (serializedObject.targetObjects.Length == 1)
            {
                UxrGrabbableObjectSnapTransform snapTransform   = serializedObject.targetObject as UxrGrabbableObjectSnapTransform;
                UxrGrabbableObject              grabbableObject = snapTransform.SafeGetComponentInParent<UxrGrabbableObject>();

                if (grabbableObject == null)
                {
                    return;
                }

                UxrAvatar avatarForGrip = UxrAvatarEditorExt.GetFromGuid(grabbableObject.Editor_GetGrabAlignTransformAvatar(snapTransform.transform));

                if (avatarForGrip)
                {
                    IEnumerable<int> grabPointIndices = grabbableObject.Editor_GetGrabPointsForGrabAlignTransform(avatarForGrip, snapTransform.transform);

                    if (grabPointIndices.Count() > 1)
                    {
                        EditorGUILayout.HelpBox($"This transform is being used in more than one grab point of {grabbableObject.name}", MessageType.Warning);
                    }
                    else if (grabPointIndices.Count() == 1)
                    {
                        UxrGrabPointInfo grabPoint     = grabbableObject.GetGrabPoint(grabPointIndices.First());
                        UxrGripPoseInfo  gripPose      = grabPoint.GetGripPoseInfo(avatarForGrip);
                        UxrHandPoseAsset gripPoseAsset = gripPose?.HandPose;

                        if (gripPoseAsset != null && gripPoseAsset.PoseType == UxrHandPoseType.Blend)
                        {
                            EditorGUILayout.HelpBox($"This slider will affect both the left and right hand of pose {gripPoseAsset.name}. Grab points in {nameof(UxrGrabbableObject)} use the same slider for both hands.",
                                                    MessageType.Info);

                            Undo.RecordObject(grabbableObject, "Change Pose Blend");
                            EditorGUILayout.Space();

                            float oldBlend = gripPose.PoseBlendValue;
                            float newBlend = EditorGUILayout.Slider($"Pose Blend ({gripPoseAsset.name})", oldBlend, 0.0f, 1.0f);
                            gripPose.PoseBlendValue = newBlend;

                            if (!Mathf.Approximately(oldBlend, newBlend))
                            {
                                // VR Battlegrounds patch 91: для аватара без данных кисти — превью по потомку.
                                UxrGrabbableObjectEditor.RefreshGrabPreviewMesh(_previewMeshLeft,  grabbableObject, _cachedSingleObjectAvatar, avatarForGrip, grabPointIndices.First(), UxrHandSide.Left);
                                UxrGrabbableObjectEditor.RefreshGrabPreviewMesh(_previewMeshRight, grabbableObject, _cachedSingleObjectAvatar, avatarForGrip, grabPointIndices.First(), UxrHandSide.Right);
                            }

                            EditorGUILayout.Space();
                        }
                    }

                    // VR Battlegrounds patch 91: грабберы для зеркалирования — у потомка, если у аватара хватов нет данных кисти.
                    UxrAvatar    grabberAvatar = UxrGrabbableObjectEditor.GetPreviewDescendantIfNeeded(avatarForGrip);
                    UxrGrabber[] grabbers     = (grabberAvatar != null ? grabberAvatar : avatarForGrip).GetComponentsInChildren<UxrGrabber>();
                    UxrGrabber   leftGrabber  = grabbers.FirstOrDefault(g => g.Side == UxrHandSide.Left);
                    UxrGrabber   rightGrabber = grabbers.FirstOrDefault(g => g.Side == UxrHandSide.Right);
                    UxrGrabber grabber = _previewMeshLeft != null  ? leftGrabber :
                                         _previewMeshRight != null ? rightGrabber : null;

                    if (grabber)
                    {
                        if (UxrEditorUtils.CenteredButton(new GUIContent("Mirror Up/Down", "")))
                        {
                            Undo.RegisterCompleteObjectUndo(snapTransform.transform, "Mirror transform");
                            snapTransform.transform.ApplyMirroring(snapTransform.transform.position, snapTransform.transform.TransformDirection(-grabber.LocalFingerDirection), grabber.RequiredMirrorType);
                        }

                        if (UxrEditorUtils.CenteredButton(new GUIContent("Mirror Front/Back", "")))
                        {
                            Undo.RegisterCompleteObjectUndo(snapTransform.transform, "Mirror transform");
                            snapTransform.transform.ApplyMirroring(snapTransform.transform.position, snapTransform.transform.TransformDirection(-grabber.LocalPalmOutDirection), grabber.RequiredMirrorType);
                        }

                        if (grabPointIndices.Count() == 1)
                        {
                            UxrGrabPointInfo grabPoint = grabbableObject.GetGrabPoint(grabPointIndices.First());
                            UxrGripPoseInfo  gripPose  = grabPoint.GetGripPoseInfo(avatarForGrip);

                            if (gripPose.GripAlignTransformHandLeft != null && gripPose.GripAlignTransformHandRight != null)
                            {
                                if (UxrEditorUtils.CenteredButton(new GUIContent("Copy From Other Hand", "")))
                                {
                                    Undo.RegisterCompleteObjectUndo(snapTransform.transform, "Set transform");
                                    snapTransform.transform.SetPositionAndRotation(snapTransform.transform == gripPose.GripAlignTransformHandLeft ? gripPose.GripAlignTransformHandRight : gripPose.GripAlignTransformHandLeft);
                                }
                            }
                        }
                    }

                    // VR Battlegrounds patch 91: точное зеркало snap-точки противоположной руки той же записи хвата.
                    DrawMirrorFromOtherHand(snapTransform, grabbableObject, avatarForGrip, grabPointIndices.ToList(), leftGrabber, rightGrabber);
                }
            }
        }

        #endregion

        #region VR Battlegrounds patch 91: зеркало с другой руки

        /// <summary>
        ///     VR Battlegrounds patch 91: зеркалит snap-точку в локальном пространстве контейнера. Плоскость — локальная YZ
        ///     через начало, нормаль — локальная X. Поворот достраивается по типу зеркала граббера теми же формулами,
        ///     что в <see cref="TransformExt.ApplyMirroring(Transform,Vector3,Vector3,TransformExt.MirrorType,bool,bool)" />.
        /// </summary>
        /// <param name="sourcePosition">Позиция источника в пространстве контейнера</param>
        /// <param name="sourceRotation">Поворот источника в пространстве контейнера</param>
        /// <param name="mirrorType">Тип зеркала граббера целевой стороны</param>
        /// <returns>Позиция и поворот зеркальной snap-точки в том же пространстве</returns>
        public static (Vector3 position, Quaternion rotation) MirrorSnapLocal(Vector3 sourcePosition, Quaternion sourceRotation, TransformExt.MirrorType mirrorType)
        {
            Vector3 normal  = Vector3.right;
            Vector3 right   = Vector3.Reflect(sourceRotation * Vector3.right,   normal);
            Vector3 up      = Vector3.Reflect(sourceRotation * Vector3.up,      normal);
            Vector3 forward = Vector3.Reflect(sourceRotation * Vector3.forward, normal);

            Quaternion rotation;

            switch (mirrorType)
            {
                case TransformExt.MirrorType.MirrorXY:
                    rotation = Quaternion.LookRotation(Vector3.Cross(right, up), up);
                    break;

                case TransformExt.MirrorType.MirrorXZ:
                    rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                    break;

                default:
                    rotation = Quaternion.LookRotation(forward, up);
                    break;
            }

            return (new Vector3(-sourcePosition.x, sourcePosition.y, sourcePosition.z), rotation);
        }

        /// <summary>
        ///     VR Battlegrounds patch 91: подбор типа зеркала по паре грабберов аватара. Левый граббер отражается через
        ///     сагиттальную плоскость аватара (нормаль — правая ось корня, расчёт в пространстве корня) каждым типом;
        ///     выбирается тип, лучше всего совпадающий с правым граббером. RequiredMirrorType одного граббера не берётся:
        ///     он зависит от осей рига (HandUniversalLocalAxes), а у prefab asset потомка грабберы лежат в отдельной
        ///     BigHandsIntegration и не совпадают с рамкой рига — выбор может дать перевёрнутый хват.
        /// </summary>
        /// <param name="avatar">Аватар с грабберами</param>
        /// <param name="leftGrabber">Левый граббер</param>
        /// <param name="rightGrabber">Правый граббер</param>
        /// <param name="errorDegrees">Угол между зеркалом левого и правым граббером для выбранного типа</param>
        /// <returns>Тип зеркала или null, если пары нет</returns>
        public static TransformExt.MirrorType? FitMirrorTypeByGrabberPair(UxrAvatar avatar, UxrGrabber leftGrabber, UxrGrabber rightGrabber, out float errorDegrees)
        {
            errorDegrees = 0.0f;

            if (avatar == null || leftGrabber == null || rightGrabber == null)
            {
                return null;
            }

            Quaternion toRoot = Quaternion.Inverse(avatar.transform.rotation);
            Quaternion left   = toRoot * leftGrabber.transform.rotation;
            Quaternion right  = toRoot * rightGrabber.transform.rotation;

            TransformExt.MirrorType? best      = null;
            float                    bestError = float.MaxValue;

            foreach (TransformExt.MirrorType type in new[] { TransformExt.MirrorType.MirrorXY, TransformExt.MirrorType.MirrorXZ, TransformExt.MirrorType.MirrorYZ })
            {
                float error = Quaternion.Angle(MirrorSnapLocal(Vector3.zero, left, type).rotation, right);

                if (error < bestError)
                {
                    bestError = error;
                    best      = type;
                }
            }

            errorDegrees = bestError;
            return best;
        }

        /// <summary>
        ///     VR Battlegrounds patch 91: ручной выбор типа зеркала (0 — авто, 1 — XY, 2 — XZ, 3 — YZ), EditorPrefs.
        /// </summary>
        private static int MirrorTypeOverride
        {
            get
            {
                try
                {
                    return Mathf.Clamp(EditorPrefs.GetInt(PrefMirrorTypeOverride, 0), 0, 3);
                }
                catch (System.Exception)
                {
                    return 0;
                }
            }
            set
            {
                try
                {
                    EditorPrefs.SetInt(PrefMirrorTypeOverride, value);
                }
                catch (System.Exception)
                {
                    // Без EditorPrefs выбор не запоминается.
                }
            }
        }

        private const string PrefMirrorTypeOverride = "VRBattlegrounds.UltimateXR.SnapMirror.TypeOverride";

        private static readonly string[] MirrorTypeOverrideNames = { "Авто (по паре грабберов)", "XY", "XZ", "YZ" };

        private static string MirrorTypeShortName(TransformExt.MirrorType type)
        {
            return type == TransformExt.MirrorType.MirrorXY ? "XY" : type == TransformExt.MirrorType.MirrorXZ ? "XZ" : "YZ";
        }

        /// <summary>
        ///     VR Battlegrounds patch 91: кнопка «Отзеркалить с другой руки». Источник — snap противоположной стороны той же
        ///     записи хвата выбранного аватара (базы); граббер целевой стороны — у потомка для превью, если у базы нет кисти.
        /// </summary>
        private void DrawMirrorFromOtherHand(UxrGrabbableObjectSnapTransform snapTransform,
                                             UxrGrabbableObject              grabbableObject,
                                             UxrAvatar                       avatarForGrip,
                                             List<int>                       grabPointIndices,
                                             UxrGrabber                      leftGrabber,
                                             UxrGrabber                      rightGrabber)
        {
            Transform   target     = snapTransform.transform;
            Transform   source     = null;
            string      reason     = null;

            if (grabPointIndices.Count != 1)
            {
                reason = grabPointIndices.Count == 0 ? "Snap-точка не найдена в записи хвата выбранного аватара." : "Snap-точка используется в нескольких точках хвата.";
            }
            else
            {
                UxrGripPoseInfo gripPose = grabbableObject.GetGrabPoint(grabPointIndices[0]).GetGripPoseInfo(avatarForGrip);

                if (gripPose == null)
                {
                    reason = "Нет записи хвата выбранного аватара.";
                }
                else if (gripPose.GripAlignTransformHandLeft == target && gripPose.GripAlignTransformHandRight == target)
                {
                    reason = "Обе руки используют эту же snap-точку.";
                }
                else if (gripPose.GripAlignTransformHandLeft == target)
                {
                    source = gripPose.GripAlignTransformHandRight;
                }
                else if (gripPose.GripAlignTransformHandRight == target)
                {
                    source = gripPose.GripAlignTransformHandLeft;
                }
                else
                {
                    reason = "Snap-точка не принадлежит записи хвата выбранного аватара.";
                }

                if (reason == null && (source == null || source == target))
                {
                    reason = "У противоположной руки нет отдельной snap-точки.";
                }
            }

            // Тип зеркала: ручной выбор или подбор по паре грабберов аватара (fallback XZ, если пары нет).
            UxrAvatar                grabberOwner = leftGrabber != null ? leftGrabber.Avatar : rightGrabber != null ? rightGrabber.Avatar : null;
            TransformExt.MirrorType? fittedType   = FitMirrorTypeByGrabberPair(grabberOwner, leftGrabber, rightGrabber, out float fitError);
            int                      typeOverride = MirrorTypeOverride;
            TransformExt.MirrorType  mirrorType;
            string                   typeLabel;

            if (typeOverride > 0)
            {
                mirrorType = (TransformExt.MirrorType)(typeOverride - 1);
                typeLabel  = $"{MirrorTypeShortName(mirrorType)}, вручную" + (fittedType.HasValue ? $"; подбор {MirrorTypeShortName(fittedType.Value)} {fitError:0.0}°" : string.Empty);
            }
            else if (fittedType.HasValue)
            {
                mirrorType = fittedType.Value;
                typeLabel  = $"{MirrorTypeShortName(mirrorType)}, подбор {fitError:0.0}°";
            }
            else
            {
                mirrorType = TransformExt.MirrorType.MirrorXZ;
                typeLabel  = "XZ по умолчанию: нет пары грабберов";
            }

            string label      = $"Отзеркалить с другой руки ({typeLabel})";
            bool   sameParent = source != null && source.parent != null && source.parent == target.parent;

            bool guiEnabled = GUI.enabled;

            EditorGUI.BeginChangeCheck();
            int newOverride = EditorGUILayout.Popup(new GUIContent("Тип зеркала snap", "Авто — подбор по паре грабберов аватара; XY/XZ/YZ — ручной выбор."), typeOverride, MirrorTypeOverrideNames);
            if (EditorGUI.EndChangeCheck())
            {
                MirrorTypeOverride = newOverride;
            }

            GUI.enabled = guiEnabled && reason == null;

            if (UxrEditorUtils.CenteredButton(new GUIContent(label, "Ставит эту snap-точку точным зеркалом snap-точки другой руки той же точки хвата: плоскость YZ общего родителя, тип зеркала — подбор по паре грабберов или ручной выбор. Ctrl+Z откатывает.")))
            {
                Undo.RecordObject(target, "Отзеркалить snap с другой руки");

                if (sameParent)
                {
                    (Vector3 position, Quaternion rotation) = MirrorSnapLocal(source.localPosition, source.localRotation, mirrorType);
                    target.localPosition = position;
                    target.localRotation = rotation;
                }
                else
                {
                    Transform root = grabbableObject.transform;
                    (Vector3 position, Quaternion rotation) = MirrorSnapLocal(root.InverseTransformPoint(source.position), Quaternion.Inverse(root.rotation) * source.rotation, mirrorType);
                    target.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
                }

                EditorUtility.SetDirty(target);

                if (PrefabUtility.IsPartOfPrefabInstance(target))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }

                // Превью-кисти — дочерние объекты snap-точки и следуют за ней, как после штатных Mirror-кнопок.
                SceneView.RepaintAll();
            }

            GUI.enabled = guiEnabled;

            if (reason != null)
            {
                EditorGUILayout.HelpBox(reason, MessageType.Info);
            }
            else if (!sameParent)
            {
                EditorGUILayout.HelpBox($"Snap-точки рук лежат под разными родителями: зеркало строится относительно корня {grabbableObject.name} (плоскость YZ через его начало).", MessageType.Warning);
            }
        }

        #endregion

        #region Event Trigger Methods

        /// <summary>
        ///     Recomputes the preview poses.
        /// </summary>
        private void OnUndoRedo()
        {
            ComputeGrabPoseMeshes();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Tries to obtain the left and right grab pose meshes of an align transform if they are available.
        /// </summary>
        /// <param name="avatarPrefab">The registered avatar prefab to get the pose meshes for</param>
        /// <param name="transform">The transform that is potentially an align transform for a UxrGrabbableObject grab point</param>
        /// <param name="previewMeshLeft">Will contain the left grab pose mesh if it was found</param>
        /// <param name="previewMeshRight">Will contain the right grab pose mesh if it was found</param>
        private static void TryGetGrabPoseMeshes(UxrAvatar avatarPrefab, Transform transform, out UxrPreviewHandGripMesh previewMeshLeft, out UxrPreviewHandGripMesh previewMeshRight)
        {
            previewMeshLeft  = null;
            previewMeshRight = null;
            UxrGrabbableObject[] grabbableObjects = transform.GetComponentsInParent<UxrGrabbableObject>();

            UxrAvatar avatar = UxrGrabbableObjectEditor.TryGetSceneAvatar(avatarPrefab.gameObject);

            foreach (UxrGrabbableObject grabbableObject in grabbableObjects)
            {
                for (int i = 0; i < grabbableObject.GrabPointCount; ++i)
                {
                    UxrGrabPointInfo grabPoint = grabbableObject.GetGrabPoint(i);

                    if (grabPoint.HideHandGrabberRenderer == false && grabPoint.SnapMode == UxrSnapToHandMode.PositionAndRotation)
                    {
                        if (grabbableObject.Editor_GetGrabPointGrabAlignTransform(avatarPrefab, i, UxrHandSide.Left) == transform && (grabPoint.HandSide == UxrHandSide.Left || grabPoint.BothHandsCompatible))
                        {
                            previewMeshLeft = UxrGrabbableObjectEditor.BuildGrabPreviewMesh(grabbableObject, avatar, avatarPrefab, i, UxrHandSide.Left); // VR Battlegrounds patch 91

                            if (previewMeshLeft != null)
                            {
                                grabPoint.GetGripPoseInfo(avatarPrefab).GrabPoseMeshLeft = previewMeshLeft.UnityMesh;
                            }
                        }

                        if (grabbableObject.Editor_GetGrabPointGrabAlignTransform(avatarPrefab, i, UxrHandSide.Right) == transform && (grabPoint.HandSide == UxrHandSide.Right || grabPoint.BothHandsCompatible))
                        {
                            previewMeshRight = UxrGrabbableObjectEditor.BuildGrabPreviewMesh(grabbableObject, avatar, avatarPrefab, i, UxrHandSide.Right); // VR Battlegrounds patch 91

                            if (previewMeshRight != null)
                            {
                                grabPoint.GetGripPoseInfo(avatarPrefab).GrabPoseMeshRight = previewMeshRight.UnityMesh;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Regenerates the grab pose meshes
        /// </summary>
        private void ComputeGrabPoseMeshes()
        {
            DestroyGrabPoseTempEditorObjects();
            CreateGrabPoseTempEditorObjects();
        }

        /// <summary>
        ///     Creates the temporal grab pose GameObjects that are used to quickly preview/edit grabbing mechanics
        ///     in the editor
        /// </summary>
        private void CreateGrabPoseTempEditorObjects()
        {
            // Create temporal grab pose objects

            _previewMeshLeft  = null;
            _previewMeshRight = null;

            foreach (Object targetObject in serializedObject.targetObjects)
            {
                UxrGrabbableObjectSnapTransform snapTransform   = targetObject as UxrGrabbableObjectSnapTransform;
                UxrGrabbableObject              grabbableObject = snapTransform.SafeGetComponentInParent<UxrGrabbableObject>();

                if (grabbableObject == null)
                {
                    continue;
                }

                // Get the avatar prefab

                UxrAvatar avatarPrefab = UxrAvatarEditorExt.GetFromGuid(grabbableObject.Editor_GetGrabAlignTransformAvatar(snapTransform.transform));

                if (avatarPrefab == null)
                {
                    return;
                }

                // Try to get a compatible instance of the prefab in the scene

                UxrAvatar avatar = UxrGrabbableObjectEditor.TryGetSceneAvatar(avatarPrefab.gameObject);

                // VR Battlegrounds patch 91: у аватара хватов без данных кисти меш и материалы берутся у префаба-потомка.
                UxrAvatar previewAvatar = UxrGrabbableObjectEditor.GetPreviewDescendantIfNeeded(avatarPrefab);

                if (avatar == null && previewAvatar == null)
                {
                    continue;
                }

                _cachedSingleObjectAvatar = avatar;

                // Get grabber renderers which will be used to know which materials to use when rendering grab poses

                UxrGrabber[] grabbers             = (previewAvatar != null ? previewAvatar : avatar).GetComponentsInChildren<UxrGrabber>(false);
                Renderer     leftGrabberRenderer  = grabbers.FirstOrDefault(g => g.Side == UxrHandSide.Left && g.HandRenderer != null)?.HandRenderer;
                Renderer     rightGrabberRenderer = grabbers.FirstOrDefault(g => g.Side == UxrHandSide.Right && g.HandRenderer != null)?.HandRenderer;

                if (snapTransform && (avatar || previewAvatar)) // VR Battlegrounds patch 91
                {
                    TryGetGrabPoseMeshes(avatarPrefab, snapTransform.transform, out _previewMeshLeft, out _previewMeshRight);

                    if (_previewMeshLeft != null && leftGrabberRenderer)
                    {
                        GameObject grabPose = CreateAndSetupPreviewMeshObject(snapTransform.transform, _previewMeshLeft, _previewMeshLeft.UnityMesh, leftGrabberRenderer.sharedMaterials);
                    }

                    if (_previewMeshRight != null && rightGrabberRenderer)
                    {
                        GameObject grabPose = CreateAndSetupPreviewMeshObject(snapTransform.transform, _previewMeshRight, _previewMeshRight.UnityMesh, rightGrabberRenderer.sharedMaterials);
                    }
                }
            }
        }

        /// <summary>
        ///     Refreshes the preview poses for a specific avatar and hand pose. Designed to be called
        ///     from the Hand Pose Editor to keep the preview meshes updated.
        /// </summary>
        /// <param name="avatarPrefab">The registered avatar to look for</param>
        /// <param name="handPoseAsset">The pose asset to look for and update</param>
        private void RefreshGrabPoseMeshesInternal(UxrAvatar avatarPrefab, UxrHandPoseAsset handPoseAsset)
        {
            foreach (Object targetObject in serializedObject.targetObjects)
            {
                UxrGrabbableObjectSnapTransform snapTransform   = targetObject as UxrGrabbableObjectSnapTransform;
                UxrGrabbableObject              grabbableObject = snapTransform.SafeGetComponentInParent<UxrGrabbableObject>();

                UxrAvatar selectedAvatar = UxrAvatarEditorExt.GetFromGuid(grabbableObject.Editor_GetGrabAlignTransformAvatar(snapTransform.transform));

                if (selectedAvatar == null || grabbableObject == null || !selectedAvatar.IsPrefabCompatibleWith(avatarPrefab))
                {
                    continue;
                }

                UxrAvatar avatar = UxrGrabbableObjectEditor.TryGetSceneAvatar(avatarPrefab.gameObject);

                // VR Battlegrounds patch 91: при превью по потомку запись хвата и snap читаются у выбранного аватара.
                UxrAvatar lookupAvatar = UxrGrabbableObjectEditor.GetPreviewDescendantIfNeeded(selectedAvatar) != null ? selectedAvatar : avatar;

                if (lookupAvatar == null)
                {
                    continue;
                }

                for (int i = 0; i < grabbableObject.GrabPointCount; ++i)
                {
                    UxrGrabPointInfo grabPoint     = grabbableObject.GetGrabPoint(i);
                    UxrGripPoseInfo  gripPose      = grabPoint.GetGripPoseInfo(lookupAvatar);
                    UxrHandPoseAsset gripPoseAsset = gripPose?.HandPose;

                    if (gripPoseAsset == null)
                    {
                        continue;
                    }

                    // Does this grab point use the pose?

                    if ((grabbableObject.Editor_GetGrabPointGrabAlignTransform(lookupAvatar, i, UxrHandSide.Left) == snapTransform.transform ||
                         grabbableObject.Editor_GetGrabPointGrabAlignTransform(lookupAvatar, i, UxrHandSide.Right) == snapTransform.transform) && gripPoseAsset.name == handPoseAsset.name)
                    {
                        UxrGrabbableObjectEditor.RefreshGrabPreviewMesh(_previewMeshLeft,  grabbableObject, _cachedSingleObjectAvatar, selectedAvatar, i, UxrHandSide.Left,  true); // VR Battlegrounds patch 91
                        UxrGrabbableObjectEditor.RefreshGrabPreviewMesh(_previewMeshRight, grabbableObject, _cachedSingleObjectAvatar, selectedAvatar, i, UxrHandSide.Right, true); // VR Battlegrounds patch 91
                    }
                }
            }
        }

        /// <summary>
        ///     Checks if a given UxrGrabbableObjectSnapTransform's transform is referenced by
        ///     an UxrGrabbableObject. Components that are not being referenced are usually the
        ///     result of duplicating manually in the editor a GameObject that is used as a
        ///     snap transform for an UxrGrabbableObject. It also happens if a transform is
        ///     moved out of the UxrGrabbableObject hierarchy.
        ///     In case of not being referenced, the preview meshes hanging from it should be
        ///     deleted. The method will also remove the UxrGrabbableObjectSnapTransform
        ///     component itself.
        /// </summary>
        /// <param name="snapTransform">The transform to check</param>
        private void CheckUnusedTransform(UxrGrabbableObjectSnapTransform snapTransform)
        {
            if (snapTransform == null)
            {
                return;
            }

            UxrGrabbableObject grabbableObject = snapTransform.SafeGetComponentInParent<UxrGrabbableObject>();

            if (grabbableObject)
            {
                // Check if it's still being referenced by grabbableObject

                bool referenced = false;

                for (int i = 0; i < grabbableObject.GrabPointCount; ++i)
                {
                    if (grabbableObject.Editor_HasGrabPointWithGrabAlignTransform(snapTransform.transform))
                    {
                        referenced = true;
                        break;
                    }
                }

                if (!referenced)
                {
                    // Hanging from UxrGrabbableObject but not referenced
                    DestroyGrabPoseTempEditorObjects(snapTransform);
                    DestroyImmediate(snapTransform);
                }
            }
            else
            {
                // Moved out of UxrGrabbableObject hierarchy
                DestroyGrabPoseTempEditorObjects(snapTransform);
                DestroyImmediate(snapTransform);
            }
        }

        /// <summary>
        ///     Destroys the temporal grab pose objects created by CreateGrabPoseTempEditorObjects()
        /// </summary>
        private void DestroyGrabPoseTempEditorObjects()
        {
            foreach (Object targetObject in serializedObject.targetObjects)
            {
                UxrGrabbableObjectSnapTransform snapTransform = targetObject as UxrGrabbableObjectSnapTransform;
                DestroyGrabPoseTempEditorObjects(snapTransform);
            }
        }

        /// <summary>
        ///     Destroys all temporal editor objects belonging to the snap transform
        /// </summary>
        /// <param name="snapTransform">Snap transform to delete temporal objects for</param>
        private void DestroyGrabPoseTempEditorObjects(UxrGrabbableObjectSnapTransform snapTransform)
        {
            if (snapTransform)
            {
                UxrGrabbableObjectPreviewMesh[] previewMeshComponents = snapTransform.GetComponentsInChildren<UxrGrabbableObjectPreviewMesh>();

                foreach (UxrGrabbableObjectPreviewMesh previewMeshComponent in previewMeshComponents)
                {
                    DestroyImmediate(previewMeshComponent.gameObject);
                }
            }
        }

        #endregion

        #region Private Types & Data

        private static readonly Dictionary<SerializedObject, UxrGrabbableObjectSnapTransformEditor> s_openEditors = new Dictionary<SerializedObject, UxrGrabbableObjectSnapTransformEditor>();

        private UxrAvatar              _cachedSingleObjectAvatar;
        private UxrPreviewHandGripMesh _previewMeshLeft;
        private UxrPreviewHandGripMesh _previewMeshRight;

        #endregion
    }
}
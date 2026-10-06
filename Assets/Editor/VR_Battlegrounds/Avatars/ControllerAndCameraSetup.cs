using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using System.Linq;

namespace VRBattlegrounds.Editor
{
    public class ControllerAndCameraSetup
    {
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");
            if (avatarObj == null)
            {
                var foundUxrAvatar = Object.FindAnyObjectByType<UltimateXR.Avatar.UxrAvatar>();
                if (foundUxrAvatar != null) avatarObj = foundUxrAvatar.gameObject;
            }
            if (avatarObj == null) return;

            Setup(avatarObj);
        }

        public static void Setup(GameObject avatarObj)
        {
            if (!avatarObj) throw new System.ArgumentNullException(nameof(avatarObj));

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 3. Controller & Camera");

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                throw new System.InvalidOperationException("На явной цели отсутствует UxrAvatar.");
            }

            // 1. Controller
            var standardController = avatarObj.GetComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>();
            if (standardController == null)
            {
                standardController = avatarObj.AddComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>();
            }

            SerializedObject soController = new SerializedObject(standardController);
            SerializedProperty propAvatar = soController.FindProperty("_avatar");
            if (propAvatar != null) propAvatar.objectReferenceValue = uxrAvatar;

            SerializedProperty propOverExtend = soController.FindProperty("_armIKOverExtendMode");
            if (propOverExtend != null) propOverExtend.intValue = (int)UltimateXR.Animation.IK.UxrArmOverExtendMode.ExtendArm;

            // Глаза и свободный угол головы — общий шаг ApplyHeadDefaults (кости глаз, затем высота по ним).
            Animator rigAnimator = avatarObj.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.isHuman);
            ApplyHeadDefaults(avatarObj, soController, rigAnimator);

            soController.ApplyModifiedProperties();

            // Setup Head/Eyes in Rig if not matched
            Transform headTransform = rigAnimator != null ? rigAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (headTransform != null)
            {
                Transform leftEye = rigAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform rightEye = rigAnimator.GetBoneTransform(HumanBodyBones.RightEye);
                if (leftEye != null && rightEye != null)
                {
                    uxrAvatar.AvatarRig.Head.LeftEye = leftEye;
                    uxrAvatar.AvatarRig.Head.RightEye = rightEye;
                }
            }

            // Слои телепорта — как у рабочего Heavy_Soldier_Base_Avatar: пол карт лежит на слое Ground,
            // без него в Valid Target Layers телепорт не находит цель (AvatarLoadoutTests.Телепорт_попадает_в_пол_карт).
            var teleports = uxrAvatar.GetComponentsInChildren<UltimateXR.Locomotion.UxrTeleportLocomotionBase>(true);
            foreach (var tp in teleports)
            {
                tp.ValidTargetLayers = LayerMask.GetMask("Ground");
                tp.BlockingTargetLayers = 63;
                EditorUtility.SetDirty(tp);
            }

            // 2. Camera Setup
            Camera existingCamera = avatarObj.GetComponentInChildren<Camera>(true);
            if (existingCamera == null)
            {
                GameObject cameraController = new GameObject("Camera Controller");
                cameraController.transform.SetPositionAndRotation(avatarObj.transform.position, avatarObj.transform.rotation);
                cameraController.transform.parent = avatarObj.transform;
                cameraController.transform.SetAsFirstSibling();
                Undo.RegisterCreatedObjectUndo(cameraController, "Create Camera Controller");

                GameObject cameraObject = new GameObject("Camera");
                cameraObject.transform.SetPositionAndRotation(cameraController.transform.position, cameraController.transform.rotation);
                cameraObject.transform.parent = cameraController.transform;
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create Camera");

                Camera newCamera = cameraObject.AddComponent<Camera>();
                newCamera.nearClipPlane = 0.01f;
                cameraObject.AddComponent<AudioListener>();
                VrBattlegrounds.Core.GameLog.Player.Info("✅ [3/4] Camera Controller hierarchy successfully created.");
            }
            else
            {
                VrBattlegrounds.Core.GameLog.Player.Info("✅ [3/4] Camera already exists, skipped camera creation.");
            }

            EditorUtility.SetDirty(standardController);
            EditorUtility.SetDirty(avatarObj);
        }

        /// <summary>
        /// Положение глаз модели, выверенное в шлеме у зеркала, если кости глаз рига стоят не там, где видимые глаза:
        /// локальные позиции костей глаз в голове (абсолютные — повторный запуск ничего не сдвигает) и свободный угол
        /// наклона головы без участия корпуса. Совпадение — по имени корня аватара.
        /// </summary>
        private static readonly (string nameContains, Vector3 leftEyeLocal, Vector3 rightEyeLocal, float headFreeRangeBend)[] HeadDefaults =
        {
            // MEF (2026-10-06): кости CC_Base_L/R_Eye модели стояли на переносице под очками — камера была на уровне носа.
            // Подобрано в шлеме: +2,95 см вверх и +5,96 см вперёд (в осях корня) — центр линз очков.
            ("MEF", new Vector3(-0.0761f, 0.108f, 0.0208f), new Vector3(-0.0761f, 0.108f, -0.0208f), 55f),
        };

        /// <summary>
        /// Кости глаз (по <see cref="HeadDefaults"/>), затем высота и вынос глаз UltimateXR — по костям глаз (+0,02 м вперёд,
        /// как у UltimateXR по умолчанию), и свободный угол наклона головы. Только эти поля: остальные настройки аватара не
        /// трогаются, поэтому шаг можно вызывать отдельно на готовом префабе.
        /// </summary>
        public static void ApplyHeadDefaults(GameObject avatarObj, SerializedObject soController, Animator rigAnimator)
        {
            if (rigAnimator == null || !rigAnimator.isHuman) return;

            Transform leftEye = rigAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform rightEye = rigAnimator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye == null || rightEye == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Warning("👀 [3/4] Humanoid rig is missing LeftEye or RightEye. Skipped auto-calculating eye offsets.");
                return;
            }

            SerializedProperty propBodyIK = soController.FindProperty("_bodyIKSettings");
            foreach (var d in HeadDefaults)
            {
                if (!avatarObj.name.Contains(d.nameContains)) continue;
                leftEye.localPosition = d.leftEyeLocal;
                rightEye.localPosition = d.rightEyeLocal;
                EditorUtility.SetDirty(leftEye);
                EditorUtility.SetDirty(rightEye);
                if (propBodyIK != null) propBodyIK.FindPropertyRelative("_headFreeRangeBend").floatValue = d.headFreeRangeBend;
                VrBattlegrounds.Core.GameLog.Player.Info($"👀 [3/4] {avatarObj.name}: кости глаз и свободный угол головы — выверенные значения ({d.nameContains}).");
            }

            if (propBodyIK == null) return;
            float eyesBaseHeight = (leftEye.position.y + rightEye.position.y) * 0.5f - avatarObj.transform.position.y;
            Vector3 leftEyeLocal = avatarObj.transform.InverseTransformPoint(leftEye.position);
            Vector3 rightEyeLocal = avatarObj.transform.InverseTransformPoint(rightEye.position);
            float eyesForwardOffset = (leftEyeLocal.z + rightEyeLocal.z) * 0.5f + 0.02f; // UXR default buffer +0.02f

            propBodyIK.FindPropertyRelative("_eyesBaseHeight").floatValue = eyesBaseHeight;
            propBodyIK.FindPropertyRelative("_eyesForwardOffset").floatValue = eyesForwardOffset;
            VrBattlegrounds.Core.GameLog.Player.Info($"👀 [3/4] Controller Setup: Auto-calculated eyes height ({eyesBaseHeight:F2}) and offset ({eyesForwardOffset:F2}).");
        }
    }
}

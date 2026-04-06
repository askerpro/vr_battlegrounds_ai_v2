using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;

namespace VRBattlegrounds.Editor
{
    public class ControllerAndCameraSetup
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/3. Controller & Camera")]
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");
            if (avatarObj == null)
            {
                var foundUxrAvatar = Object.FindObjectOfType<UltimateXR.Avatar.UxrAvatar>();
                if (foundUxrAvatar != null) avatarObj = foundUxrAvatar.gameObject;
            }
            if (avatarObj == null) return;

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 3. Controller & Camera");

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                Debug.LogError("UXR Setup: Missing UxrAvatar on AutoSetupAvatarTarget. Did you run Step 1?");
                return;
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

            // Auto-calculate "Use Avatar Eyes"
            Animator rigAnimator = avatarObj.GetComponent<Animator>();
            if (rigAnimator != null && rigAnimator.isHuman)
            {
                Transform leftEye = rigAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform rightEye = rigAnimator.GetBoneTransform(HumanBodyBones.RightEye);
                
                if (leftEye != null && rightEye != null)
                {
                    SerializedProperty propBodyIK = soController.FindProperty("_bodyIKSettings");
                    if (propBodyIK != null)
                    {
                        float eyesBaseHeight = (leftEye.position.y + rightEye.position.y) * 0.5f - avatarObj.transform.position.y;
                        Vector3 leftEyeLocal = avatarObj.transform.InverseTransformPoint(leftEye.position);
                        Vector3 rightEyeLocal = avatarObj.transform.InverseTransformPoint(rightEye.position);
                        float eyesForwardOffset = (leftEyeLocal.z + rightEyeLocal.z) * 0.5f + 0.02f; // UXR default buffer +0.02f
                        
                        propBodyIK.FindPropertyRelative("_eyesBaseHeight").floatValue = eyesBaseHeight;
                        propBodyIK.FindPropertyRelative("_eyesForwardOffset").floatValue = eyesForwardOffset;
                        
                        Debug.Log($"👀 [3/4] Controller Setup: Auto-calculated eyes height ({eyesBaseHeight:F2}) and offset ({eyesForwardOffset:F2}).");
                    }
                }
                else
                {
                    Debug.LogWarning("👀 [3/4] Humanoid rig is missing LeftEye or RightEye. Skipped auto-calculating eye offsets.");
                }
            }

            soController.ApplyModifiedProperties();

            // Setup Head/Eyes in Rig if not matched
            Transform headTransform = rigAnimator != null ? rigAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (headTransform != null)
            {
                Transform leftEye = headTransform.Find("LeftEye");
                Transform rightEye = headTransform.Find("RightEye");
                
                SerializedObject soAvatar = new SerializedObject(uxrAvatar);
                SerializedProperty uxrRigProp = soAvatar.FindProperty("_avatarRig");
                if (uxrRigProp != null)
                {
                    SerializedProperty headGroup = uxrRigProp.FindPropertyRelative("_head");
                    if (headGroup != null && leftEye != null && rightEye != null)
                    {
                        headGroup.FindPropertyRelative("leftEye").objectReferenceValue = leftEye;
                        headGroup.FindPropertyRelative("rightEye").objectReferenceValue = rightEye;
                    }
                }
                soAvatar.ApplyModifiedProperties();
            }

            // Teleportation standard layers
            var teleports = uxrAvatar.GetComponentsInChildren<UltimateXR.Locomotion.UxrTeleportLocomotionBase>(true);
            foreach (var tp in teleports)
            {
                tp.ValidTargetLayers = 1;
                tp.BlockingTargetLayers = 55;
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
                cameraObject.tag = "MainCamera";
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create Camera");

                Camera newCamera = cameraObject.AddComponent<Camera>();
                newCamera.nearClipPlane = 0.01f;
                cameraObject.AddComponent<AudioListener>();
                Debug.Log("✅ [3/4] Camera Controller hierarchy successfully created.");
            }
            else
            {
                Debug.Log("✅ [3/4] Camera already exists, skipped camera creation.");
            }

            EditorUtility.SetDirty(standardController);
            EditorUtility.SetDirty(avatarObj);
        }
    }
}

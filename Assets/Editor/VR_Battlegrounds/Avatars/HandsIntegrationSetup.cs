using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using System.Linq;

namespace VRBattlegrounds.Editor
{
    public class HandsIntegrationSetup
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

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 2. Hands Integration");

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                throw new System.InvalidOperationException("На явной цели отсутствует UxrAvatar.");
            }

            // 1. BigHandsIntegration
            string bigHandsPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/HandIntegrations/BigHandsIntegration.prefab";
            GameObject bigHandsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bigHandsPath);
            GameObject bigHandsInst = null;

            var allIntegrations = avatarObj.transform.Cast<Transform>().Where(t => t.name.Contains("BigHandsIntegration")).ToList();
            if (allIntegrations.Count > 1)
            {
                for (int i = 1; i < allIntegrations.Count; i++) Undo.DestroyObjectImmediate(allIntegrations[i].gameObject);
            }

            Transform existingIntegration = allIntegrations.Count > 0 ? allIntegrations[0] : null;
            if (existingIntegration != null)
            {
                bigHandsInst = existingIntegration.gameObject;
            }
            else
            {
                if (bigHandsPrefab != null)
                {
                    bigHandsInst = (GameObject)PrefabUtility.InstantiatePrefab(bigHandsPrefab, avatarObj.transform);
                }
                else
                {
                    throw new System.InvalidOperationException("Не найден BigHandsIntegration: " + bigHandsPath);
                }
            }

            // 2. IK Hands Models
            string leftHandPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Internal/Hands/IK/BigIKHandLeft.prefab";
            string rightHandPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Internal/Hands/IK/BigIKHandRight.prefab";

            Transform leftHandT = bigHandsInst.transform.Find("LeftHand");
            Transform rightHandT = bigHandsInst.transform.Find("RightHand");

            if (leftHandT == null || rightHandT == null)
            {
                throw new System.InvalidOperationException("В BigHandsIntegration отсутствуют LeftHand/RightHand.");
            }

            var duplicateLefts = avatarObj.transform.Cast<Transform>().Where(t => t.name.Contains("BigIKHandLeft")).ToList();
            if (duplicateLefts.Count > 1) { for (int i = 1; i < duplicateLefts.Count; i++) Undo.DestroyObjectImmediate(duplicateLefts[i].gameObject); }

            var duplicateRights = avatarObj.transform.Cast<Transform>().Where(t => t.name.Contains("BigIKHandRight")).ToList();
            if (duplicateRights.Count > 1) { for (int i = 1; i < duplicateRights.Count; i++) Undo.DestroyObjectImmediate(duplicateRights[i].gameObject); }

            GameObject leftModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(leftHandPath);
            GameObject rightModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rightHandPath);
            if (!leftModelPrefab || !rightModelPrefab) throw new System.InvalidOperationException("Не найдены SDK IK hand prefabs.");

            GameObject leftModel = duplicateLefts.Count > 0 ? duplicateLefts[0].gameObject : null;
            if (leftModel == null && leftModelPrefab != null) leftModel = (GameObject)PrefabUtility.InstantiatePrefab(leftModelPrefab, avatarObj.transform);

            GameObject rightModel = duplicateRights.Count > 0 ? duplicateRights[0].gameObject : null;
            if (rightModel == null && rightModelPrefab != null) rightModel = (GameObject)PrefabUtility.InstantiatePrefab(rightModelPrefab, avatarObj.transform);

            // 3. Update Renderers references
            Renderer[] allRenderers = avatarObj.GetComponentsInChildren<Renderer>(true);
            SerializedObject soAvatar = new SerializedObject(uxrAvatar);

            SerializedProperty propRigType = soAvatar.FindProperty("_rigType");
            if (propRigType != null) propRigType.intValue = (int)UltimateXR.Avatar.Rig.UxrAvatarRigType.HalfOrFullBody;

            SerializedProperty propRenderers = soAvatar.FindProperty("_avatarRenderers");
            if (propRenderers != null)
            {
                propRenderers.ClearArray();
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    propRenderers.InsertArrayElementAtIndex(i);
                    propRenderers.GetArrayElementAtIndex(i).objectReferenceValue = allRenderers[i];
                }
            }
            soAvatar.ApplyModifiedProperties();

            Renderer leftRend = leftModel?.GetComponentInChildren<Renderer>();
            Renderer rightRend = rightModel?.GetComponentInChildren<Renderer>();

            UxrGrabber grabberLeft = leftHandT.GetComponentInChildren<UxrGrabber>(true);
            if (grabberLeft != null && leftRend != null) grabberLeft.HandRenderer = leftRend;

            UxrGrabber grabberRight = rightHandT.GetComponentInChildren<UxrGrabber>(true);
            if (grabberRight != null && rightRend != null) grabberRight.HandRenderer = rightRend;

            EditorUtility.SetDirty(avatarObj);
            EditorUtility.SetDirty(uxrAvatar);
            VrBattlegrounds.Core.GameLog.Player.Info($"✅ [2/4] Hands Integration Setup successful: IK Models and Logic Prefabs injected.");
        }
    }
}

using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;

namespace VRBattlegrounds.Editor
{
    public class CoreAvatarSetup
    {
        [MenuItem("Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/1. Core Setup")]
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");

            if (avatarObj == null)
            {
                Debug.LogError("UXR Setup: Please select an Avatar in the hierarchy first, or name it 'AutoSetupAvatarTarget'.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 1. Core");

            string currentName = avatarObj.name.Replace("(Clone)", "").Trim();
            if (currentName.StartsWith("VR_Avatar")) currentName = "CustomAvatar";
            avatarObj.name = currentName;

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                uxrAvatar = avatarObj.AddComponent<UxrAvatar>();
                Animator animator = avatarObj.GetComponent<Animator>();
                if (animator != null)
                {
                    UltimateXR.Avatar.Rig.UxrAvatarRig.SetupRigElementsFromAnimator(uxrAvatar.AvatarRig, animator);
                }
            }

            EditorUtility.SetDirty(avatarObj);
            Debug.Log($"✅ [1/4] Core Setup successful on '{avatarObj.name}': UxrAvatar and Rig applied.");
        }
    }
}

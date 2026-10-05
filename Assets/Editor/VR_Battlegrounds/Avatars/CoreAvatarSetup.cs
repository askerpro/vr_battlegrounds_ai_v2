using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;

namespace VRBattlegrounds.Editor
{
    public class CoreAvatarSetup
    {
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");

            if (avatarObj == null)
            {
                GameLog.Player.Error("UXR Setup: Please select an Avatar in the hierarchy first, or name it 'AutoSetupAvatarTarget'.");
                return;
            }

            Setup(avatarObj);
        }

        public static void Setup(GameObject avatarObj)
        {
            if (!avatarObj) throw new System.InvalidOperationException("Передайте корень аватара явно.");
            var animators = avatarObj.GetComponentsInChildren<Animator>(true);
            var humanoids = System.Array.FindAll(animators, a => a.avatar && a.avatar.isHuman && a.avatar.isValid);
            if (humanoids.Length != 1) throw new System.InvalidOperationException("Core Setup требует ровно один валидный Humanoid.");
            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 1. Core");

            string currentName = avatarObj.name.Replace("(Clone)", "").Trim();
            if (currentName.StartsWith("VR_Avatar")) currentName = "CustomAvatar";
            avatarObj.name = currentName;

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                uxrAvatar = avatarObj.AddComponent<UxrAvatar>();
                Animator animator = humanoids[0];
                if (animator != null)
                {
                    UltimateXR.Avatar.Rig.UxrAvatarRig.SetupRigElementsFromAnimator(uxrAvatar.AvatarRig, animator);
                }
            }

            EditorUtility.SetDirty(avatarObj);
            GameLog.Player.Info($"✅ [1/4] Core Setup successful on '{avatarObj.name}': UxrAvatar and Rig applied.");
        }
    }
}

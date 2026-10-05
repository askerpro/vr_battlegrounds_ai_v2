using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace VRBattlegrounds.Editor
{
    public class CreatePrefabSetup
    {
        public static void Execute()
        {
            GameObject avatarObj = Selection.activeGameObject;
            if (avatarObj == null) avatarObj = GameObject.Find("AutoSetupAvatarTarget");
            if (avatarObj == null)
            {
                var foundUxrAvatar = Object.FindObjectOfType<UltimateXR.Avatar.UxrAvatar>();
                if (foundUxrAvatar != null) avatarObj = foundUxrAvatar.gameObject;
            }
            if (avatarObj == null)
            {
                GameLog.Player.Error("UXR Setup: Missing AutoSetupAvatarTarget in scene.");
                return;
            }

            string folderPath = "Assets/Prefabs/Player";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string baseName = avatarObj.name.Replace("(Clone)", "").Replace("_Ready", "").Replace("VR_Avatar", "").Trim();
            if (string.IsNullOrEmpty(baseName))
            {
                Object src = PrefabUtility.GetCorrespondingObjectFromSource(avatarObj);
                if (src != null) baseName = src.name.Replace("_Ready", "").Trim();
            }
            if (string.IsNullOrEmpty(baseName) || baseName == "_Ready") baseName = "CustomAvatar";

            string prefabPath = $"{folderPath}/{baseName}.prefab";
            prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

            bool success;
            PrefabUtility.SaveAsPrefabAssetAndConnect(avatarObj, prefabPath, InteractionMode.AutomatedAction, out success);

            if (success)
            {
                GameLog.Player.Info($"✅ [5/5] Avatar Prefab successfully created at: {prefabPath}");
                // Cleanup the unlinked object after prefab hook
            }
            else
            {
                GameLog.Player.Error($"UXR Setup: Failed to save Prefab at {prefabPath}");
            }
        }
    }
}

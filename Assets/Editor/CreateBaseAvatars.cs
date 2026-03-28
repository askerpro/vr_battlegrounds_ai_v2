using UnityEngine;
using UnityEditor;
using UltimateXR.Avatar;

public static class CreateBaseAvatars
{
    [MenuItem("Tools/Create Base Avatars")]
    public static void Execute()
    {
        string[] meshPaths = new string[] {
            "Assets/ThirdParty/Military Soldier Mega Bundle/Heavy Soldier/Prefabs/Heavy_Soldier_Face_Blue.prefab",
            "Assets/ThirdParty/Military Soldier Mega Bundle/Military Soldier/Military Soldier Desert Skin/Prefabs/Soldier_Rig_Face.prefab",
            "Assets/ThirdParty/Military Soldier Mega Bundle/Military Spy Set/Military Spy/Prefabs/Military_Spy_Head.prefab",
            "Assets/ThirdParty/Military Soldier Mega Bundle/Military Soldier Cap/Military Soldier Cap_Desert/Prefabs/Soldier_Cap.prefab"
        };
        string[] names = new string[] {
            "Heavy_Soldier_Base_Avatar",
            "Military_Soldier_Base_Avatar",
            "Spy_Base_Avatar",
            "Military_Cap_Base_Avatar"
        };

        string playerBasePath = "Assets/Prefabs/Player/PlayerBase.prefab";
        GameObject playerBaseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(playerBasePath);

        if (playerBaseAsset == null)
        {
            Debug.LogError("PlayerBase not found. Generate it first.");
            return;
        }

        for (int i = 0; i < meshPaths.Length; i++)
        {
            GameObject meshAsset = AssetDatabase.LoadAssetAtPath<GameObject>(meshPaths[i]);
            if (meshAsset == null)
            {
                Debug.LogError($"Could not find mesh prefab at {meshPaths[i]}");
                continue;
            }

            // Instantiate PlayerBase
            GameObject baseInstance = PrefabUtility.InstantiatePrefab(playerBaseAsset) as GameObject;
            PrefabUtility.UnpackPrefabInstance(baseInstance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            baseInstance.name = names[i];

            // Instantiate Mesh
            GameObject meshInstance = PrefabUtility.InstantiatePrefab(meshAsset, baseInstance.transform) as GameObject;
            PrefabUtility.UnpackPrefabInstance(meshInstance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            meshInstance.name = "Model";

            // Make sure Animator is humanoid
            Animator anim = meshInstance.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                UxrAvatar avatar = baseInstance.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    bool success = avatar.SetupRigElementsFromAnimator();
                    Debug.Log($"SetupRigElementsFromAnimator for {names[i]}: {success}");
                }
            }
            else
            {
                Debug.LogWarning($"No Humanoid Animator found for {names[i]}");
            }

            // Save
            string outPath = $"Assets/Prefabs/Player/{names[i]}.prefab";
            PrefabUtility.SaveAsPrefabAsset(baseInstance, outPath);
            GameObject.DestroyImmediate(baseInstance);
            Debug.Log($"Created {outPath}");
        }
    }
}

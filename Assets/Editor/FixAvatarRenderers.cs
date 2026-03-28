using UnityEngine;
using UnityEditor;
using UltimateXR.Avatar;

public static class FixAvatarRenderers
{
    [MenuItem("Tools/Fix Avatar Renderers")]
    public static void Execute()
    {
        string[] prefabs = new[] {
            "Assets/Prefabs/Player/Heavy_Soldier_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Military_Soldier_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Spy_Base_Avatar.prefab",
            "Assets/Prefabs/Player/Military_Cap_Base_Avatar.prefab"
        };

        foreach (var path in prefabs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                UxrAvatar avatar = editingScope.prefabContentsRoot.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    Renderer[] renderers = avatar.GetComponentsInChildren<Renderer>(true);
                    var so = new SerializedObject(avatar);
                    var prop = so.FindProperty("_avatarRenderers");
                    
                    if (prop != null)
                    {
                        prop.ClearArray();
                        prop.arraySize = renderers.Length;
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                        }
                        so.ApplyModifiedProperties();
                        Debug.Log($"Fixed Avatar Renderers for {path}: assigned {renderers.Length} renderers.");
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find _avatarRenderers property on {path}");
                    }
                }
            }
        }
        
        AssetDatabase.SaveAssets();
    }
}

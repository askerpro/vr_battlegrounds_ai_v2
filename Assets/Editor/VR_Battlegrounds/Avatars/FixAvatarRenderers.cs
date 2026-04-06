using UnityEngine;
using UnityEditor;
using UltimateXR.Avatar;

public static class FixAvatarRenderers
{
    [MenuItem("Tools/VR Battlegrounds/Avatars/Fix Avatar Renderers")]
    public static void Execute()
    {
        var selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("Please select at least one Avatar in the scene to fix renderers.");
            return;
        }

        int successCount = 0;
        foreach (var obj in selectedObjects)
        {
            UxrAvatar avatar = obj.GetComponent<UxrAvatar>();
            if (avatar != null)
            {
                Undo.RecordObject(avatar, "Fix Avatar Renderers");

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
                    
                    EditorUtility.SetDirty(avatar);
                    Debug.Log($"✅ Fixed Avatar Renderers for {obj.name}: automatically assigned {renderers.Length} renderers.");
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"Could not find _avatarRenderers property on {obj.name}. Is it an older version of UltimateXR?");
                }
            }
        }
        
        if (successCount == 0)
        {
            Debug.LogWarning("None of the selected objects have a UxrAvatar component attached.");
        }
    }
}

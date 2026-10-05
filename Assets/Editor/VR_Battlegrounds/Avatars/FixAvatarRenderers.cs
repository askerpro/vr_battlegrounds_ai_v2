using UnityEngine;
using UnityEditor;
using UltimateXR.Avatar;
using System;
using System.Linq;
using VrBattlegrounds.Core;

public static class FixAvatarRenderers
{
    public static void Execute()
    {
        var selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            GameLog.Player.Warning("Выберите аватар для обновления renderers.");
            return;
        }

        int successCount = 0;
        foreach (var obj in selectedObjects)
        {
            UxrAvatar avatar = obj.GetComponent<UxrAvatar>();
            if (avatar != null)
            {
                GameLog.Player.Info(Setup(avatar));
                successCount++;
            }
        }
        
        if (successCount == 0)
        {
            GameLog.Player.Warning("В выбранных объектах отсутствует UxrAvatar.");
        }
    }

    public static string Setup(UxrAvatar avatar)
    {
        if (!avatar) throw new ArgumentNullException(nameof(avatar));
        var renderers = avatar.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.GetComponentInParent<UxrHandIntegration>() == null).ToArray();
        var so = new SerializedObject(avatar);
        var prop = so.FindProperty("_avatarRenderers");
        if (prop == null) throw new InvalidOperationException("В SDK отсутствует _avatarRenderers.");
        prop.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        so.ApplyModifiedProperties();
        return $"{avatar.name}: назначено renderers {renderers.Length}.";
    }
}

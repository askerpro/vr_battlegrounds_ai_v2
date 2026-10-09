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
        // Непустой сериализованный список задаёт владение геометрией. В дочерних объектах
        // могут находиться часы, лучи и временные копии тела, которые SDK не должен переключать.
        var configured = avatar.AvatarRenderers.ToArray();
        var candidates = configured.Length > 0
            ? configured
            : avatar.GetComponentsInChildren<Renderer>(true);
        var renderers = candidates
            .Where(r => r != null && r.GetComponentInParent<UxrAvatar>(true) == avatar &&
                        r.GetComponentInParent<UxrHandIntegration>(true) == null &&
                        !IsEditorOnly(r.transform, avatar.transform)).Distinct().ToArray();
        var so = new SerializedObject(avatar);
        var prop = so.FindProperty("_avatarRenderers");
        if (prop == null) throw new InvalidOperationException("В SDK отсутствует _avatarRenderers.");
        prop.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        so.ApplyModifiedProperties();
        return $"{avatar.name}: назначено renderers {renderers.Length}.";
    }

    private static bool IsEditorOnly(Transform renderer, Transform avatarRoot)
    {
        // Неактивный LOD остаётся частью тела. Исключается только явно авторская ветка.
        for (Transform current = renderer; current != null; current = current.parent)
        {
            if (current.CompareTag("EditorOnly")) return true;
            if (current == avatarRoot) break;
        }
        return false;
    }
}

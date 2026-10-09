// Диагностика импортированного префаба и удаления старой копии на неактивном экземпляре.
// Не заменяет Play Mode, сетевую и шлемную приёмку.
var source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab");
var container = new UnityEngine.GameObject("AvatarRendererRegressionProbe");
container.SetActive(false);
try
{
    var instance = UnityEngine.Object.Instantiate(source, container.transform, false);
    var avatar = instance.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    var legacy = instance.GetComponentsInChildren<UnityEngine.Transform>(true)
        .Single(t => t.name == "Ghost" && t.parent != null && t.parent.name == "Cyborg");
    var legacyRenderers = legacy.GetComponentsInChildren<UnityEngine.Renderer>(true);
    var original = avatar.AvatarRenderers.ToArray();
    var writer = System.AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("FixAvatarRenderers", false)).First(t => t != null);
    var noAuxiliaryReferences = !original.Intersect(legacyRenderers).Any();
    var authoringTag = legacy.CompareTag("EditorOnly");
    writer.GetMethod("Setup").Invoke(null, new object[] { avatar });
    var stableRebuild = new System.Collections.Generic.HashSet<UnityEngine.Renderer>(original)
        .SetEquals(avatar.AvatarRenderers);
    UnityEngine.Object.DestroyImmediate(legacy.gameObject);
    var noDeadReferences = avatar.AvatarRenderers.All(r => r != null);
    var depthBefore = UltimateXR.Core.StateSync.UxrStateSyncImplementer.SyncCallDepth;
    avatar.RenderMode = UltimateXR.Avatar.UxrAvatarRenderModes.None;
    var hidden = avatar.AvatarRenderers.All(r => !r.enabled);
    avatar.RenderMode = UltimateXR.Avatar.UxrAvatarRenderModes.Avatar;
    var shown = avatar.AvatarRenderers.All(r => r.enabled);
    var depthAfter = UltimateXR.Core.StateSync.UxrStateSyncImplementer.SyncCallDepth;
    var ghost = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
        "Assets/Prefabs/Player/Ghost/GhostAvatar.prefab");
    var ghostAvatar = ghost.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    return new {
        validation = "inactive-prefab-instance",
        serializedCount = original.Length,
        legacyRendererCount = legacyRenderers.Length,
        editorOnly = authoringTag,
        auxiliaryExcluded = noAuxiliaryReferences,
        rebuildPreservesSerializedSet = stableRebuild,
        noDeadReferences = noDeadReferences,
        hideWorks = hidden,
        showWorks = shown,
        depthBefore = depthBefore,
        depthAfter = depthAfter,
        ghostRendererCount = ghostAvatar.AvatarRenderers.Count(),
        ghostHasNoDeadReferences = ghostAvatar.AvatarRenderers.All(r => r != null),
        ghostHasBody = ghost.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true).Any(),
        passed = original.Length == 20 && legacyRenderers.Length == 14 && authoringTag &&
            noAuxiliaryReferences && stableRebuild && noDeadReferences && hidden && shown &&
            depthAfter == depthBefore && ghostAvatar.AvatarRenderers.Any() &&
            ghostAvatar.AvatarRenderers.All(r => r != null)
    };
}
finally
{
    UnityEngine.Object.DestroyImmediate(container);
}

// Наблюдение штатного спавна: никаких изменений Renderer или SDK.
var source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab");
var ghostSource = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/Player/Ghost/GhostAvatar.prefab");
string trackedSource = null;
UltimateXR.Avatar.UxrAvatar tracked = null;
UnityEngine.Renderer[] references = null;
string[] labels = null;
int[] ids = null;
double nextRead = 0;
System.Action<VrBattlegrounds.Player.PlayerController> onSpawn = player => {
    if (player == null || (player.SourcePrefab != source && player.SourcePrefab != ghostSource)) return;
    trackedSource = UnityEditor.AssetDatabase.GetAssetPath(player.SourcePrefab);
    tracked = player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
    references = tracked.AvatarRenderers.ToArray();
    labels = references.Select(r => r == null ? "missing-at-spawn" : r.name).ToArray();
    ids = references.Select(r => r == null ? 0 : r.GetInstanceID()).ToArray();
    UnityEditor.SessionState.SetString("VRBG.RendererProbe.BeforeStart", Newtonsoft.Json.JsonConvert.SerializeObject(new {
        avatar = tracked.name, source = trackedSource, count = references.Length,
        slots = references.Select((r, i) => new { index = i, name = labels[i], instanceId = ids[i], alive = r != null,
            belongsToInstance = r != null && r.transform.IsChildOf(tracked.transform),
            persistentAssetReference = r != null && UnityEditor.EditorUtility.IsPersistent(r) }).ToArray()
    }));
};
UnityEditor.EditorApplication.CallbackFunction observe = null;
observe = () => {
    if (!UnityEditor.EditorApplication.isPlaying) {
        VrBattlegrounds.Player.Avatars.AvatarManager.AvatarSpawned -= onSpawn;
        UnityEditor.EditorApplication.update -= observe;
        return;
    }
    if (tracked == null || references == null || UnityEditor.EditorApplication.timeSinceStartup < nextRead) return;
    nextRead = UnityEditor.EditorApplication.timeSinceStartup + 0.25;
    var started = typeof(UltimateXR.Avatar.UxrAvatar).GetField("_started", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    UnityEditor.SessionState.SetString("VRBG.RendererProbe.AfterStart", Newtonsoft.Json.JsonConvert.SerializeObject(new {
        avatar = tracked.name, source = trackedSource, started = started != null && (bool)started.GetValue(tracked),
        count = tracked.AvatarRenderers.Count(), syncDepth = UltimateXR.Core.StateSync.UxrStateSyncImplementer.SyncCallDepth,
        missing = references.Select((r, i) => new { index = i, name = labels[i], instanceId = ids[i], alive = r != null }).Where(r => !r.alive).ToArray()
    }));
};
UnityEditor.SessionState.EraseString("VRBG.RendererProbe.BeforeStart");
UnityEditor.SessionState.EraseString("VRBG.RendererProbe.AfterStart");
VrBattlegrounds.Player.Avatars.AvatarManager.AvatarSpawned += onSpawn;
UnityEditor.EditorApplication.update += observe;
return new { registered = true, sourceRendererCount = source.GetComponent<UltimateXR.Avatar.UxrAvatar>().AvatarRenderers.Count() };

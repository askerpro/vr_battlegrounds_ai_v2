// Temporal probe задачи 5: один и тот же код на чистом dev (RED) и на ветке (GREEN).
// Запуск: execute_code с этим телом. Без сети и Play Mode; открытую сцену worker возвращает сам.
// 1. OldRunSnapshotOpensChannel — ответ снимка без ожидаемого запроса/запуска не должен открывать канал Relay.
// 2. DirectMapPlayWithoutProcessRoot — Play из сцены карты с выключенной галочкой «Start from Offline Scene»
//    не должен стартовать сцену карты без процессного корня (Offline).
if (UnityEditor.EditorApplication.isPlaying || Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new System.InvalidOperationException("Probe требует свободного Editor без Play/сети.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var rows = new System.Collections.Generic.List<object>();
int failed = 0;

// ── 1. Relay ────────────────────────────────────────────────────────────────
var relayObject = new UnityEngine.GameObject("RelayBarrierProbe");
try
{
    relayObject.AddComponent<Mirror.NetworkIdentity>();
    var relay = relayObject.AddComponent<VrBattlegrounds.Network.NetworkStateRelay>();
    System.Reflection.MethodInfo userCode = null;
    foreach (var m in typeof(VrBattlegrounds.Network.NetworkStateRelay).GetMethods(flags))
        if (m.Name.StartsWith("UserCode_TargetLoadInitialState")) userCode = m;
    var parameters = userCode.GetParameters();
    object[] args = parameters.Length == 2
        ? new object[] { null, new byte[0] }                                                // dev: ключа и номера нет
        : new object[] { null, System.Guid.NewGuid(), 1UL, 7u, new byte[0] };                // ветка: чужой запуск, чужой номер
    userCode.Invoke(relay, args);
    var loaded = typeof(VrBattlegrounds.Network.NetworkStateRelay).GetField("_initialStateLoaded", flags);
    bool open;
    if (loaded != null) open = (bool)loaded.GetValue(relay);
    else
    {
        var barrier = typeof(VrBattlegrounds.Network.NetworkStateRelay).GetField("_barrier", flags).GetValue(relay);
        open = (bool)barrier.GetType().GetProperty("IsOpen").GetValue(barrier);
    }
    bool passed = !open;
    if (!passed) failed++;
    rows.Add(new { name = "OldRunSnapshotOpensChannel", passed, channelOpenAfterForeignSnapshot = open,
        contract = parameters.Length == 2 ? "ответ без MapRunKey и номера запроса" : "ответ с MapRunKey и номером запроса" });
}
finally { UnityEngine.Object.DestroyImmediate(relayObject); }

// ── 2. Direct Play ──────────────────────────────────────────────────────────
const string prefKey = "VrBattlegrounds.StartFromOffline";
bool previousPref = UnityEditor.EditorPrefs.GetBool(prefKey, true);
var previousStart = UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;
var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
try
{
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Maps/TestMap1.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
    UnityEditor.EditorPrefs.SetBool(prefKey, false);
    var type = System.Type.GetType("VrBattlegrounds.Editor.PlayModeStartFromOffline, Assembly-CSharp-Editor");
    type.GetMethod("UpdateState", flags).Invoke(null, null);
    var start = UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;
    string startPath = start != null ? UnityEditor.AssetDatabase.GetAssetPath(start) : "(активная сцена карты)";
    bool passed = startPath == "Assets/Scenes/Offline.unity";
    if (!passed) failed++;
    rows.Add(new { name = "DirectMapPlayWithoutProcessRoot", passed, startScene = startPath });
}
finally
{
    UnityEditor.EditorPrefs.SetBool(prefKey, previousPref);
    UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = previousStart;
    if (setup != null && setup.Length > 0) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
    else UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
        UnityEditor.SceneManagement.NewSceneMode.Single);
}

string path = "tasks/map-runtime-bootstrap/reports/relay-direct-play-" + (failed == 0 ? "green" : "red") + ".json";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = failed == 0, failureCount = failed, checks = rows },
    Newtonsoft.Json.Formatting.Indented));
return new { passed = failed == 0, failureCount = failed, checks = rows, reportPath = path };

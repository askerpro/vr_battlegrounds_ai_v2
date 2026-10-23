// Тело для MCP execute_code (только Edit Mode, worker в своей аренде). Открывает Lobby, ставит у стены арсенала
// временный ряд стволов KINEMATION (M_*) и эталон Hands_* (SCAR, AR-15), снимает кадр, при REPACK = true вызывает
// KinemationMaterials.RepackAll() и снимает второй кадр; затем убирает стенд и открывает Offline без сохранения Lobby.
// Кадры — в tasks/weapon-metal-materials/reports/stand/ этого worktree (вне Git).
const bool REPACK = true;
string outDir = "F:/CodexWorktrees/weapon-metal-materials/tasks/weapon-metal-materials/reports/stand";
System.IO.Directory.CreateDirectory(outDir);
var log = new System.Text.StringBuilder();

UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);

Transform arsenals = null;
foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    if (t.name == "Arsenals" && t.childCount > 0) { arsenals = t; break; }
if (arsenals == null) return "Нет Arsenals в Lobby";
Transform station = arsenals.GetChild(0);
var sceneBounds = new Bounds(station.position, Vector3.zero);
foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) sceneBounds.Encapsulate(r.bounds.center);
Vector3 dir = sceneBounds.center - station.position; dir.y = 0; dir.Normalize();
Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
log.AppendLine($"станция {station.name} {station.position} dir {dir}");

string[] prefabs = {
    "Assets/Prefabs/Weapons/AK105/AK105.prefab", "Assets/Prefabs/Weapons/SRM12/SRM12.prefab",
    "Assets/Prefabs/Weapons/Herrington/Herrington.prefab", "Assets/Prefabs/Weapons/TR15/TR15.prefab",
    "Assets/Prefabs/Weapons/Mk14/Mk14.prefab", "Assets/Prefabs/Weapons/Scar/Scar.prefab",
    "Assets/Prefabs/Weapons/AR15/AR15.prefab" };
var root = new GameObject("__MetalStand");
Vector3 basePos = station.position + dir * 0.7f + Vector3.up * 1.0f;
for (int i = 0; i < prefabs.Length; i++)
{
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]);
    if (asset == null) { log.AppendLine("нет " + prefabs[i]); continue; }
    var go = UnityEngine.Object.Instantiate(asset, root.transform);
    go.name = asset.name;
    // Ствол вдоль side, правый бок к камере; колонки по 4.
    int col = i / 4, row = i % 4;
    go.transform.SetPositionAndRotation(basePos + side * (col * 1.0f - 0.5f) + Vector3.up * (row * -0.25f + 0.35f),
                                        Quaternion.LookRotation(side, Vector3.up));
    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
    foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
    var rs = go.GetComponentsInChildren<Renderer>(true);
    var mats = new System.Collections.Generic.HashSet<string>();
    foreach (var r in rs) foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m.name);
    log.AppendLine($"{asset.name}: {string.Join(", ", mats)}");
}

var camGo = new GameObject("__MetalStandCam");
var cam = camGo.AddComponent<Camera>();
cam.fieldOfView = 40; cam.nearClipPlane = 0.05f;
cam.transform.position = basePos + dir * 1.9f + Vector3.up * 0.15f;
cam.transform.LookAt(basePos + Vector3.up * 0.0f);

System.Func<string, string> shot = name =>
{
    var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
    cam.targetTexture = rt;
    cam.Render();
    RenderTexture.active = rt;
    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    RenderTexture.active = null;
    cam.targetTexture = null;
    string p = outDir + "/" + name + ".png";
    System.IO.File.WriteAllBytes(p, tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    return p;
};

log.AppendLine(shot("before"));
if (REPACK)
{
    VrBattlegrounds.Editor.Gameplay.KinemationMaterials.RepackAll();
    log.AppendLine(shot("after"));
}

UnityEngine.Object.DestroyImmediate(camGo);
UnityEngine.Object.DestroyImmediate(root);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Offline.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
return log.ToString();

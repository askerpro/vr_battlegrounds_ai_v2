// Превью светильников (lobby-reflections, 2026-10-22). Только чтение: временные объекты в отдельной сцене, без сохранения.
// 1) Габариты военных ламп MilitaryCamp (кандидаты для сетки и акцента). 2) Снимки гражданских ламп базы для решения
// пользователя. 3) Снимки шатра Lobby изнутри (потолок, стена арсенала). Выход: <reports>/fixtures/*.png, fixtures.txt.
var sb = new System.Text.StringBuilder();
string outDir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "tasks/lobby-reflections/reports/fixtures");
System.IO.Directory.CreateDirectory(outDir);
var lobby = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);

System.Func<GameObject, Bounds> measure = go => { var rs = go.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds); return b; };
System.Func<Camera, string, bool> shoot = (cam, file) => {
    var rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4; cam.targetTexture = rt; cam.Render();
    RenderTexture.active = rt; var tex = new Texture2D(640, 480, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); tex.Apply(); RenderTexture.active = null;
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, file), tex.EncodeToPNG());
    cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); return true; };

// Шатёр изнутри: потолок с лампами и стена арсенала — в сцене Lobby, временная камера.
var temp = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
UnityEditor.SceneManagement.EditorSceneManager.SetActiveScene(lobby);
var camGo = new GameObject("cam"); UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(camGo, temp);
var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = .05f; cam.fieldOfView = 80;
camGo.transform.position = new Vector3(4.5f, 1.6f, 0f); camGo.transform.LookAt(new Vector3(-7f, 2.2f, 0f)); shoot(cam, "tent_west_arsenal.png");
camGo.transform.position = new Vector3(0f, 1.6f, -6.5f); camGo.transform.LookAt(new Vector3(0f, 4.5f, 3f)); shoot(cam, "tent_ceiling.png");
camGo.transform.position = new Vector3(0f, 1.7f, 0f); camGo.transform.LookAt(new Vector3(0f, 1.2f, 9f)); shoot(cam, "tent_north_firing.png");
UnityEngine.Object.DestroyImmediate(camGo);
UnityEditor.SceneManagement.EditorSceneManager.CloseScene(temp, true);

// Каталожные лампы — на нейтральном стенде в пустой сцене со своим светом.
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
var sunGo = new GameObject("sun"); var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sunGo.transform.rotation = Quaternion.Euler(40, 140, 0);
var paths = new[] {
    "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs/SM_Lamp_long.prefab",
    "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs/SM_Door_lamp_a.prefab",
    "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs/SM_AlarmLight.prefab",
    "Assets/Prefabs/Environment/LobbyDecoration/FloodlightHead.prefab",
    "Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Prefabs/SM_TopLight.prefab",
    "Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Prefabs/SM_Lamp.prefab",
    "Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Prefabs/SM_LampPost.prefab",
    "Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Prefabs/SM_Lamp_02.prefab",
    "Assets/Art/Lobby/Imported/Catalog/PostApocalypticTown/URP/Art/Prefabs/SM_Lamp.prefab",
    "Assets/Art/Lobby/Imported/Catalog/PostApocalypticTown/URP/Art/Prefabs/SM_Light1.prefab",
    "Assets/Art/Lobby/Imported/Catalog/PostApocalypticTown/URP/Art/Prefabs/SM_Light2.prefab",
    "Assets/Art/Lobby/Imported/Catalog/PostApocalypticTown/URP/Art/Prefabs/SM_Light3.prefab",
    "Assets/Art/Lobby/Imported/Catalog/AbandonedFactory/Art/Prefabs/SM_Lamp_01a.prefab",
    "Assets/Art/Lobby/Imported/Catalog/AbandonedFactory/Art/Prefabs/SM_CoverLight_01.prefab" };
var cam2Go = new GameObject("cam2"); var cam2 = cam2Go.AddComponent<Camera>(); cam2.clearFlags = CameraClearFlags.SolidColor; cam2.backgroundColor = new Color(.62f, .66f, .70f); cam2.fieldOfView = 30;
int idx = 0;
foreach (var p in paths)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (prefab == null) { sb.AppendLine("MISSING " + p); continue; }
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
    var b = measure(go);
    int tris = 0; foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
    float r = Mathf.Max(b.extents.magnitude, .05f);
    cam2Go.transform.position = b.center + new Vector3(.8f, .45f, -1f).normalized * r / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.1f; cam2Go.transform.LookAt(b.center);
    cam2.nearClipPlane = Mathf.Max(.01f, r * .05f); cam2.farClipPlane = r * 40;
    string file = idx.ToString("00") + "_" + System.IO.Path.GetFileNameWithoutExtension(p) + "_" + p.Split('/')[p.Split('/').Length > 6 ? 5 : 3] + ".png";
    shoot(cam2, file);
    sb.AppendLine(file + " size=" + b.size.ToString("F2") + " tris~" + tris + " " + p);
    UnityEngine.Object.DestroyImmediate(go); idx++;
}
System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "fixtures.txt"), sb.ToString());
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
return sb.ToString();

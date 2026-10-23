// Прототип «свет со стороны смотрящего» для Play Mode (execute_code, задача arsenal-slot-lighting, 2026-10-23).
// Пользователь сравнил наш вид с Scene view без освещения сцены (свет от камеры): там видны блики, детали и металл.
// Имитация того, что даст запекание: светящиеся панели («софтбоксы») перед стеной и над полкой видны только пробе
// отражений станции — металл отражает их бликом; прожектор со стороны игрока — рассеянный свет (в финале — через
// пробы света). render=true строит панели, свет и пробу (кубмапа рендерится сразу) и прячет панели; render=false
// только прячет панели. Повторный запуск пересобирает с параметрами ниже.
// Лампы прототипа ленты [PerimeterProto] выключаются, лента и бортики — из префаба слота.
if (!Application.isPlaying) return "Только в Play Mode";
bool render = true;
float wallPanelDistance = 1.2f, wallPanelHeight = 2.0f, wallPanelWidth = 4.8f, wallPanelTall = 2.0f;  // 1–3 м: верх стволов отражает область выше глаз
float shelfPanelUp = 0.55f, shelfPanelTall = 0.35f;     // панель над полкой: выше полки, наклонена к ней
float panelBrightness = 1.8f;
Color panelColor = new Color(1f, .93f, .85f);
float keyIntensity = 0.5f, keyRange = 3f, keyAngle = 100f;  // рассеянный свет со стороны игрока: на стену и на полку
float probeIntensity = 1f;
const string Tag = "[Softbox]";

if (!render)
{
    int hidden = 0;
    foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        if (t.name.StartsWith(Tag + "Panel")) { t.gameObject.SetActive(false); hidden++; }
    return "Панелей спрятано " + hidden;
}

foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    if (t != null && t.name.StartsWith(Tag)) UnityEngine.Object.Destroy(t.gameObject);
foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
    if (l.name.StartsWith("[PerimeterProto]Light")) l.enabled = false;

// Геометрия станции по доскам слотов: лицо стены (+X в Lobby), центр рядов.
var walls = new System.Collections.Generic.List<Transform>(); var shelves = new System.Collections.Generic.List<Transform>();
foreach (var s in UnityEngine.Object.FindObjectsByType<VrBattlegrounds.Arsenal.ArsenalSlotController>(FindObjectsSortMode.None))
    (s.transform.parent.name.StartsWith("Shelf") ? shelves : walls).Add(s.transform.Find("PegboardSection"));
Vector3 wallCenter = Vector3.zero; foreach (var b in walls) wallCenter += b.position; wallCenter /= walls.Count;
Vector3 shelfCenter = Vector3.zero; foreach (var b in shelves) shelfCenter += b.position; shelfCenter /= shelves.Count;
Vector3 face = -walls[0].forward; face.y = 0f; face.Normalize();   // от стены к игроку

var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
mat.SetColor("_BaseColor", panelColor * panelBrightness);
System.Func<string, Vector3, Vector3, Vector2, GameObject> panel = (name, pos, lookAt, size) =>
{
    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
    UnityEngine.Object.Destroy(q.GetComponent<Collider>());
    q.name = Tag + "Panel" + name;
    q.transform.position = pos;
    q.transform.rotation = Quaternion.LookRotation(lookAt - pos);   // лицо квада (−Z) — от станции; Unlit без отсечения
    q.transform.localScale = new Vector3(size.x, size.y, 1f);
    var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    mat.SetFloat("_Cull", 0f);
    return q;
};
Vector3 wallPanelPos = new Vector3(wallCenter.x, wallPanelHeight, wallCenter.z) + face * wallPanelDistance;
panel("Wall", wallPanelPos, wallPanelPos + face, new Vector2(wallPanelWidth, wallPanelTall));
Vector3 shelfPanelPos = shelfCenter + Vector3.up * shelfPanelUp + face * 0.15f;
panel("Shelf", shelfPanelPos, shelfPanelPos + (Vector3.up * 0.7f + face * 0.3f), new Vector2(wallPanelWidth, shelfPanelTall));

// Ключевой рассеянный свет со стороны игрока (в финале — запечённый, через пробы света).
// Два ключевых прожектора с места игрока (1,8 м): на центр стены и на полку.
Vector3 keyPos = new Vector3(wallPanelPos.x, 1.8f, wallPanelPos.z);
foreach (var target in new[] { wallCenter, shelfCenter })
{
    var key = new GameObject(Tag + "Key").AddComponent<Light>();
    key.transform.position = keyPos;
    key.transform.rotation = Quaternion.LookRotation(target - keyPos);
    key.type = LightType.Spot; key.spotAngle = target == wallCenter ? keyAngle : 90f; key.innerSpotAngle = 0f;
    key.intensity = keyIntensity; key.range = keyRange; key.color = new Color(1f, .92f, .82f);
    key.shadows = LightShadows.None; key.renderMode = LightRenderMode.ForcePixel;
}
// Пробу с панелями берут только оружие и магазины: доски, бортики, карточки и опоры — без неё (иначе бликуют).
foreach (var s in UnityEngine.Object.FindObjectsByType<VrBattlegrounds.Arsenal.ArsenalSlotController>(FindObjectsSortMode.None))
{
    var keep = new System.Collections.Generic.HashSet<Renderer>();
    foreach (var anchor in new[] { "WeaponAnchor", "MagAnchor" })
    {
        var a = s.transform.Find(anchor);
        if (a != null) foreach (var r in a.GetComponentsInChildren<Renderer>()) keep.Add(r);
    }
    foreach (var r in s.GetComponentsInChildren<Renderer>())
        if (!keep.Contains(r)) r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
}

// Проба отражений станции: коробка вокруг слотов и панелей, рендер один раз (как запечённая).
var bounds = new Bounds(wallCenter, Vector3.zero);
foreach (var b in walls) bounds.Encapsulate(b.GetComponent<Renderer>().bounds);
foreach (var b in shelves) bounds.Encapsulate(b.GetComponent<Renderer>().bounds);
bounds.Encapsulate(wallPanelPos);
bounds.Expand(0.4f);
var probeGo = new GameObject(Tag + "Probe");
probeGo.transform.position = new Vector3(bounds.center.x, 1.6f, bounds.center.z);  // на уровне глаз
var probe = probeGo.AddComponent<ReflectionProbe>();
probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
probe.boxProjection = true;
probe.size = bounds.size; probe.center = bounds.center - probeGo.transform.position;
probe.resolution = 128; probe.hdr = true; probe.importance = 5; probe.intensity = probeIntensity;
// Realtime-пробы в настройках качества Quest выключены (RenderProbe не даёт текстуры): кубмапу рендерит камера
// в точке пробы, пока панели включены, — как запекание; затем панели прячутся.
var cube = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf)
    { dimension = UnityEngine.Rendering.TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, name = Tag + "Cube" };
var cubeCam = new GameObject(Tag + "CubeCam").AddComponent<Camera>();
cubeCam.transform.position = probeGo.transform.position;
cubeCam.nearClipPlane = 0.05f; cubeCam.farClipPlane = 60f; cubeCam.allowHDR = true;
cubeCam.RenderToCubemap(cube);
UnityEngine.Object.Destroy(cubeCam.gameObject);
probe.customBakedTexture = cube;
foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
    if (t.name.StartsWith(Tag + "Panel")) t.gameObject.SetActive(false);
return "Панели, свет и проба построены; панели спрятаны — видны только в отражениях.";

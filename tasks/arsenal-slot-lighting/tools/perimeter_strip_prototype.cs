// Прототип «скрытой LED-ленты по периметру слота» для Play Mode (execute_code, задача arsenal-slot-lighting).
// Только в Play: на собранные слоты станции временно вешаются светящаяся рамка (свечение у края доски, к центру гаснет),
// бортик, прячущий ленту, и realtime-лампы по краям, освещающие оружие. При выходе из Play всё исчезает; ассеты и сцена
// не меняются. Повторный запуск убирает прежний прототип и строит заново с параметрами ниже. Цвет — по состоянию слота
// на момент запуска (как у прежней лампы: доступно тёплый, недоступно — погашено, слот пуст — зелёный).
// В финале realtime-ламп не будет (цена на Quest): свет на оружии имитируется подменой проб света у оружия слота.
if (!Application.isPlaying) return "Только в Play Mode";
float glowWidth = 0.08f;      // метры от края доски, на которых свечение гаснет к центру
float glowStrength = 1.0f;    // яркость свечения рамки
float lipDepth = 0.03f, lipWidth = 0.02f;  // бортик, за которым спрятана лента
float lightIntensity = 0.6f, lightRange = 0.45f, lightForward = 0.06f;  // лампы по краям
bool lights = true;
Color available = new Color(1f, .85f, .6f), taken = Color.green;

const string Tag = "[PerimeterProto]";
foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    if (go != null && go.name.StartsWith(Tag)) UnityEngine.Object.Destroy(go.gameObject);

// Текстура: альфа 1 у края доски, плавно к 0 на glowWidth внутрь (как свет из-под карниза).
int W = 128, H = 256;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
for (int y = 0; y < H; y++)
for (int x = 0; x < W; x++)
{
    float u = (x + .5f) / W, v = (y + .5f) / H;
    // Расстояние до края в метрах для доски 0,45 × 1 м.
    float d = Mathf.Min(Mathf.Min(u, 1f - u) * 0.45f, Mathf.Min(v, 1f - v) * 1.0f);
    float t = Mathf.Clamp01(d / glowWidth);
    float a = (1f - t) * (1f - t);
    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
}
tex.Apply();
var shader = Shader.Find("Universal Render Pipeline/Unlit");
var glowMat = new Material(shader);
glowMat.SetTexture("_BaseMap", tex);
glowMat.SetFloat("_Surface", 1f); glowMat.SetFloat("_Blend", 2f);
glowMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
glowMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
glowMat.SetFloat("_ZWrite", 0f);
glowMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
glowMat.SetOverrideTag("RenderType", "Transparent");
glowMat.renderQueue = 3000;
var lipMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
lipMat.SetColor("_BaseColor", new Color(.12f, .12f, .12f));

var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
int slots = 0, lit = 0;
var sb = new System.Text.StringBuilder();
foreach (var slot in UnityEngine.Object.FindObjectsByType<VrBattlegrounds.Arsenal.ArsenalSlotController>(FindObjectsSortMode.None))
{
    var board = slot.transform.Find("PegboardSection");
    if (board == null) continue;
    slots++;
    // Прежняя индикация — погасить на время прототипа.
    var oldGlow = board.Find("SlotGlow"); if (oldGlow != null) oldGlow.gameObject.SetActive(false);
    var leds = slot.transform.Find("LEDs"); if (leds != null) leds.gameObject.SetActive(false);

    // Состояние как в ApplyOfferVisuals.
    bool present = slot.IsItemPresent;
    bool locked = (bool)typeof(VrBattlegrounds.Arsenal.ArsenalSlotController).GetProperty("IsLocked", flags).GetValue(slot);
    var offer = typeof(VrBattlegrounds.Arsenal.ArsenalSlotController).GetField("_offer", flags).GetValue(slot);
    bool grabbable = offer.ToString() == "Free" || offer.ToString() == "Affordable";
    Color? color = locked ? (Color?)null : !present ? taken : grabbable ? available : (Color?)null;

    // Лицевая сторона доски: вперёд у стены, вверх у полки.
    Vector3 face = -board.forward;
    if (Vector3.Dot(face, slot.transform.up - slot.transform.forward) < 0f) face = -face;
    Vector3 size = board.lossyScale;

    var root = new GameObject(Tag + slot.name).transform;
    root.SetPositionAndRotation(board.position, Quaternion.LookRotation(-face, Vector3.Dot(face, Vector3.up) > .7f ? slot.transform.forward * -1f : board.up));
    if (color.HasValue)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
        quad.name = Tag + "Glow";
        quad.transform.SetParent(root, false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.016f);
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = glowMat;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color.Value * glowStrength);
        r.SetPropertyBlock(block);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    // Бортик по периметру — прячет ленту.
    for (int side = 0; side < 4; side++)
    {
        var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.Destroy(lip.GetComponent<Collider>());
        lip.name = Tag + "Lip" + side;
        lip.transform.SetParent(root, false);
        bool vertical = side < 2;
        float sx = vertical ? lipWidth : size.x, sy = vertical ? size.y : lipWidth;
        float px = side == 0 ? -size.x * .5f : side == 1 ? size.x * .5f : 0f;
        float py = side == 2 ? -size.y * .5f : side == 3 ? size.y * .5f : 0f;
        lip.transform.localPosition = new Vector3(px, py, -lipDepth * .5f - 0.013f);
        lip.transform.localScale = new Vector3(sx, sy, lipDepth);
        lip.GetComponent<MeshRenderer>().sharedMaterial = lipMat;
    }
    // Лампы по краям — освещают оружие по периметру.
    if (lights && color.HasValue)
    {
        Vector3[] edge = { new Vector3(-size.x * .45f, 0f, 0f), new Vector3(size.x * .45f, 0f, 0f), new Vector3(0f, size.y * .45f, 0f), new Vector3(0f, -size.y * .45f, 0f) };
        foreach (var e in edge)
        {
            var lgo = new GameObject(Tag + "Light");
            lgo.transform.SetParent(root, false);
            lgo.transform.localPosition = e + new Vector3(0f, 0f, -lightForward);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = color.Value; l.intensity = lightIntensity; l.range = lightRange;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            lit++;
        }
    }
}
return "Слотов " + slots + ", ламп " + lit + " (прототип, только Play).";

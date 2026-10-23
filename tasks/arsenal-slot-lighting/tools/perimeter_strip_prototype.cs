// Прототип «скрытой LED-ленты по периметру слота» для Play Mode (execute_code, задача arsenal-slot-lighting).
// Только в Play: на собранные слоты станции временно вешаются светящаяся рамка (свечение у края доски, к центру гаснет),
// бортик, прячущий ленту, и realtime-лампы по краям, освещающие оружие. При выходе из Play всё исчезает; ассеты и сцена
// не меняются. Повторный запуск убирает прежний прототип и строит заново с параметрами ниже. Цвет — по состоянию слота
// на момент запуска (как у прежней лампы: доступно тёплый, недоступно — погашено, слот пуст — зелёный).
// В финале realtime-ламп не будет (цена на Quest): свет на оружии имитируется подменой проб света у оружия слота.
if (!Application.isPlaying) return "Только в Play Mode";
float glowWidth = 0.015f;     // метры от края доски, на которых свечение гаснет к центру
float glowStrength = 1.2f;    // яркость свечения рамки
bool sidesOnly = true;        // лента только слева и справа (сверху и снизу нет)
float lipDepth = 0.03f, lipWidth = 0.02f;  // бортик, за которым спрятана лента
// Лампы на оружие — 4 прожектора на слот (стена и полка одинаково): углы доски в lampForward от неё, каждая лампа
// смотрит на свою деталь оружия — центр вершин той четверти оружия, что ближе к её углу. Мягкий край конуса.
float lightIntensity = 1.0f, lightRange = 1.0f;
float spotAngle = 50f, spotInner = 0f, lampForward = 0.2f;
// Лампа, чья деталь ближе lampRefDistance, слабее пропорционально квадрату расстояния (светлые детали не выгорают).
float lampRefDistance = 0.35f;
bool lights = true, markers = false;  // markers — розовые точки ламп и их оси (только для просмотра)
// Зазор рядов (просвет между досками ≈4,5 см; корпус Lobby вмещает с запасом). 0 — зазор префаба станции.
float wallGap = 0.095f, shelfGap = 0.075f;
Color available = new Color(1f, .85f, .6f), taken = Color.green;
Color stripAvailable = new Color(1f, .6f, .25f);  // лента «доступно» — насыщенно-тёплая, чтобы не выгорала в белый

const string Tag = "[PerimeterProto]";
foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    if (go != null && go.name.StartsWith(Tag)) UnityEngine.Object.Destroy(go.gameObject);

// Текстура: альфа 1 у края доски, плавно к 0 на glowWidth внутрь (как свет из-под карниза).
int W = 512, H = 16;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
for (int y = 0; y < H; y++)
for (int x = 0; x < W; x++)
{
    float u = (x + .5f) / W, v = (y + .5f) / H;
    // Расстояние до края в метрах для доски 0,45 × 1 м.
    float d = Mathf.Min(u, 1f - u) * 0.45f;
    if (!sidesOnly) d = Mathf.Min(d, Mathf.Min(v, 1f - v) * 1.0f);
    d = Mathf.Max(0f, d - lipWidth * .5f);  // свечение начинается у внутренней кромки бортика
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
var markerMat = new Material(shader);
markerMat.SetColor("_BaseColor", new Color(1f, .1f, .9f));

var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
// Раздвинуть ряды: зазор ряда и повторная раскладка его слотов в прежнем порядке.
var gapField = typeof(VrBattlegrounds.Arsenal.ArsenalSlotRow).GetField("_gap", flags);
foreach (var row in UnityEngine.Object.FindObjectsByType<VrBattlegrounds.Arsenal.ArsenalSlotRow>(FindObjectsSortMode.None))
{
    float gap = row.name.StartsWith("Shelf") ? shelfGap : wallGap;
    if (gap <= 0f) continue;
    var rowSlots = new System.Collections.Generic.List<VrBattlegrounds.Arsenal.ArsenalSlotController>();
    foreach (Transform c in row.transform) { var s = c.GetComponent<VrBattlegrounds.Arsenal.ArsenalSlotController>(); if (s != null) rowSlots.Add(s); }
    rowSlots.Sort((a, b) => a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));
    gapField.SetValue(row, gap);
    row.Arrange(rowSlots);
}
// Общую LED-планку станции погасить: у каждого слота своё освещение.
var ledBar = GameObject.Find("SlotLedBar"); if (ledBar != null) ledBar.SetActive(false);
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
        block.SetColor("_BaseColor", (color.Value == available ? stripAvailable : color.Value) * glowStrength);
        r.SetPropertyBlock(block);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    // Бортик по периметру — прячет ленту.
    for (int side = 0; side < (sidesOnly ? 2 : 4); side++)
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
    // Лампы на оружие: 4 прожектора по углам (порядок: низ-лево, низ-право, верх-лево, верх-право в кадре доски).
    if (lights && color.HasValue)
    {
        var lampPos = new Vector3[4]; var lampAim = new Vector3[4];
        var pts = new System.Collections.Generic.List<Vector3>();
        foreach (var mr in slot.transform.Find("WeaponAnchor").GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>(); if (!mr.enabled || mf == null || mf.sharedMesh == null) continue;
            var M = mr.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices;
            for (int i = 0; i < vs.Length; i += 2) pts.Add(root.InverseTransformPoint(M.MultiplyPoint3x4(vs[i])));
        }
        Vector3 mn = Vector3.one * 9f, mx = -Vector3.one * 9f;
        foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        Vector3 mid = (mn + mx) * .5f;
        for (int i = 0; i < 4; i++)
        {
            float cx = i % 2 == 0 ? -1f : 1f, cy = i < 2 ? -1f : 1f;
            lampPos[i] = new Vector3(cx * size.x * .5f, cy * size.y * .5f, -lampForward);
            Vector3 sum = Vector3.zero; int c = 0;
            foreach (var p in pts) if ((p.x - mid.x) * cx >= 0f && (p.y - mid.y) * cy >= 0f) { sum += p; c++; }
            lampAim[i] = c > 0 ? sum / c : mid;
        }
        for (int i = 0; i < 4; i++)
        {
            var lgo = new GameObject(Tag + "Light");
            lgo.transform.SetParent(root, false);
            lgo.transform.localPosition = lampPos[i];
            lgo.transform.rotation = Quaternion.LookRotation(root.TransformDirection((lampAim[i] - lampPos[i]).normalized), root.up);
            // Ближайшая точка оружия по оси лампы (в пределах ~10°).
            Vector3 axis = (lampAim[i] - lampPos[i]).normalized; float near = 9f;
            foreach (var p in pts)
            {
                var d = p - lampPos[i]; float dist = d.magnitude;
                if (dist > 0.01f && Vector3.Dot(d / dist, axis) > 0.985f) near = Mathf.Min(near, dist);
            }
            float falloff = near < lampRefDistance ? (near / lampRefDistance) * (near / lampRefDistance) : 1f;
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot; l.color = color.Value; l.intensity = lightIntensity * falloff; l.range = lightRange;
            l.spotAngle = spotAngle; l.innerSpotAngle = spotInner;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            if (markers)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                UnityEngine.Object.Destroy(dot.GetComponent<Collider>());
                dot.name = Tag + "Marker"; dot.transform.SetParent(lgo.transform, false);
                dot.transform.localScale = Vector3.one * 0.015f; dot.GetComponent<Renderer>().sharedMaterial = markerMat;
                var ray = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.Destroy(ray.GetComponent<Collider>());
                ray.name = Tag + "MarkerRay"; ray.transform.SetParent(lgo.transform, false);
                ray.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ray.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                ray.transform.localScale = new Vector3(0.003f, 0.05f, 0.003f);
                ray.GetComponent<Renderer>().sharedMaterial = markerMat;
            }
            lit++;
        }
    }
}
return "Слотов " + slots + ", ламп " + lit + " (прототип, только Play).";

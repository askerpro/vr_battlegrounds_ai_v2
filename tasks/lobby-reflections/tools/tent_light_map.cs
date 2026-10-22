// Карта рассеянной освещённости шатра Lobby по light probes (lobby-reflections, 2026-10-22). Только чтение сцены.
// Выход: <reports>/tent-light-map.png (20 px/м, шкала 0..1,5) и tent-light-map.txt. Маркеры: белые — лампы,
// голубые — стены арсенала, пурпурные — столы/верстаки, серые — light probes на высоте ~1,3 м.
var sb = new System.Text.StringBuilder();
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
string outDir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "tasks/lobby-reflections/reports/audit-20261022");
System.IO.Directory.CreateDirectory(outDir);
System.Func<Transform, string> path = t => { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; };

// Аудитор: только новое правило.
var report = VrBattlegrounds.Editor.Performance.AssetAudit.AssetAuditRunner.RunScene("Assets/Scenes/Lobby.unity");
sb.AppendLine("AUDIT findings total=" + report.Findings.Count);
foreach (var f in report.Findings)
    if (f.RuleId == "lighting.dark-reflection-zone") sb.AppendLine("  " + f.Severity + " " + f.Message + " :: " + string.Join("; ", f.Items));
scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);

// Габариты пола арены (шатёр).
Bounds floor = new Bounds();
foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
{
    if (mb.GetType().Name != "PhysicalArenaDefinition") continue;
    var args = new object[] { null };
    bool ok = (bool)mb.GetType().GetMethod("TryFloorBounds").Invoke(mb, args);
    if (ok) floor = (Bounds)args[0];
}
sb.AppendLine("floor=" + floor.center + " size=" + floor.size);
float margin = 1f, ppm = 20f, y = floor.max.y + 1.2f;
float x0 = floor.min.x - margin, z0 = floor.min.z - margin, x1 = floor.max.x + margin, z1 = floor.max.z + margin;
int w = Mathf.CeilToInt((x1 - x0) * ppm), h = Mathf.CeilToInt((z1 - z0) * ppm);
var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
var dirs = new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
var cols = new Color[6];
var values = new System.Collections.Generic.List<float>();
System.Func<Vector3, float> lum = p => {
    UnityEngine.Rendering.SphericalHarmonicsL2 sh; LightProbes.GetInterpolatedProbe(p, null, out sh); sh.Evaluate(dirs, cols);
    float s = 0; foreach (var c in cols) s += Mathf.Max(0, c.grayscale); return s / 6f; };
// Шкала: 0 — чёрный, 0,25 — синий, 0,5 — зелёный, 0,75 — жёлтый, 1,0 — оранжевый, ≥1,5 — красный.
System.Func<float, Color> ramp = v => {
    var stops = new[] { 0f, .25f, .5f, .75f, 1f, 1.5f };
    var pal = new[] { Color.black, new Color(.1f, .2f, .9f), new Color(.1f, .8f, .2f), new Color(1f, .9f, .1f), new Color(1f, .5f, .05f), new Color(.9f, .05f, .05f) };
    if (v <= 0) return pal[0]; for (int i = 1; i < stops.Length; i++) if (v <= stops[i]) return Color.Lerp(pal[i - 1], pal[i], (v - stops[i - 1]) / (stops[i] - stops[i - 1]));
    return pal[pal.Length - 1]; };
for (int j = 0; j < h; j += 2)
for (int i = 0; i < w; i += 2)
{
    float v = lum(new Vector3(x0 + (i + 1) / ppm, y, z0 + (j + 1) / ppm));
    if (floor.Contains(new Vector3(x0 + i / ppm, floor.center.y, z0 + j / ppm))) values.Add(v);
    var c = ramp(v); tex.SetPixel(i, j, c); tex.SetPixel(i + 1, j, c); tex.SetPixel(i, j + 1, c); tex.SetPixel(i + 1, j + 1, c);
}
System.Action<Vector3, Color, int> dot = (p, c, r) => {
    int ci = Mathf.RoundToInt((p.x - x0) * ppm), cj = Mathf.RoundToInt((p.z - z0) * ppm);
    for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++) if (di * di + dj * dj <= r * r && ci + di >= 0 && ci + di < w && cj + dj >= 0 && cj + dj < h) tex.SetPixel(ci + di, cj + dj, c); };
var lp = LightmapSettings.lightProbes;
if (lp != null) foreach (var p in lp.positions) if (Mathf.Abs(p.y - (floor.max.y + 1.3f)) < .15f) dot(p, Color.gray, 1);
sb.AppendLine("--- lights in/near tent");
foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
{
    var p = l.transform.position;
    if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
    dot(p, Color.white, 4);
    sb.AppendLine(path(l.transform) + " " + l.type + " " + l.lightmapBakeType + " i=" + l.intensity + " r=" + l.range + " @" + p.ToString("F1"));
}
sb.AppendLine("--- arsenal / tables");
foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    if (mb.GetType().Name == "ArsenalStationAnchor") { var p = mb.transform.position; dot(p, Color.cyan, 5); sb.AppendLine("arsenal " + path(mb.transform) + " @" + p.ToString("F1") + " lum@1.2=" + lum(p + Vector3.up * 1.2f).ToString("0.00")); }
foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
{
    string n = t.name.ToLowerInvariant();
    if (!(n.Contains("table") || n.Contains("desk") || n.Contains("workbench") || n.Contains("bench") || n.Contains("crate"))) continue;
    var p = t.position; if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
    if (t.parent != null && (t.parent.name.ToLowerInvariant().Contains("table") || t.parent.name.ToLowerInvariant().Contains("desk"))) continue;
    dot(p, Color.magenta, 3); sb.AppendLine("prop " + path(t) + " @" + p.ToString("F1") + " lum@1.0=" + lum(new Vector3(p.x, floor.max.y + 1f, p.z)).ToString("0.00"));
}
tex.Apply();
System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "tent-light-map.png"), tex.EncodeToPNG());
values.Sort();
if (values.Count > 0) sb.AppendLine("floor lum@1.2: min=" + values[0].ToString("0.00") + " p10=" + values[values.Count / 10].ToString("0.00") + " median=" + values[values.Count / 2].ToString("0.00") + " p90=" + values[values.Count * 9 / 10].ToString("0.00") + " max=" + values[values.Count - 1].ToString("0.00") + " image=" + w + "x" + h + " x0=" + x0 + " z0=" + z0);
System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "tent-light-map.txt"), sb.ToString());
return sb.ToString();

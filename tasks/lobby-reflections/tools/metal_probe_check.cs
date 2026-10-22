// Проверка чёрного металла (lobby-reflections, 2026-10-22). Одна сцена Lobby с загруженными лайтмапами, без
// дополнительной сцены: три металлические сферы (гладкость 1,0 / 0,6 / 0,2) и матовая у стены арсенала.
// Вариант A — запечённая проба шатра как есть. Вариант B — та же проба в режиме Custom с тёплой процедурной кубмапой.
// Сцена переоткрывается без сохранения. Выход: <reports>/metal-check/*.png и строки с проверками.
var sb = new System.Text.StringBuilder();
var lobby = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
string outDir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "tasks/lobby-reflections/reports/metal-check");
System.IO.Directory.CreateDirectory(outDir);
var probe = GameObject.Find("Environment/NightLighting/TentReflectionProbe").GetComponent<ReflectionProbe>();
sb.AppendLine("lightmaps=" + LightmapSettings.lightmaps.Length + " probe=" + probe.mode + " tex=" + (probe.bakedTexture != null ? probe.bakedTexture.name + " mips=" + probe.bakedTexture.mipmapCount : "null"));
var lit = Shader.Find("Universal Render Pipeline/Lit");
System.Func<float, float, Vector3, GameObject> sphere = (metal, smooth, pos) => {
    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "TEMP_sphere"; go.transform.localScale = Vector3.one * .14f; go.transform.position = pos;
    var m = new Material(lit); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
    go.GetComponent<Renderer>().sharedMaterial = m; return go; };
Vector3 p = new Vector3(-6.2f, 1.2f, -0.4f);
var objs = new System.Collections.Generic.List<GameObject> {
    sphere(0f, .5f, p + new Vector3(0, 0, -.45f)), sphere(1f, 1f, p + new Vector3(0, 0, -.15f)),
    sphere(1f, .6f, p + new Vector3(0, 0, .15f)), sphere(1f, .2f, p + new Vector3(0, 0, .45f)) };
var camGo = new GameObject("TEMP_cam"); var camera = camGo.AddComponent<Camera>(); camera.nearClipPlane = .05f; camera.fieldOfView = 35;
camGo.transform.position = p + new Vector3(1.0f, .1f, 0f); camGo.transform.LookAt(p);
System.Action<string> shoot = file => {
    var rt = new RenderTexture(768, 384, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4; camera.targetTexture = rt; camera.Render(); camera.Render();
    RenderTexture.active = rt; var tex = new Texture2D(768, 384, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 768, 384), 0, 0); tex.Apply(); RenderTexture.active = null;
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, file), tex.EncodeToPNG());
    // Яркость центров сфер на кадре (слева направо: матовая, гладкость 1,0, 0,6, 0,2).
    var line = file + ":";
    foreach (var o in objs) { var sp = camera.WorldToScreenPoint(o.transform.position); line += " " + tex.GetPixel((int)sp.x, (int)sp.y).grayscale.ToString("0.00"); }
    sb.AppendLine(line);
    camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); };
shoot("A_baked_probe.png");
int size = 32; var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
Color top = new Color(.60f, .45f, .30f), horizon = new Color(.30f, .23f, .16f), bottom = new Color(.10f, .08f, .06f);
foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
{
    var px = new Color[size * size];
    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
    {
        float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1; Vector3 d;
        switch (face)
        {
            case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
            case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
            case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
            case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
            default: d = new Vector3(-u, -v, -1); break;
        }
        float h = d.normalized.y;
        px[y * size + x] = h >= 0 ? Color.Lerp(horizon, top, Mathf.Pow(h, .7f)) : Color.Lerp(horizon, bottom, Mathf.Pow(-h, .5f));
    }
    cube.SetPixels(px, face);
}
cube.Apply(true);
probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom; probe.customBakedTexture = cube;
shoot("B_custom_warm.png");
// Вариант C — запечённая кубмапа пробы, но через Custom (тот же ассет): отличает режим пробы от содержимого текстуры.
probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Scenes/Lobby/ReflectionProbe-0.exr");
shoot("C_custom_baked_asset.png");
foreach (var o in objs) UnityEngine.Object.DestroyImmediate(o);
UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(cube);
lobby = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
sb.AppendLine("reopened dirty=" + lobby.isDirty);
System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "metal-check.txt"), sb.ToString());
return sb.ToString();

// Тело для MCP execute_code (Edit Mode, своя аренда worker; ничего не сохраняет). Шаг 2 диагностики после
// OpenScene(Lobby) и паузы: загруженные persistent-материалы с EditorUtility.IsDirty и расхождения значений
// в памяти с текстом .mat на диске (floats, colors, keywords, shader, текстуры, renderQueue).
var sb = new System.Text.StringBuilder();
var inv = System.Globalization.CultureInfo.InvariantCulture;
int dirty = 0;
foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
{
    if (!UnityEditor.EditorUtility.IsPersistent(m) || !UnityEditor.EditorUtility.IsDirty(m)) continue;
    dirty++;
    string path = UnityEditor.AssetDatabase.GetAssetPath(m);
    sb.Append("DIRTY ").Append(path).Append('\n');
    if (!path.EndsWith(".mat")) continue;
    string[] lines = System.IO.File.ReadAllLines(path);
    string section = "";
    var fileKeywords = new System.Collections.Generic.List<string>();
    bool inKeywords = false;
    foreach (string raw in lines)
    {
        string l = raw.Trim();
        if (l.StartsWith("m_ValidKeywords:")) { inKeywords = true; continue; }
        if (inKeywords) { if (l.StartsWith("- ")) { fileKeywords.Add(l.Substring(2)); continue; } inKeywords = false; }
        if (l == "m_Floats:" || l == "m_Colors:" || l == "m_TexEnvs:" || l == "m_Ints:") { section = l; continue; }
        if (l.StartsWith("m_CustomRenderQueue:"))
        {
            int q = int.Parse(l.Substring(20).Trim(), inv);
            if (q != m.renderQueue && q != -1) sb.Append("  renderQueue file=").Append(q).Append(" mem=").Append(m.renderQueue).Append('\n');
        }
        if (!l.StartsWith("- _")) continue;
        int colon = l.IndexOf(':');
        string prop = l.Substring(2, colon - 2), val = l.Substring(colon + 1).Trim();
        if (section == "m_Floats:" && m.HasProperty(prop))
        {
            float f = float.Parse(val, inv), mem = m.GetFloat(prop);
            if (Mathf.Abs(f - mem) > 1e-5f) sb.Append("  float ").Append(prop).Append(" file=").Append(val).Append(" mem=").Append(mem.ToString("R", inv)).Append('\n');
        }
        else if (section == "m_Colors:" && m.HasProperty(prop))
        {
            Color mem = m.GetColor(prop);
            string memS = string.Format(inv, "{{r: {0}, g: {1}, b: {2}, a: {3}}}", mem.r, mem.g, mem.b, mem.a);
            var parts = val.Trim('{', '}').Split(',');
            float r = float.Parse(parts[0].Split(':')[1], inv), g = float.Parse(parts[1].Split(':')[1], inv),
                  b = float.Parse(parts[2].Split(':')[1], inv), a = float.Parse(parts[3].Split(':')[1], inv);
            if (Mathf.Abs(r - mem.r) + Mathf.Abs(g - mem.g) + Mathf.Abs(b - mem.b) + Mathf.Abs(a - mem.a) > 1e-4f)
                sb.Append("  color ").Append(prop).Append(" file=").Append(val).Append(" mem=").Append(memS).Append('\n');
        }
    }
    var memKeywords = new System.Collections.Generic.List<string>(m.shaderKeywords);
    foreach (string k in fileKeywords) if (!memKeywords.Contains(k)) sb.Append("  keyword only in file: ").Append(k).Append('\n');
    foreach (string k in memKeywords) if (!fileKeywords.Contains(k)) sb.Append("  keyword only in mem: ").Append(k).Append('\n');
    sb.Append("  shader=").Append(m.shader != null ? m.shader.name : "null").Append(" instancing=").Append(m.enableInstancing)
      .Append(" gi=").Append(m.globalIlluminationFlags).Append('\n');
}
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "scene=" + scene.path + " sceneDirty=" + scene.isDirty + " dirtyMaterials=" + dirty + "\n" + sb;

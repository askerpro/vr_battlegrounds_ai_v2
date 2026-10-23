// Тело для MCP execute_code (Edit Mode, своя аренда worker; ничего не пишет). Для каждого материала
// Assets/Art/Weapons/Kinemation/Materials: множитель, который поставил бы KinemationMaterials.Convert
// (AlbedoBoost(MeanLinear(_BC пака)) × _TintColor), против текущих _BaseColor и _Color.
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var type = typeof(VrBattlegrounds.Editor.Gameplay.KinemationMaterials);
var packMaterial = type.GetMethod("PackMaterial", flags);
var meanLinear = type.GetMethod("MeanLinear", flags);
var sb = new System.Text.StringBuilder();
int mismatch = 0, total = 0;
foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { VrBattlegrounds.Editor.Gameplay.KinemationMaterials.MaterialsFolder }))
{
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
    var src = (Material)packMaterial.Invoke(null, new object[] { mat.name });
    if (src == null) { sb.Append(mat.name).Append(": нет в паке\n"); continue; }
    total++;
    float mean = (float)meanLinear.Invoke(null, new object[] { src.GetTexture("_BC") });
    float boost = VrBattlegrounds.Editor.Gameplay.KinemationMaterials.AlbedoBoost(mean);
    Color tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.white;
    float expected = tint.r * boost;
    float baseR = mat.GetColor("_BaseColor").r, colorR = mat.GetColor("_Color").r;
    bool ok = Mathf.Abs(expected - baseR) < 0.005f && Mathf.Abs(baseR - colorR) < 0.0001f;
    if (!ok) mismatch++;
    sb.Append(ok ? "ok  " : "BAD ").Append(mat.name).Append(" mean=").Append(mean.ToString("0.0000"))
      .Append(" boost=").Append(boost.ToString("0.00")).Append(" tint=").Append(tint.r.ToString("0.00"))
      .Append(" base=").Append(baseR.ToString("0.00")).Append(" color=").Append(colorR.ToString("0.00")).Append('\n');
}
return "materials=" + total + " mismatch=" + mismatch + "\n" + sb;

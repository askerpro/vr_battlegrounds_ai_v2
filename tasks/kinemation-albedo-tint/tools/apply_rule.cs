// Тело для MCP execute_code (Edit Mode, своя аренда worker с --output Assets/Art/Weapons/Kinemation/Materials).
// Приводит _BaseColor и _Color материалов Kinemation/Materials к правилу KinemationMaterials.Convert:
// _TintColor пака × AlbedoBoost(MeanLinear(_BC пака)) — решение пользователя 2026-10-23 «привести материалы к правилу».
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var type = typeof(VrBattlegrounds.Editor.Gameplay.KinemationMaterials);
var packMaterial = type.GetMethod("PackMaterial", flags);
var meanLinear = type.GetMethod("MeanLinear", flags);
var sb = new System.Text.StringBuilder();
int changed = 0;
foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { VrBattlegrounds.Editor.Gameplay.KinemationMaterials.MaterialsFolder }))
{
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
    var src = (Material)packMaterial.Invoke(null, new object[] { mat.name });
    if (src == null) continue;
    float mean = (float)meanLinear.Invoke(null, new object[] { src.GetTexture("_BC") });
    float boost = VrBattlegrounds.Editor.Gameplay.KinemationMaterials.AlbedoBoost(mean);
    Color tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.white;
    var color = new Color(tint.r * boost, tint.g * boost, tint.b * boost, 1f);
    Color old = mat.GetColor("_BaseColor");
    if (old == color && mat.GetColor("_Color") == color) continue;
    mat.SetColor("_BaseColor", color);
    mat.SetColor("_Color", color);
    UnityEditor.EditorUtility.SetDirty(mat);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(mat);
    changed++;
    sb.Append(mat.name).Append(" mean=").Append(mean.ToString("0.0000")).Append(' ')
      .Append(old.r.ToString("0.00")).Append(" -> ").Append(color.r.ToString("0.00")).Append('\n');
}
return "changed=" + changed + "\n" + sb;

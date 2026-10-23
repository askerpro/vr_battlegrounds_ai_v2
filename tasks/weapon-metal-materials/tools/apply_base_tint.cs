// Тело для MCP execute_code (Edit Mode, worker в своей аренде с --output Assets/Art/Weapons/Kinemation/Materials).
// Поднимает _BaseColor материалов M_* до средней линейной яркости альбедо ~0,055 (множитель 1..1,6), сохраняет.
// Средние — по картам _BaseMap (tasks/weapon-metal-materials/Readme.md); значения приняты пользователем 2026-10-23
// (0,10/×3 и 0,07/×2 — «слишком светло»). Патроны R08 (альбедо 0,55) не трогаются.
const float TARGET = 0.055f, MAX = 1.6f;
var avg = new System.Collections.Generic.Dictionary<string, float> {
 {"M_AK105_Body",0.042f},{"M_AK105_Rail",0.039f},{"M_Herrington_11-87_Police",0.052f},{"M_Mk14EBR_01",0.108f},
 {"M_Mk14EBR_02",0.109f},{"M_Mk14EBR_03",0.154f},{"M_MKR9_01",0.157f},{"M_MKR9_02",0.092f},{"M_MKR9_03",0.132f},
 {"M_MKR9_Mag",0.051f},{"M_R08_Body",0.047f},{"M_SRM-12_Body",0.035f},{"M_SRM-12_Mag",0.04f},
 {"M_TR15_Attachments",0.024f},{"M_TR15_Body",0.017f},{"M_TR15_Ironsights",0.023f},{"M_TR15_Mag",0.025f},
 {"M_WK-11_Viper_Body",0.041f},{"M_WK-11_Viper_Magazine",0.036f} };
var sb = new System.Text.StringBuilder();
foreach (var kv in avg)
{
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Weapons/Kinemation/Materials/" + kv.Key + ".mat");
    if (m == null) { sb.Append("нет ").Append(kv.Key).Append("; "); continue; }
    float f = Mathf.Round(Mathf.Clamp(TARGET / kv.Value, 1f, MAX) * 100f) / 100f;
    if (f <= 1f) continue;
    // _Color — легаси-зеркало _BaseColor: при расхождении его синхронизирует загрузка сцены, материал становится
    // dirty (сбой публикации базы 1113), поэтому оба свойства пишутся одинаково.
    m.SetColor("_BaseColor", new Color(f, f, f, 1f));
    m.SetColor("_Color", new Color(f, f, f, 1f));
    UnityEditor.EditorUtility.SetDirty(m);
    sb.Append(kv.Key).Append(" x").Append(f.ToString("0.00")).Append("; ");
}
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

// Разовая настройка подложки слота арсенала (execute_code на worker, задача arsenal-slot-lighting).
// Импорт текстуры пятна, материал Unlit аддитивный, в FireArmSlotPrefab: убрать realtime-лампу LEDs,
// добавить квад SlotGlow на доске (PegboardSection) с ArsenalSlotGlow и ссылкой _glow у контроллера.
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Arsenal;

const string TexPath = "Assets/Materials/Arsenal/ArsenalSlotGlow.png";
const string MatPath = "Assets/Materials/Arsenal/ArsenalSlotGlow.mat";
const string PrefabPath = "Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab";
var log = new System.Text.StringBuilder();

var importer = (TextureImporter)AssetImporter.GetAtPath(TexPath);
importer.textureType = TextureImporterType.Default;
importer.alphaIsTransparency = true;
importer.wrapMode = TextureWrapMode.Clamp;
importer.mipmapEnabled = false;
importer.SaveAndReimport();
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);

var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
if (mat == null)
{
    mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
    AssetDatabase.CreateAsset(mat, MatPath);
}
mat.SetTexture("_BaseMap", tex);
mat.SetColor("_BaseColor", new Color(1f, .85f, .6f, 1f));
mat.SetFloat("_Surface", 1f);   // Transparent
mat.SetFloat("_Blend", 2f);     // Additive
mat.SetFloat("_Cull", 2f);
UnityEditor.Rendering.Universal.ShaderGUI.BaseShaderGUI.SetMaterialKeywords(mat);
EditorUtility.SetDirty(mat);
AssetDatabase.SaveAssetIfDirty(mat);
log.AppendLine("material queue=" + mat.renderQueue + " src=" + mat.GetFloat("_SrcBlend") + " dst=" + mat.GetFloat("_DstBlend") + " zw=" + mat.GetFloat("_ZWrite"));

var root = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var leds = root.transform.Find("LEDs");
    if (leds != null) { Object.DestroyImmediate(leds.gameObject); log.AppendLine("LEDs removed"); }
    var board = root.transform.Find("PegboardSection");
    var old = board.Find("SlotGlow");
    if (old != null) Object.DestroyImmediate(old.gameObject);
    var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
    Object.DestroyImmediate(glow.GetComponent<Collider>());
    glow.name = "SlotGlow";
    glow.transform.SetParent(board, false);
    glow.transform.localPosition = new Vector3(0f, 0f, -0.6f);
    glow.transform.localRotation = Quaternion.identity;
    glow.transform.localScale = new Vector3(0.95f, 0.9f, 1f);
    var r = glow.GetComponent<MeshRenderer>();
    r.sharedMaterial = mat;
    r.shadowCastingMode = ShadowCastingMode.Off;
    r.receiveShadows = false;
    r.lightProbeUsage = LightProbeUsage.Off;
    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    var component = glow.AddComponent<ArsenalSlotGlow>();
    var controller = root.GetComponent<ArsenalSlotController>();
    var so = new SerializedObject(controller);
    so.FindProperty("_glow").objectReferenceValue = component;
    so.ApplyModifiedPropertiesWithoutUndo();
    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    log.AppendLine("SlotGlow added, controller._glow set");
}
finally { PrefabUtility.UnloadPrefabContents(root); }
return log.ToString();

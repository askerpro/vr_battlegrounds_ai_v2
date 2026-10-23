// Этап 1 финала (execute_code в редакторе, задача arsenal-slot-lighting, решения пользователя 2026-10-23):
// лента по бокам слота, бортики, точки ламп в префабах слотов и зазор рядов станции Lobby.
// - SlotGlow — квад во всю доску с текстурой боковых полос (glow_texture.py), цвет «доступно» янтарный × 1,2;
// - StripLip_L/R — бортики 2 см × 3 см по боковым краям доски (дети PegboardSection: оси доски, масштаб доски учтён);
// - LampPoints/Lamp_0..3 — углы доски в 20 см от неё по нормали лица; направление считает инструмент запекания;
// - LobbyDemoArsenalStation: зазор ряда стены 0,095, полки 0,075 (просвет между досками ≈4,5 см).
// Повторный запуск пересоздаёт бортики и точки ламп. Тело для execute_code: UnityEngine/UnityEditor подключает обёртка.

const string TexPath = "Assets/Materials/Arsenal/ArsenalSlotGlow.png";
const string LipMatPath = "Assets/Materials/Arsenal/IndustrialSupportMetal.mat";
const string WallPath = "Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab";
const string ShelfPath = "Assets/Prefabs/Arsenal/Slots/ShelfSlotPrefab Variant.prefab";
const string StationPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
const float LipWidth = 0.02f, LipDepth = 0.03f, LampForward = 0.2f, WallGap = 0.095f, ShelfGap = 0.075f;
Color available = new Color(1f, .6f, .25f);
const float GlowStrength = 1.2f;
var log = new System.Text.StringBuilder();

var importer = (TextureImporter)AssetImporter.GetAtPath(TexPath);
importer.textureType = TextureImporterType.Default;
importer.alphaIsTransparency = true;
importer.wrapMode = TextureWrapMode.Clamp;
importer.mipmapEnabled = false;
importer.SaveAndReimport();
var lipMat = AssetDatabase.LoadAssetAtPath<Material>(LipMatPath);

System.Action<string> setup = path =>
{
    var root = PrefabUtility.LoadPrefabContents(path);
    try
    {
        var board = root.transform.Find("PegboardSection");
        // Лицо доски — к игроку: вперёд (−Z слота) у стены, вверх у полки; знак лица в осях доски.
        Vector3 face = board.localRotation * Vector3.back;
        float side = Vector3.Dot(face, Vector3.up + Vector3.back) < 0f ? 1f : -1f;  // −1: лицо −Z доски, +1: +Z
        face = board.localRotation * new Vector3(0f, 0f, side);
        Vector3 size = board.localScale;

        var glow = board.Find("SlotGlow");
        glow.localScale = Vector3.one;
        var glowSo = new SerializedObject(glow.GetComponent<VrBattlegrounds.Arsenal.ArsenalSlotGlow>());
        glowSo.FindProperty("_strength").floatValue = GlowStrength;
        glowSo.ApplyModifiedPropertiesWithoutUndo();
        var ctlSo = new SerializedObject(root.GetComponent<VrBattlegrounds.Arsenal.ArsenalSlotController>());
        ctlSo.FindProperty("_availableColor").colorValue = available;
        ctlSo.ApplyModifiedPropertiesWithoutUndo();

        foreach (string name in new[] { "StripLip_L", "StripLip_R" })
        {
            var lip = board.Find(name);
            if (lip == null)
            {
                lip = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                UnityEngine.Object.DestroyImmediate(lip.GetComponent<Collider>());
                lip.name = name;
                lip.SetParent(board, false);
            }
            float x = name.EndsWith("_L") ? -0.5f : 0.5f;
            lip.localPosition = new Vector3(x, 0f, side * (0.5f + LipDepth / size.z * .5f));
            lip.localRotation = Quaternion.identity;
            lip.localScale = new Vector3(LipWidth / size.x, 1f, LipDepth / size.z);
            var r = lip.GetComponent<MeshRenderer>();
            r.sharedMaterial = lipMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        var points = root.transform.Find("LampPoints");
        if (points == null)
        {
            points = new GameObject("LampPoints").transform;
            points.SetParent(root.transform, false);
        }
        for (int i = 0; i < 4; i++)
        {
            var lamp = points.Find("Lamp_" + i);
            if (lamp == null) { lamp = new GameObject("Lamp_" + i).transform; lamp.SetParent(points, false); }
            float cx = i % 2 == 0 ? -1f : 1f, cy = i < 2 ? -1f : 1f;
            lamp.localPosition = board.localPosition + board.localRotation * new Vector3(cx * size.x * .5f, cy * size.y * .5f, 0f)
                                 + face * LampForward;
            lamp.localRotation = Quaternion.identity;
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        log.AppendLine(path + ": side=" + side + " lamp0=" + points.Find("Lamp_0").localPosition.ToString("F3"));
    }
    finally { PrefabUtility.UnloadPrefabContents(root); }
};
setup(WallPath);
setup(ShelfPath);

var station = PrefabUtility.LoadPrefabContents(StationPath);
try
{
    foreach (var row in station.GetComponentsInChildren<VrBattlegrounds.Arsenal.ArsenalSlotRow>(true))
    {
        var so = new SerializedObject(row);
        bool shelf = row.Zone == VrBattlegrounds.Arsenal.ArsenalPresentationZone.Shelf;
        so.FindProperty("_gap").floatValue = shelf ? ShelfGap : WallGap;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine(row.name + " zone=" + row.Zone + " gap=" + (shelf ? ShelfGap : WallGap));
    }
    PrefabUtility.SaveAsPrefabAsset(station, StationPath);
}
finally { PrefabUtility.UnloadPrefabContents(station); }
return log.ToString();

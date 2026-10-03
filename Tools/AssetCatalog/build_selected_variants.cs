// Тело execute_code (Unity MCP, C# 6), запускать под unity-lock.
// Сохраняет только декоративные копии; LD_Alphabet не изменяет.
var sources = new [] {
    "Assets/env_packs/RPG_FPS_game_assets_industrial/Other_props/Generators/Generator_v1/Generator_v1.prefab",
    "Assets/env_packs/RPG_FPS_game_assets_industrial/Boxes/Wooden_box_v1/Wooden_box_v1_LD1.prefab"
};
var names = new [] {"Generator_Mid_Hard", "WoodenBox_Low_Soft"};
var report = new System.Text.StringBuilder();
for (int index = 0; index < sources.Length; index++)
{
    var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(sources[index]);
    if (source == null) throw new System.Exception("Source missing: " + sources[index]);
    var root = new GameObject(names[index]);
    try
    {
        var piece = UnityEngine.Object.Instantiate(source, root.transform);
        piece.name = source.name;
        piece.transform.rotation = Quaternion.Euler(index == 1 ? 90 : 0, 0, 0) * piece.transform.rotation;
        var bounds = new Bounds();
        bool found = false;
        foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var point = filter.transform.TransformPoint(vertex);
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (!found) throw new System.Exception("No visible mesh: " + sources[index]);
        piece.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        root.transform.localScale = index == 0
            ? new Vector3(2f / bounds.size.x, 1.6f / bounds.size.y, .15f / bounds.size.z)
            : Vector3.one * (1.2f / bounds.size.y);
        var surface = root.AddComponent<VrBattlegrounds.Maps.CoverSurface>();
        surface.Class = index == 0 ? VrBattlegrounds.Maps.CoverClass.Hard : VrBattlegrounds.Maps.CoverClass.Soft;
        if (index == 1) surface.PenetrationModifier = 3f;
        string path = "Assets/Prefabs/LevelDesign/Decorated/" + names[index] + ".prefab";
        if (UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path) == null)
            throw new System.Exception("Save failed: " + path);
        report.AppendLine(names[index] + ": " + Vector3.Scale(bounds.size, root.transform.localScale));
    }
    finally { UnityEngine.Object.DestroyImmediate(root); }
}
UnityEditor.AssetDatabase.SaveAssets();
return report.ToString();

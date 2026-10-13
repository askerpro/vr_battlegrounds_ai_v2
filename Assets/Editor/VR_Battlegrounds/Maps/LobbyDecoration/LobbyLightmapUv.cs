using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// UV2 для лайтмап статичного декора Lobby перед ночным запеканием. Источник UV2 — только ассеты задачи
    /// в Assets/Art/Lobby: модели получают «Generate Lightmap UVs» в импортёре, сгенерированные .asset-меши —
    /// <see cref="Unwrapping.GenerateSecondaryUVSet(Mesh)" />. Общие ассеты вне Assets/Art/Lobby не меняются
    /// (их рендереры освещаются пробами), как и скалы, растительность и горизонт: ночью они в тени,
    /// а лайтмапа на них — лишняя память.
    /// </summary>
    public static class LobbyLightmapUv
    {
        private const string MenuRoot = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string OwnedRoot = "Assets/Art/Lobby/";
        private const string Report = "tasks/lobby-decoration/reports/implementation/night-lighting/lightmap-uv.txt";

        // Зоны, где запечённый свет виден игроку; периметр и дальний грунт освещаются ambient.
        private static readonly HashSet<string> LitZones = new HashSet<string>(StringComparer.Ordinal)
        {
            "ArmoryRangeZone", "CheckpointZone", "BarracksZone", "ConstructionZone", "RestCanopyZone",
            "WarehouseZone", "VehicleYardZone", "ShippingDepotZone", "SharedBase", "OuterGround",
        };

        private static readonly string[] ExcludedNames = { "SM_Cliff", "SM_Rock", "Fir", "Tree", "Bush", "Grass", "Horizon", "DistantGround" };

        // Меньше — реквизит на пробах; UV2 ему не нужен.
        private const float MinLightmappedSize = 1f;

        [MenuItem(MenuRoot + "Ночь: UV2 для лайтмап")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Откройте " + ScenePath + " вне Play Mode.");
            var environment = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")
                ?? throw new InvalidOperationException("В Lobby нет корня Environment.");

            var models = new SortedSet<string>(StringComparer.Ordinal);
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            var foreign = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Transform zone in environment.transform)
            {
                if (!LitZones.Contains(zone.name)) continue;
                foreach (var renderer in zone.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || mesh.HasVertexAttribute(VertexAttribute.TexCoord1) || !Candidate(renderer, mesh)) continue;
                    string path = AssetDatabase.GetAssetPath(mesh);
                    if (!path.StartsWith(OwnedRoot, StringComparison.Ordinal)) { foreign.Add(path + " :: " + mesh.name); continue; }
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) meshes[path + "::" + mesh.name] = mesh;
                    else if (AssetImporter.GetAtPath(path) is ModelImporter) models.Add(path);
                    else foreign.Add(path + " :: " + mesh.name + " (не модель и не .asset)");
                }
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in models)
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    if (importer.generateSecondaryUV) continue;
                    importer.generateSecondaryUV = true;
                    importer.SaveAndReimport();
                }
                foreach (var mesh in meshes.Values)
                {
                    Unwrapping.GenerateSecondaryUVSet(mesh);
                    EditorUtility.SetDirty(mesh);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();

            var log = new StringBuilder();
            log.AppendLine("UV2 для лайтмап Lobby " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.AppendLine("Модели (Generate Lightmap UVs): " + models.Count);
            foreach (string path in models) log.AppendLine("  " + path);
            log.AppendLine("Сгенерированные меши (.asset): " + meshes.Count);
            foreach (string key in meshes.Keys.OrderBy(k => k, StringComparer.Ordinal)) log.AppendLine("  " + key);
            log.AppendLine("Вне " + OwnedRoot + " — не менялись, остаются на пробах: " + foreign.Count);
            foreach (string path in foreign) log.AppendLine("  " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, log.ToString());
            GameLog.Debug.Info("[LobbyLightmapUv] моделей " + models.Count + ", мешей " + meshes.Count + ", чужих " + foreign.Count + ". Отчёт: " + Report);
        }

        // Те же условия, что у static GI ночного пресета, плюс размер и исключения по имени.
        private static bool Candidate(MeshRenderer renderer, Mesh mesh)
        {
            var go = renderer.gameObject;
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            if ((flags & (StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic)) == 0 || !go.activeInHierarchy) return false;
            if (renderer.bounds.size.magnitude < MinLightmappedSize) return false;
            if (ExcludedNames.Any(n => mesh.name.Contains(n) || go.name.Contains(n))) return false;
            if (renderer.sharedMaterials.Any(m => m != null && (m.renderQueue >= (int)RenderQueue.AlphaTest || m.IsKeywordEnabled("_ALPHATEST_ON")))) return false;
            return renderer.GetComponentInParent<Rigidbody>(true) == null && renderer.GetComponentInParent<Animator>(true) == null;
        }
    }
}

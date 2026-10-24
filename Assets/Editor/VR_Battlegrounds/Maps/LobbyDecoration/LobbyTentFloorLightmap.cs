using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Пол шатра Lobby в лайтмапе. Меш floor общего nalchik_tolst.fbx (им пользуются MilitaryBase, Warehouse, BlockStand, ServiceYard,
    /// ReferenceMap04, префабы Nalchick) не имеет UV2, и пол освещался одной пробой из центра шатра, куда прямой
    /// свет ламп не доходит. Общий FBX не меняется: копия меша с исходной UV0 и сгенерированной UV2 сохраняется
    /// ассетом лобби и назначается MeshFilter пола; коллайдер остаётся на исходном меше. ReceiveGI → Lightmaps
    /// ставит «Ночь: подготовить запекание» (LobbyNightLighting.ApplyStaticGi) по наличию UV2.
    /// </summary>
    public static class LobbyTentFloorLightmap
    {
        private const string MenuRoot = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string FloorPath = "Environment/ArmoryRangeZone/MapShell/floor";
        private const string SourceModel = "Assets/Models/Arenas/Nalchick/Tolstogo_street/nalchik_tolst.fbx";
        private const string SourceMeshName = "floor";
        private const string MeshPath = "Assets/Art/Lobby/Generated/LobbyTentFloor.asset";

        [MenuItem(MenuRoot + "Ночь: пол шатра с UV2")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Откройте " + ScenePath + " вне Play Mode.");
            var floor = FindFloor(scene) ?? throw new InvalidOperationException("В Lobby нет " + FloorPath + ".");
            var filter = floor.GetComponent<MeshFilter>() ?? throw new InvalidOperationException(FloorPath + " без MeshFilter.");

            var source = AssetDatabase.LoadAllAssetsAtPath(SourceModel).OfType<Mesh>().SingleOrDefault(m => m.name == SourceMeshName)
                ?? throw new InvalidOperationException("В " + SourceModel + " нет меша " + SourceMeshName + ".");
            if (filter.sharedMesh != source && AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshPath)
                throw new InvalidOperationException(FloorPath + " показывает чужой меш " + AssetDatabase.GetAssetPath(filter.sharedMesh) + " :: " + filter.sharedMesh?.name + ".");

            var copy = Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(MeshPath);
            UnwrapParam.SetDefaults(out var unwrap);
            Unwrapping.GenerateSecondaryUVSet(copy, unwrap);
            if (!copy.HasVertexAttribute(VertexAttribute.TexCoord1))
                throw new InvalidOperationException("UV2 пола не сгенерирована.");

            // Повторный запуск обновляет тот же ассет: GUID и ссылка сцены сохраняются.
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                AssetDatabase.CreateAsset(copy, MeshPath);
                mesh = copy;
            }
            else
            {
                EditorUtility.CopySerialized(copy, mesh);
                mesh.name = copy.name;
                Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssetIfDirty(mesh);

            if (filter.sharedMesh != mesh)
            {
                Undo.RecordObject(filter, "Пол шатра с UV2");
                filter.sharedMesh = mesh;
                EditorSceneManager.MarkSceneDirty(scene);
            }
            GameLog.Debug.Info("[LobbyTentFloorLightmap] " + FloorPath + " → " + MeshPath + ": вершин " + mesh.vertexCount
                + ", UV2 " + mesh.HasVertexAttribute(VertexAttribute.TexCoord1) + ". Дальше — «Ночь: подготовить запекание» и сохранение Lobby.");
        }

        private static GameObject FindFloor(Scene scene)
        {
            string[] parts = FloorPath.Split('/');
            var root = scene.GetRootGameObjects().SingleOrDefault(r => r.name == parts[0]);
            if (root == null) return null;
            var t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            return t != null ? t.gameObject : null;
        }
    }
}

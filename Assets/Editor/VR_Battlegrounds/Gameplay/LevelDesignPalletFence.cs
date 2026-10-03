using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>Простые эталоны и отдельная декоративная сборка из двух вертикальных палет.</summary>
    public static class LevelDesignPalletFence
    {
        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать блоки можно только вне Play Mode.");
            Create("LD_PalletFence_Set_Soft", 2f);
            Create("LD_PalletFence_Single_Soft", 2f);
        }

        private static void Create(string block, float width)
        {
            string path = "Assets/Prefabs/LevelDesign/LD_Alphabet/" + block + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/GreyBlue_Mat.mat");
            if (material == null) throw new InvalidOperationException("Материал Soft не найден.");
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Wall_Mid_Soft.prefab");
            try
            {
                root.name = block;
                LevelDesignBlockTexturer.PrepareDimensions(root,block,1.6f);
                CoverClass expected;
                if (!CoverClassRules.TryExpectedClass(root, out expected)) throw new InvalidOperationException("Неверное имя Soft-палеты.");
                var surface = root.GetComponent<CoverSurface>() ?? root.AddComponent<CoverSurface>();
                surface.Class = expected;
                surface.PenetrationModifier = 3f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            LevelDesignBlockTexturer.Run(block);
        }

        public const string DecoratedPath = "Assets/Prefabs/LevelDesign/Decorated/PalletFence_TwoLevels_Soft.prefab";

        public static void BuildDecorated()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Менять декорации можно только вне Play Mode.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/env_packs/RPG_FPS_game_assets_industrial/Other_props/Palets/Palet_v1/Palet_v1_single.prefab");
            var root = new GameObject("PalletFence_TwoLevels_Soft");
            try
            {
                for (int level = 0; level < 2; level++)
                {
                    var piece = UnityEngine.Object.Instantiate(source, root.transform);
                    piece.name = "Палета " + (level + 1);
                    piece.transform.rotation = Quaternion.Euler(90, 0, 0) * piece.transform.rotation;
                    var filters = piece.GetComponentsInChildren<MeshFilter>(true);
                    var bounds = new Bounds();
                    bool found = false;
                    foreach (var filter in filters)
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var point = filter.transform.TransformPoint(vertex);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                    }
                    float scale = 0.8f / bounds.size.y;
                    piece.transform.localScale *= scale;
                    piece.transform.position = new Vector3(-bounds.center.x * scale, level * 0.8f - bounds.min.y * scale, -bounds.center.z * scale);
                    foreach (var collider in piece.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (var filter in filters)
                    {
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false;
                    }
                }
                var surface = root.AddComponent<CoverSurface>();
                surface.Class = CoverClass.Soft;
                surface.PenetrationModifier = 3f;
                PrefabUtility.SaveAsPrefabAsset(root, DecoratedPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }
    }
}

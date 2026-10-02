using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>Средние блоки выбранных секций забора: Hard и Soft, без искажения моделей.</summary>
    public static class LevelDesignConcreteFence
    {
        public const string Path = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Fence_Mid_Hard.prefab";
        private const string Source = "Assets/RPG_FPS_game_assets_industrial/Fences/Concrete_fences/Concrete_fence_v1/Concrete_fence_v1_wall_set_v2.prefab";

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Ensure Medium Concrete Fence Block")]
        public static void Ensure()
        {
            Create(Path, Source, "LD_Wall_Mid", false);
        }

        public const string SoftPath = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Fence_Mid_Soft.prefab";

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Ensure Medium Soft Fence Block")]
        public static void EnsureSoft()
        {
            Create(SoftPath, "Assets/RPG_FPS_game_assets_industrial/Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_S.prefab", "LD_Wall_Mid_Soft", true);
        }

        private static void Create(string path, string sourcePath, string template, bool thinPanel)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать блок можно только вне Play Mode.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            // Размеры эталонов фиксированы; изменения декоративных моделей их не пересчитывают.
            var dimensions = thinPanel ? new Vector3(3.538f, 1.6f, 0.336f) : new Vector3(9.065f, 1.6f, 0.349f);
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LevelDesign/LD_Alphabet/" + template + ".prefab");
            try
            {
                root.name = System.IO.Path.GetFileNameWithoutExtension(path);
                root.transform.localScale = dimensions;
                // Габарит декора включает объём деталей; Soft-панель для прострела остаётся тонкой.
                if (thinPanel)
                {
                    var collider = root.GetComponent<BoxCollider>();
                    var size = collider.size;
                    size.z = 0.1f / root.transform.localScale.z;
                    collider.size = size;
                }
                CoverClass expected;
                if (!CoverClassRules.TryExpectedClass(root, out expected))
                    throw new InvalidOperationException("Имя забора не соответствует CoverClassRules.");
                var surface = root.GetComponent<CoverSurface>() ?? root.AddComponent<CoverSurface>();
                surface.Class = expected;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            LevelDesignBlockTexturer.Run(System.IO.Path.GetFileNameWithoutExtension(path));
        }
    }
}

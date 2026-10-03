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
        private const string Source = "Assets/env_packs/RPG_FPS_game_assets_industrial/Fences/Concrete_fences/Concrete_fence_v1/Concrete_fence_v1_wall_set_v2.prefab";

        public static void Ensure()
        {
            Create(Path, Source, "LD_Wall_Mid", false);
        }

        public const string SoftPath = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Fence_Mid_Soft.prefab";

        public static void EnsureSoft()
        {
            Create(SoftPath, "Assets/env_packs/RPG_FPS_game_assets_industrial/Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_S.prefab", "LD_Wall_Mid_Soft", true);
        }

        private static void Create(string path, string sourcePath, string template, bool thinPanel)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать блок можно только вне Play Mode.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            // Размеры эталонов фиксированы; изменения декоративных моделей их не пересчитывают.
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LevelDesign/LD_Alphabet/" + template + ".prefab");
            try
            {
                root.name = System.IO.Path.GetFileNameWithoutExtension(path);
                LevelDesignBlockTexturer.PrepareDimensions(root,root.name,1.6f);
                // Модульный эталон занимает одинаковый объём в визуале и коллизии.
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

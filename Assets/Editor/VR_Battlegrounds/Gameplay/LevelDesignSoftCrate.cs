using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>Добавляет в алфавит полый Soft-ящик: 1,2 м снаружи, стенки 5 см.</summary>
    public static class LevelDesignSoftCrate
    {
        public const string Path = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Crate_Soft.prefab";

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Ensure Soft Crate Block")]
        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать блок можно только вне Play Mode.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null) return;
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Crate.prefab");
            try
            {
                root.name = "LD_Crate_Soft";
                root.transform.localScale = Vector3.one * 1.2f;
                foreach (var collider in root.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                const float t = 0.05f / 1.2f;
                // Шесть стенок без перекрытия углов; полный куб не имитирует толстую деревянную стену.
                for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var collider = root.AddComponent<BoxCollider>();
                    var size = Vector3.one;
                    for (int previous = 0; previous < axis; previous++) size[previous] = 1 - 2 * t;
                    size[axis] = t;
                    var center = Vector3.zero;
                    center[axis] = sign * (0.5f - t * 0.5f);
                    collider.size = size;
                    collider.center = center;
                }
                CoverClass expected;
                if (!CoverClassRules.TryExpectedClass(root, out expected))
                    throw new InvalidOperationException("Имя Soft-ящика не соответствует CoverClassRules.");
                var surface = root.GetComponent<CoverSurface>() ?? root.AddComponent<CoverSurface>();
                surface.Class = expected;
                surface.PenetrationModifier = 3f;
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            LevelDesignBlockTexturer.Run("LD_Crate_Soft");
        }
    }
}

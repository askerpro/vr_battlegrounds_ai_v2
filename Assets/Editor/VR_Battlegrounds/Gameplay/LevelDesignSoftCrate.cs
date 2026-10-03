using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>Добавляет модульный полый Soft-ящик высотой 1.2 м, стенки 5 см.</summary>
    public static class LevelDesignSoftCrate
    {
        public const string Path = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Crate_Soft.prefab";

        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создавать блок можно только вне Play Mode.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null) return;
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Crate.prefab");
            try
            {
                root.name = "LD_Crate_Soft";
                LevelDesignBlockTexturer.PrepareDimensions(root,root.name,1.2f);
                foreach (var collider in root.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                Bounds bounds = root.GetComponent<MeshFilter>().sharedMesh.bounds;
                Vector3 thickness = new Vector3(.05f/root.transform.localScale.x,
                    .05f/root.transform.localScale.y,.05f/root.transform.localScale.z);
                // Шесть стенок без перекрытия углов; полный куб не имитирует толстую деревянную стену.
                for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var collider = root.AddComponent<BoxCollider>();
                    var size = bounds.size;
                    for (int previous = 0; previous < axis; previous++) size[previous] -= 2 * thickness[previous];
                    size[axis] = thickness[axis];
                    var center = bounds.center;
                    center[axis] += sign * (bounds.extents[axis] - thickness[axis] * .5f);
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

using System;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.LevelDesign
{
    [Serializable]
    public struct BlockoutOpeningSettings
    {
        public bool enabled;
        public float spacing, width, sillHeight, lintelHeight;
        public static BlockoutOpeningSettings Default => new BlockoutOpeningSettings
            { spacing = .6f, width = .15f, sillHeight = .3f, lintelHeight = .3f };
    }

    [Serializable]
    public class BlockoutMaterialVariant
    {
        public CoverClass material;
        public GameObject sourcePrefab;
    }

    /// <summary>Форма отдельно от класса материала и физических щелей; размеры берутся из общего паспорта.</summary>
    public sealed class BlockoutBlockDefinition : ScriptableObject
    {
        public string shapeId, title, dimensionsSourceKey;
        public string displayName;
        [TextArea(2, 5)] public string description;
        public GameObject geometryPrefab;
        public Texture2D thumbnail;
        public bool gameplayGeometry = true;
        public bool supportsCellWall, supportsOpenings, editableDimensions, vaultShapeSuitable;
        public bool heightEditable;
        public string openingDisabledReason;
        public CoverClass defaultMaterial;
        public CoverClass[] allowedMaterials = { CoverClass.Hard, CoverClass.Soft };
        public BlockoutMaterialVariant[] materialVariants = Array.Empty<BlockoutMaterialVariant>();
        public Vector3 minDimensions = new Vector3(.3f, .1f, .3f);
        public Vector3 maxDimensions = new Vector3(60, 10, 60);
        public bool Allows(CoverClass material) => Array.IndexOf(allowedMaterials, material) >= 0;
        public bool VaultSuitable(Vector3 dimensions) => vaultShapeSuitable && dimensions.y <= 1f && dimensions.z <= .3001f;
    }
}

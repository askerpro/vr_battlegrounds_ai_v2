using System;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Выбранные свойства экземпляра; membership и смысловые идентификаторы хранятся отдельно.</summary>
    [DisallowMultipleComponent]
    public sealed class BlockoutBlockInstance : MonoBehaviour
    {
        public BlockoutBlockDefinition definition;
        public Vector3 dimensions;
        public CoverClass material;
        public BlockoutOpeningSettings openings;
        public BlockoutSectionSettings[] sections = Array.Empty<BlockoutSectionSettings>();
        public bool HasSections => sections != null && sections.Length == 3 && Array.TrueForAll(sections, s => s != null);
        public BlockoutCoverSummary MaterialSummary
        {
            get
            {
                CoverClass first = HasSections ? sections[0].material : material;
                if (HasSections)
                    for (int i = 1; i < 3; i++)
                        if (dimensions.y > BlockoutSectionSettings.Bottom(i) && sections[i].material != first) return BlockoutCoverSummary.Mixed;
                return first == CoverClass.Soft ? BlockoutCoverSummary.Soft : first == CoverClass.Visual ? BlockoutCoverSummary.Visual : BlockoutCoverSummary.Hard;
            }
        }
        public bool VaultSuitable => definition != null && definition.VaultSuitable(dimensions);
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Стабильные определения геометрии; исходные варианты префабов остаются доступны для миграции.</summary>
    public sealed class BlockoutBlockRegistry : ScriptableObject
    {
        [SerializeField] private List<BlockoutBlockDefinition> definitions = new List<BlockoutBlockDefinition>();
        public IReadOnlyList<BlockoutBlockDefinition> Definitions => definitions;
        public void SetDefinitions(IEnumerable<BlockoutBlockDefinition> values) => definitions = new List<BlockoutBlockDefinition>(values);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Editor-измеренные bounds при исходном физическом scale; runtime не читает mesh.</summary>
    [Serializable]
    public struct ArsenalItemGeometry
    {
        public bool Present;
        public GameObject Resource;
        public string SourceGuid, SourceFingerprint;
        public Vector3 PhysicalScale, DropOffsetPhysical;
        public Quaternion DropRotation;
        public Bounds PhysicalRootBounds;
    }

    [Serializable]
    public struct ArsenalWeaponGeometry
    {
        public WeaponInfo Weapon;
        public string WeaponId, SourceGuid;
        public ArsenalItemGeometry Item, Magazine;
    }

    [Serializable]
    public struct ArsenalSupportGeometry
    {
        public GameObject Module;
        public string SourceGuid, SourceFingerprint;
        public Bounds LocalBounds;
    }

    [Serializable]
    public struct ArsenalMaterialResource
    {
        public Material Material;
        public string SourceGuid, SourceFingerprint;
    }

    /// <summary>Компилированные ресурсы. Preset/Style не дублируются: состав и target poses не принадлежат catalog.</summary>
    public sealed class ArsenalCompositionCatalog : ScriptableObject
    {
        public const int CurrentCompilerVersion = 1;
        public const int CurrentLayoutVersion = 2;
        public const int CurrentIdentitySchemaVersion = 1;
        [SerializeField] private string _catalogId;
        [SerializeField] private int _compilerVersion = CurrentCompilerVersion;
        [SerializeField] private ArsenalDecorationDescriptor[] _decorations = Array.Empty<ArsenalDecorationDescriptor>();
        [SerializeField] private ArsenalWeaponGeometry[] _weapons = Array.Empty<ArsenalWeaponGeometry>();
        [SerializeField] private ArsenalSupportGeometry[] _supports = Array.Empty<ArsenalSupportGeometry>();
        [SerializeField] private ArsenalMaterialResource[] _materials = Array.Empty<ArsenalMaterialResource>();
        public string CatalogId => _catalogId;
        public int CompilerVersion => _compilerVersion;
        public IReadOnlyList<ArsenalDecorationDescriptor> Decorations => Array.AsReadOnly(_decorations);
        public IReadOnlyList<ArsenalWeaponGeometry> Weapons => Array.AsReadOnly(_weapons);
        public IReadOnlyList<ArsenalSupportGeometry> Supports => Array.AsReadOnly(_supports);
        public IReadOnlyList<ArsenalMaterialResource> Materials => Array.AsReadOnly(_materials);
        /// <summary>Только resource compiler создаёт новый каталог; existing native asset не изменяется этим API.</summary>
        public static ArsenalCompositionCatalog CreateCompiled(string id,
            IEnumerable<ArsenalDecorationDescriptor> decorations, IEnumerable<ArsenalWeaponGeometry> weapons,
            IEnumerable<ArsenalSupportGeometry> supports, IEnumerable<ArsenalMaterialResource> materials=null)
        {
            var result=CreateInstance<ArsenalCompositionCatalog>(); result._catalogId=id;
            result._decorations=new List<ArsenalDecorationDescriptor>(decorations).ConvertAll(d=>d.Freeze()).ToArray();
            result._weapons=new List<ArsenalWeaponGeometry>(weapons).ToArray();
            result._supports=new List<ArsenalSupportGeometry>(supports).ToArray();
            result._materials=new List<ArsenalMaterialResource>(materials??Array.Empty<ArsenalMaterialResource>()).ToArray(); return result;
        }
    }
}

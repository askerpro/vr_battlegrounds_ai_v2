using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Область реальной геометрии в осях корня оружия, выбранная для отдельного действия.</summary>
    public sealed class WeaponVisualRegion
    {
        public Transform Source;
        public Bounds Bounds;
        public string Name;
    }

    /// <summary>Контакт ручки отделён от источника хода. Области сохраняются выпеченными мешами.</summary>
    public sealed class WeaponInteractionRecipe
    {
        public string Name;
        public string AssetFolder;
        public Transform ActionGripPart;
        public Vector3? ActionContact;
        public WeaponVisualRegion Primary;
        public WeaponVisualRegion Support;
        public WeaponVisualRegion Action;
        public WeaponVisualRegion Insertion;
    }
}

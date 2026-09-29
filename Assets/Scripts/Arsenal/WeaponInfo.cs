using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// ScriptableObject defining a weapon/equipment item for the Arsenal Wall.
    /// One asset per weapon — reused by slots, UI, economy, and balancing systems.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "VR Battlegrounds/Arsenal/Weapon Info")]
    public class WeaponInfo : ScriptableObject
    {
        // ── Identity ───────────────────────────────────────────
        [Header("Identity")]
        [Tooltip("Display name shown on the price tag (e.g. \"AK-47\")")]
        [SerializeField] private string _displayName = "Weapon";

        [Tooltip("Short ID for logging and network sync (e.g. \"ak47\")")]
        [SerializeField] private string _weaponId = "weapon";

        [TextArea(2, 4)]
        [Tooltip("Optional description for tooltips or UI")]
        [SerializeField] private string _description;

        // ── Prefabs ────────────────────────────────────────────
        [Header("Prefabs")]
        [Tooltip("The weapon prefab (must have UxrGrabbableObject)")]
        [SerializeField] private GameObject _weaponPrefab;

        [Tooltip("Magazine prefab displayed next to the weapon (optional)")]
        [SerializeField] private GameObject _magazinePrefab;

        [Tooltip("Max number of magazines to auto-spawn into player inventory")]
        [SerializeField] private int _maxMagazineCount = 3;

        // ── Economy ────────────────────────────────────────────
        [Header("Economy")]
        [Tooltip("Purchase price in in-game currency")]
        [SerializeField] private int _price = 2700;

        // ── Slot Placement ─────────────────────────────────────
        [Header("Slot Placement")]
        [Tooltip("Category determines which slot type this item fits into")]
        [SerializeField] private WeaponCategory _category = WeaponCategory.Rifle;

        [Tooltip("Local position offset for the weapon on its anchor")]
        [SerializeField] private Vector3 _weaponPositionOffset = Vector3.zero;

        [Tooltip("Local rotation offset for the weapon on its anchor (Euler angles)")]
        [SerializeField] private Vector3 _weaponRotationOffset = Vector3.zero;

        [Tooltip("Local position offset for the magazine on its anchor")]
        [SerializeField] private Vector3 _magazinePositionOffset = Vector3.zero;

        // ── Visual ─────────────────────────────────────────────
        [Header("Visual")]
        [Tooltip("Icon for use in UI (buy menu, HUD, kill feed)")]
        [SerializeField] private Sprite _icon;

        // ── Public API ─────────────────────────────────────────
        public string DisplayName => _displayName;
        public string WeaponId => _weaponId;
        public string Description => _description;
        public GameObject WeaponPrefab => _weaponPrefab;
        public GameObject MagazinePrefab => _magazinePrefab;
        public int MaxMagazineCount => _maxMagazineCount;
        public int Price => _price;
        public WeaponCategory Category => _category;
        public Vector3 WeaponPositionOffset => _weaponPositionOffset;
        public Vector3 WeaponRotationOffset => _weaponRotationOffset;
        public Vector3 MagazinePositionOffset => _magazinePositionOffset;
        public Sprite Icon => _icon;
    }

    /// <summary>
    /// Weapon category — determines which zone of the Arsenal Wall the item belongs to.
    /// </summary>
    public enum WeaponCategory
    {
        Rifle,      // Pegboard (upper zone)
        Pistol,     // Shelf (lower zone)
        Equipment,  // Shelf — grenades, medkits
        Melee       // Shelf or pegboard side
    }
}

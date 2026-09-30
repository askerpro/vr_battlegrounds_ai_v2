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

        // ── Balance (T-38) ─────────────────────────────────────
        // Единая точка правды баланса: префабы получают эти значения командой
        // Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance, WeaponBalanceTests сверяют.
        // Числа — по Counter-Strike 2 (урон, темп, магазин, цена; разброс — нет, его даёт отдача).
        [Header("Balance (CS2)")]
        [Tooltip("Урон одной пули (дробинки) вблизи, без множителя зоны. 0 — баланс не задан (сэмплы SDK), " +
                 "Apply Weapon Balance префаб не трогает.")]
        [SerializeField] private float _damage;

        [Tooltip("Спад урона как в CS2: множитель на каждые 500 юнитов (12,7 м). 1 — без спада.")]
        [SerializeField, Range(0.3f, 1f)] private float _rangeModifier = 0.85f;

        [Tooltip("Пробитие стен как в CS2 (T-41): пистолеты, SMG, дробовики — 1; Desert Eagle, винтовки — 2; " +
                 "снайперские — 2.5. Уходит в PenetrationPower каждого выстрела.")]
        [SerializeField, Min(0f)] private float _penetration = 1f;

        [Tooltip("Темп, выстрелов в минуту.")]
        [SerializeField] private int _fireRate = 400;

        [Tooltip("Патронов в магазине.")]
        [SerializeField] private int _magazineSize = 20;

        [Tooltip("Дробинок на выстрел вместе с основным снарядом; 1 — пуля.")]
        [SerializeField, Min(1)] private int _pellets = 1;

        [Tooltip("Картина накопленной отдачи (RecoilAccumulator на префабе): подброс первого выстрела, потолок очереди, " +
                 "рыскание, возврат в паузе, множитель одной руки.")]
        [SerializeField] private VrBattlegrounds.Weapons.RecoilPattern _recoil = new VrBattlegrounds.Weapons.RecoilPattern();

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

        /// <summary>Задан ли баланс: оружие без него (сэмплы SDK) команда баланса не трогает.</summary>
        public bool HasBalance => _damage > 0f;
        public float Damage => _damage;
        public float RangeModifier => _rangeModifier;
        public float Penetration => _penetration;
        public int FireRate => _fireRate;
        public int MagazineSize => _magazineSize;
        public int Pellets => _pellets;
        public VrBattlegrounds.Weapons.RecoilPattern Recoil => _recoil;

        /// <summary>Юнитов CS на метр: 500 юнитов = 12,7 м.</summary>
        public const float CsUnitsPerMeter = 500f / 12.7f;

        /// <summary>Урон на дистанции <paramref name="meters"/> по правилу спада CS2.</summary>
        public static float DamageAt(float damage, float rangeModifier, float meters) =>
            damage * Mathf.Pow(rangeModifier, meters * CsUnitsPerMeter / 500f);

        /// <summary>
        /// Выстрелов в секунду для <c>UxrFirearmTrigger._maxShotFrequency</c> (целое у SDK): темп в минуту / 60,
        /// не меньше 1.
        /// </summary>
        public static int ShotFrequency(int fireRate) => Mathf.Max(1, Mathf.RoundToInt(fireRate / 60f));
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

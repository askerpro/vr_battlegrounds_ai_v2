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

        // Только ссылки: readiness/feedback состояния принадлежат runtime владельцам.
        [Header("Weapon Readiness")]
        [SerializeField] private VrBattlegrounds.Weapons.WeaponReadinessProfile _readinessProfile;
        [SerializeField] private VrBattlegrounds.Weapons.WeaponFeedbackProfile _commonFeedbackProfile;
        [SerializeField] private VrBattlegrounds.Weapons.WeaponFeedbackProfile _feedbackOverride;

        // ── Economy ────────────────────────────────────────────
        [Header("Economy")]
        [Tooltip("Purchase price in in-game currency")]
        [SerializeField] private int _price = 2700;

        // ── Balance (T-38) ─────────────────────────────────────
        // Единая точка правды баланса: префабы получают эти значения командой
        // Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance, WeaponBalanceTests сверяют.
        // Числа — по Counter-Strike 2 (items_game.txt), источник и дата сверки — Docs/tasks/T-38-weapon-roster-and-balance.md.
        [Header("Balance (CS2)")]
        [Tooltip("Урон одной пули (дробинки) вблизи, без множителя зоны. 0 — баланс не задан (сэмплы SDK), " +
                 "Apply Weapon Balance префаб не трогает.")]
        [SerializeField] private float _damage;

        [Tooltip("Спад урона как в CS2: множитель на каждые 500 юнитов (12,7 м). 1 — без спада.")]
        [SerializeField, Range(0.3f, 1f)] private float _rangeModifier = 0.85f;

        [Tooltip("Пробитие стен как в CS2 (T-41): пистолеты, SMG, дробовики — 1; Desert Eagle, винтовки — 2; " +
                 "снайперские — 2.5. Уходит в PenetrationPower каждого выстрела.")]
        [SerializeField, Min(0f)] private float _penetration = 1f;

        [Tooltip("CS2 armor_ratio: доля урона, проходящая сквозь броню (×0,5 в CS — у нас брони пока нет, T-43). Справочно.")]
        [SerializeField, Range(0f, 2f)] private float _armorRatio = 1f;

        [Tooltip("Темп, выстрелов в минуту.")]
        [SerializeField] private int _fireRate = 400;

        [Tooltip("CS2 is_full_auto: автоматический огонь. Влияет на картину отдачи (рыскание очереди).")]
        [SerializeField] private bool _fullAuto;

        [Tooltip("Патронов в магазине.")]
        [SerializeField] private int _magazineSize = 20;

        [Tooltip("CS2 primary_reserve_ammo_max: запас патронов. Справочно — у нас запас даёт число магазинов.")]
        [SerializeField, Min(0)] private int _reserveAmmo;

        [Tooltip("Дробинок на выстрел вместе с основным снарядом; 1 — пуля.")]
        [SerializeField, Min(1)] private int _pellets = 1;

        [Tooltip("CS2 kill_award: награда за убийство из этого оружия (для экономики раунда).")]
        [SerializeField, Min(0)] private int _killAward = 300;

        [Tooltip("Из точности CS2 применяется только spread дробовика (мрад). Пули летят по оси ствола; " +
                 "неточность стоя/движения/очереди и её восстановление сохранены справочно, в игре не применяются.")]
        [SerializeField] private VrBattlegrounds.Weapons.SpreadPattern _spread = new VrBattlegrounds.Weapons.SpreadPattern();

        [Tooltip("CS2 recoil_magnitude: сила отдачи за выстрел. Картину накопленной отдачи (RecoilAccumulator) и толчок " +
                 "SDK выводит из неё RecoilPattern.FromCs2 — одна формула на весь арсенал.")]
        [SerializeField, Min(0f)] private float _recoilMagnitude;

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

        [Tooltip("Свои раскладки слота арсенала (ассеты ArsenalSlotLayout), не больше одной на вид слота. " +
                 "Нет раскладки для вида — оружие ложится по раскладке слота по умолчанию.")]
        [SerializeField] private System.Collections.Generic.List<ArsenalSlotLayout> _layouts =
            new System.Collections.Generic.List<ArsenalSlotLayout>();

        // ── Visual ─────────────────────────────────────────────
        [Header("Visual")]
        [Tooltip("Icon for use in UI (buy menu, HUD, kill feed)")]
        [SerializeField] private Sprite _icon;

        // ── Public API ─────────────────────────────────────────
        public VrBattlegrounds.Weapons.WeaponReadinessProfile ReadinessProfile => _readinessProfile;
        public VrBattlegrounds.Weapons.WeaponFeedbackProfile CommonFeedbackProfile => _commonFeedbackProfile;
        public VrBattlegrounds.Weapons.WeaponFeedbackProfile FeedbackOverride => _feedbackOverride;
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

        /// <summary>Свои раскладки слота арсенала этого оружия.</summary>
        public System.Collections.Generic.IReadOnlyList<ArsenalSlotLayout> SlotLayouts => _layouts;

        /// <summary>Своя раскладка для вида слота; нет — берётся раскладка слота по умолчанию.</summary>
        public bool TryGetSlotLayout(ArsenalPresentationZone slotKind, out ArsenalSlotLayout layout)
        {
            foreach (var candidate in _layouts)
                if (candidate != null && candidate.SlotKind == slotKind) { layout = candidate; return true; }
            layout = null;
            return false;
        }
        public Sprite Icon => _icon;

        /// <summary>Задан ли баланс: оружие без него (сэмплы SDK) команда баланса не трогает.</summary>
        public bool HasBalance => _damage > 0f;
        public float Damage => _damage;
        public float RangeModifier => _rangeModifier;
        public float Penetration => _penetration;
        public float ArmorRatio => _armorRatio;
        public int FireRate => _fireRate;
        public bool FullAuto => _fullAuto;
        public int MagazineSize => _magazineSize;

        /// <summary>
        /// Сколько выдаваемых предметов стоит одного магазина: у ручного заряжания (FixedStoreChamber) выдаются
        /// одиночные патроны — ёмкость оружия на магазин; у остального оружия — сам магазин (1).
        /// </summary>
        public int AmmoItemsPerMagazine => _readinessProfile != null &&
            _readinessProfile.AmmoCapability == VrBattlegrounds.Weapons.WeaponAmmoCapability.FixedStoreChamber
                ? Mathf.Max(1, _magazineSize) : 1;
        public int ReserveAmmo => _reserveAmmo;
        public int Pellets => _pellets;
        public int KillAward => _killAward;
        public VrBattlegrounds.Weapons.SpreadPattern Spread => _spread;
        public float RecoilMagnitude => _recoilMagnitude;

        /// <summary>Картина накопленной отдачи — выводится из CS2 <c>recoil_magnitude</c> (<see cref="VrBattlegrounds.Weapons.RecoilPattern.FromCs2"/>).</summary>
        public VrBattlegrounds.Weapons.RecoilPattern Recoil => VrBattlegrounds.Weapons.RecoilPattern.FromCs2(_recoilMagnitude, _fullAuto);

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

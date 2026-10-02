using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Component attached to instantiated weapons to easily identify their WeaponInfo
    /// for inventory and loadout management.
    /// </summary>
    public class WeaponComponent : MonoBehaviour
    {
        [SerializeField] private WeaponInfo _weaponInfo;

        public WeaponInfo WeaponData => _weaponInfo;

        /// <summary>
        /// Слот стены, который выдал это оружие, — его «дом». Ставится один раз при выдаче
        /// и дальше не меняется, какой бы путь ни прошёл ствол: рука, кобура, пол, чужой
        /// слот. Возврат оружия домой смотрит только сюда, а не на последний якорь
        /// (последним может быть кобура). Null — оружие выдано не стеной.
        /// </summary>
        public ArsenalSlotController HomeSlot { get; private set; }

        public void Init(WeaponInfo info)
        {
            _weaponInfo = info;
        }

        /// <summary>Запоминает слот-дом, если он ещё не задан.</summary>
        public void SetHomeIfUnset(ArsenalSlotController slot)
        {
            if (HomeSlot == null) HomeSlot = slot;
        }

        // ── Оружие, выданное не стеной (T-45) ─────────────────

        /// <summary>
        /// Подписка на создание сетевых инстансов: оружие из реестра получает компонент сразу,
        /// на каждой машине. Раньше его ставила только стена при выдаче (<c>AssignNetworkItem</c>),
        /// и ствол, выданный иначе (стартовый пистолет в кобуру, оружие бота), был у клиентов
        /// «не оружием»: ни магазинов, ни учёта снаряжения.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InstallPrefabHook()
        {
            Network.NetworkUxrIdentity.InstanceCreated -= AttachByPrefab;
            Network.NetworkUxrIdentity.InstanceCreated += AttachByPrefab;
        }

        /// <summary>Ставит компонент инстансу, если префаб — оружие из <see cref="WeaponRegistry"/>.</summary>
        public static void AttachByPrefab(GameObject prefab, GameObject instance)
        {
            if (prefab == null || instance == null) return;

            WeaponRegistry registry = WeaponRegistry.Instance;
            WeaponInfo info = registry != null ? registry.GetByPrefab(prefab) : null;
            if (info == null) return;

            WeaponComponent component = instance.GetComponent<WeaponComponent>();
            if (component == null) component = instance.AddComponent<WeaponComponent>();
            if (component.WeaponData == null) component.Init(info);
        }
    }
}

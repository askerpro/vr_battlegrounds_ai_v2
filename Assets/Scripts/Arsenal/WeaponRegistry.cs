using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Central registry of all weapons available in the game.
    /// Single ScriptableObject asset — drag WeaponInfo assets here to register them.
    /// Used by WeaponSlotController (dropdown selection), economy, UI, and network sync.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponRegistry", menuName = "VR Battlegrounds/Arsenal/Weapon Registry")]
    public class WeaponRegistry : ScriptableObject
    {
        #region Singleton
        
        private static WeaponRegistry s_instance;

        /// <summary>
        /// Глобальный экземпляр реестра.
        /// Загружается из Resources/WeaponRegistry.asset.
        /// </summary>
        public static WeaponRegistry Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = Resources.Load<WeaponRegistry>(nameof(WeaponRegistry));

                if (s_instance == null)
                    GameLog.Error("[WeaponRegistry] Файл WeaponRegistry.asset не найден в папке Resources. Положите его туда.");

                return s_instance;
            }
        }

        #endregion

        [Header("All Available Weapons")]
        [Tooltip("Master list of all weapons in the game. Order matters for UI display.")]
        [SerializeField] private List<WeaponInfo> _weapons = new List<WeaponInfo>();

        [Header("Default Sidearm")]
        [Tooltip("Стартовый пистолет: игрок получает его бесплатно при появлении и в начале раунда (роль Glock-18 / USP-S " +
                 "в CS2). Должен быть в списке выше и в категории Pistol — WeaponRegistryTests.")]
        [SerializeField] private WeaponInfo _defaultSidearm;

        // ── Public API ─────────────────────────────────────────

        /// <summary>All registered weapons.</summary>
        public IReadOnlyList<WeaponInfo> Weapons => _weapons;

        /// <summary>
        /// Стартовый пистолет (T-39: WK-11 Viper). Единая точка правды для выдачи «бесплатного» ствола — выдачу
        /// делает экономика раунда, сам реестр ничего не спавнит.
        /// </summary>
        public WeaponInfo DefaultSidearm => _defaultSidearm;

        /// <summary>Number of registered weapons.</summary>
        public int Count => _weapons.Count;

        /// <summary>
        /// Find a weapon by its ID (e.g. "ak47", "shotgun").
        /// </summary>
        public WeaponInfo GetById(string weaponId)
        {
            foreach (var w in _weapons)
            {
                if (w != null && w.WeaponId == weaponId)
                    return w;
            }
            return null;
        }

        /// <summary>Оружие, чей префаб — <paramref name="prefab"/>; null — не из реестра.</summary>
        public WeaponInfo GetByPrefab(GameObject prefab)
        {
            if (prefab == null) return null;

            foreach (var w in _weapons)
            {
                if (w != null && w.WeaponPrefab == prefab)
                    return w;
            }
            return null;
        }

        /// <summary>
        /// Find a weapon by display name.
        /// </summary>
        public WeaponInfo GetByName(string displayName)
        {
            foreach (var w in _weapons)
            {
                if (w != null && w.DisplayName == displayName)
                    return w;
            }
            return null;
        }

        /// <summary>
        /// Get all weapons in a specific category.
        /// </summary>
        public List<WeaponInfo> GetByCategory(WeaponCategory category)
        {
            var result = new List<WeaponInfo>();
            foreach (var w in _weapons)
            {
                if (w != null && w.Category == category)
                    result.Add(w);
            }
            return result;
        }

        /// <summary>
        /// Get all weapon display names (for dropdown/popup selectors).
        /// </summary>
        public string[] GetDisplayNames()
        {
            var names = new string[_weapons.Count];
            for (int i = 0; i < _weapons.Count; i++)
                names[i] = _weapons[i] != null ? _weapons[i].DisplayName : "(empty)";
            return names;
        }

        /// <summary>
        /// Get the index of a WeaponInfo in the registry. Returns -1 if not found.
        /// </summary>
        public int IndexOf(WeaponInfo weapon)
        {
            return _weapons.IndexOf(weapon);
        }

        /// <summary>
        /// Get weapon by index. Returns null if out of range.
        /// </summary>
        public WeaponInfo GetByIndex(int index)
        {
            if (index >= 0 && index < _weapons.Count)
                return _weapons[index];
            return null;
        }
    }
}

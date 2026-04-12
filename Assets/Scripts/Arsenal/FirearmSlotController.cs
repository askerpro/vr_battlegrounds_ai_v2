using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Firearm slot on the Arsenal Wall (rifles, SMGs, shotguns, pistols — any weapon with a magazine).
    /// Extends <see cref="ArsenalSlotController"/> with a decorative magazine display.
    /// The magazine is non-interactive — when the player purchases the weapon,
    /// magazines spawn directly in the player's pockets.
    /// </summary>
    public class FirearmSlotController : ArsenalSlotController
    {
        [Header("Decorative Magazine")]
        [Tooltip("Anchor for the decorative magazine display (auto-found by name 'MagAnchor' if empty)")]
        [SerializeField] private UxrGrabbableObjectAnchor _magAnchor;

        private GameObject _spawnedMagazine;

        protected override void Awake()
        {
            base.Awake();

            if (_magAnchor == null)
            {
                var anchors = GetComponentsInChildren<UxrGrabbableObjectAnchor>();
                foreach (var a in anchors)
                {
                    if (a.gameObject.name.Contains("Mag"))
                    {
                        _magAnchor = a;
                        break;
                    }
                }
            }
        }

        public override void SpawnItem()
        {
            base.SpawnItem();

            // Spawn decorative magazine
            if (_magAnchor != null && WeaponData != null &&
                WeaponData.MagazinePrefab != null && _spawnedMagazine == null)
            {
                _spawnedMagazine = Instantiate(
                    WeaponData.MagazinePrefab,
                    _magAnchor.transform.position,
                    _magAnchor.transform.rotation,
                    _magAnchor.transform
                );
                _spawnedMagazine.name = WeaponData.WeaponId + "_mag_decor";

                // Disable interaction — magazine is decorative only
                foreach (var grab in _spawnedMagazine.GetComponentsInChildren<UxrGrabbableObject>(true))
                    grab.enabled = false;
                foreach (var rb in _spawnedMagazine.GetComponentsInChildren<Rigidbody>(true))
                    rb.isKinematic = true;

                GameLog.Info(ArsenalLog, $"[Arsenal] Spawned decorative magazine for '{WeaponData.DisplayName}'.");
            }
        }

        public override void DespawnItem()
        {
            base.DespawnItem();

            if (_spawnedMagazine != null)
            {
                Destroy(_spawnedMagazine);
                _spawnedMagazine = null;
            }
        }

        /// <summary>Exposed for editor preview.</summary>
        public UxrGrabbableObjectAnchor MagAnchor => _magAnchor;
    }
}

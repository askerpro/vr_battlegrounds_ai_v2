using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Base controller for any Arsenal Wall slot.
    /// Handles: single interactive item anchor, WeaponInfo configuration,
    /// LED feedback, lock/unlock, spawn/despawn, affordability.
    ///
    /// Subclasses (<see cref="FirearmSlotController"/>, <see cref="ShelfItemSlotController"/>)
    /// can add decorative elements (e.g. magazine display).
    /// </summary>
    public class ArsenalSlotController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────
        [Header("Item Data")]
        [Tooltip("WeaponInfo asset that configures this slot")]
        [SerializeField] private WeaponInfo _weaponInfo;

        [Header("Anchor")]
        [Tooltip("Snap zone for the item (auto-found if empty)")]
        [SerializeField] private UxrGrabbableObjectAnchor _itemAnchor;

        [Header("Visual Feedback")]
        [SerializeField] private Light _slotLight;
        [SerializeField] private Color _availableColor  = new Color(1f, 0.85f, 0.6f); // warm white
        [SerializeField] private Color _unavailableColor = Color.black;
        [SerializeField] private Color _takenColor       = Color.green;

        // ── Events ─────────────────────────────────────────────
        public System.Action<ArsenalSlotController> OnItemTaken;
        public System.Action<ArsenalSlotController> OnItemReturned;

        // ── Properties ─────────────────────────────────────────
        public bool IsItemPresent  => _itemAnchor != null && _itemAnchor.CurrentPlacedObject != null;
        public bool IsConfigured   => _weaponInfo != null;
        public int Price           => _weaponInfo != null ? _weaponInfo.Price : 0;
        public string DisplayName  => _weaponInfo != null ? _weaponInfo.DisplayName : "Empty";
        public WeaponInfo WeaponData => _weaponInfo;
        public UxrGrabbableObjectAnchor ItemAnchor => _itemAnchor;

        // ── State ──────────────────────────────────────────────
        protected bool IsLocked { get; private set; }
        private GameObject _spawnedItem;
        protected LogLevel ArsenalLog => GameSettings.Instance.LogLevelArsenal;

        // ── Unity ──────────────────────────────────────────────

        protected virtual void Awake()
        {
            if (_itemAnchor == null)
                _itemAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>();

            // В Play mode удаляем превью-объекты, которые визуализировал кастомный эдитор (ArsenalSlotEditorBase)
            // Иначе они останутся на сцене как мусор и будут наслаиваться на реальные игровые объекты.
            if (UnityEngine.Application.isPlaying)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.gameObject.name == "__ItemPreview__" || child.gameObject.name == "__MagPreview__")
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        protected virtual void OnEnable()
        {
            if (_itemAnchor != null)
            {
                _itemAnchor.Placed  += OnObjectPlaced;
                _itemAnchor.Removed += OnObjectRemoved;
            }
        }

        protected virtual void OnDisable()
        {
            if (_itemAnchor != null)
            {
                _itemAnchor.Placed  -= OnObjectPlaced;
                _itemAnchor.Removed -= OnObjectRemoved;
            }
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Checks if the slot is physically empty (i.e. the item was taken),
        /// meaning it needs to be replenished for the next round.
        /// </summary>
        public bool NeedsReplenishment()
        {
            return !IsItemPresent;
        }

        /// <summary>
        /// Assigns a network-spawned item to the slot anchor based on <see cref="_weaponInfo"/>.
        /// Override in subclasses to spawn decorative extras (magazines, etc.).
        /// </summary>
        public virtual void AssignNetworkItem(GameObject spawnedItem)
        {
            if (_weaponInfo == null || spawnedItem == null)
            {
                GameLog.Warning(ArsenalLog, $"[Arsenal] Slot '{name}' failed to assign network item (missing info or object).");
                return;
            }

            // Assign the object reference locally
            _spawnedItem = spawnedItem;
            
            // Parent to slot anchor and apply offset.
            // Mirror supports runtime reparenting of spawned NetworkIdentity objects
            // (nested NI is only forbidden in prefabs, not at runtime).
            _spawnedItem.transform.SetParent(_itemAnchor.transform);
            _spawnedItem.transform.localPosition = _weaponInfo.WeaponPositionOffset;
            _spawnedItem.transform.localRotation = Quaternion.Euler(_weaponInfo.WeaponRotationOffset);
            
            _spawnedItem.name = _weaponInfo.WeaponId + "_instance";

            var weaponComp = _spawnedItem.GetComponent<WeaponComponent>();
            if (weaponComp == null) weaponComp = _spawnedItem.AddComponent<WeaponComponent>();
            weaponComp.Init(_weaponInfo);

            GameLog.Info(ArsenalLog, $"[Arsenal] Assigned network weapon '{_weaponInfo.DisplayName}' to slot '{name}'.");

            SetLightColor(_availableColor);
        }

        /// <summary>
        /// Removes spawned item from the slot.
        /// Override in subclasses to clean up decorative extras.
        /// </summary>
        public virtual void DespawnItem()
        {
            if (_spawnedItem != null)
            {
                Destroy(_spawnedItem);
                _spawnedItem = null;
            }

            SetLightState(false);
        }

        /// <summary>
        /// Locks the slot — disables grabbing from the anchor.
        /// </summary>
        public void Lock()
        {
            IsLocked = true;

            if (_itemAnchor != null && _itemAnchor.CurrentPlacedObject != null)
                _itemAnchor.CurrentPlacedObject.enabled = false;

            SetLightState(false);
            GameLog.Info(ArsenalLog, $"[Arsenal] Slot '{DisplayName}' locked.");
        }

        /// <summary>
        /// Unlocks the slot — enables grabbing.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;

            if (_itemAnchor != null && _itemAnchor.CurrentPlacedObject != null)
                _itemAnchor.CurrentPlacedObject.enabled = true;

            SetLightColor(_availableColor);
            GameLog.Info(ArsenalLog, $"[Arsenal] Slot '{DisplayName}' unlocked.");
        }

        /// <summary>
        /// Updates the slot light based on whether the player can afford this item.
        /// </summary>
        public void SetAffordable(bool canAfford)
        {
            if (IsLocked) return;

            if (canAfford)
                SetLightColor(IsItemPresent ? _availableColor : _takenColor);
            else
                SetLightColor(_unavailableColor);
        }

        // ── Private: Events ────────────────────────────────────

        private void OnObjectPlaced(object sender, UxrManipulationEventArgs e)
        {
            if (IsLocked) return;

            GameLog.Info(ArsenalLog, $"[Arsenal] Item returned to slot '{DisplayName}'.");
            SetLightColor(_availableColor);
            OnItemReturned?.Invoke(this);
        }

        private void OnObjectRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (IsLocked) return;

            GameLog.Info(ArsenalLog, $"[Arsenal] Item taken from slot '{DisplayName}'.");
            SetLightColor(_takenColor);
            OnItemTaken?.Invoke(this);
        }

        // ── Protected: Light Helpers ───────────────────────────

        protected void SetLightState(bool on)
        {
            if (_slotLight != null)
                _slotLight.enabled = on;
        }

        protected void SetLightColor(Color color)
        {
            if (_slotLight != null)
            {
                _slotLight.enabled = color != _unavailableColor;
                _slotLight.color = color;
            }
        }
    }
}

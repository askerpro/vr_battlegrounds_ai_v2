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

        /// <summary>
        /// Предмет, который сейчас лежит в слоте, или null.
        ///
        /// Источников два, потому что предмет попадает в слот двумя разными путями.
        /// Штатный — игрок кладёт его руками: учёт ведёт <see cref="UxrGrabManager"/>,
        /// и предмет виден в <c>UxrGrabbableObjectAnchor.CurrentPlacedObject</c>.
        /// Сетевой — <see cref="AssignNetworkItem"/> перепарентит под якорь объект,
        /// уже заспавненный Mirror'ом; через <see cref="UxrGrabManager"/> он не проходит,
        /// поэтому <c>CurrentPlacedObject</c> остаётся пустым.
        ///
        /// Опора только на <c>CurrentPlacedObject</c> и была находкой NET-13: всё, что
        /// появилось на стене по сети, слот считал отсутствующим.
        ///
        /// Сетевой предмет числится в слоте, пока он жив и висит под якорем. Когда игрок
        /// его забирает, <see cref="UxrGrabManager"/> перепарентит объект к аватару
        /// (<c>UxrGrabbableObject.UseParenting</c> включён по умолчанию), и слот пустеет.
        /// </summary>
        public GameObject CurrentItem
        {
            get
            {
                if (_itemAnchor == null) return null;

                if (_itemAnchor.CurrentPlacedObject != null)
                    return _itemAnchor.CurrentPlacedObject.gameObject;

                if (_spawnedItem != null && _spawnedItem.transform.IsChildOf(_itemAnchor.transform))
                    return _spawnedItem;

                return null;
            }
        }

        public bool IsItemPresent  => CurrentItem != null;
        public bool IsConfigured   => _weaponInfo != null;
        public int Price           => _weaponInfo != null ? _weaponInfo.Price : 0;
        public string DisplayName  => _weaponInfo != null ? _weaponInfo.DisplayName : "Empty";
        public WeaponInfo WeaponData => _weaponInfo;
        public UxrGrabbableObjectAnchor ItemAnchor => _itemAnchor;

        // ── State ──────────────────────────────────────────────
        protected bool IsLocked { get; private set; }
        private GameObject _spawnedItem;

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
                GameLog.Arsenal.Warning($"[Arsenal] Slot '{name}' failed to assign network item (missing info or object).");
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

            GameLog.Arsenal.Info($"[Arsenal] Assigned network weapon '{_weaponInfo.DisplayName}' to slot '{name}'.");

            // Оружие приезжает позже, чем стена успевает заблокировать слоты: закрытая
            // стена блокирует их в Start(), а пополнение идёт из OnStartServer и из фазы
            // Setup. Без этой строки предмет появлялся хватаемым в закрытом арсенале.
            SetItemGrabbable(!IsLocked);

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

            SetItemGrabbable(false);

            SetLightState(false);
            GameLog.Arsenal.Info($"[Arsenal] Slot '{DisplayName}' locked.");
        }

        /// <summary>
        /// Unlocks the slot — enables grabbing.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;

            SetItemGrabbable(true);

            SetLightColor(_availableColor);
            GameLog.Arsenal.Info($"[Arsenal] Slot '{DisplayName}' unlocked.");
        }

        /// <summary>
        /// Включает или выключает захват предмета, лежащего в слоте.
        /// Ищет предмет через <see cref="CurrentItem"/>, а не через
        /// <c>CurrentPlacedObject</c>: для выданного по сети оружия второе всегда пусто,
        /// и блокировка слота была пустым вызовом (NET-13).
        /// </summary>
        private void SetItemGrabbable(bool grabbable)
        {
            GameObject item = CurrentItem;
            if (item == null) return;

            UxrGrabbableObject grab = item.GetComponent<UxrGrabbableObject>();
            if (grab != null) grab.enabled = grabbable;
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

            GameLog.Arsenal.Info($"[Arsenal] Item returned to slot '{DisplayName}'.");
            SetLightColor(_availableColor);
            OnItemReturned?.Invoke(this);
        }

        private void OnObjectRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (IsLocked) return;

            GameLog.Arsenal.Info($"[Arsenal] Item taken from slot '{DisplayName}'.");
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

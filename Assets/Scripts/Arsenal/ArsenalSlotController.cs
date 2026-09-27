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
    /// Subclasses (<see cref="FirearmSlotController"/>) can add decorative elements
    /// (e.g. magazine display).
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
        /// уже заспавненный Mirror'ом.
        ///
        /// Второй источник (<c>_spawnedItem</c> под якорем) остаётся страховкой: опора
        /// только на <c>CurrentPlacedObject</c> и была находкой NET-13, а с тех пор
        /// сетевая выдача научилась заводить учёт UltimateXR сама
        /// (<c>UxrGrabbableObject.SetNetworkAnchor</c>).
        ///
        /// Оба источника проверяются одинаково: предмет числится в слоте, только пока он
        /// физически висит под якорем. Учёт якоря сам по себе не доказательство — предмет
        /// могли перепарентить мимо <see cref="UxrGrabManager"/>, и тогда слот обязан
        /// считаться пустым, иначе он не пополнится никогда.
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

                UxrGrabbableObject placed = _itemAnchor.CurrentPlacedObject;

                if (placed != null && placed.transform.IsChildOf(_itemAnchor.transform))
                    return placed.gameObject;

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

            ConfigureAnchorCompatibility();

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
                _itemAnchor.SmoothPlaceTransitionEnded += OnSmoothPlaceTransitionEnded;
            }
        }

        protected virtual void OnDisable()
        {
            if (_itemAnchor != null)
            {
                _itemAnchor.Placed  -= OnObjectPlaced;
                _itemAnchor.Removed -= OnObjectRemoved;
                _itemAnchor.SmoothPlaceTransitionEnded -= OnSmoothPlaceTransitionEnded;
            }
        }

        /// <summary>
        /// Учит якорь слота принимать своё оружие обратно.
        ///
        /// <para>
        /// У якорей стены пустой список <c>Compatible Tags</c>, а UltimateXR понимает пустой
        /// список как «только предметы без тега». Всё оружие с тегом, поэтому слот не принимал
        /// ничего: повесить ствол обратно руками было нельзя. На стене оружие держалось только
        /// потому, что выдача кладёт его в якорь напрямую (<c>SetNetworkAnchor</c>), минуя проверку.
        /// </para>
        ///
        /// <para>
        /// Тег берётся у префаба, который слот выдаёт, — так он не разойдётся с префабом.
        /// Одного тега мало: у <c>Gun_real</c> тег <c>M16_Rifle</c>, и пистолет встал бы на слот
        /// винтовки. Поэтому ещё валидатор: только оружие того же <see cref="WeaponInfo"/>
        /// и только на открытой стене.
        /// </para>
        /// </summary>
        private void ConfigureAnchorCompatibility()
        {
            if (_itemAnchor == null || _weaponInfo == null || _weaponInfo.WeaponPrefab == null) return;

            UxrGrabbableObject prefabGrabbable = _weaponInfo.WeaponPrefab.GetComponent<UxrGrabbableObject>();
            if (prefabGrabbable == null) return;

            if (!string.IsNullOrEmpty(prefabGrabbable.Tag))
                _itemAnchor.AddCompatibleTags(prefabGrabbable.Tag);

            _itemAnchor.AddPlacingValidator(AcceptsItem);
        }

        /// <summary>Радиус укладки вокруг точки, где предмет висит на стене.</summary>
        private const float HangPlaceTolerance = 0.1f;

        /// <summary>Поза предмета на стене: смещение из <see cref="WeaponInfo"/> относительно якоря.</summary>
        private void ApplyHangPose(Transform item)
        {
            item.localPosition = _weaponInfo.WeaponPositionOffset;
            item.localRotation = Quaternion.Euler(_weaponInfo.WeaponRotationOffset);
        }

        /// <summary>
        /// Расширяет радиус укладки якоря так, чтобы ствол вставал, когда его подносят туда,
        /// где он висел. UltimateXR меряет расстояние от точки близости предмета до точки
        /// якоря, а на стене предмет висит со смещением: у M16 в висячей позе эти точки
        /// в 19 см друг от друга при радиусе 10 см, и «повесить как висел» было невозможно.
        /// Предмет должен уже стоять в висячей позе.
        /// </summary>
        private void WidenPlaceZoneFor(GameObject item)
        {
            UxrGrabbableObject grabbable = item.GetComponent<UxrGrabbableObject>();
            if (grabbable == null || _itemAnchor == null) return;

            float hangDistance = Vector3.Distance(grabbable.DropProximityTransform.position,
                                                  _itemAnchor.DropProximityTransform.position);

            _itemAnchor.MaxPlaceDistance = Mathf.Max(_itemAnchor.MaxPlaceDistance, hangDistance + HangPlaceTolerance);
        }

        /// <summary>Принимает ли слот этот предмет, если поднести его к якорю.</summary>
        public bool AcceptsItem(UxrGrabbableObject item)
        {
            if (IsLocked || item == null) return false;

            WeaponComponent weapon = item.GetComponent<WeaponComponent>();
            return weapon != null && weapon.WeaponData == _weaponInfo;
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
            ApplyHangPose(_spawnedItem.transform);
            WidenPlaceZoneFor(_spawnedItem);

            _spawnedItem.name = _weaponInfo.WeaponId + "_instance";

            // Учёт «предмет лежит в этом якоре» ведёт UltimateXR, и сетевая выдача обязана
            // его завести: без CurrentAnchor менеджер захвата при уходе предмета не поднимает
            // у якоря событие Removed, и слот не узнаёт, что оружие унесли (NET-17).
            // Тихо — потому что выдача не действие игрока: положение предмета приехало
            // спавн-сообщением Mirror, а каждая машина приходит к одному и тому же учёту сама.
            UxrGrabbableObject grabbable = _spawnedItem.GetComponent<UxrGrabbableObject>();
            if (grabbable != null) grabbable.SetNetworkAnchor(_itemAnchor);

            var weaponComp = _spawnedItem.GetComponent<WeaponComponent>();
            if (weaponComp == null) weaponComp = _spawnedItem.AddComponent<WeaponComponent>();
            weaponComp.Init(_weaponInfo);
            weaponComp.SetHomeIfUnset(this);

            // На стене предмет висит, а не лежит. Выдача свежего предмета сюда приходит
            // уже кинематической, а возврат домой — с пола, с живой физикой: без этого
            // ствол соскользнул бы со стены. SetNetworkAnchor физику не трогает.
            Rigidbody body = _spawnedItem.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

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
                // Снимаем учёт до уничтожения: иначе якорь остаётся с ссылкой
                // на удалённый предмет и считает себя занятым.
                UxrGrabbableObject grabbable = _spawnedItem.GetComponent<UxrGrabbableObject>();
                if (grabbable != null) grabbable.SetNetworkAnchor(null);

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
        ///
        /// <para>
        /// Касается всех <see cref="UxrGrabbableObject"/> в иерархии предмета: затвор,
        /// цевьё и вставленный магазин — отдельные захватываемые объекты, и при блокировке
        /// одного корня их можно было хватать на закрытой стене.
        /// </para>
        ///
        /// <para>
        /// Через <c>IsGrabbable</c>, а не выключением компонента — по той же причине, что
        /// у жетона (<see cref="DogTagController.Disable"/>): выключенный компонент стирает
        /// из <see cref="UxrGrabManager"/> запись о текущем захвате, и затвор, который игрок
        /// держал в момент закрытия стены, ломал отпускание.
        /// </para>
        /// </summary>
        private void SetItemGrabbable(bool grabbable)
        {
            GameObject item = CurrentItem;
            if (item == null) return;

            foreach (UxrGrabbableObject grab in item.GetComponentsInChildren<UxrGrabbableObject>(true))
                grab.IsGrabbable = grabbable;
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

            // Игрок повесил ствол руками. UltimateXR ставит его по своей точке выравнивания,
            // а не со смещением слота, — возвращаем позу свежего предмета. При плавной
            // укладке переход ещё идёт; поза повторится в OnSmoothPlaceTransitionEnded.
            if (e.GrabbableObject != null)
            {
                _spawnedItem = e.GrabbableObject.gameObject;
                ApplyHangPose(_spawnedItem.transform);
            }

            GameLog.Arsenal.Info($"[Arsenal] Item returned to slot '{DisplayName}'.");
            SetLightColor(_availableColor);
            OnItemReturned?.Invoke(this);
        }

        private void OnSmoothPlaceTransitionEnded(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableObject != null && e.GrabbableObject.CurrentAnchor == _itemAnchor)
                ApplyHangPose(e.GrabbableObject.transform);
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

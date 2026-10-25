using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Network;

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
        [Tooltip("Физическая роль слота: верхняя панель или нижняя полка. Не зависит от выбранного оружия.")]
        [SerializeField] private ArsenalPresentationZone _presentationZone;
        public ArsenalPresentationZone PresentationZone => _presentationZone;
        [Tooltip("Ширина слота вдоль ряда, м. Ряд корпуса (ArsenalSlotRow) раскладывает слоты по ней и своему зазору.")]
        [SerializeField] private float _slotWidth = 0.4f;
        public float SlotWidth => _slotWidth;
        [Tooltip("Раскладка слота по умолчанию: как лежит оружие без своей раскладки для этого вида слота.")]
        [SerializeField] private ArsenalSlotLayout _defaultLayout;
        public ArsenalSlotLayout DefaultLayout => _defaultLayout;
        private bool _placingValidatorAdded;
        private string _configuredAnchorTag;

        public void ConfigurePresentationZone(ArsenalPresentationZone zone) => _presentationZone = zone;

        /// <summary>Настройка до выдачи оружия; обновляет совместимость якоря и карточку.</summary>
        public void ConfigureWeapon(WeaponInfo weapon)
        {
            if (_itemAnchor == null) _itemAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            var original = _weaponInfo != null && _weaponInfo.WeaponPrefab != null
                ? _weaponInfo.WeaponPrefab.GetComponent<UxrGrabbableObject>() : null;
            if (_itemAnchor != null && !string.IsNullOrEmpty(_configuredAnchorTag))
                _itemAnchor.RemoveCompatibleTags(_configuredAnchorTag);
            if (_itemAnchor != null && original != null && !string.IsNullOrEmpty(original.Tag))
                _itemAnchor.RemoveCompatibleTags(original.Tag);
            _configuredAnchorTag = null;
            _weaponInfo = weapon;
            ConfigureAnchorCompatibility();
            if (_priceTag != null) ArsenalPriceTag.Create(this).Show(_weaponInfo, true);
        }

        [Header("Anchor")]
        [Tooltip("Snap zone for the item (auto-found if empty)")]
        [SerializeField] private UxrGrabbableObjectAnchor _itemAnchor;

        [Header("Visual Feedback")]
        [Tooltip("Боковая LED-лента — индикатор (arsenal-slot-lighting); realtime-лампы у слота нет.")]
        [SerializeField] private ArsenalSlotGlow _glow;
        [SerializeField] private Color _availableColor  = new Color(1f, 0.6f, 0.25f); // тёплый янтарный
        [Tooltip("Цвет «недоступно»: лента гаснет.")]
        [SerializeField] private Color _unavailableColor = Color.black;
        [SerializeField] private Color _takenColor       = Color.green;
        /// <summary>Лента слота и её цвет «доступно» — для редакторского превью станций (вид как у собранного слота).</summary>
        public ArsenalSlotGlow Glow => _glow;
        public Color AvailableColor => _availableColor;

        // ── Events ─────────────────────────────────────────────
        public System.Action<ArsenalSlotController> OnItemTaken;
        public System.Action<ArsenalSlotController> OnItemReturned;

        /// <summary>Предмет унесли со слота: что именно и какой рукой (рука может быть null). Для серверного списания.</summary>
        public event System.Action<ArsenalSlotController, UxrGrabbableObject, UxrGrabber> ItemTakenBy;

        /// <summary>Предмет повесили на слот руками. Для возврата денег.</summary>
        public event System.Action<ArsenalSlotController, UxrGrabbableObject> ItemReturnedBy;

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

        /// <summary>Стена, которой принадлежит слот (может быть null у слота вне стены).</summary>
        public ArsenalWallController Wall
        {
            get
            {
                if (_wall == null) _wall = GetComponentInParent<ArsenalWallController>();
                return _wall;
            }
        }

        /// <summary>Что слот предлагает сейчас (T-45): бесплатно, по карману, дорого, нет владельца.</summary>
        public SlotOffer Offer => _offer;

        /// <summary>Можно ли взять предмет: стена открыта и предложение позволяет.</summary>
        public bool AllowsGrabNow => !IsLocked && ArsenalPurchaseRules.AllowsGrab(_offer);

        // ── State ──────────────────────────────────────────────
        protected bool IsLocked { get; private set; }
        private GameObject _spawnedItem;
        private ArsenalWallController _wall;
        private SlotOffer _offer = SlotOffer.Free;

        /// <summary>Слот погашен: его предметы берут пробы в точке без подсветки (<see cref="RefreshDimming"/>).</summary>
        private bool _dimmed;
        private readonly Dictionary<Renderer, Transform> _dimmedProbeAnchors = new Dictionary<Renderer, Transform>();
        private Transform _dimProbeAnchor;

        private ArsenalPriceTag _priceTag;

        // ── Unity ──────────────────────────────────────────────

        protected virtual void Awake()
        {
            if (_itemAnchor == null)
                _itemAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>();

            ConfigureAnchorCompatibility();
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

            if (!string.IsNullOrEmpty(prefabGrabbable.Tag) && _configuredAnchorTag != prefabGrabbable.Tag)
            {
                _itemAnchor.AddCompatibleTags(prefabGrabbable.Tag);
                _configuredAnchorTag = prefabGrabbable.Tag;
            }

            if (!_placingValidatorAdded)
            {
                _itemAnchor.AddPlacingValidator(AcceptsItem);
                _placingValidatorAdded = true;
            }
        }

        /// <summary>Радиус укладки вокруг точки, где предмет висит на стене.</summary>
        private const float HangPlaceTolerance = 0.1f;

        /// <summary>Поза предмета на стене: смещение из <see cref="WeaponInfo"/> относительно якоря.</summary>
        private void ApplyHangPose(Transform item)
        {
            ArsenalPresentationApplicator.ApplyWeapon(this, item);
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
        /// Отдельными запасными магазинами владеет ArsenalMagazineSupply.
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

            // Строки на слот нет: выдачу сводит стена — одна строка на пополнение (сервер) или на
            // проход привязок (клиент), см. ArsenalWallController.ReplenishSlotsWhere / ResolvePendingSlotBindings.

            // Оружие приезжает позже, чем стена успевает заблокировать слоты: закрытая
            // стена блокирует их в Start(), а пополнение идёт из OnStartServer и из фазы
            // Setup. Без этой строки предмет появлялся хватаемым в закрытом арсенале.
            // С T-45 — и в арсенале, где на него не хватает денег. Свежую выдачу (ещё не
            // заспавненную) запись пропускает: её захват выставляет ApplyGrabAvailability после спавна.
            SetItemGrabbable(AllowsGrabNow);

            ApplyOfferVisuals();
        }

        /// <summary>
        /// Removes spawned item from the slot.
        /// Запасные магазины обслуживаются независимо от оружия.
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

            SetGlowState(false);
        }

        /// <summary>
        /// Locks the slot — disables grabbing from the anchor.
        /// </summary>
        public void Lock()
        {
            IsLocked = true;

            SetItemGrabbable(false);

            SetGlowState(false);
            SetDimmed(IsItemPresent);
            // Строку на слот не пишем: блокировку всей стены сводит ArsenalWallController — одна строка на фазу.
        }

        /// <summary>
        /// Unlocks the slot — enables grabbing.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;

            SetItemGrabbable(AllowsGrabNow);

            ApplyOfferVisuals();
            // Строку на слот не пишем: открытие всей стены сводит ArsenalWallController — одна строка на фазу.
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

            // IsGrabbable синхронизируемый, а переход фазы исполняется на каждой машине: пишет только
            // сервер, клиенты получают значение событием. Иначе каждый клиент рассылал свою запись
            // на каждый предмет (known-issues, Issue 23).
            if (!StateEventAuthority.IsWorldAuthority) return;

            // Сервер ещё не заспавнил предмет (выдача привязывает его к слоту до Spawn): UniqueId
            // не выровнены по netId, и событие ушло бы с id префаба, которого нет ни у одного
            // клиента (UxrComponentNotFoundException «IsGrabbable = False»), а сервер остался бы
            // с другим значением, чем клиенты. Доступность такому предмету выставляет выдача
            // после спавна — ApplyGrabAvailability, как у магазинов (ArsenalMagazineSupply).
            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            if (identity != null && identity.netId == 0 && NetworkServer.active) return;

            foreach (UxrGrabbableObject grab in item.GetComponentsInChildren<UxrGrabbableObject>(true))
                grab.IsGrabbable = grabbable;
        }

        /// <summary>
        /// Выставляет захват предмета в слоте по текущему правилу слота. Зовёт серверная выдача
        /// сразу после <c>NetworkUxrIdentity.SpawnServerObject</c>: только тогда синхронизируемая
        /// запись адресуется выровненными UniqueId и доходит до клиентов.
        /// </summary>
        public void ApplyGrabAvailability()
        {
            SetItemGrabbable(AllowsGrabNow);
        }

        /// <summary>
        /// Предложение слота по деньгам владельца стены (T-45). Зовёт стена на каждой машине при
        /// изменении; повтор того же значения — пустой. Захват (<c>IsGrabbable</c>) пишет только
        /// автор мира — сервер (Issue 23), представление (свет, приглушение, ценник) — каждая машина.
        /// </summary>
        public void ApplyOffer(SlotOffer offer)
        {
            if (_offer == offer) return;

            _offer = offer;
            SetItemGrabbable(AllowsGrabNow);
            ApplyOfferVisuals();
        }

        /// <summary>
        /// Подсветка по предложению: по карману (и бесплатно) — лента слота горит, ствол обычный;
        /// дорого, стена ничья или слот закрыт — лента погашена, у ствола и магазина нет подсветки (<see cref="RefreshDimming"/>).
        /// Карточка сохраняет характеристики и в разминке.
        /// </summary>
        private void ApplyOfferVisuals()
        {
            bool grabbable = ArsenalPurchaseRules.AllowsGrab(_offer);

            if (!IsLocked)
            {
                if (!IsItemPresent) SetGlowColor(_takenColor);
                else SetGlowColor(grabbable ? _availableColor : _unavailableColor);
            }

            SetDimmed(IsItemPresent && (IsLocked || !grabbable));

            if (_weaponInfo != null && _itemAnchor != null && Application.isPlaying)
            {
                if (_priceTag == null) _priceTag = ArsenalPriceTag.Create(this);
                _priceTag.Show(_weaponInfo, grabbable, _offer == SlotOffer.Free);
            }
        }

        private void SetDimmed(bool dimmed)
        {
            _dimmed = dimmed;
            RefreshDimming();
        }

        /// <summary>
        /// Гасит свет недоступного слота — предметов <see cref="DimmedParts"/>, погашенное раньше возвращает. Свет на оружии
        /// станции запечён: лампы слота — в пробах света у слота, блики — в пробе отражений станции (arsenal-slot-lighting).
        /// Погашенный предмет берёт пробы света и отражений в точке <see cref="DimProbeDistance"/> перед слотом — вне ламп
        /// и пробы станции, только слабый общий свет: оружие видно, но без подсветки (решение пользователя 2026-10-23;
        /// окраска основы делала металл чёрным). Цвет материала не меняется. Зовут слот и выдача магазина
        /// (<see cref="ArsenalMagazineOffer"/>), когда предмет пришёл в якорь или ушёл из него.
        /// </summary>
        public void RefreshDimming()
        {
            foreach (KeyValuePair<Renderer, Transform> anchor in _dimmedProbeAnchors)
                if (anchor.Key != null) anchor.Key.probeAnchor = anchor.Value;
            _dimmedProbeAnchors.Clear();
            if (!_dimmed) return;

            Transform dimAnchor = DimProbeAnchor();
            foreach (GameObject part in DimmedParts())
            {
                if (part == null) continue;
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                {
                    if (_dimmedProbeAnchors.ContainsKey(r)) continue;
                    _dimmedProbeAnchors[r] = r.probeAnchor;
                    r.probeAnchor = dimAnchor;
                }
            }
        }

        /// <summary>Предметы слота, которые гаснут вместе с ним: базовый слот — ствол.</summary>
        protected virtual IEnumerable<GameObject> DimmedParts()
        {
            if (CurrentItem != null) yield return CurrentItem;
        }

        /// <summary>Точка проб погашенного слота: <see cref="DimProbeDistance"/> перед ним (−Z слота — к игроку).</summary>
        private Transform DimProbeAnchor()
        {
            if (_dimProbeAnchor != null) return _dimProbeAnchor;
            _dimProbeAnchor = new GameObject("DimProbeAnchor").transform;
            _dimProbeAnchor.SetParent(transform, false);
            _dimProbeAnchor.localPosition = Vector3.back * DimProbeDistance + Vector3.up * .5f;
            return _dimProbeAnchor;
        }

        private const float DimProbeDistance = 2f;

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
                // Якорь уже стоит в позе раскладки — цель SDK: не прерываем его плавную укладку.
                if (!e.GrabbableObject.IsInSmoothTransition)
                    ApplyHangPose(_spawnedItem.transform);
            }

            GameLog.Arsenal.Info($"[Arsenal] Item returned to slot '{DisplayName}'.");
            ApplyOfferVisuals();
            OnItemReturned?.Invoke(this);
            ItemReturnedBy?.Invoke(this, e.GrabbableObject);
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
            SetDimmed(false);
            SetGlowColor(_takenColor);
            OnItemTaken?.Invoke(this);
            ItemTakenBy?.Invoke(this, e.GrabbableObject, e.Grabber);
        }

        // ── Protected: Glow Helpers ────────────────────────────

        protected void SetGlowState(bool on)
        {
            if (_glow == null) return;
            if (on) _glow.Show(_availableColor);
            else _glow.Hide();
        }

        protected void SetGlowColor(Color color)
        {
            if (_glow == null) return;
            if (color == _unavailableColor) _glow.Hide();
            else _glow.Show(color);
        }
    }
}

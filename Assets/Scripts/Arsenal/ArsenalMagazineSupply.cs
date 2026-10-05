using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Серверный склад магазинов: постоянные индексы оружейных слотов, состояние для позднего клиента, бесплатный допуск стены.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ArsenalWallController))]
    public sealed class ArsenalMagazineSupply : NetworkBehaviour
    {
        private readonly SyncDictionary<int, ArsenalMagazineStockBinding> _items = new SyncDictionary<int, ArsenalMagazineStockBinding>();
        private readonly Dictionary<int, NetworkIdentity> _lastIssued = new Dictionary<int, NetworkIdentity>();
        private readonly Dictionary<int, float> _lostTime = new Dictionary<int, float>();
        private readonly HashSet<int> _pending = new HashSet<int>();
        private readonly List<int> _resolved = new List<int>();
        private readonly List<(ArsenalMagazineOffer offer, UxrGrabbableObject item)> _rejected =
            new List<(ArsenalMagazineOffer, UxrGrabbableObject)>();
        private ArsenalWallController _wall;
        private GameMode _mode;
        private bool _initialRefillDone;
        private bool _refillRequested;
        private bool _serverSubscribed;
        private bool _clientSubscribed;
        private readonly List<ArsenalMagazineOffer> _subscribedOffers = new List<ArsenalMagazineOffer>();
        public IReadOnlyDictionary<int, ArsenalMagazineStockBinding> Stock => _items;

        private bool Prepare()
        {
            if (_wall == null) _wall = GetComponent<ArsenalWallController>();
            var binding = GetComponent<ArsenalStationPresetBinding>();
            return _wall != null && (binding == null || binding.TryPrepareFromScene());
        }
        private void OnEnable()
        {
            if (!Application.isPlaying || netIdentity == null) return;
            if (isServer) SubscribeServer();
            if (isClient && !isServer) SubscribeClient();
        }
        private void OnDisable() { UnsubscribeServer(); UnsubscribeClient(); }
        public override void OnStartServer() { base.OnStartServer(); SubscribeServer(); }
        public override void OnStopServer() { UnsubscribeServer(); base.OnStopServer(); }
        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!isServer) SubscribeClient();
        }
        public override void OnStopClient() { UnsubscribeClient(); base.OnStopClient(); }

        private void SubscribeServer()
        {
            if (_serverSubscribed) return;
            _serverSubscribed = true;
            MapReferee.ActiveGameModeChangedLocal += HandleModeChanged;
            HandleModeChanged(MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null);
            SubscribeOffers();
        }
        private void SubscribeOffers()
        {
            if (_wall == null) _wall = GetComponent<ArsenalWallController>();
            foreach (var slot in _wall.Slots)
            {
                var offer = slot != null ? slot.GetComponent<ArsenalMagazineOffer>() : null;
                if (offer == null || _subscribedOffers.Contains(offer)) continue;
                offer.Taken += HandleTaken;
                _subscribedOffers.Add(offer);
            }
        }
        private void UnsubscribeServer()
        {
            if (!_serverSubscribed) return;
            MapReferee.ActiveGameModeChangedLocal -= HandleModeChanged;
            if (_mode != null) _mode.ArsenalRefillRequestedServer -= RequestRefill;
            _mode = null;
            foreach (var offer in _subscribedOffers) if (offer != null) offer.Taken -= HandleTaken;
            _subscribedOffers.Clear();
            _serverSubscribed = false;
        }
        private void HandleModeChanged(GameMode mode)
        {
            if (_mode == mode) return;
            if (_mode != null) _mode.ArsenalRefillRequestedServer -= RequestRefill;
            _mode = mode;
            if (_mode != null) _mode.ArsenalRefillRequestedServer += RequestRefill;
        }
        private void RequestRefill() { if (isServer) _refillRequested = true; }

        private void SubscribeClient()
        {
            if (_clientSubscribed) return;
            _items.OnChange += HandleStockChanged;
            _clientSubscribed = true;
            foreach (var pair in _items) _pending.Add(pair.Key);
        }
        private void UnsubscribeClient()
        {
            if (_clientSubscribed) _items.OnChange -= HandleStockChanged;
            _clientSubscribed = false;
            _pending.Clear();
        }
        private void HandleStockChanged(SyncIDictionary<int, ArsenalMagazineStockBinding>.Operation op, int index, ArsenalMagazineStockBinding oldValue)
        {
            if (op == SyncIDictionary<int, ArsenalMagazineStockBinding>.Operation.OP_ADD || op == SyncIDictionary<int, ArsenalMagazineStockBinding>.Operation.OP_SET)
                _pending.Add(index);
            else if (op == SyncIDictionary<int, ArsenalMagazineStockBinding>.Operation.OP_CLEAR) _pending.Clear();
            else _pending.Remove(index);
        }

        private void Update()
        {
            if (!Prepare()) return;
            if (isServer)
            {
                SubscribeOffers();
                FlushRejected();
                if (!_initialRefillDone || _refillRequested)
                {
                    _initialRefillDone = true; _refillRequested = false;
                    ServerRefill();
                }
                MaintainLostStock(Time.deltaTime);
                foreach (var offer in _subscribedOffers) if (offer != null) offer.RefreshAvailability();
            }
            if (isClient && !isServer) ResolvePending();
        }

        /// <summary>Штатное пополнение заполняет только пустые якоря. Ранее выданные/взятые предметы не затрагиваются.</summary>
        [Server]
        public void ServerRefill()
        {
            if (!Prepare()) { _refillRequested = true; return; }
            for (int i = 0; i < _wall.Slots.Count; i++)
            {
                var slot = _wall.Slots[i];
                var offer = slot != null ? slot.GetComponent<ArsenalMagazineOffer>() : null;
                if (slot != null && slot.gameObject.activeInHierarchy && slot.WeaponData != null &&
                    offer != null && offer.Anchor != null && offer.CurrentItem == null)
                    SpawnStock(i, offer);
            }
        }

        private void SpawnStock(int index, ArsenalMagazineOffer offer)
        {
            if (!isServer || !StateEventAuthority.IsWorldAuthority) return;
            GameObject prefab = offer.Slot.WeaponData.MagazinePrefab;
            if (prefab == null || prefab.GetComponent<NetworkIdentity>() == null || prefab.GetComponent<UxrGrabbableObject>() == null ||
                prefab.GetComponent<MagazineManipulationHistory>() == null)
            {
                GameLog.Arsenal.Error("[ArsenalMagazineSupply] Не настроен сетевой захватываемый магазин: " + offer.Slot.name, this);
                return;
            }
            GameObject item = NetworkUxrIdentity.CreateInstance(prefab);
            item.SetActive(true);
            offer.Assign(item.GetComponent<UxrGrabbableObject>());
            NetworkUxrIdentity.SpawnServerObject(item);
            offer.RefreshAvailability();
            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            var binding = new ArsenalMagazineStockBinding(identity.netId);
            item.GetComponent<MagazineManipulationHistory>().AcceptBinding(binding);
            _items[index] = binding;
            _lastIssued[index] = identity;
            _lostTime.Remove(index);
        }

        private void MaintainLostStock(float deltaTime)
        {
            for (int i = 0; i < _wall.Slots.Count; i++)
            {
                var slot = _wall.Slots[i];
                var offer = slot != null ? slot.GetComponent<ArsenalMagazineOffer>() : null;
                bool lost = offer != null && offer.CurrentItem == null &&
                    (!_lastIssued.TryGetValue(i, out NetworkIdentity issued) || issued == null);
                if (!lost) { _lostTime.Remove(i); continue; }
                _items.Remove(i);
                if (_mode == null || !_mode.ArsenalRules.ReplacesLostWeapons || !slot.gameObject.activeInHierarchy || slot.WeaponData == null)
                { _lostTime.Remove(i); continue; }
                _lostTime.TryGetValue(i, out float elapsed);
                elapsed += deltaTime;
                _lostTime[i] = elapsed;
                if (elapsed >= _wall.LostItemReplaceDelay) SpawnStock(i, offer);
            }
        }

        private void HandleTaken(ArsenalMagazineOffer offer, UxrManipulationEventArgs e)
        {
            if (!isServer || !StateEventAuthority.IsWorldAuthority || e.GrabbableObject == null) return;
            int index = IndexOf(offer.Slot);
            var identity = e.GrabbableObject.GetComponent<NetworkIdentity>();
            if (index < 0 || identity == null || !_items.TryGetValue(index, out ArsenalMagazineStockBinding current) || current.NetId != identity.netId) return;
            if (!offer.AllowsGrab(e.Grabber)) { _rejected.Add((offer, e.GrabbableObject)); return; }
            _items.Remove(index);
        }
        private void FlushRejected()
        {
            if (!StateEventAuthority.IsWorldAuthority) return;
            foreach (var entry in _rejected)
            {
                if (entry.offer == null || entry.item == null) continue;
                if (UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(entry.item))
                    UxrGrabManager.Instance.ReleaseGrabs(entry.item, true);
                entry.offer.Assign(entry.item);
                entry.offer.RefreshAvailability();
                var identity = entry.item.GetComponent<NetworkIdentity>();
                int index = IndexOf(entry.offer.Slot);
                if (index >= 0 && identity != null)
                {
                    var history = entry.item.GetComponent<MagazineManipulationHistory>();
                    _items.TryGetValue(index, out ArsenalMagazineStockBinding previous);
                    var binding = new ArsenalMagazineStockBinding(identity.netId, previous.ReturnRevision + 1,
                        history != null ? history.Sequence : 0);
                    if (history != null) history.AcceptBinding(binding);
                    _items[index] = binding;
                }
            }
            _rejected.Clear();
        }
        private int IndexOf(ArsenalSlotController slot)
        {
            for (int i = 0; i < _wall.Slots.Count; i++) if (_wall.Slots[i] == slot) return i;
            return -1;
        }
        private void ResolvePending()
        {
            _resolved.Clear();
            foreach (int index in _pending)
            {
                if (!_items.TryGetValue(index, out ArsenalMagazineStockBinding binding)) { _resolved.Add(index); continue; }
                // Неполный/другой пресет не превращается в молчаливое усечение индексов.
                if (index < 0 || index >= _wall.Slots.Count) continue;
                if (!NetworkClient.spawned.TryGetValue(binding.NetId, out NetworkIdentity identity) || identity == null || !identity.gameObject.activeInHierarchy) continue;
                var slot = _wall.Slots[index];
                var offer = slot != null ? slot.GetComponent<ArsenalMagazineOffer>() : null;
                var grab = identity.GetComponent<UxrGrabbableObject>();
                if (offer == null || grab == null || !slot.gameObject.activeInHierarchy) continue;
                // Уже пришедший захват не отменяется запоздалой привязкой склада.
                if (UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(grab)) { _resolved.Add(index); continue; }
                var history = grab.GetComponent<MagazineManipulationHistory>();
                if (history == null || !history.AllowsBinding(binding)) { _resolved.Add(index); continue; }
                if (offer.CurrentItem != grab) offer.Assign(grab);
                history.AcceptBinding(binding);
                _resolved.Add(index);
            }
            foreach (int index in _resolved) _pending.Remove(index);
        }
    }
}

using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Core;
using UltimateXR.Core.StateSave;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Приёмная политика. Единственный SDK commit ведёт сервер, клиент не прибавляет ammo.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrFirearmWeapon))]
    public sealed class CartridgeIntake : NetworkBehaviour
    {
        [SerializeField] private int _triggerIndex;
        [SerializeField] private string _ammoType;
        [SerializeField] private UxrGrabbableObjectAnchor _intake;
        // Серверное удаление принятой гильзы после публикации фиксации. Commit Rpc и ObjectDestroy идут
        // по одному reliable-каналу Mirror, поэтому клиент сначала видит consumed, затем despawn.
        // Без удаления _retained упирается в RetainedUnitLimit, и оружие перестаёт заряжаться.
        [SerializeField] private bool _allowOrderedRetirement = true;
        private const int RetainedUnitLimit = 32;
        private UxrFirearmWeapon _weapon;
        private UxrGrabbableObjectAnchor _internalAnchor;
        private ulong _sequence, _localToken, _cancelledToken;
        private uint _localUnitNetId;
        private UxrFirearmAmmoUnit _localUnit;
        private float _requestDeadline;
        private bool _processing, _needsAllPeerResync;
        private ServerRequest _serverRequest;
        private readonly List<Retirement> _retirements = new List<Retirement>();
        private readonly List<UxrFirearmAmmoUnit> _retained = new List<UxrFirearmAmmoUnit>();
        public UxrGrabbableObjectAnchor Intake => _intake;
        public string AmmoType => _ammoType;

        private sealed class ServerRequest
        { public ulong Token; public uint Revision; public Guid Identity; public Cartridge Cartridge; public NetworkConnectionToClient Sender; public bool Cancelled; }
        private sealed class Retirement
        { public UxrFirearmAmmoUnit Unit; public Guid Identity; public UxrGrabbableObject Grab; public int EarliestFrame; public bool Published; }

        private void Awake() { _weapon = GetComponent<UxrFirearmWeapon>(); }
        private void OnEnable()
        {
            if (_weapon == null) _weapon = GetComponent<UxrFirearmWeapon>();
            if ((_weapon.CanAuthorAmmoAdmission != null && _weapon.CanAuthorAmmoAdmission.Target != this) ||
                (_weapon.ValidateAmmoUnitAdmission != null && _weapon.ValidateAmmoUnitAdmission.Target != this)) return;
            _weapon.CanAuthorAmmoAdmission = CanAuthorAdmission;
            _weapon.ValidateAmmoUnitAdmission = ValidateAdmission;
            _weapon.AmmoAdmissionFaulted += HandleAdmissionFault;
            if (_weapon.TryGetTriggerMagazineAnchor(_triggerIndex, out _internalAnchor) && _internalAnchor != null)
                _internalAnchor.AddPlacingValidator(ValidateFixedPlacement);
            if (_intake != null) _intake.AddPlacingValidator(CanPreviewPlacement);
            UxrStateSaveImplementer.StateSerialized += HandleStateSerialized;
        }
        private void OnDisable()
        {
            CancelLocalRequest();
            if (_intake != null) _intake.RemovePlacingValidator(CanPreviewPlacement);
            if (_internalAnchor != null) _internalAnchor.RemovePlacingValidator(ValidateFixedPlacement);
            if (_weapon != null) _weapon.AmmoAdmissionFaulted -= HandleAdmissionFault;
            UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized;
            if (_weapon != null && _weapon.CanAuthorAmmoAdmission?.Target == this) _weapon.CanAuthorAmmoAdmission = null;
            if (_weapon != null && _weapon.ValidateAmmoUnitAdmission?.Target == this) _weapon.ValidateAmmoUnitAdmission = null;
            if (_serverRequest != null) _serverRequest.Cancelled = true;
        }
        private bool CanAuthorAdmission(int trigger) => trigger == _triggerIndex && _processing &&
            StateEventAuthority.IsWorldAuthority && _serverRequest != null && !_serverRequest.Cancelled && ValidateRequest(_serverRequest);
        private bool ValidateFixedPlacement(UxrGrabbableObject item) => item != null &&
            item.TryGetComponent<UxrFirearmMag>(out var store) && store.IsFixedAmmoStore &&
            store.FixedStoreWeapon == _weapon && store.FixedStoreTrigger == _triggerIndex;
        private void HandleAdmissionFault(int trigger, ulong token, Exception failure)
        {
            if (trigger != _triggerIndex) return;
            // Receiving callback fault затрагивает именно эту копию; publication fault server лечит всех peers.
            if (NetworkClient.active && !NetworkServer.active) NetworkStateRelay.Instance?.RequestAmmoAdmissionResynchronization();
        }
        public bool IsValidatedAdmissionPublication(ulong token) => NetworkServer.active && _processing &&
            _serverRequest != null && _serverRequest.Token == token && !_serverRequest.Cancelled && ValidateRequest(_serverRequest);
        private bool ValidateAdmission(int trigger, UxrFirearmAmmoUnit unit) => trigger == _triggerIndex &&
            _serverRequest != null && _serverRequest.Cartridge != null && _serverRequest.Cartridge.Unit == unit && ValidateRequest(_serverRequest);
        private bool CanPreviewPlacement(UxrGrabbableObject item)
        {
            var cartridge = item != null ? item.GetComponent<Cartridge>() : null;
            var store = _weapon != null ? _weapon.GetFixedAmmoStore(_triggerIndex) : null;
            if (cartridge == null || !cartridge.IsAvailable || cartridge.AmmoType != _ammoType || store == null) return false;
            // Совместимость уже placed предмета и receiving projection не создаёт admission.
            if (item.CurrentAnchor == _intake || UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) return true;
            return _localToken == 0 && HasLocalHolder() && TryGetMainHand(out var mainHand) &&
                cartridge.LastHandlingAvatar == mainHand.Avatar && StateEventAuthority.IsAuthorOfItem(cartridge) &&
                _weapon.GetTotalAmmoLeft(_triggerIndex) < store.Capacity;
        }
        private bool HasLocalHolder()
        {
            if (!StateEventAuthority.IsAuthorOfItem(_weapon) || !TryGetMainHand(out var hand)) return false;
            if (!NetworkServer.active && !NetworkClient.active) return true;
            var identity = hand.Avatar.GetComponent<NetworkIdentity>();
            return identity != null && identity.netId != 0 && identity.isOwned;
        }
        private bool TryGetMainHand(out UxrGrabber hand)
        {
            hand = null;
            return UxrGrabManager.HasInstance && _weapon.TryGetTriggerGrip(_triggerIndex, out var grip, out int point) &&
                UxrGrabManager.Instance.GetGrabbingHand(grip, point, out hand) && hand != null && hand.Avatar != null;
        }
        private bool SenderOwnsMainHand(NetworkConnectionToClient sender)
        {
            if (!TryGetMainHand(out var hand)) return false;
            if (!NetworkServer.active && !NetworkClient.active) return sender == null && StateEventAuthority.IsAuthorOfItem(_weapon);
            var holder = hand.Avatar.GetComponent<NetworkIdentity>();
            return sender != null && sender.identity != null && holder != null && holder.netId != 0 && holder.connectionToClient == sender;
        }
        private bool ValidateRequest(ServerRequest request)
        {
            var cartridge = request.Cartridge; var unit = cartridge != null ? cartridge.Unit : null;
            return isActiveAndEnabled && !_needsAllPeerResync && request.Token != 0 && !request.Cancelled &&
                SenderOwnsMainHand(request.Sender) && cartridge != null && cartridge.IsAvailable && cartridge.AmmoType == _ammoType &&
                TryGetMainHand(out var mainHand) && cartridge.LastHandlingAvatar == mainHand.Avatar && cartridge.LastPlacedAnchor == _intake &&
                unit != null && unit.UniqueId == request.Identity && !unit.HasBeenConsumed &&
                _intake != null && _intake.isActiveAndEnabled && _intake.CurrentPlacedObject == cartridge.Grabbable &&
                cartridge.Grabbable.CurrentAnchor == _intake && _intake.IsCompatibleObject(cartridge.Grabbable) &&
                _weapon.GetReadinessState(_triggerIndex)?.Revision == request.Revision;
        }
        private void Update()
        {
            if (_weapon == null || _intake == null || !_weapon.UsesReadinessLedger(_triggerIndex)) return;
            if (StateEventAuthority.IsWorldAuthority)
            {
                CompleteRetirements();
                if (_needsAllPeerResync && NetworkStateRelay.Instance != null && NetworkStateRelay.Instance.ServerResynchronizeAmmoAdmissionPeers())
                {
                    _needsAllPeerResync = false;
                    _weapon.TryAcknowledgeFixedAmmoResynchronization(_triggerIndex, _weapon.GetReadinessState(_triggerIndex).Revision);
                }
                if (_serverRequest != null) ProcessServerRequest();
            }
            if (_localToken != 0)
            {
                if (!HasLocalHolder() || Time.unscaledTime > _requestDeadline || _localUnit == null ||
                    (!_localUnit.HasBeenConsumed && _intake.CurrentPlacedObject != _localUnit.GetComponent<UxrGrabbableObject>())) CancelLocalRequest();
                return;
            }
            if (!HasLocalHolder()) return;
            var cartridge = _intake.CurrentPlacedObject != null ? _intake.CurrentPlacedObject.GetComponent<Cartridge>() : null;
            var store = _weapon.GetFixedAmmoStore(_triggerIndex);
            if (cartridge == null || !cartridge.IsAvailable || cartridge.AmmoType != _ammoType ||
                !TryGetMainHand(out var holder) || cartridge.LastHandlingAvatar != holder.Avatar || cartridge.LastPlacedAnchor != _intake ||
                store == null || _weapon.GetTotalAmmoLeft(_triggerIndex) >= store.Capacity ||
                _sequence == ulong.MaxValue || !StateEventAuthority.IsAuthorOfItem(cartridge)) return;
            ulong token = ++_sequence;
            if (!_weapon.TryBeginAmmoAdmissionBarrier(_triggerIndex, token)) return;
            _localToken = token; _localUnit = cartridge.Unit; _requestDeadline = Time.unscaledTime + 3f;
            var identity = cartridge.GetComponent<NetworkIdentity>();
            if (NetworkClient.active)
            {
                if (!NetworkClient.ready || identity == null || identity.netId == 0) { CancelLocalRequest(); return; }
                _localUnitNetId = identity.netId;
                CmdRequestCartridgeAdmission(token, _weapon.GetReadinessState(_triggerIndex).Revision, identity.netId, cartridge.Unit.UniqueId);
            }
            else if (!NetworkServer.active)
                _serverRequest = new ServerRequest { Token = token, Revision = _weapon.GetReadinessState(_triggerIndex).Revision, Identity = cartridge.Unit.UniqueId, Cartridge = cartridge };
            else CancelLocalRequest(); // Dedicated server не придумывает local player.
        }
        [Command(requiresAuthority = false)]
        private void CmdRequestCartridgeAdmission(ulong token, uint revision, uint shellNetId, Guid unitIdentity, NetworkConnectionToClient sender = null)
        {
            if (token == 0 || sender == null || !SenderOwnsMainHand(sender) || _serverRequest != null ||
                !NetworkServer.spawned.TryGetValue(shellNetId, out var identity) || identity == null)
            { if (sender != null) TargetAdmissionResult(sender, token, false, revision, false); return; }
            var request = new ServerRequest { Token = token, Revision = revision, Identity = unitIdentity,
                Cartridge = identity.GetComponent<Cartridge>(), Sender = sender };
            if (!ValidateRequest(request))
            {
                GameLog.WeaponSystem.Verbose($"[CartridgeIntake] Запрос приёма отклонён при постановке: token {token}, revision {revision}.", this);
                TargetAdmissionResult(sender, token, false, revision, false);
                return;
            }
            _serverRequest = request;
        }
        [Command(requiresAuthority = false)]
        private void CmdCancelAdmission(ulong token, NetworkConnectionToClient sender = null)
        {
            if (_serverRequest != null && _serverRequest.Token == token && _serverRequest.Sender == sender) _serverRequest.Cancelled = true;
        }
        private void ProcessServerRequest()
        {
            var request = _serverRequest;
            _retained.RemoveAll(unit => unit == null);
            bool admitted = false, resync = false, published = !NetworkServer.active;
            if (_retained.Count < RetainedUnitLimit && ValidateRequest(request))
            {
                _processing = true;
                StateEventAuthority.AmmoAdmissionPublication publication = null;
                try
                {
                    if (NetworkServer.active) publication = StateEventAuthority.BeginAmmoAdmissionPublication(_weapon, request.Token);
                    var outcome = !NetworkServer.active || publication != null
                        ? _weapon.TryAcceptAmmoUnit(_triggerIndex, request.Revision, request.Cartridge.Unit, request.Token)
                        : UxrAmmoAdmissionOutcome.Rejected;
                    admitted = outcome != UxrAmmoAdmissionOutcome.Rejected;
                    published = !NetworkServer.active || publication?.Published == true;
                    resync = admitted && (!published || outcome == UxrAmmoAdmissionOutcome.CommittedWithNotificationFailure);
                    if (resync && NetworkServer.active)
                    {
                        published = NetworkStateRelay.Instance != null && NetworkStateRelay.Instance.ServerResynchronizeAmmoAdmissionPeers();
                        _needsAllPeerResync = !published;
                        if (published) _weapon.TryAcknowledgeFixedAmmoResynchronization(_triggerIndex, _weapon.GetReadinessState(_triggerIndex).Revision);
                    }
                }
                finally { publication?.Dispose(); _processing = false; }
                if (admitted)
                {
                    _retained.Add(request.Cartridge.Unit);
                    _retirements.Add(new Retirement { Unit = request.Cartridge.Unit, Identity = request.Identity,
                        Grab = request.Cartridge.Grabbable, EarliestFrame = Time.frameCount + 1, Published = published });
                }
            }
            if (!admitted)
                GameLog.WeaponSystem.Verbose($"[CartridgeIntake] Патрон не принят: retained {_retained.Count}/{RetainedUnitLimit}, " +
                    $"resyncPending {_needsAllPeerResync}, revision {request.Revision}.", this);
            else if (resync)
                GameLog.WeaponSystem.Warning($"[CartridgeIntake] Патрон принят со сбоем уведомления/публикации, пересинхронизация peers: {published}.", this);
            uint current = _weapon.GetReadinessState(_triggerIndex)?.Revision ?? 0;
            if (!admitted && request.Cartridge != null && request.Cartridge.Unit != null &&
                request.Cartridge.Unit.UniqueId == request.Identity && !request.Cartridge.Unit.HasBeenConsumed &&
                request.Cartridge.Grabbable.CurrentAnchor == _intake && UxrGrabManager.HasInstance)
                UxrGrabManager.Instance.RemoveObjectFromAnchor(request.Cartridge.Grabbable, true, true);
            if (request.Sender != null) TargetAdmissionResult(request.Sender, request.Token, admitted, current, resync);
            else ReceiveAdmissionResult(request.Token, admitted, current, resync);
            _serverRequest = null;
        }
        [TargetRpc]
        private void TargetAdmissionResult(NetworkConnectionToClient target, ulong token, bool admitted, uint revision, bool resync)
        { ReceiveAdmissionResult(token, admitted, revision, resync); }
        private void ReceiveAdmissionResult(ulong token, bool admitted, uint revision, bool resync)
        {
            // Клиент снял барьер по таймауту/потере руки, а сервер успел принять: локальный ledger мог
            // уйти вперёд и отвергнуть replay фиксации. Только полный снимок сводит состояние.
            if (token != 0 && token == _cancelledToken)
            {
                _cancelledToken = 0;
                if (admitted) NetworkStateRelay.Instance?.RequestAmmoAdmissionResynchronization();
                return;
            }
            if (token == 0 || token != _localToken) return;
            bool observed = admitted && _localUnit != null && _localUnit.HasBeenConsumed &&
                (_weapon.GetReadinessState(_triggerIndex)?.Revision ?? 0) >= revision;
            _weapon.TryEndAmmoAdmissionBarrier(_triggerIndex, token);
            _localToken = 0; _localUnit = null; _localUnitNetId = 0;
            if (resync || admitted && !observed) NetworkStateRelay.Instance?.RequestAmmoAdmissionResynchronization();
        }
        private void CancelLocalRequest()
        {
            if (_localToken == 0) return;
            ulong token = _localToken;
            _cancelledToken = token;
            _weapon.TryEndAmmoAdmissionBarrier(_triggerIndex, token);
            _localToken = 0; _localUnit = null; _localUnitNetId = 0;
            if (NetworkClient.active && NetworkClient.ready) CmdCancelAdmission(token);
            else if (_serverRequest != null && _serverRequest.Token == token) _serverRequest.Cancelled = true;
        }
        // Статическое событие переживает объект, уничтоженный без OnDisable (DestroyImmediate в EditMode).
        private void OnDestroy() { UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized; }
        private void HandleStateSerialized(object sender, UxrStateSaveEventArgs e)
        {
            if (this == null) { UxrStateSaveImplementer.StateSerialized -= HandleStateSerialized; return; }
            if (e.Serializer.IsReading && ReferenceEquals(sender, _weapon)) CancelLocalRequest();
        }
        private void CompleteRetirements()
        {
            for (int i = _retirements.Count - 1; i >= 0; i--)
            {
                var entry = _retirements[i];
                if (Time.frameCount < entry.EarliestFrame) continue;
                if (entry.Unit == null) { _retirements.RemoveAt(i); continue; }
                if (!entry.Unit.HasBeenConsumed || entry.Unit.UniqueId != entry.Identity) continue;
                // Освободить intake после Place/commit, никогда реентерабельно.
                if (entry.Grab != null && entry.Grab.CurrentAnchor == _intake && UxrGrabManager.HasInstance)
                    UxrGrabManager.Instance.RemoveObjectFromAnchor(entry.Grab, true, true);
                if (!NetworkServer.active && !NetworkClient.active)
                { Destroy(entry.Unit.gameObject); _retirements.RemoveAt(i); continue; }
                if (!_allowOrderedRetirement || !entry.Published || _needsAllPeerResync) continue;
                var identity = entry.Unit.GetComponent<NetworkIdentity>();
                if (NetworkServer.active && identity != null && identity.netId != 0 &&
                    NetworkServer.spawned.TryGetValue(identity.netId, out var known) && known == identity)
                { NetworkServer.Destroy(identity.gameObject); _retirements.RemoveAt(i); }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Серверный владелец NPC-экипировки. На копиях только проекция одного snapshot.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkIdentity)), DefaultExecutionOrder(10000)]
    public sealed partial class WeaponEquipmentBinding : NetworkBehaviour, IUxrFirearmRootRecoilPolicy, IUxrWeaponOwnerProvider
    {
        [Serializable]
        public struct EquipmentSnapshot
        {
            public uint ownerNetId;
            public int epoch;
            public Vector3 socketPosition;
            public Quaternion socketRotation;
            public Vector3 worldPosition, velocity, angularVelocity;
            public Quaternion worldRotation;
        }

        [SyncVar(hook = nameof(HandleSnapshotChanged))] private EquipmentSnapshot _snapshot;
        private UxrGrabbableObject _rootGrip;
        private Rigidbody _body;
        private NetworkTransformBase[] _transports;
        private bool[] _transportEnabled;
        private bool _rootGripEnabled;
        private bool _projectedActive;
        private int _projectedEpoch = -1;
        private readonly List<UxrGrabbableObject> _grips = new List<UxrGrabbableObject>();

        // Claim не исчезает, если owner ещё не заспавнился или компонент временно выключен.
        public bool IsActive => _snapshot.ownerNetId != 0;
        public int Epoch => _snapshot.epoch;
        public bool IsGrabAvailable => !IsActive;
        // SDK не смещает root при equipment claim, даже пока владелец не разрешён или binding выключен.
        public bool AllowsRootRecoil => !IsActive;
        public UxrAvatar Owner => IsActive ? Mirror.Utils.GetSpawnedInServerOrClient(_snapshot.ownerNetId)?.GetComponent<UxrAvatar>() : null;
        public bool OwnerAlive
        {
            get
            {
                var owner = Owner;
                var player = owner != null ? owner.GetComponent<PlayerController>() : null;
                var session = player != null ? player.Session : null;
                var socketOwner = owner != null ? owner.GetComponent<IWeaponEquipmentSocketOwner>() : null;
                return session != null && socketOwner != null && socketOwner.IsEquipmentOwner &&
                       session.ActiveAvatarNetId == player.netId && player.IsAlive;
            }
        }
        public bool HasValidAttachment => isActiveAndEnabled && IsActive && _projectedActive && _projectedEpoch == Epoch && Socket != null && !HasSdkGrabs();
        private Transform Socket => OwnerAlive ? Owner.GetComponent<IWeaponEquipmentSocketOwner>()?.EquipmentSocket : null;

        /// <summary>Активный, но невалидный claim запрещает SDK возвращаться к прежнему VR-владельцу.</summary>
        public bool TryGetOwner(out UxrActor owner)
        {
            owner = null;
            if (!IsActive) return false;
            if (!OwnerAlive || !HasValidAttachment) return true;
            var avatar = Owner;
            var actor = avatar != null ? avatar.GetComponent<UxrActor>() : null;
            if (actor != null && !actor.IsDead) owner = actor;
            return true;
        }

        private void Awake()
        {
            _rootGrip = GetComponent<UxrGrabbableObject>();
            _body = GetComponent<Rigidbody>();
            _rootGripEnabled = _rootGrip != null && _rootGrip.enabled;
            _transports = GetComponents<NetworkTransformBase>();
            _transportEnabled = _transports.Select(t => t.enabled).ToArray();
        }

        public override void OnStartClient() { base.OnStartClient(); ProjectSnapshot(); }
        public override void OnStartServer() { base.OnStartServer(); ProjectSnapshot(); }
        private void OnEnable() { if (_rootGrip != null) ProjectSnapshot(); }
        private void HandleSnapshotChanged(EquipmentSnapshot previous, EquipmentSnapshot current) => ProjectSnapshot();

        public bool TryAttach(UxrAvatar owner, Vector3 socketPosition, Quaternion socketRotation, out string reason)
        {
            if (IsActive) { reason = "Оружие уже экипировано; используйте transfer."; return false; }
            return SetOwner(owner, socketPosition, socketRotation, out reason);
        }

        public bool TryTransfer(UxrAvatar owner, Vector3 socketPosition, Quaternion socketRotation, out string reason)
        {
            if (!IsActive) { reason = "Нет экипировки для передачи."; return false; }
            return SetOwner(owner, socketPosition, socketRotation, out reason);
        }

        private bool SetOwner(UxrAvatar owner, Vector3 position, Quaternion rotation, out string reason)
        {
            if (!CanWrite(out reason)) return false;
            if (!MapRunAdmission.CanActivateActiveMap)
            { reason = "Карта не допускает экипировку (Loading/Closing)."; return false; }
            if (netIdentity.connectionToClient != null)
            { reason = "Перед экипировкой предмет должен вернуться под серверное сетевое авторство."; return false; }
            var player = owner != null ? owner.GetComponent<PlayerController>() : null;
            var session = player != null ? player.Session : null;
            var socketOwner = owner != null ? owner.GetComponent<IWeaponEquipmentSocketOwner>() : null;
            if (session == null || socketOwner == null || !socketOwner.IsEquipmentOwner || !player.IsAlive || player.netId == 0 ||
                session.ActiveAvatarNetId != player.netId || socketOwner.EquipmentSocket == null)
            { reason = "Нет текущего живого клипового AI-тела."; return false; }
            foreach (var identity in NetworkServer.spawned.Values)
            {
                var other = identity != null ? identity.GetComponent<WeaponEquipmentBinding>() : null;
                if (other != null && other != this && other.IsActive && other._snapshot.ownerNetId == player.netId)
                { reason = "Сокет правой руки уже занят другой экипировкой."; return false; }
            }
            if (_rootGrip == null || _body == null || GetComponentInChildren<UxrFirearmWeapon>(true) == null ||
                _rootGrip.CurrentAnchor != null || HasSdkGrabs())
            { reason = "Оружие не свободно от SDK-хвата/якоря или имеет неполную композицию."; return false; }
            if (!Finite(position) || !Finite(rotation) || Quaternion.Dot(rotation, rotation) < 0.0001f)
            { reason = "Невалидный authored socket offset."; return false; }
            _snapshot = new EquipmentSnapshot { ownerNetId = player.netId, epoch = Epoch + 1,
                socketPosition = position, socketRotation = rotation.normalized };
            ProjectSnapshot();
            reason = null;
            return true;
        }

        public bool TryDrop(Vector3 velocity, Vector3 angularVelocity, out string reason)
        {
            if (!CanWrite(out reason)) return false;
            if (!IsActive) { reason = "Оружие не экипировано."; return false; }
            if (!Finite(velocity) || !Finite(angularVelocity)) { reason = "Невалидная скорость drop."; return false; }
            ApplySocketPose();
            _snapshot = new EquipmentSnapshot { epoch = Epoch + 1, worldPosition = transform.position,
                worldRotation = transform.rotation, velocity = velocity, angularVelocity = angularVelocity };
            ProjectSnapshot();
            reason = null;
            return true;
        }

        private bool CanWrite(out string reason)
        {
            if (!NetworkServer.active || !isServer || netId == 0 || !StateEventAuthority.IsWorldAuthority)
            { reason = "Binding меняет только сервер на заспавненном предмете."; return false; }
            if (Epoch == int.MaxValue) { reason = "Epoch исчерпана; переход запрещён."; return false; }
            reason = null;
            return true;
        }

        private bool HasSdkGrabs()
        {
            if (!UxrGrabManager.HasInstance) return false;
            // Иерархия может получить новый магазин; List overload не создаёт массив на каждом кадре.
            GetComponentsInChildren(true, _grips);
            foreach (var grip in _grips) if (UxrGrabManager.Instance.IsBeingGrabbed(grip)) return true;
            return false;
        }

        private bool ReconcileSdkGrabs()
        {
            if (!UxrGrabManager.HasInstance) return true;
            if (UxrManager.Instance.IsInsideStateSync) return false;
            foreach (var grip in GetComponentsInChildren<UxrGrabbableObject>(true))
            foreach (var hand in UxrGrabManager.Instance.GetGrabbingHands(grip).ToArray())
            {
                // Проекция полученного серверного claim через штатный receive path; не исходящий ReleaseObject.
                var args = new UxrMethodInvokedSyncEventArgs("ReleaseObject", new object[] {
                    hand, grip, grip.transform.position, grip.transform.rotation, Vector3.zero, Vector3.zero, false });
                var result = UxrManager.Instance.ExecuteStateSyncEvent(args.SerializeEventBinary(UxrGrabManager.Instance));
                if (result.IsError)
                {
                    GameLog.WeaponSystem.Error("[Equipment] Не удалось откатить SDK prediction: " + result.ErrorMessage, this);
                    return false;
                }
            }
            return !HasSdkGrabs();
        }

        private void ProjectSnapshot()
        {
            if (_rootGrip == null || _body == null || _projectedEpoch == Epoch) return;
            if (IsActive)
            {
                // Во время outer SDK receive rollback откладывается: второго transform writer пока нет.
                if (!ReconcileSdkGrabs()) return;
                _rootGrip.enabled = false;
                foreach (var transport in _transports) { transport.enabled = false; transport.ResetState(); }
                if (!_body.isKinematic) { _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
                _body.isKinematic = true;
                _projectedActive = true;
                _projectedEpoch = Epoch;
                ApplySocketPose();
            }
            else
            {
                // Initial unbound snapshot не заменяет authored состояние обычного оружия.
                if (Epoch == 0) { _projectedEpoch = 0; return; }
                transform.SetPositionAndRotation(_snapshot.worldPosition, _snapshot.worldRotation);
                _rootGrip.enabled = _rootGripEnabled;
                _body.isKinematic = false;
                _body.linearVelocity = _snapshot.velocity;
                _body.angularVelocity = _snapshot.angularVelocity;
                for (int i = 0; i < _transports.Length; ++i)
                { _transports[i].ResetState(); _transports[i].enabled = _transportEnabled[i]; }
                _projectedActive = false;
                _projectedEpoch = Epoch;
            }
        }

        private void LateUpdate()
        {
            if (NetworkServer.active && isServer && IsActive && !OwnerAlive)
            {
                // Потеря/смена тела отзывает equipment на авторе; observer этот переход не изобретает.
                TryDrop(Vector3.zero, Vector3.zero, out _);
            }
            ProjectSnapshot();
            // Клиент не снимает claim по отсутствию owner; серверный teardown задаст следующий snapshot.
            ApplySocketPose();
        }

        private void ApplySocketPose()
        {
            if (!HasValidAttachment) return;
            var socket = Socket;
            transform.SetPositionAndRotation(socket.TransformPoint(_snapshot.socketPosition), socket.rotation * _snapshot.socketRotation);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
    }
}

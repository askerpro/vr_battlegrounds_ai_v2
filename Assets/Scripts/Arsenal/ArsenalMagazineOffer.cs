using System;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Player;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Один настоящий магазин на отдельном якоре оружейного слота. Спавном владеет ArsenalMagazineSupply.</summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalMagazineOffer : MonoBehaviour
    {
        [SerializeField] private FirearmSlotController _slot;
        [SerializeField] private UxrGrabbableObjectAnchor _anchor;
        private UxrGrabbableObject _assigned;
        private bool _subscribed;
        public FirearmSlotController Slot => _slot;
        public UxrGrabbableObjectAnchor Anchor => _anchor;
        public UxrGrabbableObject CurrentItem => _assigned != null && _assigned.CurrentAnchor == _anchor ? _assigned : null;
        public event Action<ArsenalMagazineOffer, UxrManipulationEventArgs> Taken;

        public void Configure(FirearmSlotController slot, UxrGrabbableObjectAnchor anchor)
        {
            Unsubscribe();
            _slot = slot; _anchor = anchor;
            if (isActiveAndEnabled && Application.isPlaying) Subscribe();
        }

        private void OnEnable() { if (Application.isPlaying) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (_subscribed || _anchor == null) return;
            _anchor.Removed += HandleRemoved;
            // Запас выдаётся сервером; ручной возврат не создаёт новую запись склада и не заменяет его предмет.
            _anchor.AddPlacingValidator(RejectManualPlacement);
            _subscribed = true;
        }
        private static bool RejectManualPlacement(UxrGrabbableObject item) => false;
        private void Unsubscribe()
        {
            if (!_subscribed || _anchor == null) return;
            _anchor.Removed -= HandleRemoved;
            _anchor.RemovePlacingValidator(RejectManualPlacement);
            _subscribed = false;
        }

        /// <summary>Бесплатные боеприпасы: допуск стены и её владельца, без цены оружия.</summary>
        public bool AllowsGrab(UxrGrabber grabber)
        {
            ArsenalWallController wall = _slot != null ? _slot.Wall : null;
            if (wall == null || !wall.CanTrade) return false;
            if (MatchEconomy.Current == null) return true;
            PlayerController player = grabber != null && grabber.Avatar != null
                ? grabber.Avatar.GetComponentInParent<PlayerController>() : null;
            return wall.OwnerSessionNetId != 0 && player != null && player.Session != null &&
                   player.Session.netId == wall.OwnerSessionNetId;
        }

        public void Assign(UxrGrabbableObject item)
        {
            if (item == null || _anchor == null) return;
            _assigned = item;
            // Выдача сохраняет физический worldScale, как SDK возврат оружия.
            item.transform.SetParent(_anchor.transform, true);
            ArsenalPresentationApplicator.ApplyMagazine(this, item.transform);
            item.SetNetworkAnchor(_anchor);
            if (item.RigidBodySource != null)
            {
                // Скорость кинематического тела Unity не принимает (предупреждение в консоль); SetNetworkAnchor
                // мог уже перевести тело в kinematic.
                if (!item.RigidBodySource.isKinematic)
                {
                    item.RigidBodySource.linearVelocity = Vector3.zero;
                    item.RigidBodySource.angularVelocity = Vector3.zero;
                }
                item.RigidBodySource.isKinematic = true;
            }
        }

        public void RefreshAvailability()
        {
            UxrGrabbableObject item = CurrentItem;
            if (item == null || !StateEventAuthority.IsWorldAuthority) return;
            var wall = _slot != null ? _slot.Wall : null;
            bool available = wall != null && wall.CanTrade && (MatchEconomy.Current == null || wall.OwnerSessionNetId != 0);
            if (item.IsGrabbable != available) item.IsGrabbable = available;
        }

        private void HandleRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableObject != _assigned) return;
            // SDK уже завершил перепарентинг при GrabObject. При UseParenting=false убираем только остаточную связь со станцией.
            if (e.GrabbableObject.transform.IsChildOf(_anchor.transform))
                e.GrabbableObject.transform.SetParent(null, true);
            Taken?.Invoke(this, e);
        }
    }
}

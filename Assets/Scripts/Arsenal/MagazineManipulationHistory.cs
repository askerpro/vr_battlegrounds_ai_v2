using System;
using UnityEngine;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Состояние магазина в складе. Revision отличает явный серверный возврат от первоначальной привязки.</summary>
    [Serializable]
    public struct ArsenalMagazineStockBinding
    {
        public uint NetId;
        public uint ReturnRevision;
        public ulong AllowedManipulationSequence;
        public ArsenalMagazineStockBinding(uint netId, uint revision = 0, ulong allowedSequence = 0)
        { NetId = netId; ReturnRevision = revision; AllowedManipulationSequence = allowedSequence; }
    }

    /// <summary>Необратимый локальный факт манипуляции за жизнь экземпляра. Не синхронизирует SDK и не изменяет предмет.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class MagazineManipulationHistory : MonoBehaviour
    {
        private UxrGrabbableObject _item;
        private UxrGrabManager _manager;
        private bool _subscribed;
        public bool Touched { get; private set; }
        public ulong Sequence { get; private set; }
        public bool HasStockBaseline { get; private set; }
        public uint LastReturnRevision { get; private set; }

        private void OnEnable() { if (Application.isPlaying) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (_subscribed) return;
            _item = GetComponent<UxrGrabbableObject>();
            _manager = UxrGrabManager.Instance;
            _item.Grabbed += HandleTouched;
            _item.Released += HandleTouched;
            _item.Placed += HandleTouched;
            // Эти SDK object events подавляются propagateEvents=false; direct StateChanged вызывается всё равно,
            // в том числе при SyncState входящего сетевого действия. Глобальный ComponentStateChanged для этого не годится.
            _manager.StateChanged += HandleManagerStateChanged;
            _subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_item != null)
            {
                _item.Grabbed -= HandleTouched; _item.Released -= HandleTouched; _item.Placed -= HandleTouched;
            }
            if (_manager != null) _manager.StateChanged -= HandleManagerStateChanged;
            _manager = null; _subscribed = false;
        }
        private void HandleTouched(object sender, UxrManipulationEventArgs e)
        { if (e.GrabbableObject == _item) Touched = true; }
        private void HandleManagerStateChanged(object sender, UxrSyncEventArgs e)
        {
            if (!(e is UxrMethodInvokedSyncEventArgs method)) return;
            int parameter;
            switch (method.MethodName)
            {
                case "GrabObject": case "ReleaseObject": parameter = 1; break;
                case "PlaceObject": case "RemoveObjectFromAnchor": parameter = 0; break;
                default: return;
            }
            if (method.Parameters.Length <= parameter || !ReferenceEquals(method.Parameters[parameter], _item)) return;
            Touched = true;
            // Только direct manager StateChanged увеличивает счётчик: object events не удваивают propagateEvents=true.
            if (Sequence != ulong.MaxValue) Sequence++;
        }

        public bool AllowsBinding(ArsenalMagazineStockBinding binding)
        {
            if (!Touched) return true;
            // У позднего клиента история до его спавна неизвестна. Счётчики сравнимы лишь после untouched baseline.
            return HasStockBaseline && binding.ReturnRevision > LastReturnRevision && Sequence <= binding.AllowedManipulationSequence;
        }
        public void AcceptBinding(ArsenalMagazineStockBinding binding)
        {
            HasStockBaseline = true;
            if (binding.AllowedManipulationSequence > Sequence) Sequence = binding.AllowedManipulationSequence;
            if (binding.ReturnRevision > LastReturnRevision) LastReturnRevision = binding.ReturnRevision;
            // Touched не сбрасывается ни при возврате, ни при выключении/включении компонента.
        }
    }
}

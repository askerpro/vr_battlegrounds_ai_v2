using System;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Interaction
{
    /// <summary>Выбранные SDK пары гнездо/рука с повторной проверкой состояния при чтении.</summary>
    public sealed class AnchorPlacementReadiness : IDisposable
    {
        private readonly UxrGrabManager _manager;
        private readonly Func<UxrGrabbableObjectAnchor, bool> _filter;
        private bool _disposed;

        public AnchorPlacementReadiness(UxrGrabManager manager, Func<UxrGrabbableObjectAnchor, bool> filter)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        public bool TryGetReadyGrabber(UxrGrabbableObjectAnchor anchor, out UxrGrabber grabber)
        {
            grabber = null;
            if (_disposed || _manager == null || anchor == null || !_filter(anchor)) return false;
            UxrGrabber hand = _manager.GetAnchorPlacementCandidate(anchor);
            UxrGrabbableObject item = hand != null ? hand.GrabbedObject : null;
            if (hand == null || !hand.isActiveAndEnabled || hand.Avatar == null ||
                hand.Avatar.AvatarMode != UxrAvatarMode.Local || item == null ||
                hand.GrabbedObject != item || !item.isActiveAndEnabled || !item.IsPlaceable ||
                item.UsesGrabbableParentDependency || anchor.CurrentPlacedObject != null ||
                _manager.GetHandsGrabbingCount(item, false) != 1 || !item.CanBePlacedOnAnchor(anchor)) return false;
            grabber = hand;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

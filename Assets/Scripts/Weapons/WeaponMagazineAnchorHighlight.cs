using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Подсветка приёмного гнезда, выбранного UltimateXR для текущего магазина.</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponMagazineAnchorHighlight : MonoBehaviour
    {
        [SerializeField] private UxrGrabbableObjectAnchor _anchor;
        [SerializeField] private GameObject _visual;
        private UxrGrabManager _manager;
        private AnchorPlacementReadiness _readiness;

        public GameObject Visual => _visual;
        private void OnEnable() => SetVisible(false);

        private void Update()
        {
            UxrGrabManager current = UxrGrabManager.HasInstance ? UxrGrabManager.Instance : null;
            if (_manager != current)
            {
                _readiness?.Dispose();
                _manager = current;
                _readiness = current == null ? null : new AnchorPlacementReadiness(current, a => a == _anchor);
            }
            SetVisible(_readiness != null && _readiness.TryGetReadyGrabber(_anchor, out _));
        }

        private void OnDisable()
        {
            _readiness?.Dispose();
            _readiness = null;
            _manager = null;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_visual != null && _visual.activeSelf != visible) _visual.SetActive(visible);
        }
    }
}

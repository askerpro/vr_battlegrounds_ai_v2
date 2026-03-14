using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Interaction
{
    /// <summary>
    /// Component that redirects a grab interaction to an object placed in a target anchor.
    /// Used to allow grabbing weapons from a back anchor by reaching for the shoulder.
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObject))]
    public class UxrBackGrabProxy : MonoBehaviour
    {
        #region Inspector Properties

        [SerializeField] private UxrGrabbableObjectAnchor _targetAnchor;

        #endregion

        #region Private Types & Data

        private UxrGrabbableObject _grabbableProxy;

        #endregion

        #region Unity

        private void Awake()
        {
            _grabbableProxy = GetComponent<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            _grabbableProxy.Grabbed += GrabbableProxy_Grabbed;
            
            if (_targetAnchor != null)
            {
                _targetAnchor.Placed += TargetAnchor_PlacedRemoved;
                _targetAnchor.Removed += TargetAnchor_PlacedRemoved;
                UpdateProxyState();
            }
        }

        private void OnDisable()
        {
            if (_grabbableProxy != null)
            {
                _grabbableProxy.Grabbed -= GrabbableProxy_Grabbed;
            }

            if (_targetAnchor != null)
            {
                _targetAnchor.Placed -= TargetAnchor_PlacedRemoved;
                _targetAnchor.Removed -= TargetAnchor_PlacedRemoved;
            }
        }

        #endregion

        #region Event Handling Methods

        private void TargetAnchor_PlacedRemoved(object sender, UxrManipulationEventArgs e)
        {
            UpdateProxyState();
        }

        private void GrabbableProxy_Grabbed(object sender, UxrManipulationEventArgs e)
        {
            if (_targetAnchor != null && _targetAnchor.CurrentPlacedObject != null)
            {
                UxrGrabbableObject targetObject = _targetAnchor.CurrentPlacedObject;
                UxrGrabber grabber = e.Grabber;

                // 1. Release the proxy immediately without propagating events
                UxrGrabManager.Instance.ReleaseObject(grabber, _grabbableProxy, false);

                // 2. Force the grabber to grab the real object from the anchor
                // We use grab point 0 as the default main grab point
                UxrGrabManager.Instance.GrabObject(grabber, targetObject, 0, true);
            }
            else
            {
                // Fallback: if somehow grabbed with no target, release immediately
                UxrGrabManager.Instance.ReleaseObject(e.Grabber, _grabbableProxy, false);
            }
        }

        #endregion

        #region Private Methods

        private void UpdateProxyState()
        {
            if (_grabbableProxy != null && _targetAnchor != null)
            {
                // Only enable the proxy if there's something to grab in the target anchor
                bool hasObject = _targetAnchor.CurrentPlacedObject != null;
                
                // We disable the grabbable part, or the whole object
                _grabbableProxy.IsGrabbable = hasObject;
            }
        }

        #endregion
    }
}

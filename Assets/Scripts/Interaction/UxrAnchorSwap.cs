using System.Reflection;
using UltimateXR.Core.Components.Composite;
using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Manipulation.Helpers
{
    /// <summary>
    ///     Component that, when attached to a <see cref="UxrGrabbableObjectAnchor" />, 
    ///     automatically releases any currently placed object if a new one is being placed.
    ///     This enables "swap" behavior for VR slots by temporarily "faking" an empty slot 
    ///     when a compatible object is close, allowing UltimateXR's internal checks to pass.
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public class UxrAnchorSwap : UltimateXR.Core.Components.UxrComponent<UxrAnchorSwap>
    {
        #region Inspector Properties/Serialized Fields

        [SerializeField] private UxrGrabbableObjectAnchor _anchor;

        #endregion

        #region Private Fields

        private UxrGrabbableObject _swappedObject;
        private bool               _isFakingEmpty;

        // Reflection is used to set the internal property CurrentPlacedObject of the anchor.
        private static readonly PropertyInfo CurrentPlacedObjectProperty = typeof(UxrGrabbableObjectAnchor).GetProperty(nameof(UxrGrabbableObjectAnchor.CurrentPlacedObject), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Reflection to clear the CurrentAnchor reference on the grabbable object itself.
        private static readonly PropertyInfo CurrentAnchorProperty = typeof(UxrGrabbableObject).GetProperty(nameof(UxrGrabbableObject.CurrentAnchor), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        #endregion

        #region Public Properties

        /// <summary>
        ///     Gets the anchor the component is attached to.
        /// </summary>
        public UxrGrabbableObjectAnchor Anchor => _anchor;

        #endregion

        #region Unity

        /// <summary>
        ///     Initializes the component.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            _anchor = GetComponent<UxrGrabbableObjectAnchor>();
        }

        /// <summary>
        ///     Logs initial state.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Debug.Log($"[UxrAnchorSwap] Enabled on {gameObject.name}. Target Anchor: {Anchor.name}. Initial object: {(Anchor.CurrentPlacedObject != null ? Anchor.CurrentPlacedObject.name : "None")}");
        }

        /// <summary>
        ///     Main logic to detect potential swaps and decoy the UltimateXR placement check.
        /// </summary>
        private void Update()
        {
            // CASE 1: Anchor is occupied and we haven't started faking yet.
            if (Anchor.CurrentPlacedObject != null && !_isFakingEmpty)
            {
                // Check if any hand is holding a compatible object near this anchor's placement zone.
                foreach (var grabber in UxrGrabber.EnabledComponents)
                {
                    if (grabber.GrabbedObject != null && grabber.GrabbedObject != Anchor.CurrentPlacedObject && Anchor.IsCompatibleObject(grabber.GrabbedObject))
                    {
                        // Use the proximity transform for distance calculation, just like UltimateXR does internally.
                        float distance = Vector3.Distance(grabber.GrabbedObject.DropProximityTransform.position, Anchor.DropProximityTransform.position);
                        
                        if (distance < Anchor.MaxPlaceDistance)
                        {
                            // A compatible object is close enough to be placed!
                            // To bypass the "anchor.CurrentPlacedObject == null" check in UxrGrabbableObject.CanBePlacedOnAnchor,
                            // we temporarily set the reference to null.
                            _swappedObject = Anchor.CurrentPlacedObject;
                            _isFakingEmpty = true;
                            
                            CurrentPlacedObjectProperty.SetValue(Anchor, null);
                            
                            Debug.Log($"[UxrAnchorSwap] Compatible object {grabber.GrabbedObject.name} is near occupied anchor {Anchor.name}. Temporarily clearing slot to allow swap.");
                            break;
                        }
                    }
                }
            }
            // CASE 2: We are currently faking an empty slot.
            else if (_isFakingEmpty)
            {
                // Check if something NEW was successfully placed by the manager.
                if (Anchor.CurrentPlacedObject != null)
                {
                    UxrGrabbableObject newObject = Anchor.CurrentPlacedObject;
                    Debug.Log($"[UxrAnchorSwap] Swap successful! New object {newObject.name} placed in {Anchor.name}. Forcefully ejecting old object {_swappedObject.name}.");
                    
                    // We must properly unbind and detach the old object.
                    // IMPORTANT: We do NOT use UxrGrabManager.RemoveObjectFromAnchor here 
                    // because it might clear the anchor's CurrentPlacedObject even if it now points to the NEW object!
                    EjectSwappedObject(_swappedObject);
                    
                    _swappedObject = null;
                    _isFakingEmpty = false;
                }
                else
                {
                    // Check if the player moved the held object away or released it somewhere else.
                    bool stillNearACompatibleObject = false;
                    foreach (var grabber in UxrGrabber.EnabledComponents)
                    {
                        if (grabber.GrabbedObject != null && Anchor.IsCompatibleObject(grabber.GrabbedObject))
                        {
                            float distance = Vector3.Distance(grabber.GrabbedObject.DropProximityTransform.position, Anchor.DropProximityTransform.position);
                            if (distance < Anchor.MaxPlaceDistance)
                            {
                                stillNearACompatibleObject = true;
                                break;
                            }
                        }
                    }

                    if (!stillNearACompatibleObject)
                    {
                        // The candidate object is no longer near. Restore the original object to the slot.
                        Debug.Log($"[UxrAnchorSwap] Compatible object moved away from {Anchor.name}. Restoring {_swappedObject.name} to slot.");
                        CurrentPlacedObjectProperty.SetValue(Anchor, _swappedObject);
                        _swappedObject = null;
                        _isFakingEmpty = false;
                    }
                }
            }
        }

        /// <summary>
        ///     Forcefully detaches an object from any anchor and restores its physical state.
        /// </summary>
        private void EjectSwappedObject(UxrGrabbableObject obj)
        {
            if (obj == null) return;

            // 1. Clear the anchor reference so it doesn't think it belongs to one.
            if (CurrentAnchorProperty != null)
            {
                CurrentAnchorProperty.SetValue(obj, null);
            }

            // 2. Physical detachment from the player hierarchy.
            obj.transform.parent = null;

            // 3. Restore physics behavior.
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
            
            // 4. Ensure it's grabbable again.
            obj.IsGrabbable = true;
        }

        #endregion
    }
}

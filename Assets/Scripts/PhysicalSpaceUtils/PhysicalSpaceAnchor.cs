using UnityEngine;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Represents a physical tracking point in the virtual arena.
    /// The manager requires two such anchors (usually with IDs 0 and 1) to align the real physical arena
    /// with the virtual scene.
    /// </summary>
    public class PhysicalSpaceAnchor : MonoBehaviour
    {
        [Header("Anchor Settings")]
        [Tooltip("The ID of this calibration point. Must be 0 or 1 for a two-point calibration.")]
        public int id = 0;
        
        [Header("Highlight Settings")]
        [Tooltip("Optional visual highlight object that will be toggled on when the player is supposed to register this anchor.")]
        [SerializeField] private GameObject highlightObject;

        /// <summary>
        /// Enables or disables the visual highlight of the anchor.
        /// </summary>
        /// <param name="highlighted">True to enable highlight, false to disable.</param>
        public void SetHighlighted(bool highlighted)
        {
            if (highlightObject != null)
            {
                highlightObject.SetActive(highlighted);
            }
        }
    }
}

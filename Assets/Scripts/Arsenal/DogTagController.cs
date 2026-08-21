using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Controls the Dog Tag panel on the Arsenal Wall.
    /// When the player grabs the dog tag, it triggers the arsenal closing sequence.
    /// The dog tag is the "point of no return" — once grabbed, the arsenal closes.
    /// </summary>
    public class DogTagController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────
        [Header("Dog Tag")]
        [SerializeField] private UxrGrabbableObjectAnchor _tagAnchor;
        [SerializeField] private UxrGrabbableObject _tagObject;

        [Header("Visual")]
        [SerializeField] private Light _readyLight;
        [SerializeField] private Color _readyColor = Color.cyan;
        [SerializeField] private Color _grabColor  = Color.yellow;

        // ── Events ─────────────────────────────────────────────
        /// <summary>
        /// Fired when the player grabs the dog tag from the rack.
        /// ArsenalWallController subscribes to this to start the closing sequence.
        /// </summary>
        public System.Action<PlayerController> OnTagGrabbed;

        // ── Properties ─────────────────────────────────────────
        public bool IsTagOnRack => _tagAnchor != null && _tagAnchor.CurrentPlacedObject != null;

        // ── State ──────────────────────────────────────────────
        private bool _hasBeenGrabbed;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            if (_tagAnchor == null)
                _tagAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>();
            if (_tagObject == null)
                _tagObject = GetComponentInChildren<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            if (_tagAnchor != null)
            {
                _tagAnchor.Removed += OnTagRemoved;
            }
        }

        private void OnDisable()
        {
            if (_tagAnchor != null)
            {
                _tagAnchor.Removed -= OnTagRemoved;
            }
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Resets the dog tag for a new prep phase.
        /// </summary>
        public void ResetTag()
        {
            _hasBeenGrabbed = false;
            SetLight(_readyColor, true);

            if (_tagObject != null)
                _tagObject.enabled = true;

            GameLog.Arsenal.Info("[Arsenal] Dog tag reset — ready for new prep phase.");
        }

        /// <summary>
        /// Disables the dog tag interaction (e.g. during round active phase).
        /// </summary>
        public void Disable()
        {
            if (_tagObject != null)
                _tagObject.enabled = false;

            SetLight(_readyColor, false);
        }

        // ── Private ────────────────────────────────────────────

        private void OnTagRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (_hasBeenGrabbed) return;

            _hasBeenGrabbed = true;
            SetLight(_grabColor, true);

            GameLog.Arsenal.Info("[Arsenal] DOG TAG GRABBED — Arsenal closing!");
            
            PlayerController player = null;
            if (e.Grabber != null && e.Grabber.Avatar != null)
            {
                player = e.Grabber.Avatar.GetComponentInParent<PlayerController>();
            }
            
            OnTagGrabbed?.Invoke(player);
        }

        private void SetLight(Color color, bool on)
        {
            if (_readyLight != null)
            {
                _readyLight.color   = color;
                _readyLight.enabled = on;
            }
        }
    }
}
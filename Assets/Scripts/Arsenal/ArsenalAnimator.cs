using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Drives Arsenal Wall open/close animations via Unity Animator.
    ///
    /// Animation Clips handle all movement (shelf slide, shutter roll).
    /// This script fires triggers and listens for Animation Events
    /// to notify <see cref="ArsenalWallController"/> when sequences complete.
    ///
    /// Animator Controller states:
    ///   Idle_Open ──[Close]──► Closing ──[exit]──► Idle_Closed
    ///   Idle_Closed ──[Open]──► Opening ──[exit]──► Idle_Open
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ArsenalAnimator : MonoBehaviour
    {
        // ── Animator Hashes ────────────────────────────────────
        private static readonly int TriggerClose = Animator.StringToHash("Close");
        private static readonly int TriggerOpen  = Animator.StringToHash("Open");
        private static readonly int StateClosing = Animator.StringToHash("Closing");
        private static readonly int StateOpening = Animator.StringToHash("Opening");
        private static readonly int StateIdleClosed = Animator.StringToHash("Idle_Closed");
        private static readonly int StateIdleOpen   = Animator.StringToHash("Idle_Open");

        // ── References ─────────────────────────────────────────
        private Animator _animator;

        // ── Callbacks ──────────────────────────────────────────
        private System.Action _onCloseComplete;
        private System.Action _onOpenComplete;

        // ── State ──────────────────────────────────────────────
        private bool _isAnimating;
        /// <summary>True while an animation sequence is playing.</summary>
        public bool IsAnimating => _isAnimating;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Plays the closing animation (shelf retract → shutter down).
        /// </summary>
        /// <param name="onComplete">Callback when the Closing clip finishes.</param>
        public void PlayCloseSequence(System.Action onComplete = null)
        {
            if (_isAnimating)
            {
                GameLog.Arsenal.Warning("[Arsenal Anim] Animation already in progress.");
                return;
            }

            _isAnimating = true;
            _onCloseComplete = onComplete;
            _animator.SetTrigger(TriggerClose);
            GameLog.Arsenal.Info("[Arsenal Anim] Close sequence triggered.");
        }

        /// <summary>
        /// Plays the opening animation (shutter up → shelf slide out).
        /// </summary>
        /// <param name="onComplete">Callback when the Opening clip finishes.</param>
        public void PlayOpenSequence(System.Action onComplete = null)
        {
            if (_isAnimating)
            {
                GameLog.Arsenal.Warning("[Arsenal Anim] Animation already in progress.");
                return;
            }

            _isAnimating = true;
            _onOpenComplete = onComplete;
            _animator.SetTrigger(TriggerOpen);
            GameLog.Arsenal.Info("[Arsenal Anim] Open sequence triggered.");
        }

        /// <summary>
        /// Immediately snaps to open state (no animation).
        /// </summary>
        public void SetOpenImmediate()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null || !_animator.isActiveAndEnabled) return;
            
            _isAnimating = false;
            _animator.Play(StateIdleOpen, 0, 0f);

            if (_animator.gameObject.activeInHierarchy)
            {
                _animator.Update(0f);
            }
        }

        /// <summary>
        /// Immediately snaps to closed state (no animation).
        /// </summary>
        public void SetClosedImmediate()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null || !_animator.isActiveAndEnabled) return;

            _isAnimating = false;
            _animator.Play(StateIdleClosed, 0, 0f);
            
            // Note: Animator.Update can throw if not fully initialized or if no valid states exist
            if (_animator.gameObject.activeInHierarchy)
            {
                _animator.Update(0f);
            }
        }

        // ── Animation Events (called from clips) ──────────────
        // Add these as Animation Events on the last frame of each clip.

        /// <summary>
        /// Called by Animation Event on the last frame of Arsenal_Close.
        /// </summary>
        public void OnCloseAnimationComplete()
        {
            _isAnimating = false;
            GameLog.Arsenal.Info("[Arsenal Anim] Close sequence complete.");
            _onCloseComplete?.Invoke();
            _onCloseComplete = null;
        }

        /// <summary>
        /// Called by Animation Event on the last frame of Arsenal_Open.
        /// </summary>
        public void OnOpenAnimationComplete()
        {
            _isAnimating = false;
            GameLog.Arsenal.Info("[Arsenal Anim] Open sequence complete.");
            _onOpenComplete?.Invoke();
            _onOpenComplete = null;
        }

        // ── Fallback: Check state via Update (optional safety) ─

        private void Update()
        {
            if (!_isAnimating) return;

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

            // Close finished — transitioned to Idle_Closed
            if (_onCloseComplete != null && stateInfo.shortNameHash == StateIdleClosed)
            {
                OnCloseAnimationComplete();
            }
            // Open finished — transitioned to Idle_Open
            else if (_onOpenComplete != null && stateInfo.shortNameHash == StateIdleOpen)
            {
                OnOpenAnimationComplete();
            }
        }
    }
}

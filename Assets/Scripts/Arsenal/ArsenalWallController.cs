using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Root controller for the Arsenal Wall prefab.
    /// Manages the lifecycle: Closed → Open → Closing → Closed.
    ///
    /// Animation is delegated to <see cref="ArsenalAnimator"/>.
    /// Slot interaction logic lives in <see cref="ArsenalSlotController"/> subclasses.
    /// </summary>
    public class ArsenalWallController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────
        [Header("Arsenal Slots")]
        [Tooltip("All slots on the wall (firearms + shelf items). Auto-found if empty.")]
        [SerializeField] private ArsenalSlotController[] _allSlots;

        [Header("Components")]
        [SerializeField] private DogTagController _dogTagController;
        [SerializeField] private ArsenalAnimator _animator;

        // ── State ──────────────────────────────────────────────
        public enum ArsenalState
        {
            Closed,
            Opening,
            Open,
            Closing
        }

        private ArsenalState _currentState = ArsenalState.Closed;
        public ArsenalState CurrentState => _currentState;
        private LogLevel ArsenalLog => GameSettings.Instance.LogLevelArsenal;

        // ── Events ─────────────────────────────────────────────
        /// <summary>Fired when the arsenal fully opens (after animation).</summary>
        public System.Action OnArsenalOpened;

        /// <summary>Fired when the arsenal fully closes (after animation).</summary>
        public System.Action OnArsenalClosed;

        /// <summary>
        /// Fired when an item is taken from or returned to the arsenal.
        /// Passes the slot and whether the item was taken (true) or returned (false).
        /// </summary>
        public System.Action<ArsenalSlotController, bool> OnSlotChanged;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            if (_allSlots == null || _allSlots.Length == 0)
                _allSlots = GetComponentsInChildren<ArsenalSlotController>();

            if (_dogTagController == null)
                _dogTagController = GetComponentInChildren<DogTagController>();

            if (_animator == null)
                _animator = GetComponent<ArsenalAnimator>();
        }

        private void OnEnable()
        {
            EliminationMode.OnRoundStateChangedLocal += HandleRoundStateChanged;

            if (_dogTagController != null)
                _dogTagController.OnTagGrabbed += HandleTagGrabbed;

            foreach (var slot in _allSlots)
            {
                slot.OnItemTaken    += HandleItemTaken;
                slot.OnItemReturned += HandleItemReturned;
            }
        }

        private void OnDisable()
        {
            EliminationMode.OnRoundStateChangedLocal -= HandleRoundStateChanged;

            if (_dogTagController != null)
                _dogTagController.OnTagGrabbed -= HandleTagGrabbed;

            foreach (var slot in _allSlots)
            {
                slot.OnItemTaken    -= HandleItemTaken;
                slot.OnItemReturned -= HandleItemReturned;
            }
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Opens the arsenal for a new prep phase (with animation).
        /// </summary>
        [ContextMenu("Open Arsenal")]
        public void OpenArsenal()
        {
            if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
            {
                GameLog.Warning(ArsenalLog, "[Arsenal] Arsenal is already open/opening.");
                return;
            }

            _currentState = ArsenalState.Opening;
            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal OPENING — prep phase starting...");

            if (_animator != null)
            {
                _animator.PlayOpenSequence(OnOpenComplete);
            }
            else
            {
                // No animator — open immediately
                OnOpenComplete();
            }
        }

        /// <summary>
        /// Forces the arsenal closed (e.g. from a game manager timeout).
        /// </summary>
        [ContextMenu("Force Close")]
        public void ForceClose()
        {
            if (_currentState == ArsenalState.Closed) return;
            BeginClosing();
        }

        /// <summary>
        /// Snaps the arsenal to closed state without animation (for initial setup).
        /// </summary>
        [ContextMenu("Set Closed Immediate")]
        public void SetClosedImmediate()
        {
            _currentState = ArsenalState.Closed;

            foreach (var slot in _allSlots)
                slot.Lock();

            if (_animator != null)
                _animator.SetClosedImmediate();
        }

        // ── Private: Lifecycle ─────────────────────────────────

        private void OnOpenComplete()
        {
            _currentState = ArsenalState.Open;

            // Spawn items and unlock all slots
            foreach (var slot in _allSlots)
            {
                slot.SpawnItem();
                slot.Unlock();
            }

            if (_dogTagController != null)
                _dogTagController.ResetTag();

            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal OPENED — prep phase started.");
            OnArsenalOpened?.Invoke();
        }

        private void HandleTagGrabbed(PlayerController player)
        {
            if (_currentState != ArsenalState.Open) return;
            
            if (player != null && player.isOwned && player.Session != null)
            {
                player.Session.CmdSetDogTagGrabbed(true);
            }
            
            BeginClosing();
        }

        private void BeginClosing()
        {
            _currentState = ArsenalState.Closing;
            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal CLOSING — locking all slots...");

            // Lock all slots immediately
            foreach (var slot in _allSlots)
                slot.Lock();

            if (_dogTagController != null)
                _dogTagController.Disable();

            // Play close animation
            if (_animator != null)
            {
                _animator.PlayCloseSequence(OnCloseComplete);
            }
            else
            {
                // No animator — close immediately
                OnCloseComplete();
            }
        }

        private void OnCloseComplete()
        {
            _currentState = ArsenalState.Closed;

            // Despawn items
            foreach (var slot in _allSlots)
                slot.DespawnItem();

            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal CLOSED.");
            OnArsenalClosed?.Invoke();
        }

        // ── Private: Slot Events ───────────────────────────────

        private void HandleItemTaken(ArsenalSlotController slot)
        {
            OnSlotChanged?.Invoke(slot, true);
        }

        private void HandleItemReturned(ArsenalSlotController slot)
        {
            OnSlotChanged?.Invoke(slot, false);
        }

        private void HandleRoundStateChanged(RoundState newState)
        {
            switch (newState)
            {
                case RoundState.Equipment:
                    if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing)
                    {
                        OpenArsenal();
                    }
                    break;
                
                case RoundState.Countdown:
                case RoundState.Combat:
                    if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
                    {
                        ForceClose();
                    }
                    break;
            }
        }
    }
}

using UnityEngine;
using Mirror;
using UltimateXR.Manipulation;
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
    public class ArsenalWallController : NetworkBehaviour
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

        private void Start()
        {
            // Синхронизируем визуальное состояние с логическим на старте
            if (_currentState == ArsenalState.Closed)
            {
                SetClosedImmediate();
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Авторитетная реакция на фазу приходит по серверному каналу, а не через
            // клиентский обработчик: раньше внутри HandleRoundStateChanged стояла ветка
            // `if (isServer)`, и на выделенном сервере она не исполнялась никогда (NET-06).
            EliminationMode.OnRoundStateChangedServer -= ServerHandleRoundStateChanged;
            EliminationMode.OnRoundStateChangedServer += ServerHandleRoundStateChanged;

            ReplenishWeaponsNetwork(true);
        }

        public override void OnStopServer()
        {
            EliminationMode.OnRoundStateChangedServer -= ServerHandleRoundStateChanged;
            base.OnStopServer();
        }

        /// <summary>Серверная реакция на смену фазы. Только авторитетные действия, без визуала.</summary>
        [Server]
        private void ServerHandleRoundStateChanged(RoundState newState)
        {
            if (newState != RoundState.Setup) return;

            ReplenishWeaponsNetwork(false);
        }

        [Server]
        private void ReplenishWeaponsNetwork(bool forceAll = false)
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] ReplenishWeaponsNetwork. ForceAll: {forceAll}");
            
            if (_allSlots == null || _allSlots.Length == 0)
                _allSlots = GetComponentsInChildren<ArsenalSlotController>();

            for (int i = 0; i < _allSlots.Length; i++)
            {
                var slot = _allSlots[i];
                if (slot.WeaponData == null || slot.WeaponData.WeaponPrefab == null) continue;

                if (forceAll || slot.NeedsReplenishment())
                {
                    // Instantiate inactive so UxrGrabbableObject.Awake() doesn't fire yet
                    var prefab = slot.WeaponData.WeaponPrefab;
                    bool wasActive = prefab.activeSelf;
                    prefab.SetActive(false);
                    
                    GameObject spawned = Instantiate(prefab);
                    prefab.SetActive(wasActive);
                    
                    // Disable _autoCreateStartAnchor before activation —
                    // otherwise UXR creates a rogue "Auto Anchor" parent in Awake()
                    DisableAutoAnchor(spawned);
                    
                    spawned.SetActive(true);
                    
                    // Assign on server BEFORE Spawn —
                    // Mirror captures current parent in the spawn message.
                    slot.AssignNetworkItem(spawned);

                    NetworkServer.Spawn(spawned);
                    
                    // RPC for remote clients (host already assigned above)
                    RpcAssignItemToSlot(i, spawned.GetComponent<NetworkIdentity>());
                }
            }
        }

        [ClientRpc]
        private void RpcAssignItemToSlot(int slotIndex, NetworkIdentity spawnedIdentity)
        {
            // Host already did AssignNetworkItem on server side — skip
            if (isServer) return;
            
            if (slotIndex < 0 || slotIndex >= _allSlots.Length) return;
            if (spawnedIdentity == null) return;
            
            _allSlots[slotIndex].AssignNetworkItem(spawnedIdentity.gameObject);
        }
        
        /// <summary>
        /// Disables _autoCreateStartAnchor on UxrGrabbableObject via reflection.
        /// Must be called BEFORE the GameObject is activated (before Awake fires).
        /// Otherwise UXR creates a rogue "Auto Anchor" parent that pulls weapons out of slots.
        /// </summary>
        private static void DisableAutoAnchor(GameObject obj)
        {
            var grabbable = obj.GetComponent<UxrGrabbableObject>();
            if (grabbable == null) return;
            
            var field = typeof(UxrGrabbableObject).GetField(
                "_autoCreateStartAnchor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null)
                field.SetValue(grabbable, false);
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

            // Подписка серверного канала снимается и здесь: статическое событие переживает
            // объект, а уничтоженная стена в списке подписчиков — это MissingReference на
            // ближайшей смене фазы. Повторное отписывание безвредно.
            EliminationMode.OnRoundStateChangedServer -= ServerHandleRoundStateChanged;

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
        /// Opens the arsenal for a new prep phase.
        /// </summary>
        /// <param name="immediate">If true, snaps open instantly without animation.</param>
        [ContextMenu("Open Arsenal")]
        public void OpenArsenal(bool immediate = false)
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] OpenArsenal called! Current state: {_currentState}, Immediate: {immediate}");
            if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
            {
                GameLog.Warning(ArsenalLog, "[Arsenal] Arsenal is already open/opening.");
                return;
            }

            _currentState = ArsenalState.Opening;
            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal OPENING — prep phase starting...");

            if (_animator != null && !immediate)
            {
                _animator.PlayOpenSequence(OnOpenComplete);
            }
            else
            {
                // No animator or immediate requested
                if (_animator != null) _animator.SetOpenImmediate();
                OnOpenComplete();
            }
        }

        /// <summary>
        /// Forces the arsenal closed (e.g. from a game manager timeout).
        /// </summary>
        [ContextMenu("Force Close")]
        public void ForceClose()
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] ForceClose called! Current state: {_currentState}");
            if (_currentState == ArsenalState.Closed) return;
            BeginClosing();
        }

        /// <summary>
        /// Snaps the arsenal to closed state without animation (for initial setup).
        /// </summary>
        [ContextMenu("Set Closed Immediate")]
        public void SetClosedImmediate()
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] SetClosedImmediate called! Current state: {_currentState}");
            _currentState = ArsenalState.Closed;

            if (_allSlots == null || _allSlots.Length == 0)
                _allSlots = GetComponentsInChildren<ArsenalSlotController>();

            foreach (var slot in _allSlots)
            {
                slot.Lock();
            }

            if (_animator == null)
                _animator = GetComponent<ArsenalAnimator>();


            if (_animator != null)
                _animator.SetClosedImmediate();
        }

        // ── Private: Lifecycle ─────────────────────────────────

        private void OnOpenComplete()
        {
            _currentState = ArsenalState.Open;

            // Unlock all slots
            foreach (var slot in _allSlots)
            {
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

        /// <summary>
        /// Локальная реакция на фазу — визуальный жизненный цикл стены. Исполняется на каждой
        /// машине, включая выделенный сервер: фаза раздаётся из состояния, а не из ClientRpc.
        /// Авторитетное пополнение слотов сюда не входит — оно в <see cref="ServerHandleRoundStateChanged"/>.
        /// </summary>
        private void HandleRoundStateChanged(RoundState newState)
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] HandleRoundStateChanged received: {newState}. Arsenal State: {_currentState}");
            switch (newState)
            {
                case RoundState.Equipment:
                    if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing)
                    {
                        OpenArsenal(false);
                    }
                    else
                    {
                        GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] Ignoring Open command because state is already {_currentState}");
                    }
                    break;
                
                case RoundState.Countdown:
                case RoundState.Combat:
                    if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
                    {
                        ForceClose();
                    }
                    else
                    {
                        GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] Ignoring Close command because state is {_currentState}");
                    }
                    break;
            }
        }
    }
}

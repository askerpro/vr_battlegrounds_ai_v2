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
    ///
    /// <para>
    /// <b>Стена общая, состояние — серверное (T-15, находка NET-07).</b> Объект лежит
    /// в сцене карты, один на всех, и его содержимое уже авторитетно: слоты пополняет
    /// <see cref="ReplenishWeaponsNetwork"/> через <c>NetworkServer.Spawn</c>. Состояние
    /// стены живёт там же — в <c>SyncVar</c>, который пишет только сервер. Клиент не
    /// решает, открыта стена или закрыта: он получает состояние репликацией, а закрыть
    /// её просит командой. Подключившийся посреди фазы получает актуальное состояние
    /// начальным значением спавна.
    /// </para>
    /// <para>
    /// Готовность конкретного игрока — это <b>не</b> состояние стены: она живёт
    /// на <c>PlayerSession.HasGrabbedDogTag</c> и реплицируется отдельно.
    /// </para>
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

        /// <summary>
        /// Состояние стены. Единственный источник правды — эта переменная: пишет её
        /// сервер, клиенту она приезжает репликацией, в том числе начальным значением
        /// спавна. Представление раздаёт <see cref="ApplyStateLocal"/>.
        /// </summary>
        [SyncVar(hook = nameof(OnStateSynced))]
        private ArsenalState _currentState = ArsenalState.Closed;

        /// <summary>Состояние, уже применённое к представлению на этой машине.</summary>
        private ArsenalState _appliedState = ArsenalState.Closed;

        /// <summary>Применялось ли состояние хоть раз (отличает «ещё ничего» от «применили Closed»).</summary>
        private bool _stateApplied;

        public ArsenalState CurrentState => _currentState;
        private LogLevel ArsenalLog => GameSettings.Instance.LogLevelArsenal;

        /// <summary>
        /// Стена не участвует в репликации: объект не заспавнен Mirror. Так выглядит
        /// сцена, открытая без сети (проверка в редакторе), и стена без <c>sceneId</c>
        /// (NET-14). Тогда SyncVar никто не пришлёт, и состояние приходится вести самой —
        /// иначе стена не откроется никогда.
        /// </summary>
        private bool IsStandalone => !isServer && !isClient;

        /// <summary>Вправе ли эта машина менять состояние стены.</summary>
        private bool CanWriteState => isServer || IsStandalone;

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
            EnsureReferences();
        }

        /// <summary>
        /// Достаёт ссылки, которые не проставлены в инспекторе. Вызывается не только
        /// из <c>Awake</c>: состояние может приехать с сервера раньше, чем стена
        /// успеет стартовать штатно.
        /// </summary>
        private void EnsureReferences()
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
            // Приводим визуал к состоянию. На клиенте SyncVar мог приехать раньше Start —
            // тогда стена сразу встанет в актуальную позу, а не мигнёт закрытой.
            ApplyStateLocal(_currentState);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Поздний клиент получает состояние начальным значением спавна. Хук на нём
            // не сработает, если пришедшее значение совпало с дефолтом поля (Closed), —
            // раздаём явно. Тот же приём, что у фазы раунда в EliminationMode.
            ApplyStateLocal(_currentState);
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

        /// <summary>
        /// Серверная реакция на смену фазы: пополнение слотов и смена состояния стены.
        /// Оба действия авторитетны — состояние стены общее (T-15), поэтому и открытие,
        /// и закрытие по фазе объявляет сервер, а клиенты получают их репликацией.
        /// </summary>
        [Server]
        private void ServerHandleRoundStateChanged(RoundState newState)
        {
            if (newState == RoundState.Setup)
                ReplenishWeaponsNetwork(false);

            ApplyPhaseToState(newState);
        }

        /// <summary>
        /// Выключенный контейнер, под которым рождается оружие.
        ///
        /// Зачем. <c>UxrGrabbableObject.Awake()</c> обязан отработать уже с выключенным
        /// <c>_autoCreateStartAnchor</c>, иначе UXR создаёт лишний «Auto Anchor» и вытаскивает
        /// оружие из слота. Отложить <c>Awake</c> можно только неактивностью, а трогать ради
        /// этого сам префаб нельзя — он ассет (VR-04). Выключенный родитель даёт то же самое,
        /// не касаясь ассета.
        ///
        /// Живёт под стеной, поэтому уезжает вместе с ней при смене карты. Собственный
        /// трансформ значения не имеет: оружие выходит наружу через
        /// <c>SetParent(null, worldPositionStays: false)</c> и сохраняет локальные значения
        /// префаба, ровно как при обычном <c>Instantiate</c>.
        /// </summary>
        private Transform InactiveSpawnRoot
        {
            get
            {
                if (_inactiveSpawnRoot == null)
                {
                    GameObject root = new GameObject("InactiveSpawnRoot");
                    root.transform.SetParent(transform, false);
                    root.SetActive(false);
                    _inactiveSpawnRoot = root.transform;
                }

                return _inactiveSpawnRoot;
            }
        }

        private Transform _inactiveSpawnRoot;

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
                    // Инстанцируем под выключенным контейнером: у ребёнка выключенного
                    // родителя Awake не срабатывает, пока родителя не включат. Раньше для
                    // той же цели выключали сам префаб — то есть ассет, а не инстанс:
                    // в редакторе это метило его грязным, а исключение между выключением
                    // и обратным включением оставляло префаб выключенным навсегда (VR-04).
                    GameObject spawned = Instantiate(slot.WeaponData.WeaponPrefab, InactiveSpawnRoot, false);

                    // Disable _autoCreateStartAnchor before activation —
                    // otherwise UXR creates a rogue "Auto Anchor" parent in Awake()
                    DisableAutoAnchor(spawned);

                    // Порядок важен: сначала activeSelf (объект всё ещё спит под выключенным
                    // родителем), и только потом выход из контейнера — там и сработает Awake,
                    // уже с отключённым авто-якорем и на своём мировом трансформе.
                    spawned.SetActive(true);
                    spawned.transform.SetParent(null, false);

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
        /// Гасит приватное поле <c>UxrGrabbableObject._autoCreateStartAnchor</c> через рефлексию.
        /// Звать строго до активации объекта (до его <c>Awake</c>), иначе UltimateXR успевает
        /// создать «Auto Anchor»-родителя, который вытаскивает оружие из слота.
        ///
        /// Публичного способа отключить флаг в SDK нет. Зависимость от внутреннего имени
        /// молчаливая: при обновлении UltimateXR она не даст ошибки компиляции, поэтому
        /// ненайденное поле логируется как <c>Error</c>. Разбор и кандидат на вынос
        /// в <c>.Custom.cs</c> — Docs/UltimateXR/sdk-patches.md, раздел
        /// «Зависимости от приватных членов SDK».
        /// </summary>
        private static void DisableAutoAnchor(GameObject obj)
        {
            var grabbable = obj.GetComponent<UxrGrabbableObject>();
            if (grabbable == null) return;

            var field = typeof(UxrGrabbableObject).GetField(
                "_autoCreateStartAnchor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field == null)
            {
                GameLog.Error(
                    "[ArsenalWallController] В UxrGrabbableObject больше нет приватного поля " +
                    "\"_autoCreateStartAnchor\". UltimateXR обновился и переименовал его — " +
                    "оружие будет вылетать из слотов арсенала. См. Docs/UltimateXR/sdk-patches.md, " +
                    "раздел «Зависимости от приватных членов SDK».", obj);
                return;
            }

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
        /// Состояние стены общее, поэтому вызывать имеет смысл только на сервере
        /// (или когда стена вне сети).
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

            SetState(immediate ? ArsenalState.Open : ArsenalState.Opening);
        }

        /// <summary>
        /// Forces the arsenal closed (e.g. from a game manager timeout).
        /// </summary>
        [ContextMenu("Force Close")]
        public void ForceClose()
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] ForceClose called! Current state: {_currentState}");
            if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing) return;

            SetState(ArsenalState.Closing);
        }

        /// <summary>
        /// Snaps the arsenal to closed state without animation (for initial setup).
        /// </summary>
        [ContextMenu("Set Closed Immediate")]
        public void SetClosedImmediate()
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] SetClosedImmediate called! Current state: {_currentState}");

            SetState(ArsenalState.Closed);

            // Состояние могло уже быть Closed — тогда SetState пустой, а поза стены
            // всё равно обязана стать закрытой: метод для того и существует.
            ApplyStateLocal(ArsenalState.Closed, force: true);
        }

        // ── Private: состояние ─────────────────────────────────

        /// <summary>
        /// Меняет состояние стены. Писать его вправе только сервер: стена общая.
        /// У клиента вызов пустой — состояние приедет репликацией.
        /// </summary>
        private void SetState(ArsenalState newState)
        {
            if (!CanWriteState)
            {
                GameLog.Warning(ArsenalLog,
                    $"[Arsenal] Попытка сменить состояние стены на клиенте ({_currentState} -> {newState}). " +
                    "Состояние общее и его задаёт сервер — запрос игнорирован.");
                return;
            }

            if (_currentState == newState) return;

            _currentState = newState;

            // Хук SyncVar под ServerOnly не срабатывает (Mirror зовёт его в сеттере
            // только при NetworkServer.activeHost), поэтому раздаём вручную. Под хостом
            // раздача пройдёт дважды — на этот случай ApplyStateLocal идемпотентна.
            ApplyStateLocal(newState);
        }

        /// <summary>Хук SyncVar: состояние приехало с сервера.</summary>
        private void OnStateSynced(ArsenalState oldState, ArsenalState newState)
        {
            ApplyStateLocal(newState);
        }

        /// <summary>
        /// Применяет состояние к представлению этой машины ровно один раз на значение.
        /// </summary>
        /// <param name="state">Состояние, которое надо показать.</param>
        /// <param name="force">Применить, даже если это состояние уже применялось.</param>
        private void ApplyStateLocal(ArsenalState state, bool force = false)
        {
            if (!force && _stateApplied && _appliedState == state) return;

            EnsureReferences();

            bool initial = !_stateApplied;

            _stateApplied = true;
            _appliedState = state;

            switch (state)
            {
                case ArsenalState.Opening:
                    PlayOpening();
                    break;

                case ArsenalState.Open:
                    ApplyOpen();
                    break;

                case ArsenalState.Closing:
                    PlayClosing();
                    break;

                case ArsenalState.Closed:
                    // Стена стартует закрытой — это не событие закрытия, а исходная поза.
                    ApplyClosed(raiseEvent: !initial);
                    break;
            }
        }

        /// <summary>
        /// Реакция на фазу раунда: что стена должна показывать в этой фазе.
        /// Общая точка для серверного канала и для стены вне сети.
        /// </summary>
        private void ApplyPhaseToState(RoundState phase)
        {
            switch (phase)
            {
                case RoundState.Equipment:
                    if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing)
                        SetState(ArsenalState.Opening);
                    break;

                case RoundState.Countdown:
                case RoundState.Combat:
                    if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
                        SetState(ArsenalState.Closing);
                    break;
            }
        }

        // ── Private: Lifecycle ─────────────────────────────────

        private void PlayOpening()
        {
            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal OPENING — prep phase starting...");

            if (_animator != null)
            {
                _animator.PlayOpenSequence(HandleOpenSequenceFinished);
                return;
            }

            HandleOpenSequenceFinished();
        }

        /// <summary>
        /// Анимация открытия доиграла. Состояние <c>Open</c> объявляет сервер: если бы
        /// его ставила каждая машина сама, слоты разблокировались бы у всех в своё время
        /// и стена перестала бы быть общей.
        /// </summary>
        private void HandleOpenSequenceFinished()
        {
            SetStateIfAuthoritative(ArsenalState.Open);
        }

        private void ApplyOpen()
        {
            // Догоняем позу, если анимацию не проигрывали: поздний клиент, immediate,
            // либо состояние приехало раньше, чем доиграла своя анимация.
            if (_animator != null && !_animator.IsAnimating)
                _animator.SetOpenImmediate();

            foreach (var slot in _allSlots)
                slot.Unlock();

            if (_dogTagController != null)
                _dogTagController.ResetTag();

            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal OPENED — prep phase started.");
            OnArsenalOpened?.Invoke();
        }

        private void PlayClosing()
        {
            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal CLOSING — locking all slots...");

            // Слоты блокируются сразу: возвращать оружие на стену уже нельзя.
            foreach (var slot in _allSlots)
                slot.Lock();

            if (_dogTagController != null)
                _dogTagController.Disable();

            if (_animator != null)
            {
                _animator.PlayCloseSequence(HandleCloseSequenceFinished);
                return;
            }

            HandleCloseSequenceFinished();
        }

        private void HandleCloseSequenceFinished()
        {
            SetStateIfAuthoritative(ArsenalState.Closed);
        }

        private void ApplyClosed(bool raiseEvent)
        {
            foreach (var slot in _allSlots)
                slot.Lock();

            if (_dogTagController != null)
                _dogTagController.Disable();

            if (_animator != null && !_animator.IsAnimating)
                _animator.SetClosedImmediate();

            GameLog.Info(ArsenalLog, "[Arsenal] Arsenal CLOSED.");

            if (raiseEvent)
                OnArsenalClosed?.Invoke();
        }

        /// <summary>
        /// Продвигает состояние, если эта машина вправе его писать. Клиент здесь молчит:
        /// он ждёт значения с сервера, и предупреждение <see cref="SetState"/> было бы
        /// ложной тревогой — это штатный ход событий, а не попытка обойти сервер.
        /// </summary>
        private void SetStateIfAuthoritative(ArsenalState newState)
        {
            if (!CanWriteState) return;

            SetState(newState);
        }

        // ── Private: Жетон ─────────────────────────────────────

        private void HandleTagGrabbed(PlayerController player)
        {
            if (_currentState != ArsenalState.Open) return;

            // Готовность игрока — его собственное состояние, оно живёт на сессии
            // и реплицируется отдельно от стены.
            if (player != null && player.isOwned && player.Session != null)
            {
                player.Session.CmdSetDogTagGrabbed(true);
            }

            // Закрыть общую стену вправе только сервер, поэтому клиент шлёт команду.
            if (isClient && !isServer)
            {
                CmdCloseByDogTag();
                return;
            }

            ServerCloseByDogTag();
        }

        /// <summary>
        /// Жетон схватили у клиента. <c>requiresAuthority = false</c>: стена — объект
        /// сцены, владельца среди клиентов у неё нет.
        /// </summary>
        [Command(requiresAuthority = false)]
        private void CmdCloseByDogTag()
        {
            ServerCloseByDogTag();
        }

        /// <summary>
        /// Закрытие стены по жетону — общая точка для команды клиента, для хоста
        /// и для стены вне сети.
        ///
        /// Без атрибута <c>[Server]</c> намеренно: метод обслуживает и стену вне сети,
        /// где <c>NetworkServer.active</c> ложно и заглушка Mirror съела бы вызов.
        /// Право записи проверяет <see cref="SetState"/>.
        /// </summary>
        private void ServerCloseByDogTag()
        {
            if (_currentState != ArsenalState.Open && _currentState != ArsenalState.Opening) return;

            SetState(ArsenalState.Closing);
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
        /// Локальная реакция на фазу. Исполняется на каждой машине, включая выделенный
        /// сервер, но после T-15 состояние стены общее и задаёт его сервер — представление
        /// приезжает хуком <see cref="OnStateSynced"/>, а не отсюда.
        ///
        /// Здесь остался только случай <see cref="IsStandalone"/>: стена не заспавнена,
        /// реплицировать состояние некому, и вести его приходится самой.
        /// </summary>
        private void HandleRoundStateChanged(RoundState newState)
        {
            GameLog.Info(ArsenalLog, $"[Arsenal DEBUG] HandleRoundStateChanged received: {newState}. Arsenal State: {_currentState}");

            if (!IsStandalone) return;

            ApplyPhaseToState(newState);
        }
    }
}

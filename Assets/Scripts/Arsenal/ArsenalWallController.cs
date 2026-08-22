using UnityEngine;
using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Network;
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

        /// <summary>
        /// Что лежит в слотах: индекс слота → <c>netId</c> выданного предмета.
        ///
        /// <para>
        /// <b>Это состояние, а не событие (NET-23).</b> Раньше слот на клиенте узнавал
        /// о своём предмете единственным разовым <c>ClientRpc</c>, который сервер слал
        /// сразу после <c>NetworkServer.Spawn</c>. На выделенном сервере этот момент
        /// приходится на середину загрузки карты у клиента: тот ещё не <c>isReady</c>,
        /// а <c>ClientRpc</c> уходит только готовым наблюдателям. Предметы доезжали
        /// обычными спавн-сообщениями — уже без всякой привязки, и слот у клиента
        /// оставался пустым с точки зрения кода: <c>Lock()</c> не запирал лежащее в нём
        /// оружие, а вся будущая экономика опиралась бы на пустоту.
        /// </para>
        ///
        /// <para>
        /// Состояние переживает и позднее подключение, и смену карты: клиент получает
        /// словарь начальным значением спавна и раздаёт его в <see cref="OnStartClient"/>.
        /// Тот же вывод, что у фазы раунда и у состояния стены выше.
        /// </para>
        /// </summary>
        private readonly SyncDictionary<int, uint> _slotItems = new SyncDictionary<int, uint>();

        /// <summary>
        /// Слоты, чью привязку клиент получил, но ещё не смог применить: предмет с таким
        /// <c>netId</c> у клиента пока не заспавнен. Порядок спавн-сообщений и дельт
        /// <c>SyncDictionary</c> Mirror не согласовывает, поэтому ждать приходится явно.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<int> _pendingSlotBindings =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>Буфер разобранных привязок: не хочется плодить мусор в <c>Update</c>.</summary>
        private readonly System.Collections.Generic.List<int> _resolvedSlotBindings =
            new System.Collections.Generic.List<int>();

        /// <summary>Состояние, уже применённое к представлению на этой машине.</summary>
        private ArsenalState _appliedState = ArsenalState.Closed;

        /// <summary>Применялось ли состояние хоть раз (отличает «ещё ничего» от «применили Closed»).</summary>
        private bool _stateApplied;

        public ArsenalState CurrentState => _currentState;

        /// <summary>
        /// Слоты стены в том порядке, в каком их адресует <see cref="_slotItems" />.
        /// Порядок одинаков во всех процессах — на этом держится вся сетевая выдача оружия,
        /// потому что по сети едет индекс слота, а не ссылка на него.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<ArsenalSlotController> Slots
        {
            get
            {
                EnsureReferences();
                return _allSlots;
            }
        }

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

            // На хосте предметы разложил сервер, второй раз не надо.
            if (isServer) return;

            _slotItems.OnChange += HandleSlotItemChanged;

            foreach (var pair in _slotItems)
                QueueSlotBinding(pair.Key);

            ResolvePendingSlotBindings();
        }

        public override void OnStopClient()
        {
            if (!isServer)
                _slotItems.OnChange -= HandleSlotItemChanged;

            base.OnStopClient();
        }

        /// <summary>
        /// Привязка «предмет → слот» приехала или изменилась. Применить её прямо здесь
        /// получается не всегда: предмет мог ещё не заспавниться у клиента.
        /// </summary>
        private void HandleSlotItemChanged(SyncIDictionary<int, uint>.Operation op, int slotIndex, uint oldItemNetId)
        {
            if (op == SyncIDictionary<int, uint>.Operation.OP_ADD ||
                op == SyncIDictionary<int, uint>.Operation.OP_SET)
            {
                QueueSlotBinding(slotIndex);
            }
        }

        private void QueueSlotBinding(int slotIndex)
        {
            EnsureReferences();

            if (slotIndex < 0 || slotIndex >= _allSlots.Length) return;

            _pendingSlotBindings.Add(slotIndex);
        }

        private void Update()
        {
            if (_pendingSlotBindings.Count > 0)
                ResolvePendingSlotBindings();
        }

        /// <summary>
        /// Пытается выдать слотам предметы, которые уже приехали к клиенту. То, что ещё
        /// не приехало, остаётся в очереди до следующего кадра.
        /// </summary>
        private void ResolvePendingSlotBindings()
        {
            if (_pendingSlotBindings.Count == 0) return;

            _resolvedSlotBindings.Clear();

            foreach (int slotIndex in _pendingSlotBindings)
            {
                if (!_slotItems.TryGetValue(slotIndex, out uint itemNetId))
                {
                    // Привязку успели снять — ждать больше нечего.
                    _resolvedSlotBindings.Add(slotIndex);
                    continue;
                }

                if (!NetworkClient.spawned.TryGetValue(itemNetId, out NetworkIdentity item) || item == null)
                    continue;

                // Mirror кладёт объект в список заспавненных раньше, чем включает его:
                // до включения Awake не отработал. Привязываться в этот момент нельзя —
                // UxrGrabbableObject.Awake выставляет CurrentAnchor из своего стартового
                // якоря (у нас пустого) и стёр бы учёт, который заводит AssignNetworkItem.
                // Тогда слот у клиента не узнал бы об уходе предмета (NET-17), а положение
                // предмета переписал бы спавн-пакет.
                if (!item.gameObject.activeInHierarchy)
                    continue;

                ArsenalSlotController slot = _allSlots[slotIndex];

                if (slot != null && slot.CurrentItem != item.gameObject)
                    slot.AssignNetworkItem(item.gameObject);

                _resolvedSlotBindings.Add(slotIndex);
            }

            foreach (int slotIndex in _resolvedSlotBindings)
                _pendingSlotBindings.Remove(slotIndex);

            _resolvedSlotBindings.Clear();
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

        [Server]
        private void ReplenishWeaponsNetwork(bool forceAll = false)
        {
            GameLog.Arsenal.Info($"[Arsenal DEBUG] ReplenishWeaponsNetwork. ForceAll: {forceAll}");

            if (_allSlots == null || _allSlots.Length == 0)
                _allSlots = GetComponentsInChildren<ArsenalSlotController>();

            for (int i = 0; i < _allSlots.Length; i++)
            {
                var slot = _allSlots[i];
                if (slot.WeaponData == null || slot.WeaponData.WeaponPrefab == null) continue;

                if (forceAll || slot.NeedsReplenishment())
                {
                    // Создание инстанса ведёт сетевой слой: он же гасит «Auto Anchor»
                    // до Awake и выравнивает UniqueId после спавна. Стена о идентичности
                    // предметов не знает и знать не должна — см. NetworkUxrIdentity.
                    GameObject spawned = NetworkUxrIdentity.CreateInstance(slot.WeaponData.WeaponPrefab);
                    if (spawned == null) continue;

                    spawned.SetActive(true);

                    // Привязка к слоту — до Spawn: Mirror кладёт в спавн-сообщение
                    // текущий трансформ объекта.
                    slot.AssignNetworkItem(spawned);

                    NetworkUxrIdentity.SpawnServerObject(spawned);

                    // Привязка «предмет → слот» — состояние, а не сообщение (NET-23).
                    // Разовый ClientRpc здесь не работал по построению: на выделенном
                    // сервере выдача приходится на середину загрузки карты у клиента,
                    // когда тот ещё не isReady, и до него рассылка не доходит вовсе.
                    NetworkIdentity identity = spawned.GetComponent<NetworkIdentity>();

                    if (identity != null && identity.netId != 0)
                        _slotItems[i] = identity.netId;
                }
            }
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
            GameLog.Arsenal.Info($"[Arsenal DEBUG] OpenArsenal called! Current state: {_currentState}, Immediate: {immediate}");
            if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
            {
                GameLog.Arsenal.Warning("[Arsenal] Arsenal is already open/opening.");
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
            GameLog.Arsenal.Info($"[Arsenal DEBUG] ForceClose called! Current state: {_currentState}");
            if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing) return;

            SetState(ArsenalState.Closing);
        }

        /// <summary>
        /// Snaps the arsenal to closed state without animation (for initial setup).
        /// </summary>
        [ContextMenu("Set Closed Immediate")]
        public void SetClosedImmediate()
        {
            GameLog.Arsenal.Info($"[Arsenal DEBUG] SetClosedImmediate called! Current state: {_currentState}");

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
                GameLog.Arsenal.Warning(
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
            GameLog.Arsenal.Info("[Arsenal] Arsenal OPENING — prep phase starting...");

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

            GameLog.Arsenal.Info("[Arsenal] Arsenal OPENED — prep phase started.");
            OnArsenalOpened?.Invoke();
        }

        private void PlayClosing()
        {
            GameLog.Arsenal.Info("[Arsenal] Arsenal CLOSING — locking all slots...");

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

            GameLog.Arsenal.Info("[Arsenal] Arsenal CLOSED.");

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
            GameLog.Arsenal.Info($"[Arsenal DEBUG] HandleRoundStateChanged received: {newState}. Arsenal State: {_currentState}");

            if (!IsStandalone) return;

            ApplyPhaseToState(newState);
        }
    }
}

using UnityEngine;
using Mirror;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Economy;
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
    /// решает, открыта стена или закрыта: он получает состояние репликацией.
    /// Подключившийся посреди фазы получает актуальное состояние
    /// начальным значением спавна.
    /// </para>
    /// <para>
    /// Готовность конкретного игрока — это <b>не</b> состояние стены: она живёт
    /// на <c>PlayerSession.ReadyState</c> и реплицируется отдельно.
    /// </para>
    /// <para>
    /// <b>Стену закрывает фаза, а не жетон (T-29, дефект RDY-01).</b> Пока состояние
    /// стены было локальным, «взял жетон — закрыл арсенал» работало как личное действие.
    /// После T-15 стена стала общей, и то же правило означало уже другое: первый
    /// взявший жетон закрывал арсенал <b>всем</b>, и второй игрок оставался
    /// без снаряжения. Теперь жетон объявляет готовность своего игрока
    /// (<c>PlayerSession.CmdSetReady</c>), а закрывается стена там же, где и раньше, —
    /// по правилу режима при выходе из <c>Equipment</c>. Готовность всех
    /// живых игроков и есть условие этого выхода, поэтому «закрылась по общей готовности»
    /// и «закрылась по началу отсчёта» — один и тот же момент, а правило остаётся одно.
    /// </para>
    /// <para>
    /// <b>Стена не знает конкретных режимов.</b> Открыта ли она, нужен ли жетон, заменять
    /// ли пропавшее оружие — объявляет активный режим через
    /// <see cref="GameMode.ArsenalRules"/>, а стена сверяется с этим каждый кадр
    /// (<see cref="ApplyModeRules"/>). Elimination отвечает по фазе раунда, лобби
    /// (<see cref="WarmupMode"/>) — «открыт всегда, без жетона». Разовое пополнение пустых
    /// слотов (новый раунд, новый режим на карте) приходит событием экземпляра активного
    /// режима <see cref="GameMode.ArsenalRefillRequestedServer"/>.
    /// Режима нет — правил нет, стена стоит как стояла.
    /// </para>
    /// <para>
    /// <b>Владелец стены (T-45).</b> Стена общая как объект, но при экономике матча покупает с неё
    /// один игрок — владелец (<see cref="OwnerSessionNetId"/>, <c>SyncVar</c>, пишет сервер). Кто чей,
    /// решает <c>ArsenalOwnershipPolicy</c> на префабе режима по зоне спавна стены
    /// (<see cref="ArsenalOwnership"/>). Стена по деньгам владельца раздаёт слотам предложение
    /// (<see cref="RefreshOffers"/>): подсветка — каждая машина, запрет хвата — сервер.
    /// Списывает деньги <c>ArsenalCheckout</c> по серверным событиям <see cref="ItemTakenServer"/>.
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

        /// <summary>
        /// Последнее оружие, выданное каждым слотом (сервер). По нему стена отличает
        /// «ствол унесли» от «ствол пропал» — пополнять нужно только второе.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, NetworkIdentity> _homeItems =
            new System.Collections.Generic.Dictionary<int, NetworkIdentity>();

        /// <summary>Буфер разобранных привязок: не хочется плодить мусор в <c>Update</c>.</summary>
        private readonly System.Collections.Generic.List<int> _resolvedSlotBindings =
            new System.Collections.Generic.List<int>();

        /// <summary>Состояние, уже применённое к представлению на этой машине.</summary>
        private ArsenalState _appliedState = ArsenalState.Closed;

        /// <summary>Применялось ли состояние хоть раз (отличает «ещё ничего» от «применили Closed»).</summary>
        private bool _stateApplied;

        [Header("Правила режима")]
        [Tooltip("Через сколько секунд слот, чьё оружие пропало совсем (уничтожено, выпало из мира), " +
                 "получает новое — если режим этого требует (ArsenalRules.ReplacesLostWeapons, лобби).")]
        [Min(0f)]
        [SerializeField] private float _lostWeaponReplaceDelay = 2f;

        /// <summary>Сколько секунд у стены есть пропавший слот (сервер).</summary>
        private float _lostSlotTime;

        /// <summary>
        /// Какое «нужен ли жетон» уже применено к жетону на этой машине. null — ещё ничего:
        /// режим может приехать к клиенту позже, чем стена показала своё состояние.
        /// </summary>
        private bool? _appliedDogTagInUse;

        public ArsenalState CurrentState => _currentState;

        /// <summary>
        /// <c>netId</c> сессии игрока, за которым закреплена стена (T-45); 0 — ничья. Пишет только сервер
        /// (<see cref="ServerSetOwner"/>), поздний клиент получает начальным значением спавна.
        /// </summary>
        [SyncVar] private uint _ownerSessionNetId;

        public uint OwnerSessionNetId => _ownerSessionNetId;

        /// <summary>Сессия владельца на этой машине или null (ничья, сессия ещё не приехала).</summary>
        public PlayerSession OwnerSession
        {
            get
            {
                if (_ownerSessionNetId == 0) return null;
                NetworkIdentity identity = Mirror.Utils.GetSpawnedInServerOrClient(_ownerSessionNetId);
                return identity != null ? identity.GetComponent<PlayerSession>() : null;
            }
        }

        /// <summary>Закрепляет стену за игроком (0 — снять). Только сервер.</summary>
        [Server]
        public void ServerSetOwner(uint sessionNetId)
        {
            if (_ownerSessionNetId == sessionNetId) return;
            _ownerSessionNetId = sessionNetId;
            GameLog.Arsenal.Info($"[Arsenal] Стена '{name}' закреплена за сессией {sessionNetId}.", this);
        }

        /// <summary>Сервер: со стены унесли предмет (стена, слот, предмет, рука или null). Слушает <c>ArsenalCheckout</c>.</summary>
        public static event System.Action<ArsenalWallController, ArsenalSlotController, UxrGrabbableObject, UxrGrabber> ItemTakenServer;

        /// <summary>Сервер: на стену руками повесили предмет. Слушает <c>ArsenalCheckout</c> (возврат денег).</summary>
        public static event System.Action<ArsenalWallController, ArsenalSlotController, UxrGrabbableObject> ItemReturnedServer;

        /// <summary>Последнее разданное слотам предложение — чтобы не раздавать каждый кадр.</summary>
        private (bool economy, uint owner, bool known, int money) _appliedOffer = (false, 0u, false, -1);

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

            // Табло денег владельца — представление стены, создаётся само (префаб стены общий для карт).
            if (GetComponent<ArsenalWalletDisplay>() == null) gameObject.AddComponent<ArsenalWalletDisplay>();
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

            ApplyModeRules(Time.deltaTime);
            RefreshOffers();
        }

        /// <summary>
        /// Раздаёт слотам предложение по деньгам владельца (T-45): без экономики — бесплатно,
        /// иначе по карману / дорого / ничья. Каждая машина считает сама по реплицированным
        /// владельцу и деньгам; пустой, пока ничего не изменилось. Зовётся из <c>Update</c> и тестов.
        /// </summary>
        public void RefreshOffers()
        {
            MatchEconomy economy = MatchEconomy.Current;
            int money = 0;
            bool known = false;

            if (economy != null)
            {
                PlayerSession owner = OwnerSession;
                known = owner != null && economy.TryGetMoney(owner, out money);
            }

            var signature = (economy != null, _ownerSessionNetId, known, money);
            if (signature == _appliedOffer) return;
            _appliedOffer = signature;

            EnsureReferences();
            foreach (ArsenalSlotController slot in _allSlots)
            {
                if (slot != null)
                    slot.ApplyOffer(ArsenalPurchaseRules.Evaluate(economy != null, known, money, slot.Price));
            }
        }

        /// <summary>
        /// Сервер отменяет захват: предмет взял не владелец или не хватило денег (клиенту не доверяем).
        /// Рука отпускает (синхронно для всех), ствол возвращается в свой слот.
        /// </summary>
        [Server]
        public void ServerRejectTake(UxrGrabbableObject item)
        {
            if (item == null) return;

            if (UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(item))
                UxrGrabManager.Instance.ReleaseGrabs(item, true);

            WeaponComponent weapon = item.GetComponentInParent<WeaponComponent>();
            if (weapon != null && !ServerReturnHome(weapon))
                GameLog.Arsenal.Warning($"[Arsenal] Отменённую покупку '{weapon.name}' не удалось вернуть в слот.", this);
        }

        /// <summary>
        /// Один шаг сверки стены с правилами активного режима (<see cref="GameMode.ArsenalRules"/>).
        /// Зовётся каждый кадр из <c>Update</c> и напрямую из EditMode-тестов.
        ///
        /// <list type="bullet">
        /// <item>Жетон: нужен ли он — на каждой машине (это представление).</item>
        /// <item>Открыта/закрыта — только там, где стена вправе писать состояние
        ///       (<see cref="CanWriteState"/>: сервер или стена вне сети); клиенты
        ///       получают его репликацией.</item>
        /// <item>Замена пропавшего оружия — только сервер: выдача сетевая.</item>
        /// </list>
        /// Режима нет — стена не трогается: так ведёт себя карта до старта матча.
        /// </summary>
        public void ApplyModeRules(float deltaTime)
        {
            RefreshDogTagUse();

            GameMode mode = ActiveMode;
            if (mode == null)
            {
                _lostSlotTime = 0f;
                return;
            }

            ArsenalRules rules = mode.ArsenalRules;

            if (CanWriteState)
                ApplyOpenRule(rules.IsOpen);

            KeepLostSlotsReplaced(rules.ReplacesLostWeapons, deltaTime);
        }

        /// <summary>Активный режим этой машины или null (нет сцены с режимом, режим не стартовал).</summary>
        private static GameMode ActiveMode
        {
            get
            {
                Managers.MapReferee manager = Managers.MapReferee.Instance;
                if (manager == null) return null;

                GameMode mode = manager.ActiveGameMode;
                return mode != null ? mode : null;
            }
        }

        /// <summary>Приводит состояние к «открыта»/«закрыта», не перебивая идущую анимацию того же знака.</summary>
        private void ApplyOpenRule(bool shouldBeOpen)
        {
            if (shouldBeOpen)
            {
                if (_currentState == ArsenalState.Closed || _currentState == ArsenalState.Closing)
                    SetState(ArsenalState.Opening);
            }
            else if (_currentState == ArsenalState.Open || _currentState == ArsenalState.Opening)
            {
                SetState(ArsenalState.Closing);
            }
        }

        /// <summary>
        /// Выдаёт замену слоту, чьё оружие пропало совсем, спустя <see cref="_lostWeaponReplaceDelay"/>.
        /// Унесённое оружие слот не заменяет: оно вернётся само (уборщик предметов на полу
        /// возвращает его домой), и число стволов остаётся постоянным.
        /// </summary>
        private void KeepLostSlotsReplaced(bool replaces, float deltaTime)
        {
            if (!replaces || !isServer || !HasLostSlots())
            {
                _lostSlotTime = 0f;
                return;
            }

            _lostSlotTime += deltaTime;
            if (_lostSlotTime < _lostWeaponReplaceDelay) return;

            _lostSlotTime = 0f;
            ServerReplenishLostSlots();
        }

        /// <summary>
        /// Показывает или убирает жетон, когда меняется ответ «нужен ли он». Отдельно от
        /// состояния стены: режим может приехать к клиенту позже, чем стена открылась,
        /// и тогда жетон, показанный «по умолчанию», обязан исчезнуть.
        /// </summary>
        private void RefreshDogTagUse()
        {
            if (_dogTagController == null) return;

            bool inUse = IsDogTagInUse();
            if (_appliedDogTagInUse == inUse) return;
            _appliedDogTagInUse = inUse;

            _dogTagController.SetInUse(inUse);

            if (!inUse || _currentState != ArsenalState.Open)
                _dogTagController.Disable();
            else
                _dogTagController.ResetTag();
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

            // Разовое пополнение пустых слотов объявляет режим (Elimination — к новому
            // раунду) серверным событием базового GameMode. Серверный канал, а не
            // клиентский обработчик: на выделенном сервере клиентская ветка не исполнялась
            // никогда (NET-06). Событие — у экземпляра режима, а режим на карте меняется
            // на месте (разминка → матч → разминка): стена следит за сменой активного
            // режима и переподписывается.
            Managers.MapReferee.ActiveGameModeChangedLocal -= HandleActiveModeChanged;
            Managers.MapReferee.ActiveGameModeChangedLocal += HandleActiveModeChanged;
            HandleActiveModeChanged(ActiveMode);

            ReplenishWeaponsNetwork(true);
        }

        public override void OnStopServer()
        {
            Managers.MapReferee.ActiveGameModeChangedLocal -= HandleActiveModeChanged;
            HandleActiveModeChanged(null);
            base.OnStopServer();
        }

        /// <summary>Режим, на чей запрос пополнения стена подписана сейчас.</summary>
        private GameMode _refillSource;

        /// <summary>Активный режим сменился — подписка на его запрос пополнения.</summary>
        private void HandleActiveModeChanged(GameMode mode)
        {
            if (_refillSource == mode) return;

            if (_refillSource != null) _refillSource.ArsenalRefillRequestedServer -= ServerRefillEmptySlots;
            _refillSource = mode;
            if (_refillSource != null) _refillSource.ArsenalRefillRequestedServer += ServerRefillEmptySlots;
        }

        /// <summary>Режим попросил пополнить пустые слоты (новый раунд).</summary>
        [Server]
        private void ServerRefillEmptySlots()
        {
            ReplenishWeaponsNetwork(false);
        }

        /// <summary>
        /// Пополняет слоты, чьё оружие пропало совсем (уничтожено, выпало из мира).
        /// Слот, чей ствол жив — в руке, в кобуре, на полу, — ждёт его возвращения
        /// (<see cref="ServerReturnHome"/>), а не получает дубль. Так число стволов
        /// вне раунда постоянно. Зовёт сама стена, если режим этого требует
        /// (<see cref="ArsenalRules.ReplacesLostWeapons"/>, лобби).
        /// </summary>
        [Server]
        public void ServerReplenishLostSlots()
        {
            ReplenishSlotsWhere(IsSlotLost);
        }

        /// <summary>Есть ли слот, чьё оружие пропало и которому нужна замена.</summary>
        public bool HasLostSlots()
        {
            EnsureReferences();

            for (int i = 0; i < _allSlots.Length; i++)
            {
                if (IsSlotLost(i)) return true;
            }

            return false;
        }

        /// <summary>
        /// Слот пуст, и выданного им оружия больше нет. Учёт выданного ведёт только
        /// сервер, поэтому у клиента ответ всегда «нет».
        /// </summary>
        private bool IsSlotLost(int index)
        {
            ArsenalSlotController slot = _allSlots[index];
            if (slot == null || !slot.IsConfigured || !slot.NeedsReplenishment()) return false;

            // Уничтоженный объект Unity сравнивается с null как null.
            return !_homeItems.TryGetValue(index, out NetworkIdentity item) || item == null;
        }

        /// <summary>
        /// Возвращает оружие в слот, который его выдал (<see cref="WeaponComponent.HomeSlot"/>).
        /// Путь тот же, что у выдачи: привязка «предмет → слот» в <c>_slotItems</c>,
        /// и каждая машина раскладывает предмет сама — поза на стене та же, что у свежего.
        /// </summary>
        /// <returns>false — дом не на этой стене, занят, или предмет в руке.</returns>
        [Server]
        public bool ServerReturnHome(WeaponComponent weapon)
        {
            if (weapon == null || weapon.HomeSlot == null) return false;

            EnsureReferences();

            int index = System.Array.IndexOf(_allSlots, weapon.HomeSlot);
            if (index < 0 || weapon.HomeSlot.IsItemPresent) return false;

            UltimateXR.Manipulation.UxrGrabbableObject grabbable =
                weapon.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
            if (grabbable != null && grabbable.IsBeingGrabbed) return false;

            NetworkIdentity identity = weapon.GetComponent<NetworkIdentity>();
            if (identity == null || identity.netId == 0) return false;

            weapon.HomeSlot.AssignNetworkItem(weapon.gameObject);

            // OP_SET уходит и при том же значении — клиенты перепривяжут предмет.
            _slotItems[index] = identity.netId;

            GameLog.Arsenal.Info($"[Arsenal] '{weapon.name}' вернулся в свой слот '{weapon.HomeSlot.name}'.", this);
            return true;
        }

        [Server]
        private void ReplenishWeaponsNetwork(bool forceAll = false)
        {
            GameLog.Arsenal.Info($"[Arsenal DEBUG] ReplenishWeaponsNetwork. ForceAll: {forceAll}");

            ReplenishSlotsWhere(i => forceAll || _allSlots[i].NeedsReplenishment());
        }

        [Server]
        private void ReplenishSlotsWhere(System.Func<int, bool> needsWeapon)
        {
            if (_allSlots == null || _allSlots.Length == 0)
                _allSlots = GetComponentsInChildren<ArsenalSlotController>();

            for (int i = 0; i < _allSlots.Length; i++)
            {
                var slot = _allSlots[i];
                if (slot.WeaponData == null || slot.WeaponData.WeaponPrefab == null) continue;

                if (needsWeapon(i))
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
                    {
                        _slotItems[i] = identity.netId;
                        _homeItems[i] = identity;
                    }
                }
            }
        }

        private void OnEnable()
        {
            if (_dogTagController != null)
                _dogTagController.OnTagGrabbed += HandleTagGrabbed;

            foreach (var slot in _allSlots)
            {
                slot.OnItemTaken    += HandleItemTaken;
                slot.OnItemReturned += HandleItemReturned;
                slot.ItemTakenBy    += HandleItemTakenBy;
                slot.ItemReturnedBy += HandleItemReturnedBy;
            }
        }

        private void OnDisable()
        {
            // Подписка серверного канала снимается и здесь: статическое событие переживает
            // объект, а уничтоженная стена в списке подписчиков — это MissingReference на
            // ближайшем запросе пополнения. Повторное отписывание безвредно.
            Managers.MapReferee.ActiveGameModeChangedLocal -= HandleActiveModeChanged;
            if (_refillSource != null) _refillSource.ArsenalRefillRequestedServer -= ServerRefillEmptySlots;
            _refillSource = null;

            if (_dogTagController != null)
                _dogTagController.OnTagGrabbed -= HandleTagGrabbed;

            foreach (var slot in _allSlots)
            {
                slot.OnItemTaken    -= HandleItemTaken;
                slot.OnItemReturned -= HandleItemReturned;
                slot.ItemTakenBy    -= HandleItemTakenBy;
                slot.ItemReturnedBy -= HandleItemReturnedBy;
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
            {
                bool tagInUse = IsDogTagInUse();
                _dogTagController.SetInUse(tagInUse);
                if (tagInUse) _dogTagController.ResetTag();
            }

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
            {
                _dogTagController.SetInUse(IsDogTagInUse());
                _dogTagController.Disable();
            }

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

        /// <summary>
        /// Нужен ли жетон. Отвечает активный режим (<see cref="ArsenalRules.UsesReadinessTag"/>)
        /// из своих полей и реплицируемого состояния, поэтому клиент знает ответ так же,
        /// как сервер. Режима нет (карта до старта матча) — жетон показываем, как было
        /// до появления правила.
        /// </summary>
        private bool IsDogTagInUse()
        {
            GameMode mode = ActiveMode;
            return mode == null || mode.ArsenalRules.UsesReadinessTag;
        }

        /// <summary>
        /// Жетон взят: игрок объявляет готовность к раунду. Стену это не закрывает —
        /// см. разбор RDY-01 в комментарии к классу.
        ///
        /// Команду шлёт владелец сессии — сам игрок. Стена о готовности не знает ничего
        /// сверх того, что переслала жест по адресу: решение «пора закрываться» принимает
        /// сервер, и приходит оно фазой раунда.
        /// </summary>
        private void HandleTagGrabbed(PlayerController player)
        {
            if (_currentState != ArsenalState.Open) return;

            if (player == null || player.Session == null)
            {
                GameLog.Arsenal.Info(
                    "[Arsenal] Жетон взят, но игрок не определён — объявлять готовность не за кого.");
                return;
            }

            if (!player.isOwned)
            {
                // Чужой аватар: его готовность объявит его собственная машина.
                return;
            }

            // Готовность игрока — его собственное состояние, оно живёт на сессии
            // и реплицируется отдельно от стены. Жест записываем тоже: он остаётся
            // отдельным фактом и служит обратным каналом сценариям яруса C.
            player.Session.CmdSetDogTagGrabbed(true);
            player.Session.CmdSetReady(true);

            GameLog.Arsenal.Info(
                $"[Arsenal] Жетон взят игроком {player.Session.PlayerName} — объявлена готовность к раунду.");
        }

        // ── Private: Slot Events ───────────────────────────────

        private void HandleItemTaken(ArsenalSlotController slot)
        {
            OnSlotChanged?.Invoke(slot, true);
        }

        /// <summary>Предмет унесли — серверу на списание денег. Клиенты ничего не решают.</summary>
        private void HandleItemTakenBy(ArsenalSlotController slot, UxrGrabbableObject item, UxrGrabber grabber)
        {
            if (isServer) ItemTakenServer?.Invoke(this, slot, item, grabber);
        }

        /// <summary>Предмет повесили обратно — серверу на возврат денег.</summary>
        private void HandleItemReturnedBy(ArsenalSlotController slot, UxrGrabbableObject item)
        {
            if (isServer) ItemReturnedServer?.Invoke(this, slot, item);
        }

        private void HandleItemReturned(ArsenalSlotController slot)
        {
            // Ствол повесили руками — привязка «предмет → слот» обязана это знать:
            // по ней слот занят у позднего клиента, и по ней стена не выдаёт дубль.
            if (isServer && slot != null && slot.CurrentItem != null)
            {
                int index = System.Array.IndexOf(_allSlots, slot);
                NetworkIdentity identity = slot.CurrentItem.GetComponent<NetworkIdentity>();

                if (index >= 0 && identity != null && identity.netId != 0)
                    _slotItems[index] = identity.netId;
            }

            OnSlotChanged?.Invoke(slot, false);
        }
    }
}

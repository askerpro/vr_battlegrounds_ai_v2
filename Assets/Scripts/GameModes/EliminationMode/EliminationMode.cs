using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим "Ликвидация": карта → раунды, как в CS.
    ///
    /// Счёт карты — выигранные раунды за обе половины вместе (базовый счёт <see cref="GameMode"/>).
    /// После <c>_roundsPerHalf</c> раундов команды меняются сторонами; своего победителя
    /// у половины нет. Карта кончается, когда у команды больше половины всех раундов,
    /// или когда сыграны все; равный счёт — ничья.
    ///
    /// Фазы одного раунда ведёт <see cref="RoundPhases"/> — чистый C#-класс, который режим
    /// создаёт через new и тикает из <see cref="ServerTick"/>. Счёт, номер раунда и переход
    /// к следующему раунду — здесь, как и вся сетевая синхронизация (SyncVar, ClientRpc).
    /// </summary>
    public enum EliminationState
    {
        WaitingForPlayers,
        Active,
        Finished
    }

    public class EliminationMode : GameMode
    {
        [Header("Настройки")]
        [Tooltip("Раундов в половине. Всего на карте вдвое больше: после половины команды меняются " +
                 "сторонами (зонами спавна), счёт раундов общий, как в CS. Карту берёт тот, кто первым " +
                 "выиграл больше половины всех раундов; равный счёт — ничья.")]
        [FormerlySerializedAs("_roundsPerSet")]
        [Min(1)]
        [SerializeField] private int _roundsPerHalf = 3;

        [Tooltip("Длительность обратного отсчёта перед раундом (сек).")]
        [SerializeField] private float _countdownDuration = 3f;

        [Tooltip("Максимальная длительность раунда (сек).")]
        [SerializeField] private float _roundDuration = 90f;

        [Header("Готовность к раунду (T-29)")]
        [Tooltip("Чем кончается закупка. Readiness — ждать готовности всех (жетон), предел ниже " +
                 "— страховка. Timer — закупка длится ровно предел ниже, жетон скрыт.")]
        [SerializeField] private RoundStartRule _roundStartRule = RoundStartRule.Readiness;

        [Tooltip("Readiness: сколько секунд фаза Equipment ждёт готовности всех живых игроков, " +
                 "ноль и меньше — ждать без предела. Timer: длительность закупки, должна быть больше нуля.")]
        [SerializeField] private float _readinessTimeLimit = RoundReadiness.DefaultTimeLimit;

        [Tooltip("Что делать, когда предел ожидания истёк, а готовы не все.")]
        [SerializeField] private RoundReadinessTimeoutRule _readinessTimeoutRule =
            RoundReadinessTimeoutRule.AutoReady;

        // Счёт матча теперь синхронизируется через базовый класс GameMode

        // Состояние раунда — синхронизируется для UI (таймер, countdown)
        [SyncVar] private EliminationState _state = EliminationState.WaitingForPlayers;

        /// <summary>
        /// Фаза раунда. Единственный источник правды — эта переменная, а не сетевое сообщение:
        /// вновь подключившийся клиент получает её начальным значением спавна.
        /// Раздачу подписчикам делает <see cref="ApplyRoundPhaseLocal"/>.
        /// </summary>
        [SyncVar(hook = nameof(OnRoundPhaseSynced))]
        private RoundPhase _roundPhase = RoundPhase.Setup;

        /// <summary>
        /// Момент начала текущей фазы по <see cref="NetworkTime"/>. Пишется ровно один раз
        /// на фазу, остаток каждая машина считает локально и потому рисует плавно.
        ///
        /// Раньше в сеть уезжало само значение таймера, и присваивалось оно в тике каждый
        /// кадр: объект был грязным до конца матча, а в HUD значение приходило ступеньками
        /// раз в <c>syncInterval</c> (NET-09).
        /// </summary>
        [SyncVar] private double _phaseStartTime;

        /// <summary>
        /// Сколько секунд боя израсходовал раунд. Во время боя не пишется — остаток
        /// считается от <see cref="_phaseStartTime"/>; значение фиксируется один раз,
        /// на выходе из <see cref="RoundPhase.Combat"/>, чтобы экран итогов показывал
        /// остаток на момент конца боя, как и до перевода таймеров на NetworkTime.
        /// </summary>
        [SyncVar] private float _combatElapsed;

        /// <summary>
        /// Обратный отсчёт стоит: кто-то из живых вне своей зоны (<see cref="RoundPhases.CountdownHeld"/>).
        /// Пока стоит, клиент показывает полный отсчёт; отпустили — фаза отсчитывается заново
        /// от нового <see cref="_phaseStartTime"/>.
        /// </summary>
        [SyncVar] private bool _countdownHeld;

        /// <summary>Обратный отсчёт стоит — кто-то из живых вне своей зоны. Верно и у клиента.</summary>
        public bool CountdownHeld => _countdownHeld;

        /// <summary>
        /// Номер идущего раунда на карте, с 1; 0 — раунды не начинались. Растёт только
        /// в <see cref="StartNextRound"/> (MATCH-06).
        /// </summary>
        [SyncVar] private int _currentRound;

        /// <summary>
        /// Счёт на момент старта идущего раунда — его и запоминает пауза: прерванный раунд
        /// не засчитывается, даже если его победитель уже известен (фаза Resolution).
        /// </summary>
        private readonly Dictionary<int, int> _scoresAtRoundStart = new Dictionary<int, int>();

        /// <summary>Победитель доигрываемого раунда — объявляется серии по окончании раунда.</summary>
        private TeamData _lastRoundWinner;

        /// <summary>Карта решена: тик больше не двигает раунды до остановки режима.</summary>
        private bool _mapDecided;

        /// <summary>
        /// Команды поменялись сторонами (вторая половина карты). Состояние, а не событие: поздний
        /// клиент обязан знать, чья зона чья. Раздаёт <see cref="ApplySidesLocal"/> в <see cref="SpawnSides"/>.
        /// </summary>
        [SyncVar(hook = nameof(OnSidesSwappedSynced))]
        private bool _sidesSwapped;

        /// <summary>Команды сейчас играют со сторон второй половины.</summary>
        public bool SidesSwapped => _sidesSwapped;

        /// <summary>Клиент: команды поменялись сторонами. Для HUD («Смена сторон»).</summary>
        public static event Action SidesSwappedLocal;

        /// <summary>Фаза, уже разданная локальным подписчикам на этой машине.</summary>
        private RoundPhase _appliedRoundPhase = RoundPhase.Setup;

        /// <summary>Была ли фаза раздана хотя бы раз (отличает «ещё ничего» от «раздали Setup»).</summary>
        private bool _roundPhaseApplied;

        /// <summary>
        /// Кого раунд ждёт: <c>netId</c> сессий живых игроков, не объявивших готовность.
        ///
        /// <para>
        /// <b>Состояние, а не событие</b> — по той же причине, что и фаза раунда.
        /// Состав неготовых нужен HUD'у, чтобы показать «ждём Петю», а вновь
        /// подключившийся обязан узнать его сам: список приезжает начальным значением
        /// спавна, а не следующим изменением. Разовым <c>ClientRpc</c> это не решается
        /// (урок Корня 3).
        /// </para>
        ///
        /// <para>
        /// Почему <c>netId</c>, а не имена: имя игрок вправе сменить, а сессию по netId
        /// клиент разрешает сам через <c>NetworkClient.spawned</c> — и получает
        /// не строку, а объект, у которого можно спросить команду и всё остальное.
        /// Список пуст вне фазы <c>Equipment</c>: в остальных фазах готовности не ждут.
        /// </para>
        /// </summary>
        private readonly SyncList<uint> _pendingReadiness = new SyncList<uint>();

        /// <summary>Буфер сравнения: не хочется писать SyncList каждый тик без изменений.</summary>
        private readonly List<uint> _pendingReadinessBuffer = new List<uint>();

        /// <summary>
        /// Живые подписки отложенного респавна. Список нужен именно потому, что подписка
        /// может так и не сработать: снять её иначе, чем изнутри обработчика, было нечем
        /// (MATCH-05). Только сервер — на клиенте <c>PrepareNextRound</c> не исполняется.
        /// </summary>
        private readonly List<PendingRespawn> _pendingRespawns = new List<PendingRespawn>();

        // Серверная машина фаз раунда — создаётся при старте игры, не требует NetworkBehaviour
        private RoundPhases _roundManager;
        public RoundPhases RoundPhases => _roundManager;

        // ── Глобальные семантические события для UI (Клиент) ───────────────

        public static event Action<int> RoundStartedLocal;
        public static event Action<TeamData> RoundEndedLocal;

        /// <summary>
        /// Фаза раунда изменилась на ЭТОЙ машине. Срабатывает одинаково на обычном клиенте,
        /// на хосте и на выделенном сервере — раздача идёт от <see cref="_roundPhase"/>,
        /// а не от сетевого сообщения. Для представления: арсенал, HUD, зоны спавна.
        /// </summary>
        public static event Action<RoundPhase> RoundPhaseChangedLocal;

        /// <summary>
        /// Фаза раунда изменилась, и эта машина — сервер. Для авторитетных реакций,
        /// которые обязан выполнить именно сервер (пополнение слотов арсенала и т.п.).
        /// Подписываться только из <c>OnStartServer</c>: на клиенте не срабатывает никогда.
        /// </summary>
        public static event Action<RoundPhase> RoundPhaseChangedServer;

        // ── Публичные свойства для UI ────────────────────────────────────────

        public EliminationState CurrentState => _state;
        public RoundPhase CurrentRoundPhase => _roundPhase;

        /// <summary>
        /// Кого раунд ждёт — <c>netId</c> сессий неготовых живых игроков. Доступно
        /// и на сервере, и на клиенте; подписаться на изменения можно через
        /// <c>PendingReadiness.OnChange</c>.
        /// </summary>
        public SyncList<uint> PendingReadiness => _pendingReadiness;

        /// <summary>Предел ожидания готовности, секунды. Ноль и меньше — предела нет.</summary>
        public float ReadinessTimeLimit => _readinessTimeLimit;

        /// <summary>Правило матча при истечении предела ожидания.</summary>
        public RoundReadinessTimeoutRule ReadinessTimeoutRule => _readinessTimeoutRule;

        /// <summary>
        /// Чем кончается закупка. Поле префаба, поэтому одинаково известно серверу и
        /// клиентам без синхронизации: стена по нему решает, показывать ли жетон.
        /// </summary>
        public RoundStartRule RoundStartRule => _roundStartRule;

        /// <summary>
        /// Сколько осталось закупки до обратного отсчёта: при старте по таймеру — до его
        /// конца, при ожидании готовности — до предела. Вне фазы <c>Equipment</c> и без
        /// предела — ноль. Считается локально, как и остальные таймеры фаз.
        /// </summary>
        public float EquipmentTimeRemaining
        {
            get
            {
                float limit = RoundReadiness.EffectiveTimeLimit(_roundStartRule, _readinessTimeLimit);
                if (_roundPhase != RoundPhase.Equipment || limit <= 0f) return 0f;
                return Mathf.Max(0f, limit - PhaseElapsed);
            }
        }
        /// <summary>Сколько секунд идёт текущая фаза. Считается локально, без обращения к сети.</summary>
        private float PhaseElapsed => (float)Math.Max(0d, NetworkTime.time - _phaseStartTime);

        /// <summary>
        /// Остаток времени раунда. Боевое время расходуется только в фазе Combat:
        /// до неё показывается полная длительность, после — то, что оставалось
        /// в момент конца боя. Так же считал и <c>RoundPhases</c> до T-19.
        /// </summary>
        public float RoundTimeRemaining
        {
            get
            {
                float elapsed = _roundPhase == RoundPhase.Combat ? PhaseElapsed : _combatElapsed;
                return Mathf.Max(0f, _roundDuration - elapsed);
            }
        }

        /// <summary>
        /// Остаток текущей паузы: обратного отсчёта, паузы после победы или экрана итогов.
        /// Вне этих трёх фаз паузы нет, поэтому ноль. Набор фаз повторяет
        /// <see cref="RoundPhases.CountdownTimeRemaining"/> — свойство читает HUD.
        /// </summary>
        public float CountdownTimeRemaining
        {
            get
            {
                switch (_roundPhase)
                {
                    case RoundPhase.Countdown:
                        return _countdownHeld ? _countdownDuration : Mathf.Max(0f, _countdownDuration - PhaseElapsed);
                    case RoundPhase.Resolution:
                        return Mathf.Max(0f, RoundPhases.ResolutionDuration - PhaseElapsed);
                    case RoundPhase.Scoreboard:
                        return Mathf.Max(0f, RoundPhases.ScoreboardDuration - PhaseElapsed);
                    default:
                        return 0f;
                }
            }
        }
        public int CurrentRoundNumber => _currentRound;
        public int RoundsPerHalf => _roundsPerHalf;

        /// <summary>Всего раундов на карте: две половины.</summary>
        public int TotalRounds => _roundsPerHalf * 2;

        /// <summary>Сколько раундов нужно, чтобы взять карту досрочно, — больше половины всех.</summary>
        public int RoundsToWin => _roundsPerHalf + 1;

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => false;

        /// <summary>Оружие стреляет только в бою. Фаза — SyncVar, поэтому ответ верен и у клиента.</summary>
        public override bool WeaponsEnabled => _roundPhase == RoundPhase.Combat;

        /// <summary>Матч: границы зон видны — кому и когда, решает <c>SpawnZoneVisibility</c>.</summary>
        public override bool ShowsSpawnZones => true;

        /// <summary>
        /// Арсенал открыт только в закупке; жетон нужен, если закупка кончается готовностью.
        /// Пустые слоты пополняются событием на входе в <c>Setup</c> (<see cref="ServerSetRoundPhase"/>).
        /// </summary>
        public override ArsenalRules ArsenalRules => new ArsenalRules(
            isOpen: _roundPhase == RoundPhase.Equipment,
            usesReadinessTag: _roundStartRule == RoundStartRule.Readiness,
            replacesLostWeapons: false);

        protected override bool CanBegin()
        {
            // Базовый класс больше не ждет. Наша локальная машина состояний ждет появления игроков.
            return true;
        }

        /// <summary>
        /// Набралось ли людей, чтобы начинать матч.
        ///
        /// Об игроках спрашиваем <see cref="GameMode.PlayerRoster"/>, а не
        /// <c>PlayersManager.Instance</c> напрямую: синглтон разыменовывался без проверки,
        /// и режим падал с NRE на первом же кадре везде, где менеджера нет, — в сцене,
        /// открытой без сети, и в любом тесте логики матча (находка NET-18).
        /// Нет менеджера — реестр отвечает пустым списком, и матч просто не начинается.
        /// </summary>
        private bool IsPlayersReady()
        {
            // Минимум — из данных этого режима, а не из выбора матча в SessionManager.
            int currentPlayers = PlayerRoster.GetAllPlayers().Count();
            if (currentPlayers < MinPlayersToStart) return false;

            // Этап Б: команду матча игрок выбирает сам на карте (или её выдаёт админ).
            // Пока хоть у кого-то её нет, матч стоит: иначе игрок остался бы вне игры.
            if (!AllPlayersHaveModeTeam()) return false;

            return _teamStates.Values.All(t => t.HasPlayers());
        }

        /// <summary>
        /// Матч начался — сам игрок команду больше не меняет, только админ.
        /// Фаза матча — SyncVar, поэтому ответ верен и у клиента (планшет).
        /// </summary>
        public override bool TeamChoiceLocked => _state != EliminationState.WaitingForPlayers;

        /// <summary>
        /// Зовёт корутина старта базового режима — кадром позже спавна. К этому моменту
        /// <see cref="ServerTick"/> мог уже поднять матч (игроки на месте, или продолжение
        /// после паузы), и сброс в <c>WaitingForPlayers</c> начал бы сет заново с раунда 1.
        /// Ожидание игроков и так начальное состояние — здесь только лог.
        /// </summary>
        [Server]
        protected override void Begin()
        {
            if (_state != EliminationState.WaitingForPlayers) return;
            GameLog.Match.Info("[EliminationMode] Матч инициализирован. Ждем игроков.");

            // Аватары, созданные до режима (смена карты, «Начать матч» из разминки), — такие же
            // новые в матче: до старта раунда игрок либо выбывший, либо в своей зоне.
            foreach (PlayerSession session in PlayerRoster.GetAllPlayers().ToList())
            {
                if (session != null) ServerAdmitAvatar(session.ActiveAvatar, continuesPrevious: false);
            }
        }

        [Server]
        private void InitializeActiveGame()
        {
            _state = EliminationState.Active;

            // Машина фаз — обычный C#-объект, без GameObject и NetworkBehaviour. Событий у неё
            // нет: исход тика она возвращает значением, поэтому подписаться дважды (MATCH-01)
            // физически не на что.
            _roundManager = new RoundPhases(PlayerRoster, _readinessTimeLimit, _readinessTimeoutRule, _roundStartRule);
            _mapDecided = false;

            string teamsStr = string.Join(" vs ", Teams.Select(t => t != null ? t.Name : "null"));
            GameLog.Match.Info(
                $"[EliminationMode] Активная игра начата: {teamsStr}, " +
                $"раундов на карте: {TotalRounds} ({_roundsPerHalf} в половине), до победы: {RoundsToWin}");

            // Счёт продолженного матча уже вернул RestoreSnapshot; прерванный раунд сыграется заново.
            int roundToReplay = _resume != null ? _resume.RoundToReplay : 0;
            _resume = null;

            _currentRound = Math.Max(0, roundToReplay - 1);
            ServerSetSidesSwapped(_currentRound >= _roundsPerHalf);

            if (roundToReplay > 0)
                GameLog.Match.Info($"[EliminationMode] Матч продолжен после паузы с раунда {roundToReplay}, счёт: {DescribeScore()}.");

            StartNextRound();
        }

        // ── Пауза: снимок и продолжение ─────────────────────────────────────

        /// <summary>Снимок, с которого матч продолжится; null — обычный старт.</summary>
        private PauseSnapshot _resume;

        /// <summary>Elimination встаёт на паузу: счёт карты и номер раунда сохраняются.</summary>
        public override bool SupportsPause => true;

        /// <summary>
        /// Счёт карты <b>без</b> идущего раунда и его номер — прерванный раунд сыграется
        /// заново и не засчитается. Базовый снимок взял бы живой счёт, в котором раунд
        /// уже засчитан с фазы Resolution.
        /// </summary>
        [Server]
        public override PauseSnapshot CaptureSnapshot()
        {
            PauseSnapshot snapshot = base.CaptureSnapshot();
            if (_roundManager == null) return snapshot;

            foreach (var kvp in _scoresAtRoundStart) snapshot.TeamScores[kvp.Key] = kvp.Value;
            snapshot.RoundToReplay = _currentRound;
            return snapshot;
        }

        [Server]
        public override void RestoreSnapshot(PauseSnapshot snapshot)
        {
            base.RestoreSnapshot(snapshot);
            _resume = snapshot;
        }

        [Server]
        public override void ForceStop()
        {
            _mapDecided = true;
            if (_roundManager != null) _roundManager.ForceStop();
            _roundManager = null;

            // Раунда больше не будет — ждать возвращения в зону некому и незачем.
            ClearPendingRespawns();

            // Вне матча стороны на месте: разминка и следующая карта начинаются с первой половины.
            ServerSetSidesSwapped(false);

            // Выбывшие в конце последнего раунда оживают: в разминке мёртвых нет.
            ServerReviveAll();

            // И готовности ждать больше не от кого: список обязан опустеть, иначе HUD
            // остановленного матча так и будет показывать «ждём Петю».
            _pendingReadiness.Clear();

            GameLog.Match.Info(
                $"[EliminationMode] Матч остановлен.");
        }

        // ── Тик ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!isServer) return;
            ServerTick(Time.deltaTime);
        }

        /// <summary>
        /// Один серверный шаг матча. Отдельно от <c>Update</c> намеренно: шаг времени
        /// приходит параметром, поэтому весь матч прогоняется EditMode-тестом
        /// за миллисекунды, без Play-режима и без <c>Time.deltaTime</c>.
        /// </summary>
        [Server]
        public void ServerTick(float deltaTime)
        {
            if (_state == EliminationState.WaitingForPlayers)
            {
                if (IsPlayersReady()) InitializeActiveGame();
                return;
            }

            if (_state == EliminationState.Finished) return;

            if (_roundManager == null) return;

            if (!_mapDecided) TickRounds(deltaTime);

            // Единственное, что может измениться за тик, — фаза. Таймеры в сеть больше
            // не пишутся: клиент считает остаток сам, от момента старта фазы (NET-09).
            ServerSetRoundPhase(_roundManager.State);
            ServerSyncCountdownHold();

            ServerPublishPendingReadiness();
        }

        // ── Раунды карты ────────────────────────────────────────────────────

        /// <summary>
        /// Шаг раундов. Единственное место, где раунд переходит в раунд, а карта — в конец.
        ///
        /// <para>
        /// <b>Владелец машины раунда.</b> Режим тикает <see cref="RoundPhases"/> и применяет
        /// единственный переход, который тот не делает сам, — «итоги показаны → новый раунд».
        /// Больше этот переход не делает никто: второй владелец обошёл бы счётчик раундов
        /// и <c>RpcOnRoundStarted</c> (MATCH-06).
        /// </para>
        ///
        /// <para>
        /// <b>Когда что происходит.</b> Очко за раунд начисляется при входе в фазу Resolution —
        /// чтобы экран итогов показывал уже новый счёт. Исход карты решается позже, при выходе
        /// из Scoreboard: карта не может закончиться раньше, чем показали итоги последнего раунда.
        /// </para>
        /// </summary>
        [Server]
        private void TickRounds(float deltaTime)
        {
            RoundTickResult tick = _roundManager.Tick(deltaTime);

            // Исход раунда известен с момента входа в Resolution — с него и начинается пауза.
            if (tick.PhaseChanged && tick.To == RoundPhase.Resolution)
                ScoreRound(_roundManager.RoundWinner);

            // Итоги показаны: раунд доигран — только теперь он идёт в общий счёт серии
            // (прерванный паузой раунд до этой точки не доходит). Дальше — ещё раунд или конец карты.
            if (tick.CycleCompleted)
            {
                RaiseRoundWon(_lastRoundWinner);
                DecideAfterScoreboard();
            }
        }

        /// <summary>Начисляет очко за раунд и оповещает клиентов об исходе.</summary>
        [Server]
        private void ScoreRound(TeamData winner)
        {
            _lastRoundWinner = winner;

            if (winner != null && _teamStates.TryGetValue(winner.teamIndex, out TeamRuntimeData winnerState))
                winnerState.AddScore(1);

            string winnerName = winner != null ? winner.Name : "ничья";
            GameLog.Match.Info(
                $"[EliminationMode] Раунд {_currentRound}/{TotalRounds} завершён, победитель: {winnerName}, счёт: {DescribeScore()}");

            RpcOnRoundEnded(winner != null ? winner.teamIndex : -1);
        }

        /// <summary>
        /// Экран итогов показан целиком: карта решена или идёт следующий раунд, а после
        /// последнего раунда половины — со сменой сторон.
        ///
        /// Лидер считается в два прохода: сначала максимум, потом — сколько команд его
        /// набрали. Одним проходом это писать нельзя: сравнение с текущим максимумом на
        /// первой же итерации даёт <c>0 == 0</c> и взводит ничью до того, как посчитан хоть
        /// один результат, а дальше исход зависит от порядка обхода словаря (MATCH-04).
        /// Команды берутся из <see cref="GameMode.TeamStates"/> — состава, переданного
        /// в <c>Initialize</c>, — а не из глобального <c>TeamRegistry</c> (T-08).
        /// </summary>
        [Server]
        private void DecideAfterScoreboard()
        {
            // Проход 1 — максимум раундов.
            int highest = 0;
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                if (state.Score > highest) highest = state.Score;
            }

            // Проход 2 — сколько команд набрали этот максимум и кто первая из них.
            int leadersCount = 0;
            TeamData leader = null;
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                if (state.Score != highest) continue;

                leadersCount++;
                if (leader == null) leader = state.Team;
            }

            if (highest >= RoundsToWin || _currentRound >= TotalRounds)
            {
                // Ничья — несколько лидеров либо нулевой максимум: карта, на которой никто
                // не выиграл ни одного раунда, победителя не имеет.
                FinishMap(highest > 0 && leadersCount == 1 ? leader : null);
                return;
            }

            if (_currentRound == _roundsPerHalf) SwapSides();
            StartNextRound();
        }

        /// <summary>Вторая половина: команды меняются зонами спавна, счёт остаётся.</summary>
        [Server]
        private void SwapSides()
        {
            ServerSetSidesSwapped(!_sidesSwapped);
            RpcOnSidesSwapped();
            GameLog.Match.Info(
                $"[EliminationMode] Смена сторон: команды играют со сторон {(_sidesSwapped ? "второй" : "первой")} половины.");
        }

        /// <summary>
        /// Запускает следующий раунд. Только отсюда растёт счётчик раундов и уходит
        /// <c>RpcOnRoundStarted</c>, поэтому обходить этот метод нельзя (MATCH-06).
        /// </summary>
        [Server]
        private void StartNextRound()
        {
            _scoresAtRoundStart.Clear();
            foreach (TeamRuntimeData state in _teamStates.Values)
                _scoresAtRoundStart[state.Team.teamIndex] = state.Score;
            _lastRoundWinner = null;

            ServerBeginRound(_currentRound + 1);
            GameLog.Match.Info($"[EliminationMode] Раунд {_currentRound}/{TotalRounds}");

            _roundManager.StartRound(Teams, _countdownDuration, _roundDuration);
            PrepareNextRound();
        }

        /// <summary>Карта решена: победитель (null — ничья) уходит менеджеру карты.</summary>
        [Server]
        private void FinishMap(TeamData winner)
        {
            _mapDecided = true;

            string winnerName = winner != null ? winner.Name : "ничья";
            GameLog.Match.Info($"[EliminationMode] Карта завершена, победитель: {winnerName}, счёт: {DescribeScore()}");

            // Карта уходит в разминку без ForceStop — оживляем здесь.
            ServerReviveAll();
            RaiseFinished(winner);
        }

        private string DescribeScore() =>
            string.Join(" — ", _teamStates.Values.Select(s => $"{s.Team.Name} {s.Score}"));

        // ── Стороны ─────────────────────────────────────────────────────────

        [Server]
        private void ServerSetSidesSwapped(bool swapped)
        {
            _sidesSwapped = swapped;

            // На выделенном сервере хук SyncVar не вызывается — раздаём сами.
            ApplySidesLocal();
        }

        private void OnSidesSwappedSynced(bool oldValue, bool newValue) => ApplySidesLocal();

        /// <summary>Режим ушёл со сцены (смена режима, карты) — стороны на место: зоны не должны помнить матч.</summary>
        public override void OnStopServer()
        {
            SpawnSides.Reset();
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            SpawnSides.Reset();
            base.OnStopClient();
        }

        /// <summary>Отдаёт состояние сторон зонам этой машины (<see cref="SpawnSides"/>).</summary>
        private void ApplySidesLocal()
        {
            TeamData[] teams = Teams;
            if (teams == null || teams.Length < 2)
            {
                SpawnSides.Reset();
                return;
            }

            SpawnSides.Set(teams[0], teams[1], _sidesSwapped);
        }

        [ClientRpc]
        private void RpcOnSidesSwapped()
        {
            SidesSwappedLocal?.Invoke();
        }

        /// <summary>
        /// Вызывается из RoundPhases при гибели игрока.
        /// Возвращает победителя раунда или null если раунд продолжается.
        /// </summary>
        public TeamData CheckRoundWinCondition()
        {
            TeamData lastAlive = null;
            int aliveTeamsCount = 0;

            foreach (var state in TeamStates.Values)
            {
                if (state.HasAlivePlayers())
                {
                    lastAlive = state.Team;
                    aliveTeamsCount++;
                }
            }

            if (aliveTeamsCount > 1) return null;
            return aliveTeamsCount == 1 ? lastAlive : null;
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока в этом режиме.
        /// Условие победы считает режим — он владеет составом команд; машина раунда
        /// получает только заявку «бой пора заканчивать».
        /// </summary>
        [Server]
        public override void OnPlayerDied(PlayerController player)
        {
            if (_roundManager == null || _roundManager.State != RoundPhase.Combat) return;

            GameLog.Match.Verbose(
                $"[EliminationMode] Игрок {player.name} погиб — проверяем условие победы");

            TeamData winner = CheckRoundWinCondition();
            if (winner == null && !AreAllTeamsDead()) return;

            _roundManager.RequestRoundEnd(winner);
        }

        /// <summary>Ни в одной команде не осталось живых — выигрывать раунд некому.</summary>
        private bool AreAllTeamsDead()
        {
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                if (state.HasAlivePlayers()) return false;
            }
            return true;
        }

        [Server]
        public void PrepareNextRound()
        {
            // Раунд начинается заново — обработчики прошлого раунда снимаются все разом,
            // до того как повесить новые. Раньше подписка снималась только изнутри себя
            // самой, то есть исключительно когда игрок вернулся в зону. Не вернулся —
            // подписка жила вечно, замыкание держало ссылку на уничтоженный
            // PlayerController, а в следующем раунде срабатывало и возрождало игрока
            // посреди боя (MATCH-05).
            ClearPendingRespawns();

            var zones = UnityEngine.Object.FindObjectsByType<Maps.TeamSpawnZone>(FindObjectsSortMode.None);

            foreach (var teamState in _teamStates.Values)
            {
                var team = teamState.Team;
                var zone = zones.FirstOrDefault(z => z.Team == team);
                if (zone == null) continue;

                foreach (var session in teamState.Sessions)
                {
                    var player = session.ActiveAvatar;
                    // Мертвы все, кто был в прошлом бою (выжившие выбывают в конце боя): оживают на своей базе.
                    if (player == null || player.IsAlive) continue;

                    ServerArrangeRespawn(zone, player);
                }
            }
        }

        /// <summary>
        /// Выбывший оживает на своей базе: уже в зоне — сразу, нет — при входе в неё.
        ///
        /// <para>
        /// Зона — условие респавна, а не его точка: игрок физически стоит в зале и в зону
        /// приходит сам. Респавн восстанавливает только состояние и никого не двигает.
        /// </para>
        /// </summary>
        [Server]
        private void ServerArrangeRespawn(Maps.TeamSpawnZone zone, PlayerController player)
        {
            if (zone.GetPlayersInZone().Contains(player))
            {
                GameLog.Match.Info(
                    $"[EliminationMode] Игрок {player.name} уже в зоне — респаун сразу.");
                player.Respawn();
                return;
            }

            GameLog.Match.Info(
                $"[EliminationMode] Игрок {player.name} не в зоне — ожидание возвращения для респауна.");

            PendingRespawn pending = new PendingRespawn { Zone = zone };
            pending.Handler = (z, p) =>
            {
                if (p != player) return;

                // Оживают только до боя: опоздавший (предел возвращения на базу истёк)
                // иначе ожил бы у себя на базе посреди боя. Подписка остаётся — её
                // снимет следующий PrepareNextRound, и там решится заново.
                if (!CanRespawnNow(_roundPhase))
                {
                    GameLog.Match.Info(
                        $"[EliminationMode] Игрок {player.name} вернулся в зону в фазе {_roundPhase} — оживёт в следующем раунде.");
                    return;
                }

                z.PlayerEntered -= pending.Handler;
                _pendingRespawns.Remove(pending);

                p.Respawn();
                GameLog.Match.Info(
                    $"[EliminationMode] Игрок {player.name} вернулся в зону — отложенный респаун выполнен.");
            };

            zone.PlayerEntered += pending.Handler;
            _pendingRespawns.Add(pending);
        }

        /// <summary>
        /// Новый аватар в матче. Живым игрок бывает только в закупке и бою, поэтому аватар без
        /// прошлого (смена карты, первый вход) входит выбывшим — призраком там, где стоит, — и
        /// оживает на своей базе, как все. Иначе после смены карты игрок появлялся живым там, где
        /// его поставила точка спавна, хоть посреди карты. Аватар взамен прежнего (смена скина,
        /// команды, переподключение) уже несёт его жизнь; выбывшему здесь назначается возрождение —
        /// прежний обработчик зоны ждал уничтоженный аватар.
        ///
        /// <para>
        /// В разминке и после конца матча — ничего: там мёртвых нет.
        /// </para>
        /// </summary>
        [Server]
        public override void ServerAdmitAvatar(PlayerController player, bool continuesPrevious)
        {
            if (player == null || IsWarmup || _state == EliminationState.Finished) return;

            if (!continuesPrevious) player.ServerEliminateSilently("новый аватар в матче");
            if (player.IsAlive) return;

            // До старта матча и в бою возрождения нет: первое раздаст PrepareNextRound.
            if (_state != EliminationState.Active || !CanRespawnNow(_roundPhase)) return;

            Maps.TeamSpawnZone zone = UnityEngine.Object.FindObjectsByType<Maps.TeamSpawnZone>(FindObjectsSortMode.None)
                                                  .FirstOrDefault(z => z.Team != null && z.Team == player.Team);
            if (zone != null) ServerArrangeRespawn(zone, player);
        }

        /// <summary>
        /// Возрождение — только на своей базе и только до боя: на подготовке и закупке.
        /// Тем, кто погиб в раунде, это уже следующий раунд.
        /// </summary>
        public static bool CanRespawnNow(RoundPhase state) =>
            state == RoundPhase.Setup || state == RoundPhase.Equipment;

        /// <summary>
        /// Снимает все отложенные подписки на вход в зону спавна. Зовётся в начале каждого
        /// раунда и при остановке матча — это единственные два момента, после которых
        /// прошлые обработчики заведомо не нужны.
        /// </summary>
        [Server]
        private void ClearPendingRespawns()
        {
            foreach (PendingRespawn pending in _pendingRespawns)
            {
                if (pending.Zone != null) pending.Zone.PlayerEntered -= pending.Handler;
            }

            _pendingRespawns.Clear();
        }

        /// <summary>
        /// Отложенный респавн: игрок погиб и к началу раунда оказался вне своей зоны.
        /// Зона и обработчик хранятся парой, потому что снять подписку можно только
        /// имея на руках ровно тот делегат, который на неё вешали.
        ///
        /// Класс, а не структура: обработчик вычёркивает себя из списка по ссылке.
        /// </summary>
        private sealed class PendingRespawn
        {
            public Maps.TeamSpawnZone Zone;
            public Action<Maps.TeamSpawnZone, PlayerController> Handler;
        }

        // ── Фаза раунда: состояние, а не событие ─────────────────────────────
        //
        // Раздача одинакова на всех машинах, но приходит с разных сторон:
        //   · обычный клиент — из хука SyncVar, который Mirror зовёт в OnDeserialize;
        //   · хост — из того же хука: в сеттере Mirror зовёт его при NetworkServer.activeHost;
        //   · выделенный сервер — из ServerSetRoundPhase, потому что в сеттере хук
        //     под ServerOnly не срабатывает (Mirror.NetworkBehaviour.GeneratedSyncVarSetter).
        //
        // Поэтому ApplyRoundPhaseLocal идемпотентна по значению: под хостом её зовут дважды,
        // и второй вызов обязан быть пустым. Подряд идущих одинаковых фаз в машине состояний
        // нет (Setup → Equipment → Countdown → Combat → Resolution → Scoreboard → Setup),
        // так что гашение по значению ничего не теряет.

        /// <summary>Меняет фазу на сервере: пишет состояние и поднимает обе раздачи.</summary>
        [Server]
        private void ServerSetRoundPhase(RoundPhase newState)
        {
            if (_roundPhase == newState) return;

            // Выход из боя — единственный момент, когда боевое время перестаёт течь.
            // Дальше _phaseStartTime уже про другую фазу, поэтому остаток замораживаем.
            if (_roundPhase == RoundPhase.Combat) _combatElapsed = PhaseElapsed;

            _roundPhase = newState;
            _phaseStartTime = NetworkTime.time;

            ApplyRoundPhaseLocal(newState);
            RoundPhaseChangedServer?.Invoke(newState);

            // Новый раунд — стены пополняют пустые слоты. Раньше стена сама слушала
            // фазы Elimination; теперь она знает только базовый GameMode.
            if (newState == RoundPhase.Setup)
                RaiseArsenalRefillRequestedServer();

            // Бой кончился — выжившие выбывают (без записи в статистику). Живым игрок бывает
            // только в закупке и бою; оживают все на своей базе в подготовке следующего раунда.
            if (newState == RoundPhase.Resolution)
            {
                ServerEndRoundDeaths();

                // Оружие раунд не переживает: у всех забирается и с пола убирается, каждый
                // раунд экипировка заново (стены пополняются на входе в Setup, выше).
                EquipmentStrip.ServerStripAll("конец раунда");
            }
        }

        /// <summary>
        /// Отсчёт встал или пошёл заново. Пошёл — фаза отсчитывается с этого момента: клиент
        /// считает остаток сам, от <see cref="_phaseStartTime"/>.
        /// </summary>
        [Server]
        private void ServerSyncCountdownHold()
        {
            bool held = _roundManager != null && _roundManager.CountdownHeld;
            if (held == _countdownHeld) return;

            _countdownHeld = held;
            if (!held && _roundPhase == RoundPhase.Countdown) _phaseStartTime = NetworkTime.time;
        }

        [Server]
        private void ServerEndRoundDeaths()
        {
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                foreach (PlayerSession session in state.Sessions)
                {
                    PlayerController player = session != null ? session.ActiveAvatar : null;
                    if (player != null && player.IsAlive) player.ServerEliminateSilently("бой кончился");
                }
            }
        }

        /// <summary>Матч кончился — все оживают на месте: в разминке мёртвых нет.</summary>
        [Server]
        private void ServerReviveAll()
        {
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                foreach (PlayerSession session in state.Sessions)
                {
                    PlayerController player = session != null ? session.ActiveAvatar : null;
                    if (player != null && !player.IsAlive) player.Respawn();
                }
            }
        }

        // ── Состав неготовых: тоже состояние ─────────────────────────────────

        /// <summary>
        /// Выкладывает клиентам список тех, кого раунд ждёт. Зовётся каждый серверный тик,
        /// но пишет <see cref="_pendingReadiness" /> только когда состав действительно
        /// изменился: <c>SyncList</c> шлёт дельту на каждую операцию, и переписывание
        /// одного и того же состава давало бы ровно тот же фоновый трафик, из-за которого
        /// таймеры фаз в своё время съехали на <c>NetworkTime</c> (NET-09).
        /// </summary>
        [Server]
        private void ServerPublishPendingReadiness()
        {
            _pendingReadinessBuffer.Clear();

            // Ждать готовности имеет смысл только в той фазе, где её ждут. В остальных
            // список обязан быть пуст, иначе HUD покажет «ждём Петю» посреди боя.
            if (_roundManager != null && _roundPhase == RoundPhase.Equipment)
            {
                foreach (PlayerSession session in _roundManager.Readiness.Pending)
                {
                    if (session != null) _pendingReadinessBuffer.Add(session.netId);
                }
            }

            if (SamePendingReadiness(_pendingReadinessBuffer)) return;

            _pendingReadiness.Clear();
            foreach (uint netId in _pendingReadinessBuffer)
                _pendingReadiness.Add(netId);
        }

        /// <summary>Совпадает ли выложенный состав с только что посчитанным.</summary>
        private bool SamePendingReadiness(List<uint> fresh)
        {
            if (_pendingReadiness.Count != fresh.Count) return false;

            for (int i = 0; i < fresh.Count; i++)
            {
                if (_pendingReadiness[i] != fresh[i]) return false;
            }

            return true;
        }

        /// <summary>Хук SyncVar: фаза приехала с сервера.</summary>
        private void OnRoundPhaseSynced(RoundPhase oldState, RoundPhase newState)
        {
            ApplyRoundPhaseLocal(newState);
        }

        /// <summary>Раздаёт фазу локальным подписчикам этой машины ровно один раз на значение.</summary>
        private void ApplyRoundPhaseLocal(RoundPhase state)
        {
            if (_roundPhaseApplied && _appliedRoundPhase == state) return;

            _roundPhaseApplied = true;
            _appliedRoundPhase = state;

            GameLog.Match.Info(
                $"[EliminationMode] Фаза раунда: {state}");
            RoundPhaseChangedLocal?.Invoke(state);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Поздний клиент получает фазу начальным значением спавна. Хук на нём не сработает,
            // если пришедшее значение совпало с дефолтом поля (Setup), — раздаём явно,
            // иначе подписчики так и не узнают, в какой фазе идёт раунд.
            ApplyRoundPhaseLocal(_roundPhase);
            ApplySidesLocal();
        }

        /// <summary>
        /// Начало раунда на сервере. Номер раунда — состояние, поэтому пишется в SyncVar
        /// серверным кодом; Rpc остаётся разовым уведомлением для UI (NET-08).
        /// Раньше присваивание жило внутри Rpc: на выделенном сервере он не исполняется,
        /// и <see cref="CurrentRoundNumber"/> навсегда оставался нулём.
        /// </summary>
        [Server]
        private void ServerBeginRound(int roundNum)
        {
            _currentRound = roundNum;

            // Отметку старта фазы ставим и здесь, а не только в ServerSetRoundPhase:
            // раунд начинается с Setup, а это же значение стоит в _roundPhase по умолчанию,
            // поэтому в первом раунде смены состояния — а значит и отметки — не случилось бы,
            // и весь раунд считался бы от нуля (T-19).
            _combatElapsed = 0f;
            _phaseStartTime = NetworkTime.time;

            RpcOnRoundStarted(roundNum);
        }

        [ClientRpc]
        private void RpcOnRoundStarted(int roundNum)
        {
            RoundStartedLocal?.Invoke(roundNum);
        }

        [ClientRpc]
        private void RpcOnRoundEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            RoundEndedLocal?.Invoke(winner);
        }
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим "Ликвидация": матч → сеты → раунды.
    /// Побеждает команда, выигравшая больше сетов.
    ///
    /// SetManager и RoundManager — чистые C# классы (не NetworkBehaviour).
    /// EliminationMode создаёт их через new и управляет тиком в Update().
    /// Вся сетевая синхронизация (SyncVar, ClientRpc) — здесь.
    ///
    /// Не возрождает игроков между раундами.
    /// </summary>
    public enum EliminationMatchState
    {
        WaitingForPlayers,
        Active,
        Finished
    }

    public class EliminationMode : GameMode
    {
        [Header("Настройки")]
        [Tooltip("Максимум сетов в матче (нечётное число рекомендуется).")]
        [SerializeField] private int _maxSets = 5;

        [Tooltip("Раундов в одном сете.")]
        [SerializeField] private int _roundsPerSet = 3;

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
        [SyncVar] private EliminationMatchState _matchState = EliminationMatchState.WaitingForPlayers;

        /// <summary>
        /// Фаза раунда. Единственный источник правды — эта переменная, а не сетевое сообщение:
        /// вновь подключившийся клиент получает её начальным значением спавна.
        /// Раздачу подписчикам делает <see cref="ApplyRoundStateLocal"/>.
        /// </summary>
        [SyncVar(hook = nameof(OnRoundStateSynced))]
        private RoundState _roundState = RoundState.Setup;

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
        /// на выходе из <see cref="RoundState.Combat"/>, чтобы экран итогов показывал
        /// остаток на момент конца боя, как и до перевода таймеров на NetworkTime.
        /// </summary>
        [SyncVar] private float _combatElapsed;

        [SyncVar] private int _currentRound;

        /// <summary>Фаза, уже разданная локальным подписчикам на этой машине.</summary>
        private RoundState _appliedRoundState = RoundState.Setup;

        /// <summary>Была ли фаза раздана хотя бы раз (отличает «ещё ничего» от «раздали Setup»).</summary>
        private bool _roundStateApplied;

        private readonly SyncDictionary<int, int> _syncedRoundScores = new SyncDictionary<int, int>();

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

        // Серверные машины состояний — создаются при StartGameplay, не требуют NetworkBehaviour
        private SetManager _setManager;
        private RoundManager _roundManager;
        public RoundManager RoundManager => _roundManager;

        // ── Глобальные семантические события для UI (Клиент) ───────────────

        public static event Action<int> OnSetStartedLocal;
        public static event Action<TeamData> OnSetEndedLocal;
        public static event Action<int> OnRoundStartedLocal;
        public static event Action<TeamData> OnRoundEndedLocal;

        /// <summary>
        /// Фаза раунда изменилась на ЭТОЙ машине. Срабатывает одинаково на обычном клиенте,
        /// на хосте и на выделенном сервере — раздача идёт от <see cref="_roundState"/>,
        /// а не от сетевого сообщения. Для представления: арсенал, HUD, зоны спавна.
        /// </summary>
        public static event Action<RoundState> OnRoundStateChangedLocal;

        /// <summary>
        /// Фаза раунда изменилась, и эта машина — сервер. Для авторитетных реакций,
        /// которые обязан выполнить именно сервер (пополнение слотов арсенала и т.п.).
        /// Подписываться только из <c>OnStartServer</c>: на клиенте не срабатывает никогда.
        /// </summary>
        public static event Action<RoundState> OnRoundStateChangedServer;

        // ── Публичные свойства для UI ────────────────────────────────────────

        public EliminationMatchState CurrentMatchState => _matchState;
        public RoundState CurrentRoundState => _roundState;

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
                if (_roundState != RoundState.Equipment || limit <= 0f) return 0f;
                return Mathf.Max(0f, limit - PhaseElapsed);
            }
        }
        /// <summary>Сколько секунд идёт текущая фаза. Считается локально, без обращения к сети.</summary>
        private float PhaseElapsed => (float)Math.Max(0d, NetworkTime.time - _phaseStartTime);

        /// <summary>
        /// Остаток времени раунда. Боевое время расходуется только в фазе Combat:
        /// до неё показывается полная длительность, после — то, что оставалось
        /// в момент конца боя. Так же считал и <c>RoundManager</c> до T-19.
        /// </summary>
        public float RoundTimeRemaining
        {
            get
            {
                float elapsed = _roundState == RoundState.Combat ? PhaseElapsed : _combatElapsed;
                return Mathf.Max(0f, _roundDuration - elapsed);
            }
        }

        /// <summary>
        /// Остаток текущей паузы: обратного отсчёта, паузы после победы или экрана итогов.
        /// Вне этих трёх фаз паузы нет, поэтому ноль. Набор фаз повторяет
        /// <see cref="RoundManager.CountdownTimeRemaining"/> — свойство читает HUD.
        /// </summary>
        public float CountdownTimeRemaining
        {
            get
            {
                switch (_roundState)
                {
                    case RoundState.Countdown:
                        return Mathf.Max(0f, _countdownDuration - PhaseElapsed);
                    case RoundState.Resolution:
                        return Mathf.Max(0f, RoundManager.ResolutionDuration - PhaseElapsed);
                    case RoundState.Scoreboard:
                        return Mathf.Max(0f, RoundManager.ScoreboardDuration - PhaseElapsed);
                    default:
                        return 0f;
                }
            }
        }
        public int CurrentRoundNumber => _currentRound;
        public int RoundsPerSet => _roundsPerSet;

        public int GetRoundScore(TeamData team)
        {
            if (team == null) return 0;
            return _syncedRoundScores.TryGetValue(team.teamIndex, out int score) ? score : 0;
        }

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => false;

        /// <summary>Оружие стреляет только в бою. Фаза — SyncVar, поэтому ответ верен и у клиента.</summary>
        public override bool WeaponsEnabled => _roundState == RoundState.Combat;

        /// <summary>
        /// Арсенал открыт только в закупке; жетон нужен, если закупка кончается готовностью.
        /// Пустые слоты пополняются событием на входе в <c>Setup</c> (<see cref="ServerSetRoundState"/>).
        /// </summary>
        public override ArsenalRules ArsenalRules => new ArsenalRules(
            isOpen: _roundState == RoundState.Equipment,
            usesReadinessTag: _roundStartRule == RoundStartRule.Readiness,
            replacesLostWeapons: false);

        protected override bool CanStartGameplay()
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
        public override bool TeamChoiceLocked => _matchState != EliminationMatchState.WaitingForPlayers;

        /// <summary>
        /// Зовёт корутина старта базового режима — кадром позже спавна. К этому моменту
        /// <see cref="ServerTick"/> мог уже поднять матч (игроки на месте, или продолжение
        /// после паузы), и сброс в <c>WaitingForPlayers</c> начал бы сет заново с раунда 1.
        /// Ожидание игроков и так начальное состояние — здесь только лог.
        /// </summary>
        [Server]
        protected override void StartGameplay()
        {
            if (_matchState != EliminationMatchState.WaitingForPlayers) return;
            GameLog.Match.Info("[EliminationMode] Матч инициализирован. Ждем игроков.");
        }

        [Server]
        private void InitializeActiveGame()
        {
            _matchState = EliminationMatchState.Active;

            // Создаём менеджеры как обычные C# объекты — без GameObject, без NetworkBehaviour.
            // Связывание с OnSetEnded живёт ровно здесь, в конструкторе: событий у SetManager
            // нет, поэтому подписаться дважды (MATCH-01) физически не на что.
            _roundManager = new RoundManager(PlayerRoster, _readinessTimeLimit, _readinessTimeoutRule, _roundStartRule);
            _setManager = new SetManager(_roundManager, OnSetEnded);

            string teamsStr = string.Join(" vs ", Teams.Select(t => t != null ? t.displayName : "null"));
            GameLog.Match.Info(
                $"[EliminationMode] Активная игра начата: {teamsStr}, " +
                $"сетов: {_maxSets}, раундов в сете: {_roundsPerSet}");

            if (_resume != null && _resume.RoundToReplay > 0)
            {
                ResumeSetFromSnapshot(_resume);
                _resume = null;
                return;
            }

            _resume = null;
            StartNextSet(swapSides: false);
        }

        // ── Пауза: снимок и продолжение ─────────────────────────────────────

        /// <summary>Снимок, с которого матч продолжится; null — обычный старт.</summary>
        private MatchSnapshot _resume;

        /// <summary>Elimination встаёт на паузу: сеты, счёт раундов и номер раунда сохраняются.</summary>
        public override bool SupportsPause => true;

        /// <summary>
        /// Сеты (базовый счёт), счёт раундов сета <b>без</b> идущего раунда и его номер —
        /// прерванный раунд сыграется заново и не засчитается.
        /// </summary>
        [Server]
        public override MatchSnapshot CaptureSnapshot()
        {
            MatchSnapshot snapshot = base.CaptureSnapshot();
            if (_setManager == null) return snapshot;

            foreach (var kvp in _setManager.ScoresAtRoundStart) snapshot.RoundScores[kvp.Key] = kvp.Value;
            snapshot.RoundToReplay = _setManager.CurrentRound;
            return snapshot;
        }

        [Server]
        public override void RestoreSnapshot(MatchSnapshot snapshot)
        {
            base.RestoreSnapshot(snapshot);
            _resume = snapshot;
        }

        /// <summary>Продолжение: тот же сет, тот же номер раунда, сохранённый счёт раундов.</summary>
        [Server]
        private void ResumeSetFromSnapshot(MatchSnapshot snapshot)
        {
            int currentSet = 1 + _teamStates.Values.Sum(s => s.Score);
            RpcOnSetStarted(currentSet);

            _syncedRoundScores.Clear();
            foreach (var kvp in snapshot.RoundScores) _syncedRoundScores[kvp.Key] = kvp.Value;

            GameLog.Match.Info(
                $"[EliminationMode] Матч продолжен после паузы: сет {currentSet}, раунд {snapshot.RoundToReplay}.");

            _setManager.ResumeSet(Teams, this, _roundsPerSet, _countdownDuration, _roundDuration,
                                  snapshot.RoundScores, snapshot.RoundToReplay);
        }

        /// <summary>Раунд доигран — в общий счёт серии. Зовёт <see cref="SetManager"/>.</summary>
        [Server]
        public void ServerReportRoundWon(TeamData winner)
        {
            RaiseRoundWon(winner);
        }

        [Server]
        public override void StopGameplay()
        {
            if (_setManager != null)
            {
                _setManager.ForceStop();
            }
            _roundManager = null;
            _setManager = null;

            // Раунда больше не будет — ждать возвращения в зону некому и незачем.
            ClearPendingRespawns();

            // И готовности ждать больше не от кого: список обязан опустеть, иначе HUD
            // остановленного матча так и будет показывать «ждём Петю».
            _pendingReadiness.Clear();

            GameLog.Match.Info(
                $"[EliminationMode] Матч остановлен.");
        }

        // ── Тик (делегирует в RoundManager) ─────────────────────────────────

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
            if (_matchState == EliminationMatchState.WaitingForPlayers)
            {
                if (IsPlayersReady()) InitializeActiveGame();
                return;
            }

            if (_matchState == EliminationMatchState.Finished) return;

            if (_setManager == null) return;

            _setManager.Tick(deltaTime);

            // Единственное, что может измениться за тик, — фаза. Таймеры в сеть больше
            // не пишутся: клиент считает остаток сам, от момента старта фазы (NET-09).
            ServerSetRoundState(_roundManager.State);

            ServerPublishPendingReadiness();

            // Синхронизируем счёт раундов из SetManager
            if (_setManager != null)
            {
                foreach (var kvp in _setManager.TeamRoundScores)
                {
                    if (!_syncedRoundScores.ContainsKey(kvp.Key) || _syncedRoundScores[kvp.Key] != kvp.Value)
                    {
                        _syncedRoundScores[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // ── Внутренняя логика ────────────────────────────────────────────────

        [Server]
        private void StartNextSet(bool swapSides)
        {
            if (swapSides)
                _setManager.SwapTeams();

            int currentSet = 1 + _teamStates.Values.Sum(s => s.Score);
            RpcOnSetStarted(currentSet);

            _syncedRoundScores.Clear();
            _currentRound = 0;

            _setManager.StartSet(Teams, this, _roundsPerSet, _countdownDuration, _roundDuration);
        }

        /// <summary>
        /// Сет доигран: начисляем очко и решаем, закончился ли матч.
        ///
        /// Победитель матча берётся из <see cref="_teamStates"/> — состава, переданного
        /// в <c>Initialize</c>, — а не из глобального <c>TeamRegistry</c>. Считается
        /// в два прохода по той же причине, что и победитель сета: сравнение с текущим
        /// максимумом внутри одного прохода взводит ничью на первой же итерации,
        /// когда максимум ещё равен нулю (MATCH-04).
        /// </summary>
        [Server]
        private void OnSetEnded(TeamData winner)
        {
            if (winner != null && _teamStates.TryGetValue(winner.teamIndex, out TeamRuntimeData winnerState))
            {
                winnerState.AddScore(1);
            }

            int setsToWin = _maxSets / 2 + 1;

            // Проход 1 — сколько сетов сыграно всего и каков максимум.
            int totalSetsPlayed = 0;
            int highestSets = 0;
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                totalSetsPlayed += state.Score;
                if (state.Score > highestSets) highestSets = state.Score;
            }

            // Проход 2 — сколько команд набрали максимум и кто первая из них.
            int leadersCount = 0;
            TeamData leader = null;
            foreach (TeamRuntimeData state in _teamStates.Values)
            {
                if (state.Score != highestSets) continue;

                leadersCount++;
                if (leader == null) leader = state.Team;
            }

            // Ничья — несколько лидеров либо нулевой максимум: матч, в котором никто
            // не выиграл ни одного сета, победителя не имеет.
            TeamData matchWinner = highestSets > 0 && leadersCount == 1 ? leader : null;

            if (highestSets >= setsToWin || totalSetsPlayed >= _maxSets)
            {
                RaiseGameplayEnded(matchWinner);
            }
            else
            {
                StartNextSet(swapSides: true);
            }
        }

        /// <summary>
        /// Вызывается из RoundManager при гибели игрока.
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
            if (_roundManager == null || _roundManager.State != RoundState.Combat) return;

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
                    if (player == null || player.IsAlive) continue;

                    // Зона — условие респавна, а не его точка: игрок физически стоит
                    // в зале и в зону приходит сам. Респавн восстанавливает только
                    // состояние и никого не двигает.
                    if (zone.GetPlayersInZone().Contains(player))
                    {
                        GameLog.Match.Info(
                            $"[EliminationMode] Игрок {player.name} уже в зоне — респаун сразу.");
                        player.Respawn();
                    }
                    else
                    {
                        // Если нет - создаем разовое событие для респауна при входе
                        GameLog.Match.Info(
                            $"[EliminationMode] Игрок {player.name} не в зоне — ожидание возвращения для респауна.");

                        PendingRespawn pending = new PendingRespawn { Zone = zone };
                        pending.Handler = (z, p) =>
                        {
                            if (p != player) return;

                            z.PlayerEntered -= pending.Handler;
                            _pendingRespawns.Remove(pending);

                            p.Respawn();
                            GameLog.Match.Info(
                                $"[EliminationMode] Игрок {player.name} вернулся в зону — отложенный респаун выполнен.");
                        };

                        zone.PlayerEntered += pending.Handler;
                        _pendingRespawns.Add(pending);
                    }
                }
            }
        }

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
        //   · выделенный сервер — из ServerSetRoundState, потому что в сеттере хук
        //     под ServerOnly не срабатывает (Mirror.NetworkBehaviour.GeneratedSyncVarSetter).
        //
        // Поэтому ApplyRoundStateLocal идемпотентна по значению: под хостом её зовут дважды,
        // и второй вызов обязан быть пустым. Подряд идущих одинаковых фаз в машине состояний
        // нет (Setup → Equipment → Countdown → Combat → Resolution → Scoreboard → Setup),
        // так что гашение по значению ничего не теряет.

        /// <summary>Меняет фазу на сервере: пишет состояние и поднимает обе раздачи.</summary>
        [Server]
        private void ServerSetRoundState(RoundState newState)
        {
            if (_roundState == newState) return;

            // Выход из боя — единственный момент, когда боевое время перестаёт течь.
            // Дальше _phaseStartTime уже про другую фазу, поэтому остаток замораживаем.
            if (_roundState == RoundState.Combat) _combatElapsed = PhaseElapsed;

            _roundState = newState;
            _phaseStartTime = NetworkTime.time;

            ApplyRoundStateLocal(newState);
            OnRoundStateChangedServer?.Invoke(newState);

            // Новый раунд — стены пополняют пустые слоты. Раньше стена сама слушала
            // фазы Elimination; теперь она знает только базовый GameMode.
            if (newState == RoundState.Setup)
                RaiseArsenalRefillRequestedServer();
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
            if (_roundManager != null && _roundState == RoundState.Equipment)
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
        private void OnRoundStateSynced(RoundState oldState, RoundState newState)
        {
            ApplyRoundStateLocal(newState);
        }

        /// <summary>Раздаёт фазу локальным подписчикам этой машины ровно один раз на значение.</summary>
        private void ApplyRoundStateLocal(RoundState state)
        {
            if (_roundStateApplied && _appliedRoundState == state) return;

            _roundStateApplied = true;
            _appliedRoundState = state;

            GameLog.Match.Info(
                $"[EliminationMode] Фаза раунда: {state}");
            OnRoundStateChangedLocal?.Invoke(state);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Поздний клиент получает фазу начальным значением спавна. Хук на нём не сработает,
            // если пришедшее значение совпало с дефолтом поля (Setup), — раздаём явно,
            // иначе подписчики так и не узнают, в какой фазе идёт раунд.
            ApplyRoundStateLocal(_roundState);
        }

        [ClientRpc]
        public void RpcOnSetStarted(int setNum)
        {
            OnSetStartedLocal?.Invoke(setNum);
        }

        [ClientRpc]
        public void RpcOnSetEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            OnSetEndedLocal?.Invoke(winner);
        }

        /// <summary>
        /// Начало раунда на сервере. Номер раунда — состояние, поэтому пишется в SyncVar
        /// серверным кодом; Rpc остаётся разовым уведомлением для UI (NET-08).
        /// Раньше присваивание жило внутри Rpc: на выделенном сервере он не исполняется,
        /// и <see cref="CurrentRoundNumber"/> навсегда оставался нулём.
        /// </summary>
        [Server]
        public void ServerBeginRound(int roundNum)
        {
            _currentRound = roundNum;

            // Отметку старта фазы ставим и здесь, а не только в ServerSetRoundState:
            // раунд начинается с Setup, а это же значение стоит в _roundState по умолчанию,
            // поэтому в первом раунде смены состояния — а значит и отметки — не случилось бы,
            // и весь раунд считался бы от нуля (T-19).
            _combatElapsed = 0f;
            _phaseStartTime = NetworkTime.time;

            RpcOnRoundStarted(roundNum);
        }

        [ClientRpc]
        private void RpcOnRoundStarted(int roundNum)
        {
            OnRoundStartedLocal?.Invoke(roundNum);
        }

        [ClientRpc]
        public void RpcOnRoundEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            OnRoundEndedLocal?.Invoke(winner);
        }
    }
}


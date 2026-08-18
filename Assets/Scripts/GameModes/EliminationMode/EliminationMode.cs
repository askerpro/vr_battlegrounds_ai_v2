using System;
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

        [SyncVar] private float _roundTimer;
        [SyncVar] private float _countdownTimer;
        [SyncVar] private int _currentRound;

        /// <summary>Фаза, уже разданная локальным подписчикам на этой машине.</summary>
        private RoundState _appliedRoundState = RoundState.Setup;

        /// <summary>Была ли фаза раздана хотя бы раз (отличает «ещё ничего» от «раздали Setup»).</summary>
        private bool _roundStateApplied;

        private readonly SyncDictionary<int, int> _syncedRoundScores = new SyncDictionary<int, int>();

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
        public float RoundTimeRemaining => Mathf.Max(0f, _roundDuration - _roundTimer);
        public float CountdownTimeRemaining => Mathf.Max(0f, _countdownDuration - _countdownTimer);
        public int CurrentRoundNumber => _currentRound;
        public int RoundsPerSet => _roundsPerSet;

        public int GetRoundScore(TeamData team)
        {
            if (team == null) return 0;
            return _syncedRoundScores.TryGetValue(team.teamIndex, out int score) ? score : 0;
        }

        // ── Реализация GameMode ──────────────────────────────────────────────

        public override bool CanRespawn() => false;

        protected override bool CanStartGameplay()
        {
            // Базовый класс больше не ждет. Наша локальная машина состояний ждет появления игроков.
            return true;
        }

        private bool IsPlayersReady()
        {
            var sessionManager = VrBattlegrounds.Managers.SessionManager.Instance;
            int minPlayers = 2;
            if (sessionManager != null && sessionManager.SelectedGameModeData != null)
            {
                minPlayers = sessionManager.SelectedGameModeData.minPlayersToStart;
            }

            int currentPlayers = PlayersManager.Instance.Sessions.Count();
            if (currentPlayers < minPlayers) return false;

            return _teamStates.Values.All(t => t.HasPlayers());
        }

        [Server]
        protected override void StartGameplay()
        {
            _matchState = EliminationMatchState.WaitingForPlayers;
            GameLog.Info(GameSettings.Instance.LogLevelMatch, "[EliminationMode] Матч инициализирован. Ждем игроков.");
        }

        [Server]
        private void InitializeActiveGame()
        {
            // Создаём менеджеры как обычные C# объекты — без GameObject, без NetworkBehaviour
            _roundManager = new RoundManager();
            _setManager = new SetManager(_roundManager);
            // Подписка делается в StartNextSet: OnSetEnded отписывается в начале обработчика
            // и перевзводится на следующий сет. Вторая подписка здесь давала двойной вызов
            // и удвоение счёта сетов.

            string teamsStr = string.Join(" vs ", Teams.Select(t => t != null ? t.displayName : "null"));
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Активная игра начата: {teamsStr}, " +
                $"сетов: {_maxSets}, раундов в сете: {_roundsPerSet}");

            StartNextSet(swapSides: false);
        }

        [Server]
        public override void StopGameplay()
        {
            if (_setManager != null)
            {
                _setManager.SetEnded -= OnSetEnded;
                _setManager.ForceStop();
            }
            _roundManager = null;
            _setManager = null;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[EliminationMode] Матч остановлен.");
        }

        // ── Тик (делегирует в RoundManager) ─────────────────────────────────

        private void Update()
        {
            if (!isServer) return;

            if (_matchState == EliminationMatchState.WaitingForPlayers)
            {
                if (IsPlayersReady())
                {
                    _matchState = EliminationMatchState.Active;
                    InitializeActiveGame();
                }
                return;
            }

            if (_matchState == EliminationMatchState.Finished) return;

            if (GameplayManager.Instance != null && GameplayManager.Instance.CurrentState == GameplayState.Paused)
                return;

            if (_roundManager == null) return;

            _roundManager.Tick(Time.deltaTime);

            // Обновляем SyncVar каждый тик
            ServerSetRoundState(_roundManager.State);
            _roundTimer = _roundDuration - _roundManager.RoundTimeRemaining;
            _countdownTimer = _countdownDuration - _roundManager.CountdownTimeRemaining;

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

            _setManager.SetEnded += OnSetEnded;

            int currentSet = 1 + _teamStates.Values.Sum(s => s.Score);
            RpcOnSetStarted(currentSet);

            _syncedRoundScores.Clear();
            _currentRound = 0;

            _setManager.StartSet(Teams, this, _roundsPerSet, _countdownDuration, _roundDuration);
        }

        [Server]
        private void OnSetEnded(TeamData winner)
        {
            _setManager.SetEnded -= OnSetEnded;

            if (winner != null && _teamStates.TryGetValue(winner.teamIndex, out TeamRuntimeData winnerState))
            {
                winnerState.AddScore(1);
            }

            int setsToWin = _maxSets / 2 + 1;
            TeamData matchWinner = null;
            int totalSetsPlayed = 0;
            int highestSets = 0;

            foreach (var state in _teamStates.Values)
            {
                totalSetsPlayed += state.Score;
                if (state.Score > highestSets)
                {
                    highestSets = state.Score;
                    matchWinner = state.Team;
                }
                else if (state.Score == highestSets)
                {
                    matchWinner = null; // tie
                }

                if (state.Score >= setsToWin)
                {
                    matchWinner = state.Team;
                    break;
                }
            }

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
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            _roundManager?.OnPlayerDied(player);
        }

        [Server]
        public void PrepareNextRound()
        {
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

                    // Если игрок уже в зоне - респавним сразу
                    if (zone.GetPlayersInZone().Contains(player))
                    {
                        GameLog.Info(GameSettings.Instance.LogLevelMatch,
                            $"[EliminationMode] Игрок {player.name} уже в зоне — респаун сразу.");
                        player.Respawn(zone.transform);
                    }
                    else
                    {
                        // Если нет - создаем разовое событие для респауна при входе
                        GameLog.Info(GameSettings.Instance.LogLevelMatch,
                            $"[EliminationMode] Игрок {player.name} не в зоне — ожидание возвращения для респауна.");

                        Action<Maps.TeamSpawnZone, PlayerController> onEntered = null;
                        onEntered = (z, p) =>
                        {
                            if (p == player)
                            {
                                z.PlayerEntered -= onEntered;
                                p.Respawn(z.transform);
                                GameLog.Info(GameSettings.Instance.LogLevelMatch,
                                    $"[EliminationMode] Игрок {player.name} вернулся в зону — отложенный респаун выполнен.");
                            }
                        };
                        zone.PlayerEntered += onEntered;
                    }
                }
            }
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

            _roundState = newState;
            ApplyRoundStateLocal(newState);
            OnRoundStateChangedServer?.Invoke(newState);
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

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
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


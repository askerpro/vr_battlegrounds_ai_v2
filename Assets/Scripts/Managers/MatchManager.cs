using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет матчем: проводит до _maxSets сетов, считает победы команд, определяет победителя.
    /// Команды и начальные стороны берутся из GameMode.Teams (первые два элемента).
    /// Выполняется только на сервере.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Настройки матча")]
        [SerializeField] private int _maxSets = 10;
        [SerializeField] private GameMode _gameMode;

        // Счёт хранится по teamIndex — две команды из GameMode.Teams[0] и [1]
        [SyncVar] private int _teamAIndex;
        [SyncVar] private int _teamBIndex;
        [SyncVar] public int teamAScore;
        [SyncVar] public int teamBScore;
        [SyncVar] private int _currentSet;
        [SyncVar] private bool _matchActive;

        private SetManager _setManager;

        /// <summary>Матч завершён. Null = ничья.</summary>
        public event System.Action<TeamData> MatchEnded;

        /// <summary>Команда A матча (первая в GameMode.Teams).</summary>
        public TeamData TeamA => TeamRegistry.Instance?.GetByIndex(_teamAIndex);

        /// <summary>Команда B матча (вторая в GameMode.Teams).</summary>
        public TeamData TeamB => TeamRegistry.Instance?.GetByIndex(_teamBIndex);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Server]
        public void StartMatch()
        {
            if (_matchActive)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, "[MatchManager] Матч уже идёт");
                return;
            }

            if (_gameMode == null || _gameMode.Teams.Length < 2)
            {
                GameLog.Error("[MatchManager] GameMode не назначен или содержит менее 2 команд");
                return;
            }

            _teamAIndex = _gameMode.Teams[0].teamIndex;
            _teamBIndex = _gameMode.Teams[1].teamIndex;
            teamAScore = 0;
            teamBScore = 0;
            _currentSet = 0;
            _matchActive = true;

            _setManager = GetComponent<SetManager>();
            _setManager.SetEnded += OnSetFinished;

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Матч начат: {TeamA} vs {TeamB}, максимум сетов: {_maxSets}");

            StartNextSet();
        }

        [Server]
        private void StartNextSet()
        {
            _currentSet++;
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Сет {_currentSet}/{_maxSets}");

            if (_currentSet > 1)
                _setManager.SwapTeams();

            _setManager.StartSet(TeamA, TeamB, _gameMode);
        }

        [Server]
        public void OnSetFinished(TeamData winner)
        {
            _setManager.SetEnded -= OnSetFinished;

            if (winner != null)
            {
                if (winner.teamIndex == _teamAIndex) teamAScore++;
                else if (winner.teamIndex == _teamBIndex) teamBScore++;
            }

            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Счёт сетов: {TeamA} {teamAScore} : {teamBScore} {TeamB}");

            int setsToWin = _maxSets / 2 + 1;
            if (teamAScore >= setsToWin)
            {
                EndMatch(TeamA);
            }
            else if (teamBScore >= setsToWin)
            {
                EndMatch(TeamB);
            }
            else if (_currentSet >= _maxSets)
            {
                TeamData matchWinner = teamAScore > teamBScore ? TeamA
                    : teamBScore > teamAScore ? TeamB
                    : null;
                EndMatch(matchWinner);
            }
            else
            {
                _setManager.SetEnded += OnSetFinished;
                StartNextSet();
            }
        }

        [Server]
        private void EndMatch(TeamData winner)
        {
            _matchActive = false;
            string winnerName = winner != null ? winner.displayName : "ничья";
            RpcOnMatchEnded(winnerName);
            MatchEnded?.Invoke(winner);
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Матч завершён, победитель: {winnerName}. Счёт: {teamAScore}:{teamBScore}");
        }

        [ClientRpc]
        private void RpcOnMatchEnded(string winnerName)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[MatchManager] Матч завершён (клиент), победитель: {winnerName}");
        }
    }
}
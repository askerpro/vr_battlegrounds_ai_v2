using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Абстрактный базовый класс игрового режима.
    ///
    /// Каждый режим самостоятельно управляет своей внутренней структурой —
    /// сетами, раундами, таймерами или любой другой логикой.
    ///
    /// GameplayManager инстанцирует префаб режима через NetworkServer.Spawn,
    /// вызывает Initialize() → StartGameplay(), и ждёт события GameplayEnded.
    /// При StopGameplay() вызывает StopGameplay() на режиме и уничтожает инстанс.
    /// </summary>
    public abstract class GameMode : NetworkBehaviour
    {
        /// <summary>
        /// Срабатывает когда режим определил победителя матча.
        /// Null = ничья. Подписывается GameplayManager.
        /// </summary>
        public event Action<TeamData> GameplayEnded;

        // ── Глобальные семантические события для UI (Клиент) ───────────────

        /// <summary>Срабатывает на клиенте при старте матча.</summary>
        public static event Action OnMatchStartedLocal;

        /// <summary>Срабатывает на клиенте при завершении матча. Null = ничья.</summary>
        public static event Action<TeamData> OnMatchEndedLocal;

        // Команды передаются через Initialize() на сервере.
        private TeamData[] _teams = new TeamData[0];

        protected readonly SyncList<int> _syncedTeamIndices = new SyncList<int>();
        protected readonly SyncDictionary<int, int> _teamScores = new SyncDictionary<int, int>();
        protected readonly Dictionary<int, TeamRuntimeData> _teamStates = new Dictionary<int, TeamRuntimeData>();

        private IPlayerRoster _roster;

        /// <summary>
        /// Откуда режим узнаёт о подключённых игроках. По умолчанию — боевой реестр
        /// поверх <see cref="PlayersManager"/>, который отсутствие менеджера переживает
        /// (NET-12, NET-18). Точка подмены для EditMode-тестов: с заглушкой весь серверный
        /// путь режима прогоняется без живых аватаров и без синглтонов.
        ///
        /// Ставить только до <c>Initialize</c>: состояния команд запоминают реестр в момент
        /// создания.
        /// </summary>
        public IPlayerRoster PlayerRoster
        {
            get => _roster ?? (_roster = new PlayersManagerRoster());
            set => _roster = value;
        }

        /// <summary>Команды, участвующие в матче.</summary>
        public TeamData[] Teams => _teams;

        /// <summary>Объекты состояния команд для серверной логики (ООП).</summary>
        public IReadOnlyDictionary<int, TeamRuntimeData> TeamStates => _teamStates;

        private void Awake()
        {
            _syncedTeamIndices.Callback += OnTeamIndicesChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameplayManager.OnPlayerTeamChangeRequested += OnPlayerTeamChange;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            GameplayManager.OnPlayerTeamChangeRequested -= OnPlayerTeamChange;
        }

        /// <summary>
        /// Клиенту ссылка на активный режим нужна не меньше, чем серверу: через неё
        /// <see cref="GameplayManager"/> читает фазу раунда и блокирует оружие вне боя.
        /// Без этого поле <c>_gameMode</c> заполнялось только в серверном StartGameplay,
        /// на клиенте оставалось null, и блокировка не работала (MATCH-03).
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            if (GameplayManager.Instance != null)
                GameplayManager.Instance.RegisterActiveGameMode(this);
        }

        public override void OnStopClient()
        {
            if (GameplayManager.Instance != null)
                GameplayManager.Instance.UnregisterActiveGameMode(this);

            base.OnStopClient();
        }

        /// <summary>
        /// Вызывается сервером (через GameplayManager) когда игрок подтвердил смену команды.
        /// Здесь режим может сбросить статистику игрока, вычесть очки и т.д.
        /// </summary>
        [Server]
        protected virtual void OnPlayerTeamChange(PlayerSession session, int newTeamId, int newAvatarId)
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch,
                $"[{GetType().Name}] Игрок {session.PlayerName} меняет команду на {newTeamId}. (Место для сброса статистики)");
        }

        private void Start()
        {
            if (isClient && !isServer)
            {
                // При спавне на клиенте первоначальное заполнение SyncList может не вызывать Callback.
                // Поэтому принудительно инициализируем массив команд из скачанных данных списка.
                RefreshTeamsFromSyncList();
            }
        }

        private void OnTeamIndicesChanged(SyncList<int>.Operation op, int itemIndex, int oldItem, int newItem)
        {
            if (isServer) return; // Сервер получает команды в Initialize()
            RefreshTeamsFromSyncList();
        }

        private void RefreshTeamsFromSyncList()
        {
            _teams = new TeamData[_syncedTeamIndices.Count];
            for (int i = 0; i < _syncedTeamIndices.Count; i++)
            {
                _teams[i] = TeamRegistry.Instance.GetByIndex(_syncedTeamIndices[i]);
            }
        }

        /// <summary>
        /// Инициализирует режим командами перед стартом.
        /// Вызывается GameplayManager-ом.
        /// </summary>
        [Server]
        public void Initialize(TeamData[] teams)
        {
            _teams = teams ?? new TeamData[0];
            _syncedTeamIndices.Clear();
            _teamScores.Clear();
            _teamStates.Clear();

            foreach (TeamData t in _teams)
            {
                if (t != null)
                {
                    _syncedTeamIndices.Add(t.teamIndex);
                    _teamScores[t.teamIndex] = 0;
                    _teamStates[t.teamIndex] = new TeamRuntimeData(t, this, PlayerRoster);
                }
            }
        }

        /// <summary>
        /// Вызывается менеджером для запуска матча.
        /// Режим сам решает, когда он готов фактически начать игру (например, дождавшись нужного количества игроков).
        /// </summary>
        /// <summary>
        /// Вызывается менеджером для запуска матча.
        /// Режим сам решает, когда он готов фактически начать игру (по умолчанию - ждет CanStartGameplay).
        /// </summary>
        [Server]
        public virtual void StartGameplayWhenReady()
        {
            StartCoroutine(WaitAndStartGameplayRoutine());
        }

        [Server]
        private System.Collections.IEnumerator WaitAndStartGameplayRoutine()
        {
            GameLog.Info(GameSettings.Instance.LogLevelMatch, $"[{GetType().Name}] Ожидание выполнения условий старта матча...");
            yield return new WaitUntil(CanStartGameplay);

            StartGameplay();
        }

        /// <summary>
        /// Условие готовности режима к фактическому старту (например, наличие игроков в командах).
        /// </summary>
        protected abstract bool CanStartGameplay();

        /// <summary>Секвенция фактического запуска матча. Запускается внутренне из StartGameplayWhenReady.</summary>
        protected abstract void StartGameplay();

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Вызывается GameplayManager-ом при StopGameplay() администратора.
        /// </summary>
        public abstract void StopGameplay();

        /// <summary>
        /// Возвращает текущий счёт команды (фраги, раунды, сеты — зависит от режима).
        /// Используется UI для отображения счёта.
        /// </summary>
        public virtual int GetScore(TeamData team)
        {
            if (team == null) return 0;
            return _teamScores.TryGetValue(team.teamIndex, out int score) ? score : 0;
        }

        /// <summary>Устанавливает счет команде (только на сервере).</summary>
        [Server]
        public virtual void SetScore(TeamData team, int score)
        {
            if (team == null) return;
            _teamScores[team.teamIndex] = score;
        }

        /// <summary>Может ли игрок возродиться в текущем режиме.</summary>
        public abstract bool CanRespawn();

        /// <summary>
        /// Вызвать из конкретного режима когда определён победитель матча.
        /// </summary>
        protected void RaiseGameplayEnded(TeamData winner)
        {
            RpcOnMatchEnded(winner != null ? winner.teamIndex : -1);
            GameplayEnded?.Invoke(winner);
        }

        // ── Сетевые вызовы для UI ──────────────────────────────────────────

        [ClientRpc]
        protected void RpcOnMatchStarted()
        {
            OnMatchStartedLocal?.Invoke();
        }

        [ClientRpc]
        protected void RpcOnMatchEnded(int winnerIndex)
        {
            TeamData winner = winnerIndex >= 0 ? TeamRegistry.Instance.GetByIndex(winnerIndex) : null;
            OnMatchEndedLocal?.Invoke(winner);
        }
    }
}


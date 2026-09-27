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
            PlayersManager.OnSessionConnected += HandleSessionConnected;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            GameplayManager.OnPlayerTeamChangeRequested -= OnPlayerTeamChange;
            PlayersManager.OnSessionConnected -= HandleSessionConnected;
        }

        /// <summary>
        /// Статические события переживают объект: уничтоженный режим в списке
        /// подписчиков — это MissingReference на ближайшем подключении.
        /// </summary>
        private void OnDestroy()
        {
            PlayersManager.OnSessionConnected -= HandleSessionConnected;
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
            GameLog.Match.Info(
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
        /// <summary>
        /// Данные режима (<see cref="GameModeData"/>): команды, минимум игроков, политика
        /// команд, HUD. На сервере — переданные в <see cref="Initialize(GameModeData)"/>,
        /// на клиенте — найденные по реплицируемому <c>modeId</c>.
        /// </summary>
        public GameModeData ModeData
        {
            get
            {
                if (_modeData == null && !string.IsNullOrEmpty(_modeId))
                    _modeData = GameModeCatalog.Find(_modeId);
                return _modeData;
            }
        }

        private GameModeData _modeData;

        /// <summary>Идентификатор данных режима — по нему клиент находит <see cref="ModeData"/>.</summary>
        [SyncVar] private string _modeId = "";

        /// <summary>Инициализация из данных режима — основной путь <c>GameplayManager</c>.</summary>
        [Server]
        public void Initialize(GameModeData data)
        {
            _modeData = data;
            _modeId = data != null ? data.modeId : "";
            Initialize(data != null ? data.teams : null);
        }

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
            // Игроки, пришедшие без команды этого режима (из лобби, где команда своя),
            // получают её сразу: матчу нужны составы команд, спавну — их зоны.
            ServerAssignTeams();

            StartCoroutine(WaitAndStartGameplayRoutine());
        }

        [Server]
        private System.Collections.IEnumerator WaitAndStartGameplayRoutine()
        {
            GameLog.Match.Info($"[{GetType().Name}] Ожидание выполнения условий старта матча...");
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

        // ── Правила, которые режим объявляет остальной игре ─────────────────
        //
        // Системы вне режима (стена арсенала, блокировка оружия) спрашивают активный
        // режим через эти свойства и не знают его конкретного типа. Новый режим
        // объявляет свои правила здесь — и получает их везде, включая лобби, которое
        // теперь тоже режим (LobbyMode). Все свойства читаются на любой машине: режим
        // заспавнен и у клиента, а отвечают они из реплицируемого состояния.

        /// <summary>
        /// Проходит ли урон по игрокам. По умолчанию — да. Лобби отвечает «нет»: смерти
        /// в лобби не бывает. Отменяет урон <c>PlayerController</c> на
        /// <c>UxrActor.DamageReceiving</c>; стрельба по мишеням и предметам не затронута.
        /// </summary>
        public virtual bool PlayersTakeDamage => true;

        /// <summary>
        /// Выбор команды закрыт для самого игрока (матч начался). Сменить команду после
        /// этого может только админ. По умолчанию — открыт.
        /// </summary>
        public virtual bool TeamChoiceLocked => false;

        /// <summary>Режим этой машины или null — единая точка для систем вне режима.</summary>
        public static GameMode Current
        {
            get
            {
                GameplayManager manager = GameplayManager.Instance;
                if (manager == null) return null;

                GameMode mode = manager.ActiveGameMode;
                return mode != null ? mode : null;
            }
        }

        /// <summary>
        /// Стреляет ли оружие прямо сейчас. Читает <c>GameplayManager</c> каждый кадр
        /// и переключает <c>UxrWeaponManager</c>. По умолчанию — да.
        /// </summary>
        public virtual bool WeaponsEnabled => true;

        /// <summary>Что режим требует от стены арсенала сейчас. По умолчанию — закрыта.</summary>
        public virtual ArsenalRules ArsenalRules => ArsenalRules.Closed;

        /// <summary>
        /// Сервер: пора пополнить пустые слоты стен арсенала (например, к новому раунду).
        /// Разовое действие, поэтому событие, а не свойство: см. <see cref="ArsenalRules"/>.
        /// </summary>
        public static event Action ArsenalRefillRequestedServer;

        /// <summary>Объявить стенам, что пустые слоты пора пополнить. Только сервер.</summary>
        protected static void RaiseArsenalRefillRequestedServer()
        {
            ArsenalRefillRequestedServer?.Invoke();
        }

        /// <summary>
        /// Игрок погиб. Зовёт <c>GameplayManager</c> на сервере; что это значит —
        /// решает режим. По умолчанию — ничего.
        /// </summary>
        [Server]
        public virtual void OnPlayerDied(PlayerController player)
        {
        }

        private ITeamAssignmentPolicy _teamAssignmentPolicy;

        /// <summary>
        /// Как режим раздаёт свои команды игрокам без команды режима. Задаётся данными
        /// режима (<see cref="GameModeData.teamAssignment"/>): лобби — автобаланс (команда
        /// одна), матч — выбор игроком (<see cref="PlayerChoiceTeamPolicy"/>). Без данных —
        /// выбор игроком: режим никого не двигает сам. Подменяется целиком (тесты).
        /// </summary>
        public ITeamAssignmentPolicy TeamAssignmentPolicy
        {
            get
            {
                if (_teamAssignmentPolicy == null)
                {
                    GameModeData data = ModeData;
                    _teamAssignmentPolicy = data != null && data.teamAssignment == TeamAssignmentKind.AutoBalance
                        ? (ITeamAssignmentPolicy)new AutoBalanceTeamPolicy()
                        : new PlayerChoiceTeamPolicy();
                }
                return _teamAssignmentPolicy;
            }
            set => _teamAssignmentPolicy = value;
        }

        /// <summary>
        /// Раздаёт команды режима игрокам, у которых их нет, — по <see cref="TeamAssignmentPolicy"/>.
        /// Зовётся при старте режима и при каждом подключении. Игрок с командой режима
        /// не трогается; зрители (<see cref="GameRole.Spectator"/>) команд не получают.
        /// </summary>
        [Server]
        public void ServerAssignTeams()
        {
            if (_teams.Length == 0) return;

            List<PlayerSession> players = new List<PlayerSession>();
            foreach (PlayerSession session in PlayerRoster.GetAllPlayers())
            {
                if (session != null && session.Role == GameRole.Player) players.Add(session);
            }

            foreach (KeyValuePair<PlayerSession, TeamData> pair in TeamAssignmentPolicy.Plan(_teams, players))
                SessionTeamAssigner.Apply(pair.Key, pair.Value, GetType().Name);
        }

        /// <summary>
        /// У каждого подключённого игрока (не зрителя) есть команда этого режима. Условие
        /// старта матча при ручном выборе команд: менеджер ждёт, пока выберут все.
        /// </summary>
        public bool AllPlayersHaveModeTeam()
        {
            // Состав команд берём у реестра (GetPlayers), а не из TeamIndex сессии напрямую:
            // тот же источник правды, что у TeamRuntimeData и условий раунда.
            var assigned = new HashSet<PlayerSession>();
            foreach (TeamData team in _teams)
            {
                if (team == null) continue;
                foreach (PlayerSession session in PlayerRoster.GetPlayers(team))
                    assigned.Add(session);
            }

            foreach (PlayerSession session in PlayerRoster.GetAllPlayers())
            {
                if (session == null || session.Role != GameRole.Player) continue;
                if (!assigned.Contains(session)) return false;
            }

            return true;
        }

        /// <summary>
        /// Минимум игроков для старта: из данных режима, без них — два.
        /// Раньше режим брал его из выбора матча в <c>SessionManager</c> — то есть из чужих данных.
        /// </summary>
        public int MinPlayersToStart => ModeData != null ? ModeData.minPlayersToStart : 2;

        /// <summary>Новый игрок на сервере: команда ему нужна ещё до спавна аватара.</summary>
        private void HandleSessionConnected(PlayerSession session)
        {
            ServerAssignTeams();
        }

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


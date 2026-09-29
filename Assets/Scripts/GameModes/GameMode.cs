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
            MatchTeams.TeamChangeRequested += OnPlayerTeamChange;
            PlayersManager.OnSessionConnected += HandleSessionConnected;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            MatchTeams.TeamChangeRequested -= OnPlayerTeamChange;
            PlayersManager.OnSessionConnected -= HandleSessionConnected;
        }

        /// <summary>
        /// Статические события переживают объект: уничтоженный режим в списке
        /// подписчиков — это MissingReference на ближайшем подключении.
        /// </summary>
        private void OnDestroy()
        {
            PlayersManager.OnSessionConnected -= HandleSessionConnected;
            MatchTeams.TeamChangeRequested -= OnPlayerTeamChange;
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
            // Сервер получает команды в Initialize(). Проверка — NetworkServer.active, а не
            // isServer: режим инициализируется до спавна (данные уезжают клиенту начальным
            // состоянием), и там isServer ещё false — колбэк стирал _teams посреди Initialize.
            if (isServer || NetworkServer.active) return;
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
                if (_modeData == null && !string.IsNullOrEmpty(_modeId) && SessionManager.Instance != null)
                    _modeData = SessionManager.Instance.FindModeData(_modeId);
                return _modeData;
            }
        }

        /// <summary>
        /// Режим — разминка (<see cref="GameModeData.isWarmup"/>): между матчами, с неё
        /// стартует любая карта. Известно и клиенту — по данным режима.
        /// </summary>
        public bool IsWarmup => ModeData != null && ModeData.isWarmup;

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
            TeamData[] assigned = teams ?? new TeamData[0];
            _syncedTeamIndices.Clear();
            _teamScores.Clear();
            _teamStates.Clear();

            _teams = assigned;

            foreach (TeamData t in assigned)
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
            // Команды по политике режима — сразу: матчу нужны составы, спавну — зоны.
            ServerAssignTeams();

            // Новый режим начинает с полной стены: пустые слоты пополняются по его правилам
            // (режим на карте теперь меняется на месте — разминка → матч → разминка).
            RaiseArsenalRefillRequestedServer();

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
        // режим (GameplayManager.ActiveGameMode) через эти свойства и не знают его
        // конкретного типа. Новый режим объявляет свои правила здесь — и получает их
        // везде, включая разминку (WarmupMode). Все свойства читаются на любой машине:
        // режим заспавнен и у клиента, а отвечают они из реплицируемого состояния.

        /// <summary>
        /// Проходит ли урон по игрокам. По умолчанию — да. Разминка отвечает «нет»: смерти
        /// в разминке не бывает. Отменяет урон <c>PlayerController</c> на
        /// <c>UxrActor.DamageReceiving</c>; стрельба по мишеням и предметам не затронута.
        /// </summary>
        public virtual bool PlayersTakeDamage => true;

        /// <summary>
        /// Выбор команды закрыт для самого игрока (матч начался). Сменить команду после
        /// этого может только админ. По умолчанию — открыт.
        /// </summary>
        public virtual bool TeamChoiceLocked => false;

        /// <summary>
        /// Стреляет ли оружие прямо сейчас. Читает <c>GameplayManager</c> каждый кадр
        /// и переключает <c>UxrWeaponManager</c>. По умолчанию — да.
        /// </summary>
        public virtual bool WeaponsEnabled => true;

        /// <summary>
        /// Рисуются ли границы зон спавна (<c>TeamSpawnZone</c>). Зоны нужны матчу — где стоять перед
        /// боем, куда вернуться выбывшему; разминка (лобби и боевая карта до «Начать матч») —
        /// свободная арена без границ. Логика зон (спавн, «в зоне») работает независимо от этого.
        /// Свойство класса режима, без сети — ответ одинаков у сервера и клиента.
        /// </summary>
        public virtual bool ShowsSpawnZones => false;

        /// <summary>Что режим требует от стены арсенала сейчас. По умолчанию — закрыта.</summary>
        public virtual ArsenalRules ArsenalRules => ArsenalRules.Closed;

        /// <summary>
        /// Сервер: пора пополнить пустые слоты стен арсенала (например, к новому раунду).
        /// Разовое действие, поэтому событие, а не свойство: см. <see cref="ArsenalRules"/>.
        /// Событие экземпляра: режим на карте теперь меняется на месте, и статическое
        /// событие пришлось бы разбирать, чей это запрос. Стена подписывается на активный
        /// режим и переподписывается при его смене.
        /// </summary>
        public event Action ArsenalRefillRequestedServer;

        /// <summary>Объявить стенам, что пустые слоты пора пополнить. Только сервер.</summary>
        protected void RaiseArsenalRefillRequestedServer()
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

        /// <summary>
        /// Сервер создал аватар сессии (<c>AvatarManager</c>): первый вход, пересоздание после
        /// смены карты, смена скина или команды. В каком состоянии он входит в игру, решает
        /// режим. По умолчанию — как создан: живым.
        /// </summary>
        /// <param name="player">Новый аватар, уже в сети.</param>
        /// <param name="continuesPrevious">
        /// Аватар продолжает прежний — смена скина или команды, возвращение после переподключения.
        /// Жизнь и выбывание уже перенесены с прежнего, режиму решать нечего, кроме возрождения.
        /// </param>
        [Server]
        public virtual void ServerAdmitAvatar(PlayerController player, bool continuesPrevious)
        {
        }

        /// <summary>
        /// Погибшего убил <paramref name="killer"/> (сессия; null — урон без источника или
        /// самоубийство). Определяет по урону <c>DamageLedger</c>. Зовётся после
        /// <see cref="OnPlayerDied"/>. По умолчанию — ничего; Respawn считает фраги.
        /// </summary>
        [Server]
        public virtual void OnPlayerKilled(PlayerController victim, PlayerSession killer)
        {
        }

        // ── Раунды и пауза ───────────────────────────────────────────────────

        /// <summary>
        /// Сервер: раунд доигран до конца и у него есть победитель. Для общего счёта серии
        /// (<c>MatchSeries</c> через <c>GameplayManager.RoundWon</c>). Прерванный паузой раунд
        /// сюда не приходит — поэтому событие поднимается по окончании раунда, а не в момент,
        /// когда победитель стал известен.
        /// </summary>
        public event Action<TeamData> RoundWonServer;

        /// <summary>Объявить, что раунд доигран и выигран. Только сервер.</summary>
        protected void RaiseRoundWon(TeamData winner)
        {
            if (winner != null) RoundWonServer?.Invoke(winner);
        }

        /// <summary>
        /// Умеет ли режим встать на паузу и продолжиться: «Пауза» у админа сохраняет его
        /// состояние (<see cref="CaptureSnapshot"/>), карта уходит в разминку, «Продолжить»
        /// спавнит режим заново и возвращает состояние (<see cref="RestoreSnapshot"/>).
        /// По умолчанию — нет (разминке ставить на паузу нечего).
        /// </summary>
        public virtual bool SupportsPause => false;

        /// <summary>
        /// Снимок состояния матча для паузы. База — счёт команд режима (сеты, фраги); режим
        /// с раундами добавляет своё. Прерванный раунд в снимок не входит.
        /// </summary>
        [Server]
        public virtual MatchSnapshot CaptureSnapshot()
        {
            var snapshot = new MatchSnapshot { ModeId = ModeData != null ? ModeData.modeId : "" };
            foreach (KeyValuePair<int, int> pair in _teamScores) snapshot.TeamScores[pair.Key] = pair.Value;
            return snapshot;
        }

        /// <summary>
        /// Возвращает состояние из снимка. Зовётся после <see cref="Initialize(GameModeData)"/>
        /// (она обнуляет счёт) и до старта режима.
        /// </summary>
        [Server]
        public virtual void RestoreSnapshot(MatchSnapshot snapshot)
        {
            if (snapshot == null) return;

            foreach (KeyValuePair<int, int> pair in snapshot.TeamScores)
            {
                if (_teamScores.ContainsKey(pair.Key)) _teamScores[pair.Key] = pair.Value;
            }
        }

        private TeamAssignmentKind? _teamAssignmentOverride;

        /// <summary>
        /// Как режим раздаёт свои команды игрокам без команды режима. Задаётся данными
        /// режима (<see cref="GameModeData.teamAssignment"/>): разминка — сохранить команду
        /// матча или дать свою (<see cref="TeamAssignmentKind.KeepOrDefault"/>), матч —
        /// выбор игроком. Без данных — выбор игроком: режим никого не двигает сам.
        /// Присваивание подменяет значение данных (тесты).
        /// </summary>
        public TeamAssignmentKind TeamAssignment
        {
            get
            {
                if (_teamAssignmentOverride.HasValue) return _teamAssignmentOverride.Value;
                GameModeData data = ModeData;
                return data != null ? data.teamAssignment : TeamAssignmentKind.PlayerChoice;
            }
            set => _teamAssignmentOverride = value;
        }

        /// <summary>
        /// Раздаёт команды режима по <see cref="TeamAssignment"/>. Зовётся при старте режима
        /// и при каждом подключении. Зрители (<see cref="GameRole.Spectator"/>) команд не получают.
        /// <list type="bullet">
        /// <item><c>PlayerChoice</c> — никого: выбирает игрок или выдаёт админ.</item>
        /// <item><c>AutoBalance</c> — всех без команды режима, в самую малочисленную.</item>
        /// <item><c>KeepOrDefault</c> — только тех, у кого команды нет вовсе (0): команда
        ///       матча (CT/T) в разминке сохраняется, а с ней общий состав серии.</item>
        /// </list>
        /// </summary>
        [Server]
        public void ServerAssignTeams()
        {
            if (_teams.Length == 0) return;

            TeamAssignmentKind kind = TeamAssignment;
            if (kind == TeamAssignmentKind.PlayerChoice) return;

            List<PlayerSession> players = new List<PlayerSession>();
            foreach (PlayerSession session in PlayerRoster.GetAllPlayers())
            {
                if (session == null || session.Role != GameRole.Player) continue;
                if (kind == TeamAssignmentKind.KeepOrDefault && session.TeamIndex != 0) continue;
                players.Add(session);
            }

            foreach (KeyValuePair<PlayerSession, TeamData> pair in TeamAutoBalance.Plan(_teams, players, s => s.TeamIndex))
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


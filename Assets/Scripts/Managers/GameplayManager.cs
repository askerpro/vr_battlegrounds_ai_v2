using System.Linq;
// Forced compilation trigger
using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using UltimateXR.Mechanics.Weapons;

using VrBattlegrounds.Player.Avatars;
namespace VrBattlegrounds.Managers
{
    public enum GameplayState
    {
        NotActive,
        Active,
        Paused
    }

    /// <summary>
    /// Тонкий оркестратор матча: инстанцирует префаб режима через NetworkServer.Spawn,
    /// передаёт ему команды и ждёт события GameMode.GameplayEnded.
    ///
    /// Вся логика матча (сеты, раунды, таймеры, счёт) живёт внутри конкретного GameMode.
    /// GameplayManager не знает о структуре режима — только Start/Stop и результат,
    /// а правила (оружие, арсенал) читает через виртуальные свойства базового GameMode.
    ///
    /// Какой режим запускать — <see cref="ResolveGameModeData"/>: режим сцены (лобби —
    /// <c>LobbyMode</c>, стартует сам) или выбор администратора (карта, старт по команде).
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.GameplayManager)]
    public class GameplayManager : NetworkBehaviour
    {
        public static GameplayManager Instance { get; private set; }

        /// <summary>Матч завершён. Null = ничья.</summary>
        public event Action<TeamData> GameplayEnded;

        [Header("Режим сцены")]
        [Tooltip("Режим, заданный самой сценой (лобби). Задан — стартует сам при старте сервера " +
                 "и не зависит от выбора администратора. Пусто — режим матча, выбранный " +
                 "администратором (SessionManager.SelectedGameModeData), старт по команде.")]
        [SerializeField] private GameModeData _sceneGameMode;

        [SyncVar] private GameplayState _currentState = GameplayState.NotActive;

        /// <summary>Режим задан сценой и стартует без администратора.</summary>
        public bool HasSceneGameMode => _sceneGameMode != null;

        /// <summary>Режим, заданный сценой, или null.</summary>
        public GameModeData SceneGameMode => _sceneGameMode;

        /// <summary>
        /// Какой режим запускать: режим сцены, если он задан, иначе выбор матча.
        ///
        /// <para>
        /// Два источника не смешиваются. <c>SessionManager.SelectedGameModeData</c> — выбор
        /// администратора для <b>следующего матча</b>, он живёт всю сессию и меняется в лобби.
        /// Режим сцены — свойство самой сцены (лобби — всегда <c>LobbyMode</c>), и выбор
        /// администратора на него влиять не должен.
        /// </para>
        /// </summary>
        public GameModeData ResolveGameModeData(GameModeData matchChoice)
        {
            return _sceneGameMode != null ? _sceneGameMode : matchChoice;
        }

        /// <summary>
        /// Режим сцены стартует сам, без администратора: лобби не ждёт кнопки «Старт».
        /// Спавн дочернего объекта из <c>OnStartServer</c> законен — так же стена
        /// арсенала выдаёт оружие.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (HasSceneGameMode && _currentState == GameplayState.NotActive)
                StartGameplay();
        }

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public GameplayState CurrentState => _currentState;
        public bool IsGameplayActive => _currentState == GameplayState.Active;

        public GameMode ActiveGameMode => _gameMode;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        /// <summary>
        /// Клиент увидел заспавненный режим и сообщает о нём. На сервере ссылку ставит
        /// <see cref="StartGameplay"/>; повторная запись того же объекта на хосте безвредна.
        /// </summary>
        internal void RegisterActiveGameMode(GameMode mode)
        {
            if (mode == null) return;
            _gameMode = mode;
            ActiveGameModeChangedLocal?.Invoke(mode);
        }

        /// <summary>Режим уничтожен на этой машине. Снимаем ссылку, если она указывает на него.</summary>
        internal void UnregisterActiveGameMode(GameMode mode)
        {
            if (_gameMode != mode) return;
            _gameMode = null;
            ActiveGameModeChangedLocal?.Invoke(null);
        }

        /// <summary>
        /// Режим этой машины появился (или исчез — null). Для представления, которому нужен
        /// именно активный режим, а не выбор матча: HUD игрока.
        /// </summary>
        public static event Action<GameMode> ActiveGameModeChangedLocal;

        private void Update()
        {

            // Управляем доступностью оружия через UxrWeaponManager.
            // Правило объявляет режим (GameMode.WeaponsEnabled), а он известен и клиенту
            // (MATCH-03): раньше _gameMode заполнялся только серверным StartGameplay,
            // на клиенте оставался null, и оружие не блокировалось вне боя.
            // Конкретный режим здесь не упоминается: лобби отвечает «всегда»,
            // Elimination — «только в бою».
            if (UxrWeaponManager.HasInstance)
            {
                bool weaponsEnabled = _gameMode == null || _gameMode.WeaponsEnabled;

                if (UxrWeaponManager.Instance.WeaponSystemEnabled != weaponsEnabled)
                {
                    UxrWeaponManager.Instance.SetWeaponSystemEnabled(weaponsEnabled);
                }
            }
        }

        /// <summary>
        /// Оркестратор матча появился на этой машине и готов принимать команды.
        ///
        /// <para>
        /// В отличие от остальных менеджеров он живёт не в <c>PersistentRoot</c>, а в сцене
        /// (карты и лобби — у каждой свой экземпляр). Значит <c>Instance</c> равен null
        /// в окне смены сцены и в Offline, и это норма, а не сбой. Событие даёт подписаться
        /// на его появление вместо того, чтобы опрашивать <c>Instance</c> каждый кадр
        /// или пробовать «ещё раз через кадр».
        /// </para>
        ///
        /// <para>
        /// Опоздавший подписчик не теряет сигнал: <see cref="SubscribeToInstance" />
        /// сразу отдаёт уже существующий экземпляр. Иначе система, стартовавшая позже
        /// загрузки карты, снова начала бы угадывать.
        /// </para>
        /// </summary>
        private static event Action<GameplayManager> InstanceReady;

        /// <summary>
        /// Подписка на появление оркестратора матча. Если он уже есть — обработчик
        /// вызывается немедленно, ещё до возврата из метода.
        /// </summary>
        public static void SubscribeToInstance(Action<GameplayManager> handler)
        {
            if (handler == null) return;

            InstanceReady += handler;

            if (Instance != null) handler(Instance);
        }

        /// <summary>Отписка. Обязательна в <c>OnDisable</c>: событие статическое и переживает сцену.</summary>
        public static void UnsubscribeFromInstance(Action<GameplayManager> handler)
        {
            if (handler == null) return;
            InstanceReady -= handler;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            InstanceReady?.Invoke(this);
        }

        /// <summary>
        /// Карта выгружена — оркестратор уничтожен вместе с ней. Снимаем статическую ссылку:
        /// уничтоженный объект равен null по правилам Unity, но не по правилам C#, и код,
        /// сравнивающий через <c>ReferenceEquals</c> или кэширующий ссылку, увидел бы живой
        /// менеджер там, где его уже нет.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Server]
        public void StartGameplay()
        {
            if (_currentState != GameplayState.NotActive)
            {
                GameLog.Match.Warning("[GameplayManager] Матч уже идёт или на паузе");
                return;
            }

            GameModeData gameModeData;

            if (HasSceneGameMode)
            {
                // Режим сцены (лобби). Выбор администратора сюда не спрашивается вовсе.
                gameModeData = ResolveGameModeData(null);
            }
            else
            {
                if (SessionManager.Instance == null)
                {
                    GameLog.Error("[GameplayManager] StartGameplay: SessionManager.Instance == null. " +
                                  "Убедитесь что SessionManager добавлен на MirrorNetworkManager в сцене Offline.");
                    return;
                }

                gameModeData = ResolveGameModeData(SessionManager.Instance.SelectedGameModeData);

                if (gameModeData == null)
                {
                    GameLog.Error($"[GameplayManager] StartGameplay: режим не найден. " +
                                  $"SelectedModeId='{SessionManager.Instance.SelectedModeId}', " +
                                  $"SelectedMapScene='{SessionManager.Instance.SelectedMapScene}'.");
                    return;
                }
            }

            if (gameModeData.modePrefab == null)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{gameModeData.modeId}' не содержит modePrefab");
                return;
            }

            // Минимум одна команда: лобби — режим с одной командой. Сколько команд нужно
            // матчу, решает сам режим (Elimination ждёт игроков в каждой из своих).
            if (gameModeData.teams == null || gameModeData.teams.Length < 1)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{gameModeData.modeId}' не содержит команд");
                return;
            }

            // Инстанцируем и спавним через Mirror — клиенты увидят объект
            _gameModeInstance = Instantiate(gameModeData.modePrefab, transform);
            NetworkServer.Spawn(_gameModeInstance);

            _gameMode = _gameModeInstance.GetComponent<GameMode>();
            if (_gameMode == null)
            {
                GameLog.Error($"[GameplayManager] Префаб '{gameModeData.modeId}' не содержит компонент GameMode");
                NetworkServer.UnSpawn(_gameModeInstance);
                Destroy(_gameModeInstance);
                _gameModeInstance = null;
                return;
            }

            _gameMode.Initialize(gameModeData);
            _gameMode.GameplayEnded += OnGameplayEnded;

            _currentState = GameplayState.Active;

            GameLog.Match.Info(
                $"[GameplayManager] Запуск режима: {gameModeData.modeId} ({gameModeData.displayName})");

            _gameMode.StartGameplayWhenReady();
        }

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Используется администратором для возврата в Lobby.
        /// </summary>
        [Server]
        public void StopGameplay()
        {
            if (_currentState == GameplayState.NotActive)
            {
                GameLog.Match.Warning("[GameplayManager] StopGameplay: матч не активен");
                return;
            }

            _gameMode?.StopGameplay();
            CleanupGameMode();

            _currentState = GameplayState.NotActive;
            GameLog.Match.Info("[GameplayManager] Матч остановлен администратором");
            RpcOnMatchStopped();
        }

        [Server]
        public void PauseGameplay()
        {
            if (_currentState == GameplayState.Active)
            {
                _currentState = GameplayState.Paused;
                GameLog.Match.Info("[GameplayManager] Матч поставлен на паузу");
            }
        }

        [Server]
        public void ResumeGameplay()
        {
            if (_currentState == GameplayState.Paused)
            {
                _currentState = GameplayState.Active;
                GameLog.Match.Info("[GameplayManager] Матч снят с паузы");
            }
        }

        [Server]
        private void OnGameplayEnded(TeamData winner)
        {
            CleanupGameMode();
            _currentState = GameplayState.NotActive;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info(
                $"[GameplayManager] Матч завершён, победитель: {winnerName}");

            GameplayEnded?.Invoke(winner);
            RpcOnGameplayEnded(winnerName);
        }

        [Server]
        private void CleanupGameMode()
        {
            if (_gameMode != null)
            {
                _gameMode.GameplayEnded -= OnGameplayEnded;
                _gameMode = null;
            }

            if (_gameModeInstance != null)
            {
                NetworkServer.UnSpawn(_gameModeInstance);
                Destroy(_gameModeInstance);
                _gameModeInstance = null;
            }
        }

        [ClientRpc]
        private void RpcOnGameplayEnded(string winnerName)
        {
            GameLog.Match.Info(
                $"[GameplayManager] Матч завершён (клиент), победитель: {winnerName}");
        }

        [ClientRpc]
        private void RpcOnMatchStopped()
        {
            GameLog.Match.Info("[GameplayManager] Матч остановлен (клиент)");
        }

        /// <summary>
        /// Вызывается PlayerController при гибели игрока.
        /// Делегирует в активный GameMode — каждый режим обрабатывает гибель по-своему.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player)
        {
            if (_gameMode != null)
                _gameMode.OnPlayerDied(player);
        }

        // ── Команды: выбор игроком, выдача админом ────────────────────────────
        //
        // Правила «кто и когда» — TeamChangeRules, исполнение — SessionTeamAssigner.
        // Здесь только входы: GameplayManager знает активный режим и его команды.

        /// <summary>Глобальное серверное событие: игроку меняют команду/скин (хуки режима).</summary>
        public static event Action<PlayerSession, int, int> OnPlayerTeamChangeRequested;

        /// <summary>Поднимает <see cref="OnPlayerTeamChangeRequested"/>. Зовёт исполнитель смены.</summary>
        internal static void NotifyTeamChangeRequested(PlayerSession session, int teamId, int avatarId)
        {
            OnPlayerTeamChangeRequested?.Invoke(session, teamId, avatarId);
        }

        /// <summary>
        /// Игрок сам выбрал команду и скин в планшете (<c>PlayerSession.CmdRequestTeamChange</c>).
        /// Разрешено только в команду активного режима и только пока выбор открыт
        /// (<see cref="GameMode.TeamChoiceLocked"/> — до старта матча); скин в своей команде — всегда.
        /// </summary>
        [Server]
        public void ProcessTeamChangeRequest(PlayerSession session, int newTeamId, int newAvatarId)
        {
            if (session == null) return;

            GameLog.Match.Info(
                $"[GameplayManager] Игрок {session.PlayerName} запросил смену: Команда {newTeamId}, Скин {newAvatarId}");

            if (!TeamChangeRules.CanPlayerChoose(_gameMode, session.TeamIndex, newTeamId, out string reason))
            {
                GameLog.Match.Warning($"[GameplayManager] Смена отклонена ({session.PlayerName}): {reason}.");
                return;
            }

            TeamData team = FindTeam(newTeamId);
            if (team == null)
            {
                GameLog.Match.Warning($"[GameplayManager] Смена отклонена ({session.PlayerName}): команды {newTeamId} нет в реестре.");
                return;
            }

            SessionTeamAssigner.Apply(session, team, newAvatarId, "GameplayManager/выбор игрока");
        }

        /// <summary>
        /// Админ выдаёт игроку команду. Отдельная серверная точка: выбор команды игроком
        /// закрыт после старта матча, а админ переводит игрока всегда и в любую команду
        /// из <c>TeamRegistry</c> (например, опоздавшего — в команду матча). Скин сохраняется,
        /// если он есть в новой команде.
        /// </summary>
        /// <returns>true — команда выдана.</returns>
        [Server]
        public bool ServerAdminAssignTeam(PlayerSession admin, PlayerSession target, int teamId)
        {
            if (!TeamChangeRules.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[GameplayManager] Выдача команды отклонена: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            TeamData team = FindTeam(teamId);
            if (target == null || team == null)
            {
                GameLog.Match.Warning($"[GameplayManager] Выдача команды отклонена: нет игрока или команды {teamId}.");
                return false;
            }

            SessionTeamAssigner.Apply(target, team, $"GameplayManager/админ {admin.PlayerName}");
            return true;
        }

        /// <summary>
        /// Админ разово раскладывает игроков без команды режима автобалансом
        /// (<see cref="AutoBalanceTeamPolicy"/>). Политику режима не меняет.
        /// </summary>
        /// <returns>Сколько игроков получили команду.</returns>
        [Server]
        public int ServerAdminAutoBalance(PlayerSession admin)
        {
            if (!TeamChangeRules.IsAdmin(admin) || _gameMode == null) return 0;

            var players = new System.Collections.Generic.List<PlayerSession>();
            foreach (PlayerSession session in _gameMode.PlayerRoster.GetAllPlayers())
            {
                if (session != null && session.Role == GameRole.Player) players.Add(session);
            }

            var plan = new AutoBalanceTeamPolicy().Plan(_gameMode.Teams, players);
            foreach (var pair in plan)
                SessionTeamAssigner.Apply(pair.Key, pair.Value, $"GameplayManager/автобаланс админа {admin.PlayerName}");

            return plan.Count;
        }

        /// <summary>Команда по индексу: сначала из активного режима, затем из реестра.</summary>
        private TeamData FindTeam(int teamId)
        {
            if (_gameMode != null)
            {
                foreach (TeamData t in _gameMode.Teams)
                    if (t != null && t.teamIndex == teamId) return t;
            }

            return teamId != 0 && TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamId) : null;
        }
    }
}

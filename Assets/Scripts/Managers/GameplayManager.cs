using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Managers
{
    public enum GameplayState
    {
        /// <summary>Матча нет — на карте разминка (или режима нет вовсе).</summary>
        NotActive,
        /// <summary>Идёт матч — режим матча, выбранный администратором.</summary>
        Active,
        Paused
    }

    /// <summary>
    /// Жизнь режима на карте: какой режим сейчас заспавнен и как он меняется.
    ///
    /// <para>
    /// <b>Режим на карте меняется на месте, без перезагрузки сцены.</b> Любая карта
    /// (лобби тоже) стартует в разминке (<see cref="ServerStartWarmup"/>, из
    /// <c>OnStartServer</c>). «Начать матч» (<see cref="StartMatch"/>) останавливает
    /// разминку и спавнит режим матча; матч кончился (<see cref="GameplayEnded"/>) —
    /// снова разминка, а серия (<see cref="MatchSeries"/>) решает, какая карта следующая.
    /// Какой режим допустим на карте — <see cref="MapModeRules"/> по <c>MapData.supportedModes</c>.
    /// </para>
    ///
    /// <para>
    /// Вся логика матча (сеты, раунды, таймеры, счёт на карте) живёт в конкретном
    /// <see cref="GameMode"/>; правила (оружие, арсенал) менеджер читает через виртуальные
    /// свойства базового класса. Команды — не здесь, а в <see cref="MatchTeams"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.GameplayManager)]
    public class GameplayManager : NetworkBehaviour
    {
        public static GameplayManager Instance { get; private set; }

        /// <summary>Матч на карте завершён (сервер). Null = ничья. Слушает серия карт.</summary>
        public event Action<TeamData> GameplayEnded;

        [SyncVar] private GameplayState _currentState = GameplayState.NotActive;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public GameplayState CurrentState => _currentState;

        /// <summary>Идёт матч (режим матча, а не разминка).</summary>
        public bool IsMatchActive => _currentState != GameplayState.NotActive;

        /// <summary>Режим этой машины: разминка или матч. Null — в окне смены режима или сцены.</summary>
        public GameMode ActiveGameMode => _gameMode;

        /// <summary>
        /// Имя сцены вместо своей — для EditMode-тестов, где объект живёт в безымянной сцене.
        /// </summary>
        public string SceneNameOverride { get; set; }

        /// <summary>
        /// Создание экземпляра режима из данных. По умолчанию — <c>Instantiate(modePrefab)</c>;
        /// тесты подменяют: в EditMode Unity не зовёт <c>Awake</c> у инстанцированных префабов.
        /// </summary>
        public Func<GameModeData, GameObject> ModeFactory { get; set; }

        /// <summary>Карта, на которой живёт менеджер; null — сцены нет в реестре карт.</summary>
        public MapData CurrentMap =>
            SessionManager.Instance != null ? SessionManager.Instance.FindMap(SceneName) : null;

        private string SceneName => !string.IsNullOrEmpty(SceneNameOverride) ? SceneNameOverride : gameObject.scene.name;

        private static GameModeRegistry Registry =>
            SessionManager.Instance != null ? SessionManager.Instance.ModeRegistry : null;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        /// <summary>Карта (любая, и лобби) стартует в разминке — без администратора.</summary>
        public override void OnStartServer()
        {
            base.OnStartServer();
            _startedOnServer = true;

            if (_gameMode == null)
                ServerStartWarmup();

            if (_matchRequestedBeforeSpawn)
            {
                _matchRequestedBeforeSpawn = false;
                StartMatch();
            }
        }

        /// <summary>
        /// «Начать матч» пришёл раньше, чем объект заспавнен (автостарт отладки слушает
        /// <see cref="SubscribeToInstance"/>, а тот срабатывает в <c>Awake</c>). Спавнить режим
        /// дочерним объектом незаспавненного менеджера нельзя — запрос ждёт <c>OnStartServer</c>.
        /// </summary>
        private bool _matchRequestedBeforeSpawn;

        /// <summary>
        /// <c>OnStartServer</c> уже прошёл. Своя отметка, а не <c>isServer</c>: в <c>Awake</c>
        /// у <c>NetworkBehaviour</c> ещё нет <c>netIdentity</c>, и <c>isServer</c> падает с NRE.
        /// </summary>
        private bool _startedOnServer;

        /// <summary>
        /// Режим заспавнен на этой машине. На сервере ссылку ставит <see cref="ServerSwitchTo"/>,
        /// клиенту — <c>GameMode.OnStartClient</c>; повтор того же объекта ничего не делает.
        /// </summary>
        internal void RegisterActiveGameMode(GameMode mode)
        {
            if (mode == null || _gameMode == mode) return;
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
        /// Режим этой машины появился или сменился (null — исчез). Для представления и для
        /// тех, кто слушает события экземпляра режима: HUD игрока, стена арсенала.
        /// </summary>
        public static event Action<GameMode> ActiveGameModeChangedLocal;

        private void Update()
        {
            // Доступность оружия объявляет режим (GameMode.WeaponsEnabled), и он известен
            // и клиенту (MATCH-03). Разминка отвечает «всегда», Elimination — «только в бою».
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
        /// Оркестратор режима появился на этой машине. Живёт в сцене (у каждой карты и
        /// у лобби свой), поэтому <c>Instance</c> равен null в окне смены сцены и в Offline.
        /// Опоздавший подписчик не теряет сигнал: <see cref="SubscribeToInstance" /> сразу
        /// отдаёт уже существующий экземпляр.
        /// </summary>
        private static event Action<GameplayManager> InstanceReady;

        /// <summary>
        /// Подписка на появление оркестратора. Если он уже есть — обработчик вызывается
        /// немедленно, ещё до возврата из метода.
        /// </summary>
        public static void SubscribeToInstance(Action<GameplayManager> handler)
        {
            if (handler == null) return;

            InstanceReady += handler;

            if (Instance != null) handler(Instance);
        }

        /// <summary>Отписка. Обязательна: событие статическое и переживает сцену.</summary>
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
        /// уничтоженный объект равен null по правилам Unity, но не по правилам C#.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Смена режима на карте ─────────────────────────────────────────────

        /// <summary>
        /// Разминка на этой карте (<see cref="MapModeRules.ResolveWarmup"/>). Идущий режим
        /// останавливается. Команды, общий счёт серии и статистика не трогаются.
        /// </summary>
        /// <returns>false — разминки нет ни у карты, ни в реестре.</returns>
        [Server]
        public bool ServerStartWarmup()
        {
            GameModeData warmup = MapModeRules.ResolveWarmup(CurrentMap, Registry);
            if (warmup == null)
            {
                GameLog.Error($"[GameplayManager] На карте '{SceneName}' нет разминки: нет ни в MapData.supportedModes, " +
                              "ни в GameModeRegistry (режим с флагом isWarmup).");
                return false;
            }

            _currentState = GameplayState.NotActive;
            return ServerSwitchTo(warmup, stopCurrent: true);
        }

        /// <summary>
        /// «Начать матч»: разминка → режим, выбранный администратором
        /// (<c>SessionManager.SelectedGameModeData</c>), на месте. Выбор несовместим с картой —
        /// первый совместимый режим матча; совместимых нет (лобби) — матч не начинается.
        /// </summary>
        /// <returns>true — режим матча заспавнен.</returns>
        [Server]
        public bool StartMatch()
        {
            if (IsMatchActive)
            {
                GameLog.Match.Warning("[GameplayManager] Матч уже идёт или на паузе.");
                return false;
            }

            if (!_startedOnServer)
            {
                _matchRequestedBeforeSpawn = true;
                GameLog.Match.Verbose("[GameplayManager] «Начать матч» до спавна карты — после разминки.");
                return false;
            }

            GameModeData selected = SessionManager.Instance != null &&
                                    !string.IsNullOrEmpty(SessionManager.Instance.SelectedModeId)
                ? SessionManager.Instance.SelectedGameModeData
                : null;

            MapData map = CurrentMap;
            GameModeData mode = MapModeRules.ResolveMatchMode(map, selected, Registry);

            if (mode == null)
            {
                GameLog.Match.Warning(
                    $"[GameplayManager] Матч не начат: на карте '{SceneName}' нет совместимого режима матча " +
                    $"(выбран '{(selected != null ? selected.modeId : "ничего")}').");
                return false;
            }

            if (selected != null && mode != selected)
            {
                GameLog.Match.Info(
                    $"[GameplayManager] Режим '{selected.modeId}' несовместим с картой '{SceneName}' — " +
                    $"берётся первый совместимый: '{mode.modeId}'.");
            }

            if (!ServerSwitchTo(mode, stopCurrent: true)) return false;

            _currentState = GameplayState.Active;
            return true;
        }

        /// <summary>
        /// Принудительно останавливает матч без победителя — карта возвращается в разминку.
        /// Серию это не двигает: следующую карту или лобби выбирает администратор.
        /// </summary>
        [Server]
        public void StopMatch()
        {
            if (!IsMatchActive)
            {
                GameLog.Match.Warning("[GameplayManager] StopMatch: матч не идёт.");
                return;
            }

            GameLog.Match.Info("[GameplayManager] Матч остановлен администратором — разминка.");
            ServerStartWarmup();
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

        /// <summary>
        /// Смена режима на месте: текущий останавливается и уходит из сети, новый спавнится.
        ///
        /// <para>
        /// Что принадлежит режиму, уходит вместе с ним: его правила-компоненты (уборка пола,
        /// карман, раундовые магазины), счёт на карте, машина раундов. Новый режим стартует
        /// с чистого пола (<c>ModeStartCleanup</c> на префабе) и полной стены (запрос
        /// пополнения в <see cref="GameMode.StartGameplayWhenReady"/>). Команды, общий счёт
        /// серии и статистика игроков живут вне режима и не трогаются.
        /// </para>
        /// </summary>
        /// <param name="stopCurrent">false — текущий режим завершился сам (объявил победителя)
        /// и уже не идёт: <c>StopGameplay</c> ему не нужен.</param>
        [Server]
        private bool ServerSwitchTo(GameModeData data, bool stopCurrent)
        {
            if (data == null) return false;

            if (data.modePrefab == null && ModeFactory == null)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{data.modeId}' не содержит modePrefab");
                return false;
            }

            if (data.teams == null || data.teams.Length < 1)
            {
                GameLog.Error($"[GameplayManager] GameModeData '{data.modeId}' не содержит команд");
                return false;
            }

            string previous = _gameMode != null && _gameMode.ModeData != null ? _gameMode.ModeData.modeId : "нет";

            if (stopCurrent && _gameMode != null)
                _gameMode.StopGameplay();

            CleanupGameMode();

            GameObject instance = ModeFactory != null ? ModeFactory(data) : Instantiate(data.modePrefab, transform);
            GameMode mode = instance != null ? instance.GetComponent<GameMode>() : null;
            if (mode == null)
            {
                GameLog.Error($"[GameplayManager] Префаб '{data.modeId}' не содержит компонент GameMode");
                DestroyObject(instance);
                return false;
            }

            // Данные — до спавна: modeId и команды уезжают клиенту начальным состоянием,
            // и HUD хоста в OnStartClient уже знает свой режим.
            mode.Initialize(data);

            _gameModeInstance = instance;
            NetworkServer.Spawn(instance);

            _gameMode = null;
            RegisterActiveGameMode(mode);
            mode.GameplayEnded += OnGameplayEnded;

            GameLog.Match.Info(
                $"[GameplayManager] Режим на карте '{SceneName}': {previous} → {data.modeId} ({data.displayName}).");

            mode.StartGameplayWhenReady();
            return true;
        }

        /// <summary>
        /// Режим матча объявил победителя. Серия получает итог, карта возвращается в разминку.
        /// Режим к этому моменту уже не идёт — останавливать его не нужно.
        /// </summary>
        [Server]
        private void OnGameplayEnded(TeamData winner)
        {
            _currentState = GameplayState.NotActive;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info($"[GameplayManager] Матч завершён, победитель: {winnerName}. Карта — в разминку.");

            GameplayEnded?.Invoke(winner);
            RpcOnGameplayEnded(winnerName);

            GameModeData warmup = MapModeRules.ResolveWarmup(CurrentMap, Registry);
            if (warmup != null) ServerSwitchTo(warmup, stopCurrent: false);
            else CleanupGameMode();
        }

        [Server]
        private void CleanupGameMode()
        {
            if (_gameMode != null)
            {
                _gameMode.GameplayEnded -= OnGameplayEnded;
                UnregisterActiveGameMode(_gameMode);
                _gameMode = null;
            }

            if (_gameModeInstance != null)
            {
                NetworkServer.UnSpawn(_gameModeInstance);
                DestroyObject(_gameModeInstance);
                _gameModeInstance = null;
            }
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
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
    }
}

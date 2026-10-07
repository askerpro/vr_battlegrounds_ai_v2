using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Player;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Managers
{
    public enum MapState
    {
        /// <summary>Матча нет — на карте разминка (или режима нет вовсе).</summary>
        Warmup,
        /// <summary>Карта live: идёт режим матча, выбранный администратором.</summary>
        Live,
        /// <summary>Матч на паузе: на карте разминка, снимок матча ждёт «Продолжить».</summary>
        Paused
    }

    /// <summary>
    /// Жизнь режима на карте: какой режим сейчас заспавнен и как он меняется.
    ///
    /// <para>
    /// <b>Судья принадлежит запуску карты.</b> Его спавнит <see cref="MapBootstrap"/> из каталога и до спавна
    /// передаёт свой запуск (<see cref="InitializeRun"/>); разминку судья начинает по <see cref="ServerStartRun"/>
    /// после CompositionReady. Судьи без запуска (сценового, «неуправляемого») больше нет: режим и состояние
    /// карты публикует только descriptor <see cref="MapRunAuthority"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Режим на карте меняется на месте, без перезагрузки сцены.</b> «Начать матч» (<see cref="GoLive"/>)
    /// останавливает разминку и спавнит режим матча, согласованный при загрузке (<see cref="MapRunConfig.MatchIntent"/>);
    /// матч кончился (<see cref="Finished"/>) — снова разминка, а серия (<see cref="Series"/>) решает, какая
    /// карта следующая.
    /// </para>
    ///
    /// <para>
    /// Вся логика матча (раунды, таймеры, счёт на карте) живёт в конкретном
    /// <see cref="GameMode"/>; правила (оружие, арсенал) менеджер читает через виртуальные
    /// свойства базового класса. Команды — не здесь, а в <see cref="TeamChangeRequests"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.MapReferee)]
    public class MapReferee : NetworkBehaviour
    {
        public static MapReferee Instance { get; private set; }

        /// <summary>Матч на карте завершён (сервер). Null = ничья. Слушает серия карт.</summary>
        public event Action<TeamData> Finished;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        /// <summary>Сервер: запуск карты, которому принадлежит этот судья. На клиенте null.</summary>
        private IMapRunHost _run;

        /// <summary>
        /// Состояние карты — из единого descriptor запуска (сервер и клиент читают одно). Пока descriptor
        /// этого судьи не опубликован — разминка.
        /// </summary>
        public MapState CurrentState => TryGetRunSnapshot(out MapRunSnapshot snapshot) ? snapshot.MapState : MapState.Warmup;

        /// <summary>Descriptor запуска, который опубликовал именно этот экземпляр судьи.</summary>
        private bool TryGetRunSnapshot(out MapRunSnapshot snapshot)
        {
            snapshot = default;
            MapRunAuthority authority = MapRunAuthority.Instance;
            if (authority == null || netIdentity == null || netId == 0) return false;
            snapshot = authority.Current;
            return snapshot.RefereeNetId == netId && snapshot.Config != null;
        }

        /// <summary>
        /// Сервер, до спавна: судья принадлежит запуску карты. Разминку он начнёт не в
        /// <c>OnStartServer</c>, а по <see cref="ServerStartRun"/> — после CompositionReady.
        /// </summary>
        internal void InitializeRun(IMapRunHost run) => _run = run;

        /// <summary>Идёт матч (режим матча, а не разминка).</summary>
        public bool IsLiveOrPaused => CurrentState != MapState.Warmup;

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

        /// <summary>Сцена, на которой живёт менеджер (или подменённая тестом).</summary>
        public string SceneName => !string.IsNullOrEmpty(SceneNameOverride) ? SceneNameOverride : gameObject.scene.name;

        private static GameModeRegistry Registry =>
            SessionManager.Instance != null ? SessionManager.Instance.ModeRegistry : null;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        /// <summary>
        /// Разминку судья здесь не начинает: состав карты ещё не опубликован. Её начнёт
        /// <see cref="ServerStartRun"/> от своего запуска. Судья без запуска — ошибка состава сцены.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnedOnServer = true;

            if (_run == null)
                GameLog.Error($"[MapReferee] Судья карты '{SceneName}' заспавнен без запуска карты: режимов не будет. " +
                    "Судью создаёт только MapBootstrap (MapRoot сцены).", this);
        }

        /// <summary>
        /// Сервер: состав карты опубликован (CompositionReady) — разминка. Её коммит открывает server Ready.
        /// Отложенный «Начать матч» выполняется следом.
        /// </summary>
        [Server]
        internal bool ServerStartRun()
        {
            if (_run == null || _gameMode != null) return false;
            if (!ServerStartWarmup()) return false;
            ProcessDeferredGoLive();
            return true;
        }

        private void ProcessDeferredGoLive()
        {
            if (!_goLiveRequestedBeforeSpawn) return;
            _goLiveRequestedBeforeSpawn = false;
            GoLive();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Хост пользуется серверной ссылкой; удалённый клиент берёт режим из descriptor.
            if (!isServer && MapRunAuthority.Instance != null)
                MapRunAuthority.Instance.Subscribe(HandleRunSnapshotClient);
        }

        public override void OnStopClient()
        {
            if (MapRunAuthority.Instance != null)
                MapRunAuthority.Instance.Unsubscribe(HandleRunSnapshotClient);
            base.OnStopClient();
        }

        /// <summary>
        /// Клиент: descriptor назвал текущий режим. Если объект режима уже пришёл — он становится
        /// активным; иначе его зарегистрирует <c>GameMode.OnStartClient</c>. Порядок прихода любой.
        /// </summary>
        private void HandleRunSnapshotClient(MapRunSnapshot snapshot)
        {
            if (snapshot.RefereeNetId != netId || snapshot.ActiveModeNetId == 0) return;
            if (NetworkClient.spawned.TryGetValue(snapshot.ActiveModeNetId, out NetworkIdentity identity) &&
                identity != null && identity.TryGetComponent(out GameMode mode))
                RegisterActiveGameMode(mode);
        }

        /// <summary>
        /// «Начать матч» пришёл раньше server Ready карты (автостарт отладки слушает
        /// <see cref="SubscribeToInstance"/>, а тот срабатывает в <c>Awake</c>). Запрос выполняется
        /// сразу после разминки — в <see cref="ServerStartRun"/>.
        /// </summary>
        private bool _goLiveRequestedBeforeSpawn;

        /// <summary>
        /// <c>OnStartServer</c> уже прошёл. Своя отметка, а не <c>isServer</c>: в <c>Awake</c>
        /// у <c>NetworkBehaviour</c> ещё нет <c>netIdentity</c>, и <c>isServer</c> падает с NRE.
        /// </summary>
        private bool _spawnedOnServer;

        /// <summary>
        /// Режим заспавнен на этой машине. На сервере ссылку ставит <see cref="ServerSwitchTo"/>,
        /// клиенту — <c>GameMode.OnStartClient</c>; повтор того же объекта ничего не делает.
        /// </summary>
        internal void RegisterActiveGameMode(GameMode mode)
        {
            if (mode == null || _gameMode == mode) return;

            // Удалённый клиент управляемой карты: активен только режим из descriptor. Поздний
            // OnStartClient прежнего режима или кандидата не перебивает текущую ссылку.
            if (!NetworkServer.active && TryGetRunSnapshot(out MapRunSnapshot snapshot) &&
                mode.netId != snapshot.ActiveModeNetId)
                return;

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
        private static event Action<MapReferee> InstanceReady;

        /// <summary>
        /// Подписка на появление оркестратора. Если он уже есть — обработчик вызывается
        /// немедленно, ещё до возврата из метода.
        /// </summary>
        public static void SubscribeToInstance(Action<MapReferee> handler)
        {
            if (handler == null) return;

            InstanceReady += handler;

            if (Instance != null) handler(Instance);
        }

        /// <summary>Отписка. Обязательна: событие статическое и переживает сцену.</summary>
        public static void UnsubscribeFromInstance(Action<MapReferee> handler)
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
        /// Разминка (<see cref="GameModeRegistry.Warmup"/>) — состояние карты без запущенного
        /// режима матча: на старте карты, на паузе, после конца матча. Идущий режим
        /// останавливается. Команды, общий счёт серии и статистика не трогаются.
        /// </summary>
        /// <returns>false — разминки нет в реестре.</returns>
        [Server]
        public bool ServerStartWarmup() => ServerStartWarmup(MapState.Warmup);

        /// <param name="state">Warmup — обычная разминка, Paused — разминка паузы матча.</param>
        [Server]
        private bool ServerStartWarmup(MapState state)
        {
            GameModeData warmup = Registry != null ? Registry.Warmup : null;
            if (warmup == null)
            {
                GameLog.Error($"[MapReferee] На карте '{SceneName}' нет разминки: не задано поле warmup в GameModeRegistry.");
                return false;
            }

            return ServerSwitchTo(warmup, stopCurrent: true, state);
        }

        /// <summary>
        /// «Начать матч»: разминка → режим матча, согласованный один раз при загрузке карты
        /// (<see cref="MapRunConfig.MatchIntent"/>), на месте. Текущий выбор меню не читается: выбор во
        /// время карты относится к следующей серии. Режима матча у запуска нет (лобби) — матч не начинается.
        /// </summary>
        /// <returns>true — режим матча заспавнен.</returns>
        [Server]
        public bool GoLive()
        {
            if (IsLiveOrPaused)
            {
                GameLog.Match.Warning("[MapReferee] Матч уже идёт или на паузе.");
                return false;
            }

            if (_run == null && _spawnedOnServer)
            {
                GameLog.Error($"[MapReferee] «Начать матч» на карте '{SceneName}': у судьи нет запуска карты.", this);
                return false;
            }

            // Судья ещё не связан с запуском (сигнал из Awake приходит внутри Instantiate, до InitializeRun)
            // или карта не открыла server Ready — запрос выполнится после разминки.
            if (_run == null || !_run.IsServerReady)
            {
                _goLiveRequestedBeforeSpawn = true;
                GameLog.Match.Verbose("[MapReferee] «Начать матч» до готовности карты — после разминки.");
                return false;
            }

            MapMatchIntent intent = _run.MatchIntent;
            if (!intent.HasMatch)
            {
                GameLog.Match.Warning($"[MapReferee] Матч не начат: запуск карты '{SceneName}' без режима матча ({intent.ResolutionReason}).");
                return false;
            }

            GameModeData mode = FindRegisteredMode(intent.ModeId);
            if (mode == null)
            {
                GameLog.Error($"[MapReferee] Режим запуска '{intent.ModeId}' отсутствует в GameModeRegistry.");
                return false;
            }

            return ServerSwitchTo(mode, stopCurrent: true, MapState.Live);
        }

        private static GameModeData FindRegisteredMode(string modeId)
        {
            GameModeRegistry registry = Registry;
            if (registry == null || registry.modes == null) return null;
            foreach (GameModeData mode in registry.modes)
                if (mode != null && mode.modeId == modeId) return mode;
            return null;
        }

        /// <summary>
        /// Принудительно останавливает матч без победителя — карта возвращается в разминку.
        /// Серию это не двигает. Снимок паузы, если был, отбрасывается.
        /// </summary>
        [Server]
        public void Stop()
        {
            if (!IsLiveOrPaused)
            {
                GameLog.Match.Warning("[MapReferee] Stop: матч не идёт.");
                return;
            }

            GameLog.Match.Info("[MapReferee] Матч остановлен администратором — разминка.");
            _pausedSnapshot = null;
            _pausedMode = null;
            ServerStartWarmup();
            RpcOnStopped();
        }

        // ── Пауза ─────────────────────────────────────────────────────────────
        //
        // «Пауза» прерывает раунд без победителя и возвращает карту в разминку («лобби
        // текущей карты»); «Продолжить» спавнит режим матча заново и возвращает ему снимок
        // (PauseSnapshot): счёт карты, номер прерванного раунда. Экземпляр режима
        // на паузе не живёт — почему, см. PauseSnapshot. Снимок — состояние матча на этой
        // карте, поэтому хранится здесь и уходит вместе со сценой.

        private PauseSnapshot _pausedSnapshot;
        private GameModeData _pausedMode;

        /// <summary>Матч на паузе: на карте разминка, «Продолжить» вернёт матч.</summary>
        public bool IsPaused => CurrentState == MapState.Paused;

        /// <summary>Идёт ли сейчас сам матч (не пауза и не разминка) — для кнопки «Пауза».</summary>
        public bool IsLive => CurrentState == MapState.Live;

        /// <summary>
        /// «Пауза»: снимок матча, идущий раунд прерывается без победителя (не засчитывается),
        /// карта — в разминку, снаряжение забирается.
        /// </summary>
        /// <returns>false — матч не идёт или режим паузу не умеет.</returns>
        [Server]
        public bool Pause()
        {
            if (CurrentState != MapState.Live || _gameMode == null || !_gameMode.SupportsPause)
            {
                GameLog.Match.Warning("[MapReferee] Пауза: матч не идёт или режим не умеет паузу.");
                return false;
            }

            _pausedSnapshot = _gameMode.CaptureSnapshot();
            _pausedMode = _gameMode.ModeData;

            GameLog.Match.Info(
                $"[MapReferee] Пауза: режим '{_pausedSnapshot.ModeId}', раунд {_pausedSnapshot.RoundToReplay} " +
                "прерван без победителя, карта — в разминку.");

            if (!ServerStartWarmup(MapState.Paused))
            {
                _pausedSnapshot = null;
                _pausedMode = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// «Продолжить»: режим матча заново, со снимка — тот же номер раунда и счёт.
        /// Снаряжение разминки забирается.
        /// </summary>
        [Server]
        public bool Resume()
        {
            if (!IsPaused || _pausedMode == null)
            {
                GameLog.Match.Warning("[MapReferee] «Продолжить»: матч не на паузе.");
                return false;
            }

            PauseSnapshot snapshot = _pausedSnapshot;
            GameModeData mode = _pausedMode;

            if (!ServerSwitchTo(mode, stopCurrent: true, MapState.Live, restore: snapshot)) return false;

            _pausedSnapshot = null;
            _pausedMode = null;

            GameLog.Match.Info($"[MapReferee] Матч продолжен: '{mode.modeId}', раунд {snapshot?.RoundToReplay}.");
            return true;
        }

        /// <summary>
        /// Смена режима на месте: текущий останавливается и уходит из сети, новый спавнится.
        ///
        /// <para>
        /// Что принадлежит режиму, уходит вместе с ним: его правила-компоненты (уборка пола,
        /// карман, раундовые магазины), счёт на карте, машина раундов. Новый режим стартует
        /// с чистого пола (<c>ModeStartCleanup</c> на префабе) и полной стены (запрос
        /// пополнения в <see cref="GameMode.BeginWhenReady"/>). Команды, общий счёт
        /// серии и статистика игроков живут вне режима и не трогаются.
        /// </para>
        /// </summary>
        /// <param name="stopCurrent">false — текущий режим завершился сам (объявил победителя)
        /// и уже не идёт: <c>ForceStop</c> ему не нужен.</param>
        /// <param name="state">Состояние карты, которое публикуется вместе с новым режимом.</param>
        /// <param name="restore">Снимок паузы — вернуть режиму после инициализации («Продолжить»).</param>
        [Server]
        private bool ServerSwitchTo(GameModeData data, bool stopCurrent, MapState state, PauseSnapshot restore = null)
        {
            if (data == null || _run == null) return false;

            if (data.modePrefab == null && ModeFactory == null)
            {
                GameLog.Error($"[MapReferee] GameModeData '{data.modeId}' не содержит modePrefab");
                return false;
            }

            // У разминки команд нет — это не режим матча. Режиму матча без команд играть некем.
            bool isWarmup = Registry != null && data == Registry.Warmup;
            if (!isWarmup && (data.teams == null || data.teams.Length < 1))
            {
                GameLog.Error($"[MapReferee] GameModeData '{data.modeId}' не содержит команд");
                return false;
            }

            string previous = _gameMode != null && _gameMode.ModeData != null ? _gameMode.ModeData.modeId : "нет";

            if (stopCurrent && _gameMode != null)
                _gameMode.ForceStop();

            // Снаряжение не переживает смену режима: ни разминочное — матча, ни матчевое —
            // разминки. Первый режим карты (старт сцены) снимать нечего.
            if (_gameMode != null)
                EquipmentStrip.ServerStripAll($"смена режима {previous} → {data.modeId}");

            CleanupGameMode();

            GameObject instance = ModeFactory != null ? ModeFactory(data) : InstantiateMode(data.modePrefab);
            GameMode mode = instance != null ? instance.GetComponent<GameMode>() : null;
            if (mode == null)
            {
                GameLog.Error($"[MapReferee] Префаб '{data.modeId}' не содержит компонент GameMode");
                DestroyObject(instance);
                return false;
            }

            // Данные — до спавна: modeId и команды уезжают клиенту начальным состоянием,
            // и HUD хоста в OnStartClient уже знает свой режим.
            mode.Initialize(data);
            if (restore != null) mode.RestoreSnapshot(restore);

            _gameModeInstance = instance;
            NetworkServer.Spawn(instance);

            _gameMode = null;
            RegisterActiveGameMode(mode);
            mode.Finished += OnModeFinished;
            mode.RoundWonServer += OnModeRoundWon;

            // Единственная публикация — descriptor запуска. Коммит до BeginWhenReady: команды,
            // пополнение стен и старт режима идут уже под опубликованным режимом (первый коммит
            // открывает server Ready).
            if (_run == null || !_run.CommitMode(state, mode))
            {
                GameLog.Error($"[MapReferee] Режим '{data.modeId}' не опубликован: запуск карты '{SceneName}' уже снят.");
                return false;
            }

            GameLog.Match.Info(
                $"[MapReferee] Режим на карте '{SceneName}': {previous} → {data.modeId} ({data.displayName}).");

            mode.BeginWhenReady();
            return true;
        }

        /// <summary>
        /// Экземпляр режима — отдельный сетевой корень в сцене карты, без вложения под NetworkIdentity судьи.
        /// </summary>
        private GameObject InstantiateMode(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
            return instance;
        }

        /// <summary>
        /// Режим матча объявил победителя. Серия получает итог, карта возвращается в разминку.
        /// Режим к этому моменту уже не идёт — останавливать его не нужно.
        /// </summary>
        [Server]
        private void OnModeFinished(TeamData winner)
        {
            _pausedSnapshot = null;
            _pausedMode = null;

            string winnerName = winner != null ? winner.Name : "ничья";
            GameLog.Match.Info($"[MapReferee] Матч завершён, победитель: {winnerName}. Карта — в разминку.");

            Finished?.Invoke(winner);
            RpcOnMapFinished(winnerName);

            GameModeData warmup = Registry != null ? Registry.Warmup : null;
            if (warmup != null) ServerSwitchTo(warmup, stopCurrent: false, MapState.Warmup);
            else CleanupGameMode();
        }

        [Server]
        private void CleanupGameMode()
        {
            if (_gameMode != null)
            {
                _gameMode.Finished -= OnModeFinished;
                _gameMode.RoundWonServer -= OnModeRoundWon;
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
        private void RpcOnMapFinished(string winnerName)
        {
            GameLog.Match.Info(
                $"[MapReferee] Матч завершён (клиент), победитель: {winnerName}");
        }

        [ClientRpc]
        private void RpcOnStopped()
        {
            GameLog.Match.Info("[MapReferee] Матч остановлен (клиент)");
        }

        /// <summary>Сервер: раунд на карте доигран и выигран. Слушает серия (общий счёт).</summary>
        public event Action<TeamData> RoundWon;

        /// <summary>
        /// Сервер: игрок погиб (жертва, убийца или null, ассистенты). Слушает серия (статистика).
        /// </summary>
        public event Action<PlayerSession, PlayerSession, IReadOnlyList<PlayerSession>> PlayerKilled;

        private void OnModeRoundWon(TeamData winner) => RoundWon?.Invoke(winner);

        /// <summary>
        /// Вызывается PlayerController при гибели игрока; убийцу и ассистентов он определил
        /// по урону (<c>DamageLedger</c>). Режим решает, что значит гибель, серия пишет статистику.
        /// </summary>
        [Server]
        public void OnPlayerDied(PlayerController player, PlayerSession killer, IReadOnlyList<PlayerSession> assists)
        {
            if (_gameMode != null)
            {
                _gameMode.OnPlayerDied(player);
                _gameMode.OnPlayerKilled(player, killer);
            }

            PlayerSession victim = player != null ? player.Session : null;
            PlayerKilled?.Invoke(victim, killer, assists);

            // Клиентам — для HUD: убийцу знает только сервер (DamageLedger).
            if (victim != null)
            {
                RpcPlayerKilled(victim.netId, victim.PlayerName, victim.TeamIndex,
                                killer != null ? killer.netId : 0u,
                                killer != null ? killer.PlayerName : string.Empty,
                                killer != null ? killer.TeamIndex : 0);
            }
        }

        /// <summary>
        /// Клиент: игрок погиб. Для HUD (лента убийств, «вы погибли», звук). Сессии — по netId,
        /// имена и команды приходят сразу: сессия убитого могла уже уйти (бот убран, игрок отключился).
        /// </summary>
        public static event Action<KillNotice> PlayerKilledLocal;

        [ClientRpc]
        private void RpcPlayerKilled(uint victimNetId, string victimName, int victimTeam,
                                     uint killerNetId, string killerName, int killerTeam)
        {
            PlayerKilledLocal?.Invoke(new KillNotice(victimNetId, victimName, victimTeam, killerNetId, killerName, killerTeam));
        }
    }

    /// <summary>Кто кого убил — то, что сервер рассказывает клиентам. Убийца 0 — урон без источника.</summary>
    public readonly struct KillNotice
    {
        public readonly uint VictimNetId;
        public readonly string VictimName;
        public readonly int VictimTeam;
        public readonly uint KillerNetId;
        public readonly string KillerName;
        public readonly int KillerTeam;

        public KillNotice(uint victimNetId, string victimName, int victimTeam,
                          uint killerNetId, string killerName, int killerTeam)
        {
            VictimNetId = victimNetId;
            VictimName = victimName;
            VictimTeam = victimTeam;
            KillerNetId = killerNetId;
            KillerName = killerName;
            KillerTeam = killerTeam;
        }

        public bool HasKiller => KillerNetId != 0 && KillerNetId != VictimNetId;
    }
}

using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
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
    /// <b>Режим на карте меняется на месте, без перезагрузки сцены.</b> Любая карта
    /// (лобби тоже) стартует в разминке (<see cref="ServerStartWarmup"/>, из
    /// <c>OnStartServer</c>). «Начать матч» (<see cref="GoLive"/>) останавливает
    /// разминку и спавнит режим матча; матч кончился (<see cref="Finished"/>) —
    /// снова разминка, а серия (<see cref="Series"/>) решает, какая карта следующая.
    /// Какой режим допустим на карте — <see cref="MapModeRules"/> по <c>MapData.supportedModes</c>.
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

        [SyncVar] private MapState _currentState = MapState.Warmup;

        private GameMode _gameMode;
        private GameObject _gameModeInstance;

        public MapState CurrentState => _currentState;

        /// <summary>Идёт матч (режим матча, а не разминка).</summary>
        public bool IsLiveOrPaused => _currentState != MapState.Warmup;

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

        /// <summary>Карта (любая, и лобби) стартует в разминке — без администратора.</summary>
        public override void OnStartServer()
        {
            base.OnStartServer();
            _startedOnServer = true;

            if (_gameMode == null)
                ServerStartWarmup();

            if (_goLiveRequestedBeforeSpawn)
            {
                _goLiveRequestedBeforeSpawn = false;
                GoLive();
            }
        }

        /// <summary>
        /// «Начать матч» пришёл раньше, чем объект заспавнен (автостарт отладки слушает
        /// <see cref="SubscribeToInstance"/>, а тот срабатывает в <c>Awake</c>). Спавнить режим
        /// дочерним объектом незаспавненного менеджера нельзя — запрос ждёт <c>OnStartServer</c>.
        /// </summary>
        private bool _goLiveRequestedBeforeSpawn;

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
        public bool ServerStartWarmup()
        {
            GameModeData warmup = Registry != null ? Registry.Warmup : null;
            if (warmup == null)
            {
                GameLog.Error($"[MapReferee] На карте '{SceneName}' нет разминки: не задано поле warmup в GameModeRegistry.");
                return false;
            }

            _currentState = MapState.Warmup;
            return ServerSwitchTo(warmup, stopCurrent: true);
        }

        /// <summary>
        /// «Начать матч»: разминка → режим, выбранный администратором
        /// (<c>SessionManager.SelectedGameModeData</c>), на месте. Выбор несовместим с картой —
        /// первый совместимый режим матча; совместимых нет (лобби) — матч не начинается.
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

            if (!_startedOnServer)
            {
                _goLiveRequestedBeforeSpawn = true;
                GameLog.Match.Verbose("[MapReferee] «Начать матч» до спавна карты — после разминки.");
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
                    $"[MapReferee] Матч не начат: на карте '{SceneName}' нет совместимого режима матча " +
                    $"(выбран '{(selected != null ? selected.modeId : "ничего")}').");
                return false;
            }

            if (selected != null && mode != selected)
            {
                GameLog.Match.Info(
                    $"[MapReferee] Режим '{selected.modeId}' несовместим с картой '{SceneName}' — " +
                    $"берётся первый совместимый: '{mode.modeId}'.");
            }

            if (!ServerSwitchTo(mode, stopCurrent: true)) return false;

            _currentState = MapState.Live;
            return true;
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
        public bool IsPaused => _currentState == MapState.Paused;

        /// <summary>Идёт ли сейчас сам матч (не пауза и не разминка) — для кнопки «Пауза».</summary>
        public bool IsLive => _currentState == MapState.Live;

        /// <summary>
        /// «Пауза»: снимок матча, идущий раунд прерывается без победителя (не засчитывается),
        /// карта — в разминку, снаряжение забирается.
        /// </summary>
        /// <returns>false — матч не идёт или режим паузу не умеет.</returns>
        [Server]
        public bool Pause()
        {
            if (_currentState != MapState.Live || _gameMode == null || !_gameMode.SupportsPause)
            {
                GameLog.Match.Warning("[MapReferee] Пауза: матч не идёт или режим не умеет паузу.");
                return false;
            }

            _pausedSnapshot = _gameMode.CaptureSnapshot();
            _pausedMode = _gameMode.ModeData;

            GameLog.Match.Info(
                $"[MapReferee] Пауза: режим '{_pausedSnapshot.ModeId}', раунд {_pausedSnapshot.RoundToReplay} " +
                "прерван без победителя, карта — в разминку.");

            if (!ServerStartWarmup())
            {
                _pausedSnapshot = null;
                _pausedMode = null;
                return false;
            }

            _currentState = MapState.Paused;
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

            if (!ServerSwitchTo(mode, stopCurrent: true, restore: snapshot)) return false;

            _pausedSnapshot = null;
            _pausedMode = null;
            _currentState = MapState.Live;

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
        /// <param name="restore">Снимок паузы — вернуть режиму после инициализации («Продолжить»).</param>
        [Server]
        private bool ServerSwitchTo(GameModeData data, bool stopCurrent, PauseSnapshot restore = null)
        {
            if (data == null) return false;

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

            GameObject instance = ModeFactory != null ? ModeFactory(data) : Instantiate(data.modePrefab, transform);
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

            GameLog.Match.Info(
                $"[MapReferee] Режим на карте '{SceneName}': {previous} → {data.modeId} ({data.displayName}).");

            mode.BeginWhenReady();
            return true;
        }

        /// <summary>
        /// Режим матча объявил победителя. Серия получает итог, карта возвращается в разминку.
        /// Режим к этому моменту уже не идёт — останавливать его не нужно.
        /// </summary>
        [Server]
        private void OnModeFinished(TeamData winner)
        {
            _currentState = MapState.Warmup;
            _pausedSnapshot = null;
            _pausedMode = null;

            string winnerName = winner != null ? winner.Name : "ничья";
            GameLog.Match.Info($"[MapReferee] Матч завершён, победитель: {winnerName}. Карта — в разминку.");

            Finished?.Invoke(winner);
            RpcOnMapFinished(winnerName);

            GameModeData warmup = Registry != null ? Registry.Warmup : null;
            if (warmup != null) ServerSwitchTo(warmup, stopCurrent: false);
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

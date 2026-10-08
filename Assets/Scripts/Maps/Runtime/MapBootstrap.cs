using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>
    /// Стадия запуска на сервере. <see cref="ComposingStations"/> — запуск начат, станции собраны и включены,
    /// ждём регистрации ID сгенерированных станций; служебные объекты ещё не созданы.
    /// </summary>
    public enum MapBootstrapStage : byte { WaitingPrerequisites, CompositionReady, Ready, Failed, ComposingStations }

    /// <summary>
    /// Сборка запуска одной карты. Сервер проверяет <see cref="MapRoot"/>, разрешает
    /// <see cref="MapRunConfig"/> по центральному <see cref="MapRuntimeCatalog"/>, собирает станции (авторские —
    /// ассортимент паспорта, сгенерированные — через <see cref="MapArsenalCompositionAdapter"/>), спавнит служебные
    /// MapReferee и координатор развёртывания из зарегистрированных префабов, публикует CompositionReady
    /// и запускает разминку; server Ready открывает только первый закоммиченный режим.
    /// Клиент сетевых объектов не создаёт: собирает сгенерированные станции своей сцены тем же адаптером с ключом
    /// принятого запуска и связывает уже заспавненный координатор со станциями сцены.
    ///
    /// <para>
    /// Все пути старта (смена карты, первый onlineScene, сцена без смены) сходятся в одном
    /// опросе предусловий, а не в единственном сетевом callback. Отказ — именованный, до выдачи
    /// оружия, спавна аватаров и начала режима. Teardown — через <see cref="MapRunScope"/> запуска.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MapRoot))]
    [DefaultExecutionOrder(-1680)]
    public sealed class MapBootstrap : MonoBehaviour, IMapRunHost
    {
        private const float StuckWarningSeconds = 10f;
        private static readonly List<MapBootstrap> Loaded = new List<MapBootstrap>();

        /// <summary>
        /// Наибольший ключ, уже принятый любым MapBootstrap этого клиента. Descriptor старого запуска той же
        /// сцены (повторный LoadMap, «Lobby → карта → Lobby») приходит в любом порядке относительно смены сцены
        /// и не должен стать запуском нового экземпляра. Новая сессия сервера — новая эпоха, счёт заново;
        /// новое соединение (переподключение к той же сессии) — тоже: идущий запуск надо принять снова.
        /// </summary>
        private static MapRunKey _highestAcceptedKey;

        /// <summary>Соединение, на котором принят <see cref="_highestAcceptedKey"/>.</summary>
        private static NetworkConnection _acceptedOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProcessState()
        {
            Loaded.Clear();
            _highestAcceptedKey = default;
            _acceptedOn = null;
            LocalReadinessChanged = null;
        }

        /// <summary>
        /// Локальная готовность запуска какой-либо загруженной карты изменилась (клиент принял run, запуск
        /// закрылся). Подписчик — барьер начального снимка <c>NetworkStateRelay</c>.
        /// </summary>
        public static event Action LocalReadinessChanged;

        /// <summary>Ключ запуска, принятого этой сценой на клиенте; на сервере — ключ текущего scope.</summary>
        public MapRunKey LocalRunKey => NetworkServer.active ? (_scope != null ? _scope.Key : default) : _localRunKey;

        private MapRunKey _localRunKey;
        private bool _localRunClosed;
        private bool _localRunFailed;

        /// <summary>Клиент: scope принятого запуска — владелец собранных здесь сгенерированных станций.</summary>
        private MapRunScope _localScope;
        private MapArsenalCompositionAdapter _localArsenal;

        /// <summary>Сборщик станций. Игра — <see cref="ArsenalStationComposer"/>; EditMode-тесты подставляют свой.</summary>
        internal IArsenalStationComposer Composer { get; set; } = RuntimeArsenalStationComposer.Instance;

        [Tooltip("Длительность выдвижения оборудования станций; передаётся координатору до спавна.")]
        [SerializeField, Min(0.1f)] private float _deploymentDuration = 1f;

        private MapRoot _root;
        private MapRunAuthority _authority;
        private MapRunScope _scope;
        private MapRunConfig _config;
        private MapReferee _referee;
        private MapRootBindings _bindings;
        private MapRuntimeCatalog _catalog;
        private MapArsenalCompositionAdapter _arsenal;
        private uint _boundCoordinatorNetId;
        private MapRunKey _checkedClientKey;
        private float _waitingSince = -1f;
        private bool _stuckReported;
        private bool _closing;

        /// <summary>Bootstrap загруженной сцены или null — сцена без MapRoot (стенд, Offline).</summary>
        public static MapBootstrap ForScene(Scene scene)
        {
            foreach (MapBootstrap bootstrap in Loaded)
                if (bootstrap != null && bootstrap.gameObject.scene == scene) return bootstrap;
            return null;
        }

        public MapBootstrapStage Stage { get; private set; }

        /// <summary>Разрешённый config текущего запуска (сервер); null до BeginRun.</summary>
        public MapRunConfig Config => _config;

        /// <summary>Именованная причина отказа; пусто, пока отказа не было.</summary>
        public string FailureCode { get; private set; } = string.Empty;

        /// <summary>Сервер: запуск этой сцены закоммитил режим, gameplay карты открыт.</summary>
        public bool IsServerReady =>
            Stage == MapBootstrapStage.Ready && _scope != null && !_scope.IsDisposed &&
            _authority != null && _authority == MapRunAuthority.Instance &&
            _authority.Current.IsReady && _authority.Current.Key == _scope.Key && StationsPassed(_arsenal);

        /// <summary>Все сгенерированные станции запуска собраны и зарегистрированы (последний опрос); без них — да.</summary>
        private static bool StationsPassed(MapArsenalCompositionAdapter arsenal) =>
            arsenal == null || arsenal.Readiness == ArsenalCompositionReadiness.Passed;

        private void Awake()
        {
            _root = GetComponent<MapRoot>();
            if (ForScene(gameObject.scene) != null)
            {
                GameLog.Error($"[MapBootstrap] Второй MapBootstrap в сцене '{gameObject.scene.name}' — игнорируется.", this);
                enabled = false;
                return;
            }
            Loaded.Add(this);
            MapLoader.MapLoadStarted += HandleMapLoadStarted;
            MapLoader.MapLoadCancelled += HandleMapLoadCancelled;
        }

        private void OnDestroy()
        {
            MapLoader.MapLoadStarted -= HandleMapLoadStarted;
            MapLoader.MapLoadCancelled -= HandleMapLoadCancelled;
            Loaded.Remove(this);
            RetireServerRun("выгрузка сцены");
            DisposeLocalScope();
        }

        private void Update()
        {
            if (NetworkServer.active) UpdateServer();
            else if (NetworkClient.active)
            {
                UpdateClientRun();
                BindClientServices();
            }
        }

        /// <summary>
        /// Готов ли запуск <paramref name="key"/> этой сцены локально — всё, что нужно до начального снимка
        /// SDK, на этой машине зарегистрировано. Сервер: scope этого ключа собран (CompositionReady/Ready) и не
        /// закрыт. Клиент: run принят (<see cref="LocalRunKey"/>), не закрыт, не отказал, станции готовы. Авторские
        /// станции — объекты сцены с авторскими UniqueId и готовы сразу; сгенерированные — когда все их handles дали
        /// <c>ValidateReady</c> Passed по фактическим регистрациям ролей (опрос в <c>Update</c>). Отдельного реестра
        /// участников нет: забытый участник открыл бы барьер раньше времени.
        /// </summary>
        public bool IsLocallyReady(MapRunKey key)
        {
            if (!key.IsValid) return false;
            if (NetworkServer.active)
                return _scope != null && !_scope.IsClosed && _scope.Key == key &&
                       (Stage == MapBootstrapStage.CompositionReady || Stage == MapBootstrapStage.Ready) &&
                       StationsPassed(_arsenal);
            return key == _localRunKey && !_localRunClosed && !_localRunFailed && _localArsenal != null &&
                   StationsPassed(_localArsenal);
        }

        // ── Сервер ───────────────────────────────────────────────────────────

        private void UpdateServer()
        {
            // Сервер перезапущен в той же сцене: прежний scope закрыл старый authority.
            if (Stage != MapBootstrapStage.WaitingPrerequisites && _authority != MapRunAuthority.Instance)
                ResetServerRun();

            if (Stage == MapBootstrapStage.WaitingPrerequisites && !_closing) TryBeginRun();
            else if (Stage == MapBootstrapStage.ComposingStations) ContinueComposition();
            else if (Stage == MapBootstrapStage.CompositionReady || Stage == MapBootstrapStage.Ready) WatchStations();
        }

        /// <summary>
        /// Принята загрузка следующей карты: запуск этой сцены закрывается сразу, а не на выгрузке.
        /// Descriptor получает Closing, scope отменяется — допуск, коммиты режима и выдача стены закрыты,
        /// владельцы по токену останавливают пополнение и RPC. Teardown остаётся на выгрузке сцены.
        /// Ещё не начатый запуск этой сцены уже не начнётся.
        /// </summary>
        private void HandleMapLoadStarted(string nextScene)
        {
            if (!NetworkServer.active || _closing) return;
            _closing = true;
            MapRunAdmission.DiscardAvatars("принята загрузка карты " + nextScene);
            if (_scope == null || _scope.IsDisposed || _authority == null) return;
            if (_authority.Close(_scope, _authority.Current.Revision))
                GameLog.Match.Verbose($"[MapBootstrap] Запуск '{_config?.MapScene}' ({_scope.Key}) закрыт: загрузка '{nextScene}'.", this);
        }

        /// <summary>
        /// Принятая загрузка отменена, а сцена этой карты осталась (Mirror не начал смену при живом сервере).
        /// Закрытый scope не открывается заново: запуск получает именованный отказ, gameplay карты остаётся
        /// закрыт до следующей загрузки. Остановленный сервер сам снимает запуск в <c>MapRunAuthority</c>.
        /// </summary>
        private void HandleMapLoadCancelled(string nextScene)
        {
            if (!NetworkServer.active || !_closing || _scope == null || _scope.IsDisposed) return;
            FailRun("Load.Cancelled", "загрузка '" + nextScene + "' отменена после закрытия запуска");
        }

        private void TryBeginRun()
        {
            MapRunAuthority authority = MapRunAuthority.Instance;
            string waitingFor = authority == null || !authority.isServer ? "MapRunAuthority на SessionContext"
                : !authority.CanBeginRun ? "открытая серверная сессия"
                : NetworkServer.isLoadingScene ? "завершение загрузки сцены Mirror"
                : null;
            if (waitingFor != null)
            {
                ReportStuck(waitingFor);
                return;
            }

            _authority = authority;
            MapRuntimeCatalog catalog = GameNetworkManager.MapCatalog;
            if (catalog == null)
            {
                FailBeforeRun("Catalog.NotInstalled", null);
                return;
            }

            // Сканы всей сцены — авторская проверка (preflight сборки): в рантайме UltimateXR и другие
            // системы уже добавили в сцену свои компоненты. Привязки и станции проверяются всегда.
            MapRootValidation validation = _root.ValidateBindings(includeSceneScans: false);
            if (!validation.Passed)
            {
                FailBeforeRun("MapRoot.Invalid", validation.Errors);
                return;
            }

            MapData map = validation.Bindings.Map;

            // Описания сгенерированных станций — до публикации config: их выбор оформления и layout hash входят в
            // config, клиент сверяет с ними своё описание. Отказ описания — отказ запуска, без отката на Authored.
            var arsenalErrors = new List<string>();
            MapArsenalCompositionAdapter arsenal = MapArsenalCompositionAdapter.Describe(validation.Bindings.Stations,
                map.arsenalPreset, catalog.ArsenalComposition, Composer, arsenalErrors);
            if (arsenalErrors.Count != 0)
            {
                FailBeforeRun("Arsenal.Generated.Description", arsenalErrors);
                return;
            }
            MapRootBindings bindings = validation.Bindings.WithDescription(arsenal.Apply(validation.Bindings.Description));

            var request = new MapRunRequest(authority.NextKey, map.sceneName, CapturedModeId(map),
                catalog.ContentFingerprintFor(map));
            MapRunResolution resolution = catalog.Resolve(request, bindings,
                NetworkManager.singleton != null ? NetworkManager.singleton.spawnPrefabs : null);
            if (!resolution.Passed)
            {
                FailBeforeRun("Map.Resolution", resolution.Errors);
                return;
            }

            if (!authority.BeginRun(resolution, out MapRunScope scope))
            {
                FailBeforeRun("Run.BeginRejected", null);
                return;
            }

            _scope = scope;
            _config = resolution.Config;
            _bindings = bindings;
            _catalog = catalog;
            _arsenal = arsenal;
            GameLog.Match.Info($"[MapBootstrap] Запуск '{_config.MapScene}' ({_config.Key}): режим матча " +
                $"'{(_config.MatchIntent.HasMatch ? _config.MatchIntent.ModeId : "нет")}' ({_config.MatchIntent.ResolutionReason}).", this);

            try
            {
                // Ассортимент авторских станций — из канонического паспорта карты, до любой выдачи. Повтор того же
                // preset безопасен. Сгенерированные станции готовит сборщик по описанию, не этот путь.
                foreach (ArsenalStationCompositionBinding station in _bindings.Stations)
                    if (station.Mode == ArsenalCompositionMode.Authored)
                        station.AuthoredBinding.Prepare(map.arsenalPreset);
            }
            catch (Exception error)
            {
                FailRun("Composition.Exception", error.Message);
                return;
            }

            if (!ComposeGeneratedStations(_arsenal, _scope, out string composeError))
            {
                FailRun("Arsenal.Generated.Compose", composeError);
                return;
            }

            Stage = MapBootstrapStage.ComposingStations;
            _waitingSince = -1f;
            _stuckReported = false;
            ContinueComposition();
        }

        /// <summary>
        /// Собрать и включить сгенерированные станции в scope запуска (сервер и клиент одинаково). Сбой сборщика
        /// назван им самим («ArsenalComposer.*», «NetworkUxrIdentity.Generated.*»); уже собранное разберёт scope.
        /// </summary>
        private bool ComposeGeneratedStations(MapArsenalCompositionAdapter arsenal, MapRunScope scope, out string error)
        {
            error = null;
            if (!arsenal.HasStations) return true;
            try
            {
                arsenal.Compose(scope);
            }
            catch (Exception failure)
            {
                error = failure.Message;
                return false;
            }

            // Машина без графики: выключенные компоненты с ID внутри включённых станций регистрируем сами.
            HeadlessPrecacheGuard.RegisterAfterComposition(gameObject.scene);
            arsenal.Poll();
            return true;
        }

        /// <summary>
        /// Сервер, станции собраны: ждать регистрации ID всех сгенерированных станций (опрос каждый кадр), затем
        /// создать служебные объекты. Pending — ждать, Failed — именованный отказ запуска. Closing — не отказ:
        /// запуск уже закрыт загрузкой следующей карты.
        /// </summary>
        private void ContinueComposition()
        {
            if (_closing || _scope == null || _scope.IsClosed) return;
            ArsenalCompositionReadiness readiness = _arsenal.Poll();
            if (readiness == ArsenalCompositionReadiness.Pending)
            {
                ReportStuck("регистрация сгенерированных станций (" + _arsenal.Reason + ")");
                return;
            }
            if (readiness == ArsenalCompositionReadiness.Failed)
            {
                FailRun("Arsenal.Generated.NotReady", _arsenal.Reason);
                return;
            }

            try
            {
                ComposeServices(_bindings, _catalog);
            }
            catch (Exception error)
            {
                FailRun("Composition.Exception", error.Message);
            }
        }

        /// <summary>
        /// Сервер, запуск собран: сгенерированная станция, потерявшая регистрацию (ID занял чужой, поддерево
        /// уничтожено), — именованный отказ, а не молчаливое закрытие допуска.
        /// </summary>
        private void WatchStations()
        {
            if (_arsenal == null || !_arsenal.HasStations || _closing || _scope == null || _scope.IsClosed) return;
            if (_arsenal.Poll() == ArsenalCompositionReadiness.Failed)
                FailRun("Arsenal.Generated.Lost", _arsenal.Reason);
        }

        /// <summary>
        /// Намерение режима. Идёт серия — режим, зафиксированный при её начале; карта без серии —
        /// выбор администратора в момент принятой загрузки. Дальнейшая смена выбора этот запуск не меняет.
        /// </summary>
        private static string CapturedModeId(MapData map)
        {
            if (map.kind == MapRunKind.Lobby) return string.Empty;
            Series series = Series.Instance;
            if (series != null && series.IsRunning) return series.CapturedModeId ?? string.Empty;
            SessionManager session = SessionManager.Instance;
            return session != null ? session.SelectedModeId ?? string.Empty : string.Empty;
        }

        private void ComposeServices(MapRootBindings bindings, MapRuntimeCatalog catalog)
        {
            Scene scene = gameObject.scene;
            GameObject refereeObject = SpawnService(catalog.RefereePrefab.gameObject, scene);
            _referee = refereeObject.GetComponent<MapReferee>();
            _referee.InitializeRun(this);
            NetworkServer.Spawn(refereeObject);

            GameObject coordinatorObject = SpawnService(catalog.CoordinatorPrefab.gameObject, scene);
            var coordinator = coordinatorObject.GetComponent<ArsenalBoundaryWall>();
            coordinator.Configure(DeploymentAnimators(bindings.Stations), _deploymentDuration);
            NetworkServer.Spawn(coordinatorObject);

            // Выделенный сервер без прогрева: выключенные компоненты с UniqueId служб, перенесённых в сцену
            // после её загрузки, регистрируем сами — до первого события синхронизации.
            HeadlessPrecacheGuard.RegisterAfterComposition(scene);

            uint refereeNetId = _referee.netId;
            uint coordinatorNetId = coordinator.netId;
            if (!_authority.CommitPrepared(_scope, _authority.Current.Revision, refereeNetId, coordinatorNetId))
            {
                FailRun("Run.CompositionCommitRejected", null);
                return;
            }

            Stage = MapBootstrapStage.CompositionReady;
            if (!_referee.ServerStartRun())
            {
                FailRun("Mode.WarmupFailed", null);
                return;
            }

            if (!IsServerReady)
            {
                FailRun("Run.ReadyNotCommitted", null);
                return;
            }

            GameLog.Match.Info($"[MapBootstrap] Карта '{_config.MapScene}' готова: разминка, допуск открыт.", this);
            MapRunAdmission.DrainAvatars();
        }

        MapMatchIntent IMapRunHost.MatchIntent => _config != null ? _config.MatchIntent : default;

        bool IMapRunHost.CommitMode(MapState state, GameMode mode) => CommitMode(state, mode);

        /// <summary>Сервер: режим заспавнен и стал текущим. Первый коммит открывает server Ready.</summary>
        internal bool CommitMode(MapState state, GameMode mode)
        {
            if (_scope == null || _scope.IsDisposed || _authority == null || mode == null || mode.ModeData == null)
                return false;
            if (!_authority.CommitMode(_scope, _authority.Current.Revision, state, mode.ModeData.modeId, mode.netId))
                return false;
            if (Stage == MapBootstrapStage.CompositionReady) Stage = MapBootstrapStage.Ready;
            return true;
        }

        private GameObject SpawnService(GameObject prefab, Scene scene)
        {
            GameObject instance = Instantiate(prefab);
            instance.name = prefab.name;
            SceneManager.MoveGameObjectToScene(instance, scene);
            _scope.Own(() => DespawnService(instance));
            return instance;
        }

        private static void DespawnService(GameObject instance)
        {
            if (instance == null) return;
            if (NetworkServer.active && instance.TryGetComponent(out NetworkIdentity identity) && identity.netId != 0)
                NetworkServer.Destroy(instance);
            else
                Destroy(instance);
        }

        private void FailBeforeRun(string code, IReadOnlyList<string> errors)
        {
            Stage = MapBootstrapStage.Failed;
            FailureCode = code;
            GameLog.Error($"[MapBootstrap] Карта '{gameObject.scene.name}' не запущена: {code}{Describe(errors)}. " +
                "Оружие, аватары и режим не выдаются.", this);
            MapRunAdmission.DiscardAvatars("отказ запуска карты");
        }

        private void FailRun(string code, string detail)
        {
            Stage = MapBootstrapStage.Failed;
            FailureCode = code;
            GameLog.Error($"[MapBootstrap] Запуск '{gameObject.scene.name}' ({_scope?.Key}) отказал: {code}" +
                (string.IsNullOrEmpty(detail) ? "" : " — " + detail) + ". Gameplay карты закрыт.", this);
            if (_authority != null && _scope != null && !_scope.IsDisposed)
                _authority.Fail(_scope, _authority.Current.Revision, code);
            MapRunAdmission.DiscardAvatars("отказ запуска карты");
        }

        private void RetireServerRun(string reason)
        {
            if (_scope == null || _scope.IsDisposed || _authority == null) return;
            GameLog.Match.Verbose($"[MapBootstrap] Запуск '{_config?.MapScene}' ({_scope.Key}) снят: {reason}.", this);
            _authority.Retire(_scope, _authority.Current.Revision);
            MapRunAdmission.DiscardAvatars(reason);
        }

        private void ResetServerRun()
        {
            _scope = null;
            _config = null;
            _referee = null;
            _bindings = null;
            _catalog = null;
            _arsenal = null;
            _authority = null;
            FailureCode = string.Empty;
            _closing = false;
            Stage = MapBootstrapStage.WaitingPrerequisites;
            _waitingSince = -1f;
            _stuckReported = false;
        }

        /// <summary>Диагностика ожидания: сообщает, чего ждём, но не превращает ожидание в успех.</summary>
        private void ReportStuck(string waitingFor)
        {
            if (_waitingSince < 0f) _waitingSince = Time.unscaledTime;
            if (_stuckReported || Time.unscaledTime - _waitingSince < StuckWarningSeconds) return;
            _stuckReported = true;
            GameLog.Match.Warning($"[MapBootstrap] Карта '{gameObject.scene.name}' ждёт {StuckWarningSeconds:0} с: {waitingFor}.", this);
        }

        private static string Describe(IReadOnlyList<string> errors)
        {
            if (errors == null || errors.Count == 0) return string.Empty;
            return " [" + string.Join(", ", errors.Take(10)) + (errors.Count > 10 ? $", … ещё {errors.Count - 10}" : "") + "]";
        }

        private static ArsenalDeploymentAnimator[] DeploymentAnimators(IEnumerable<ArsenalStationCompositionBinding> stations) =>
            stations.Where(s => s != null)
                .Select(s => s.GetComponent<ArsenalDeploymentAnimator>())
                .Where(a => a != null)
                .ToArray();

        // ── Клиент ───────────────────────────────────────────────────────────

        /// <summary>
        /// Единственная клиентская точка «run известен локально». Descriptor принимается, если описывает сцену
        /// этого MapBootstrap, запуск собран (CompositionReady/Ready), эпоха сессии текущая, а LoadSequence строго
        /// больше ключа, уже принятого любым прежним MapBootstrap этого клиента. Принятый run закрывается, когда
        /// descriptor уходит в Closing/Retiring/Failed или сменяется другим ключом; новый run — уже новый
        /// экземпляр сцены. Host и dedicated сюда не заходят: у них сервер.
        /// </summary>
        private void UpdateClientRun()
        {
            MapRunAuthority authority = MapRunAuthority.Instance;
            MapRunSnapshot snapshot = authority != null ? authority.Current : default;

            if (!_localRunKey.IsValid)
            {
                if (!ReferenceEquals(_acceptedOn, NetworkClient.connection)) _highestAcceptedKey = default;
                if (!AcceptsClientRun(snapshot, gameObject.scene.name, _highestAcceptedKey)) return;
                _highestAcceptedKey = snapshot.Key;
                _acceptedOn = NetworkClient.connection;
                MapRuntimeCatalog catalog = GameNetworkManager.MapCatalog;
                AcceptLocalRun(snapshot.Config, catalog != null ? catalog.ArsenalComposition : null);
                return;
            }

            if (_localRunClosed) return;
            if (snapshot.Key == _localRunKey && Assembled(snapshot.Status))
            {
                PollLocalRun();
                return;
            }
            GameLog.Match.Verbose($"[MapBootstrap] Клиент: запуск {_localRunKey} закрыт (descriptor {snapshot.Key}, {snapshot.Status}).", this);
            CloseLocalRun();
        }

        /// <summary>
        /// Клиент принял запуск <paramref name="config"/>: собрать сгенерированные станции своей сцены тем же
        /// адаптером и с тем же ключом, что сервер. Описание сверяется с опубликованным config до сборки; расхождение,
        /// отказ описания или сборщика — именованный отказ запуска на этом клиенте (снимок не запрашивается,
        /// взаимодействие закрыто), без подмены авторскими слотами.
        /// </summary>
        internal void AcceptLocalRun(MapRunConfig config, ArsenalCompositionCatalog compositionCatalog)
        {
            _localRunKey = config.Key;
            _waitingSince = -1f;
            _stuckReported = false;
            GameLog.Match.Info($"[MapBootstrap] Клиент принял запуск '{config.MapScene}' ({config.Key}).", this);

            var errors = new List<string>();
            MapArsenalCompositionAdapter arsenal = MapArsenalCompositionAdapter.Describe(_root.StationBindings,
                _root.Map != null ? _root.Map.arsenalPreset : null, compositionCatalog, Composer, errors);
            if (errors.Count != 0)
            {
                FailLocalRun("Arsenal.Generated.Description", Join(errors));
                return;
            }
            if (!arsenal.Verify(config, _root.StationBindings, errors))
            {
                FailLocalRun("Arsenal.Generated.ConfigMismatch", Join(errors));
                return;
            }

            _localArsenal = arsenal;
            if (arsenal.HasStations)
            {
                _localScope = new MapRunScope(config.Key);
                if (!ComposeGeneratedStations(arsenal, _localScope, out string composeError))
                {
                    FailLocalRun("Arsenal.Generated.Compose", composeError);
                    return;
                }
                if (arsenal.Readiness == ArsenalCompositionReadiness.Failed)
                {
                    FailLocalRun("Arsenal.Generated.NotReady", arsenal.Reason);
                    return;
                }
            }

            LocalReadinessChanged?.Invoke();
        }

        /// <summary>
        /// Клиент: опрос сгенерированных станций принятого запуска. Pending → Passed сообщает барьеру Relay готовность,
        /// Failed — именованный отказ.
        /// </summary>
        internal void PollLocalRun()
        {
            if (_localArsenal == null || !_localArsenal.HasStations || _localRunFailed || _localRunClosed) return;
            ArsenalCompositionReadiness before = _localArsenal.Readiness;
            ArsenalCompositionReadiness now = _localArsenal.Poll();
            if (now == ArsenalCompositionReadiness.Failed)
            {
                FailLocalRun("Arsenal.Generated.NotReady", _localArsenal.Reason);
                return;
            }
            if (now == ArsenalCompositionReadiness.Pending) ReportStuck("регистрация сгенерированных станций (" + _localArsenal.Reason + ")");
            if (now != before) LocalReadinessChanged?.Invoke();
        }

        /// <summary>Клиент: descriptor ушёл из собранного состояния — запуск закрыт, писатели по scope остановлены.</summary>
        internal void CloseLocalRun()
        {
            if (_localRunClosed) return;
            _localRunClosed = true;
            _localScope?.Close();
            LocalReadinessChanged?.Invoke();
        }

        private void FailLocalRun(string code, string detail)
        {
            _localRunFailed = true;
            FailureCode = code;
            GameLog.Error($"[MapBootstrap] Клиент: запуск '{gameObject.scene.name}' ({_localRunKey}) отказал: {code}" +
                (string.IsNullOrEmpty(detail) ? "" : " — " + detail) +
                ". Снимок состояния не запрашивается, взаимодействие с картой закрыто.", this);
            DisposeLocalScope();
            LocalReadinessChanged?.Invoke();
        }

        /// <summary>Разборка того, что собрал клиент: scope принятого запуска (станции генератора).</summary>
        private void DisposeLocalScope()
        {
            MapRunScope scope = _localScope;
            _localScope = null;
            if (scope == null) return;
            try { scope.Dispose(); }
            catch (Exception error) { GameLog.Error("[MapBootstrap] Ошибка разборки станций клиента: " + error, this); }
        }

        private static string Join(IReadOnlyList<string> errors) =>
            errors == null || errors.Count == 0 ? string.Empty
                : string.Join(", ", errors.Take(10)) + (errors.Count > 10 ? $", … ещё {errors.Count - 10}" : "");

        /// <summary>Правило «run известен локально» в чистом виде (проверка — MapBootstrapClientRunTests).</summary>
        internal static bool AcceptsClientRun(MapRunSnapshot snapshot, string sceneName, MapRunKey highestAccepted) =>
            snapshot.Config != null && snapshot.Config.MapScene == sceneName && Assembled(snapshot.Status) &&
            (snapshot.Key.SessionEpoch != highestAccepted.SessionEpoch ||
             snapshot.Key.LoadSequence > highestAccepted.LoadSequence);

        private static bool Assembled(MapBootstrapStatus status) =>
            status == MapBootstrapStatus.CompositionReady || status == MapBootstrapStatus.Ready;

        /// <summary>
        /// Клиент связывает заспавненный сервером координатор со станциями своей сцены.
        /// Связь — по netId из descriptor текущего запуска, в любом порядке прихода объекта и descriptor.
        /// </summary>
        private void BindClientServices()
        {
            MapRunAuthority authority = MapRunAuthority.Instance;
            if (authority == null) return;
            MapRunSnapshot snapshot = authority.Current;
            // Только принятый run этой сцены: descriptor старого запуска той же сцены координатор не связывает.
            if (snapshot.Config == null || !_localRunKey.IsValid || _localRunClosed || snapshot.Key != _localRunKey) return;
            CheckClientContent(snapshot.Config);
            if (snapshot.CoordinatorNetId == 0 || snapshot.CoordinatorNetId == _boundCoordinatorNetId) return;
            if (!NetworkClient.spawned.TryGetValue(snapshot.CoordinatorNetId, out NetworkIdentity identity) || identity == null)
                return;
            if (!identity.TryGetComponent(out ArsenalBoundaryWall coordinator)) return;

            coordinator.Configure(DeploymentAnimators(_root.StationBindings), _deploymentDuration);
            _boundCoordinatorNetId = snapshot.CoordinatorNetId;
        }

        /// <summary>
        /// Клиент и сервер обязаны загрузить одно содержимое карты: другой отпечаток — другая сборка
        /// (сцена, MapData или режимы), и ссылки описания запуска ей не соответствуют. Один отчёт на запуск.
        /// </summary>
        private void CheckClientContent(MapRunConfig config)
        {
            if (config.Key == _checkedClientKey) return;
            _checkedClientKey = config.Key;
            MapRuntimeCatalog catalog = GameNetworkManager.MapCatalog;
            string local = catalog != null && _root.Map != null ? catalog.ContentFingerprintFor(_root.Map) : null;
            if (local != config.ContentFingerprint)
                GameLog.Error($"[MapBootstrap] Содержимое карты '{config.MapScene}' у клиента не совпадает с сервером " +
                    $"(сервер {config.ContentFingerprint}, клиент {local ?? "нет в каталоге"}): разные сборки.", this);
        }
    }
}

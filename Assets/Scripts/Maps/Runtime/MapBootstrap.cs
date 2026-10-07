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
    public enum MapBootstrapStage : byte { WaitingPrerequisites, CompositionReady, Ready, Failed }

    /// <summary>
    /// Сборка запуска одной карты. Сервер проверяет <see cref="MapRoot"/>, разрешает
    /// <see cref="MapRunConfig"/> по центральному <see cref="MapRuntimeCatalog"/>, спавнит служебные
    /// MapReferee и координатор развёртывания из зарегистрированных префабов, публикует CompositionReady
    /// и запускает разминку; server Ready открывает только первый закоммиченный режим.
    /// Клиент ничего не создаёт: связывает уже заспавненный координатор с авторскими станциями сцены.
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

        [Tooltip("Длительность выдвижения оборудования станций; передаётся координатору до спавна.")]
        [SerializeField, Min(0.1f)] private float _deploymentDuration = 1f;

        private MapRoot _root;
        private MapRunAuthority _authority;
        private MapRunScope _scope;
        private MapRunConfig _config;
        private MapReferee _referee;
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
            _authority.Current.IsReady && _authority.Current.Key == _scope.Key;

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
        /// закрыт. Клиент: run принят (<see cref="LocalRunKey"/>), не закрыт, станции готовы. Авторские станции —
        /// объекты сцены с авторскими UniqueId и готовы сразу; handles генерируемых станций (задача 7 плана)
        /// добавят сюда свой ValidateReady по фактическим регистрациям. Отдельного реестра участников нет:
        /// забытый участник открыл бы барьер раньше времени.
        /// </summary>
        public bool IsLocallyReady(MapRunKey key)
        {
            if (!key.IsValid) return false;
            if (NetworkServer.active)
                return _scope != null && !_scope.IsClosed && _scope.Key == key &&
                       (Stage == MapBootstrapStage.CompositionReady || Stage == MapBootstrapStage.Ready);
            return key == _localRunKey && !_localRunClosed;
        }

        // ── Сервер ───────────────────────────────────────────────────────────

        private void UpdateServer()
        {
            // Сервер перезапущен в той же сцене: прежний scope закрыл старый authority.
            if (Stage != MapBootstrapStage.WaitingPrerequisites && _authority != MapRunAuthority.Instance)
                ResetServerRun();

            if (Stage == MapBootstrapStage.WaitingPrerequisites && !_closing) TryBeginRun();
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
            var request = new MapRunRequest(authority.NextKey, map.sceneName, CapturedModeId(map),
                catalog.ContentFingerprintFor(map));
            MapRunResolution resolution = catalog.Resolve(request, validation.Bindings,
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
            GameLog.Match.Info($"[MapBootstrap] Запуск '{_config.MapScene}' ({_config.Key}): режим матча " +
                $"'{(_config.MatchIntent.HasMatch ? _config.MatchIntent.ModeId : "нет")}' ({_config.MatchIntent.ResolutionReason}).", this);

            try
            {
                Compose(validation.Bindings, catalog);
            }
            catch (Exception error)
            {
                FailRun("Composition.Exception", error.Message);
            }
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

        private void Compose(MapRootBindings bindings, MapRuntimeCatalog catalog)
        {
            // Ассортимент — из канонического паспорта карты, до любой выдачи. Повтор того же preset безопасен.
            foreach (ArsenalStationCompositionBinding station in bindings.Stations)
                station.AuthoredBinding.Prepare(bindings.Map.arsenalPreset);

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
                _localRunKey = snapshot.Key;
                _highestAcceptedKey = snapshot.Key;
                _acceptedOn = NetworkClient.connection;
                GameLog.Match.Info($"[MapBootstrap] Клиент принял запуск '{snapshot.Config.MapScene}' ({snapshot.Key}).", this);
                LocalReadinessChanged?.Invoke();
                return;
            }

            if (_localRunClosed || (snapshot.Key == _localRunKey && Assembled(snapshot.Status))) return;
            _localRunClosed = true;
            GameLog.Match.Verbose($"[MapBootstrap] Клиент: запуск {_localRunKey} закрыт (descriptor {snapshot.Key}, {snapshot.Status}).", this);
            LocalReadinessChanged?.Invoke();
        }

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

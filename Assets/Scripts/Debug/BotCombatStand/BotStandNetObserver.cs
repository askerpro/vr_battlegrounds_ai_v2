using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mirror;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Клиентский наблюдатель этапа pf-network: как бот-аватар выглядит у клиента. Только чтение — ничего не пишет в
    /// трансформы, хват, сеть и учёт; не вызывает Cmd/Rpc. Единственное исключение — A/B attempt-6
    /// (<see cref="BotStandNetObserverRequest.ClientNoRecoil"/>): на локальную копию оружия в руке бота вешается
    /// <see cref="BotStandNoRecoilPolicy"/> (запрет корневой отдачи SDK только на этой машине, без синхронизации).
    /// Работает только в процессе «чистого» клиента (клиент активен, сервер — нет); включается заявкой
    /// <see cref="BotStandNetObserverGate"/>.
    /// <para>
    /// Бот у клиента опознаётся по реплицированному <c>PlayerSession.DeviceToken</c> (префикс <see cref="BotDirector.TokenPrefix"/>),
    /// контрольная цель стенда — по своему префиксу; остальные аватары — удалённые люди. Записи тел ведутся по паре
    /// (эпоха подключения, netId аватара): после reconnect netId тот же, но состояние приходит заново — это отдельная запись.
    /// </para>
    /// Замеры: входящие байты Mirror по netId аватара/сессии (<c>NetworkDiagnostics.InMessageEvent</c>); угол «ствол у
    /// наблюдателя ↔ луч выстрела» в момент повтора синхронизированного <c>UxrProjectileSource.Shoot</c> (событие
    /// <c>StateChanged</c> компонента приходит и при повторе); оценка задержки позы по буферу снимков NetworkTransform;
    /// зазор граббер↔снап; хват/якоря (кобура) и их смены; дубли хвата; бот-компоненты на клиенте; судьба оружия при
    /// смерти; ошибки консоли (отдельно Relay).
    /// <para>
    /// Клиент стенда — обычный игрок со своим (локальным) аватаром без команды режима. Свой аватар в записи тел не входит
    /// и из проверки бот-компонентов исключён (только справка <c>localAvatars</c>/<c>localAvatarTypesExcluded</c>):
    /// проверяются тела ботов и остальная сцена.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Порядок 32000: Update наблюдателя идёт после Update NetworkTransform (порядок 0), где клиент применяет снимок, —
    /// так видна поза запястий «сразу после NT» (до Animator и до LateUpdate UXR).
    /// </remarks>
    [DefaultExecutionOrder(32000)]
    public sealed class BotStandNetObserver : MonoBehaviour
    {
        private const string TargetTokenPrefix = "bot-stand-target-";
        private const float DiscoverySeconds = 0.5f;
        private const float ScanSeconds = 2f;
        private const float WriteSeconds = 5f;
        /// <summary>Потолок общих рядов клиента (кадр, RTT, буфер): при достижении ряд прореживается через один.</summary>
        private const int MaxSeriesSamples = 8192;
        private const float PoseDelaySampleSeconds = 0.1f;
        private const int MaxTimeline = 64;
        private const int MaxShotsKept = 400;
        private const int MaxErrorSamples = 20;
        private static readonly float[] SnapshotDelays = { 1f, 3f };
        /// <summary>Замеры оружия прежнего тела: в момент смерти/исчезновения тела (или появления призрака), через 1 и 3 с.</summary>
        private static readonly float[] DeathWeaponDelays = { 0f, 1f, 3f };
        /// <summary>Выстрел, не дождавшийся своей позы на шкале клиента за это время, считается неразрешённым.</summary>
        private const float ShotResolveTimeoutSeconds = 3f;

        private BotStandNetObserverRequest _request;
        private string _participant;
        private string _reportPath;
        private string _startedUtc;
        private bool _everClient;
        private bool _subscribed;
        private float _nextDiscovery, _nextScan, _nextWrite, _nextPoseDelay;

        // Сеть.
        private readonly Dictionary<uint, long> _bytesByNetId = new Dictionary<uint, long>();
        private readonly Dictionary<uint, int> _messagesByNetId = new Dictionary<uint, int>();
        private long _bytesTotal;
        private int _messagesTotal;
        private double _clientSeconds;
        private readonly List<object> _connections = new List<object>();
        private bool _connected;
        private int _epoch;
        // Время Mirror и кадр клиента, пока подключён (последнее значение после отключения не показательно).
        private const float TimingSampleSeconds = 0.5f;
        private float _nextTiming;
        private readonly List<float> _rttMs = new List<float>(), _bufferTimeMs = new List<float>(), _frameMs = new List<float>();

        // Свой (локальный) аватар клиента-игрока: в проверку бот-компонентов не входит, только справка.
        private readonly HashSet<string> _localAvatarBotTypes = new HashSet<string>();
        private readonly List<object> _localAvatars = new List<object>();
        private uint _localAvatarNetId;

        // Тела и выстрелы.
        private readonly Dictionary<string, Body> _bodies = new Dictionary<string, Body>();
        private readonly List<Body> _order = new List<Body>();
        // Ушедшие тела (смерть/смена тела/отключение) после завершения замеров дописываются строкой в .bodies.jsonl и
        // выбывают из памяти и периодического отчёта: иначе за матч с ботами отчёт рос на каждую жизнь, и каждая запись
        // сериализовала все прежние тела (торможение клонов в C' 2026-10-20).
        private readonly HashSet<string> _retiredKeys = new HashSet<string>();
        private string _retiredPath;
        private int _retiredCount;
        private readonly Dictionary<UxrProjectileSource, Body> _sources = new Dictionary<UxrProjectileSource, Body>();
        private UxrWeaponManager _weaponManager;

        // Глобальные проверки.
        private readonly HashSet<string> _globalBotTypes = new HashSet<string>();
        private int _globalBotComponentsMax;
        private bool _botDirectorSeen;
        private int _duplicateWeaponIdsMax;

        // Консоль (колбэк может прийти из другого потока).
        private readonly object _logLock = new object();
        private int _errors, _relayErrors, _relayWarnings;
        private readonly List<string> _errorSamples = new List<string>();

        private sealed class Body
        {
            public string Key, Kind, Token, Name, FirstSeenUtc, LastSeenUtc;
            public int Epoch;
            public uint AvatarNetId, SessionNetId;
            public float FirstSeen, LastSeen;
            public PlayerController Player;
            public UxrAvatar Avatar;
            public PlayerSession Session;
            public NetworkTransformBase[] Transforms;
            // Якоря пересобираются на каждом проходе: снимок позднего входа вешает предметы с вложенными якорями
            // (PPK → MagAnchor) уже после обнаружения тела (разбор manipulation 5712).
            public readonly List<UxrGrabbableObjectAnchor> Anchors = new List<UxrGrabbableObjectAnchor>();
            public int Frames, Shots, Hits, NotSampledFrames;
            public readonly List<float> PoseDelayMs = new List<float>();
            public readonly List<float> RightGapMm = new List<float>(), LeftGapMm = new List<float>();
            public readonly List<float> RightGapRawMm = new List<float>(), LeftGapRawMm = new List<float>();
            // Угол/смещение ствол↔луч на позе той же серверной метки времени (основной ряд) и в момент приёма события.
            public readonly List<float> ShotAngleDeg = new List<float>(), ShotOffsetMm = new List<float>();
            public readonly List<float> ShotReceiptAngleDeg = new List<float>(), ShotReceiptOffsetMm = new List<float>();
            public readonly List<float> ShotResolveWaitMs = new List<float>(), ShotResolveSlackMs = new List<float>();
            public int ShotsUnresolved, ShotsWithoutTimestamp, CommitsApplied;
            public readonly Dictionary<string, int> Divergences = new Dictionary<string, int>();
            // Последнее известное снаряжение и высота корня — для оружия тела, исчезнувшего без видимой смерти.
            public UxrGrabbableObject LastRight, LastLeft;
            public float LastRootY;
            public bool WeaponsRemembered;
            public uint PreviousAvatarNetId;
            // Запястья бота за кадр: A — сразу после NT (Update 32000), B — начало PostUpdate UXR, C — после всех стадий.
            public float[] AfterNtL, AfterNtR, PreUxrL, PreUxrR;
            public int AfterNtFrame = -1, PreUxrFrame = -1;
            public readonly List<float> WristNtToFinalMm = new List<float>(), WristNtToFinalDeg = new List<float>();
            public readonly List<float> WristNtToPreUxrMm = new List<float>(), WristPreUxrToFinalMm = new List<float>();
            // По рукам отдельно (агрегаты выше: afterNtToFinal — max по двум рукам, остальные — только правая).
            public readonly WristSeries RightWrist = new WristSeries(), LeftWrist = new WristSeries();
            // ClientNoRecoil: установки политики на копии оружия этого тела.
            public readonly List<object> NoRecoilInstalls = new List<object>();
            public readonly List<object> ShotPoses = new List<object>();
            public Dictionary<string, object> Inventory;
            // Позы на сетке серверного времени (как у сервера) и проверка «снимки NT применяются».
            public readonly BotStandPoseGrid Grid = new BotStandPoseGrid(400);
            public readonly Dictionary<string, NtCheck> NtChecks = new Dictionary<string, NtCheck>();
            public readonly List<object> ShotSamples = new List<object>();
            public readonly List<object> Timeline = new List<object>();
            public readonly List<object> Snapshots = new List<object>();
            public readonly List<object> DeathWeapons = new List<object>();
            public readonly HashSet<string> BotComponents = new HashSet<string>();
            public int ForeignHolderFrames, ManagerMismatchFrames, DoubleSideFrames;
            public string RightHeld, LeftHeld, Anchored;
            public readonly HashSet<UxrGrabbableObject> AnchoredItems = new HashSet<UxrGrabbableObject>();
            public bool AliveKnown, Alive;
            public int SnapshotIndex;
            public readonly List<PendingWeapon> PendingWeapons = new List<PendingWeapon>();
            public bool Gone;
        }

        /// <summary>Перезапись запястья у клиента за кадр по одной руке: мм и градусы для каждого отрезка.</summary>
        private sealed class WristSeries
        {
            public readonly List<float> NtToFinalMm = new List<float>(), NtToFinalDeg = new List<float>();
            public readonly List<float> NtToPreUxrMm = new List<float>(), NtToPreUxrDeg = new List<float>();
            public readonly List<float> PreUxrToFinalMm = new List<float>(), PreUxrToFinalDeg = new List<float>();
        }

        private sealed class PendingWeapon
        {
            public UxrGrabbableObject Grip;
            public string Name, Where, Reason;
            public float DeathTime, RootY;
            public int Index;
        }

        /// <summary>
        /// Применяет ли NT бота снимки у этого клиента: поза цели сразу после Update NT против интерполяции буфера
        /// снимков на <c>NetworkTime.time</c> (как делает NetworkTransformUnreliable.UpdateClientInterpolation).
        /// </summary>
        private sealed class NtCheck
        {
            public string SyncDirection, CoordinateSpace;
            public bool IsOwned;
            public int Frames, FramesWithoutSnapshots, LastSnapshotCount;
            public readonly List<float> ErrorMm = new List<float>(), ErrorDeg = new List<float>(), MotionMm = new List<float>();
            public Vector3 LastPosition;
            public bool HasLast;
        }

        /// <summary>Выстрел, ждущий на шкале клиента позы оружия той же серверной метки времени.</summary>
        private sealed class PendingShot
        {
            public Body Body;
            public Transform Barrel;
            public Vector3 RayPosition, PreviousPosition;
            public Quaternion RayRotation, PreviousRotation;
            public double Target, PreviousTime, Stamp;
            public float ReceivedAt, ReceiptAngle, ReceiptOffset;
            public string ReceivedUtc;
        }

        private readonly List<PendingShot> _pendingShots = new List<PendingShot>();
        private readonly Dictionary<UxrFirearmWeapon, Body> _firearms = new Dictionary<UxrFirearmWeapon, Body>();
        private readonly Dictionary<UxrFirearmWeapon, Action<UxrFirearmReplayDivergence>> _divergenceHandlers =
            new Dictionary<UxrFirearmWeapon, Action<UxrFirearmReplayDivergence>>();
        private string _lastWriteReason;

        // attempt-6: приём NT (ResetState, подстановка осей из трансформа, камера в спавне) и ClientNoRecoil.
        private readonly BotStandNetReceiveTrace _receive = new BotStandNetReceiveTrace();
        private readonly HashSet<UxrFirearmWeapon> _noRecoilInstalled = new HashSet<UxrFirearmWeapon>();

        public void Configure(BotStandNetObserverRequest request)
        {
            _request = request;
            _participant = ParticipantName();
            _startedUtc = DateTime.UtcNow.ToString("o");
            string file = Sanitize(_participant) + "-pid" + Process.GetCurrentProcess().Id;
            _reportPath = Path.Combine(request.OutputDirectory, file + ".json");
            _retiredPath = Path.Combine(request.OutputDirectory, file + ".bodies.jsonl");
        }

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += OnQuitting;
        }

        /// <summary>
        /// Выход из Play клона. В H1C (attempt-2) клон после отключения от хоста до OnDisable не дошёл (лог обрывается в
        /// обработчике отключения Mirror) — поэтому отчёт пишется ещё и при отключении и при Application.quitting.
        /// </summary>
        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= OnQuitting;
            Unsubscribe();
            if (_everClient) Write(true, "disable");
        }

        private void OnQuitting()
        {
            if (_everClient) Write(true, "quit");
        }

        private void Update()
        {
            if (_request == null) return;
            bool clientOnly = NetworkClient.active && !NetworkServer.active;
            if (!clientOnly)
            {
                if (_subscribed) Unsubscribe();
                TrackConnection(false);
                return;
            }
            _everClient = true;
            if (!_subscribed) Subscribe();
            TrackConnection(NetworkClient.isConnected);
            if (_connected) _clientSeconds += Time.unscaledDeltaTime;

            float now = Time.unscaledTime;
            try
            {
                if (now >= _nextDiscovery) { _nextDiscovery = now + DiscoverySeconds; Discover(); }
                if (now >= _nextScan) { _nextScan = now + ScanSeconds; Scan(); }
                if (now >= _nextPoseDelay) { _nextPoseDelay = now + PoseDelaySampleSeconds; SamplePoseDelay(); }
                if (_connected) AddSample(_frameMs, Time.unscaledDeltaTime * 1000f);
                if (_connected && now >= _nextTiming) { _nextTiming = now + TimingSampleSeconds; SampleTiming(); }
                if (_weaponManager == null && UxrWeaponManager.HasInstance)
                {
                    _weaponManager = UxrWeaponManager.Instance;
                    _weaponManager.DamageReceived += OnDamage;
                }
                PendingChecks(now);
                RetireGoneBodies();
                CaptureAfterNt();
            }
            catch (Exception e) { GameLog.Debug.Warning("[BotStandNetObserver] " + e.Message); }
            if (now >= _nextWrite) { _nextWrite = now + WriteSeconds; Write(false, "periodic"); }
        }

        private void Subscribe()
        {
            _subscribed = true;
            NetworkDiagnostics.InMessageEvent += OnInMessage;
            UxrManager.AvatarsUpdated += OnAvatarsUpdated;
            UxrManager.StageUpdating += OnStageUpdating;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            NetworkDiagnostics.InMessageEvent -= OnInMessage;
            UxrManager.AvatarsUpdated -= OnAvatarsUpdated;
            UxrManager.StageUpdating -= OnStageUpdating;
            foreach (var source in _sources.Keys) if (source != null) source.StateChanged -= OnSourceStateChanged;
            _sources.Clear();
            foreach (var firearm in _firearms.Keys) if (firearm != null) firearm.StateChanged -= OnFirearmStateChanged;
            _firearms.Clear();
            foreach (var pair in _divergenceHandlers) if (pair.Key != null) pair.Key.ReadinessReplayDiverged -= pair.Value;
            _divergenceHandlers.Clear();
            foreach (PendingShot pending in _pendingShots) pending.Body.ShotsUnresolved++;
            _pendingShots.Clear();
            if (_weaponManager != null) _weaponManager.DamageReceived -= OnDamage;
            _weaponManager = null;
        }

        private void TrackConnection(bool connected)
        {
            if (connected == _connected) return;
            _connected = connected;
            if (connected) _epoch++;
            _connections.Add(new Dictionary<string, object>
            {
                ["event"] = connected ? "connected" : "disconnected", ["epoch"] = _epoch, ["utc"] = DateTime.UtcNow.ToString("o"),
                ["time"] = Time.unscaledTime
            });
            if (!connected)
            {
                foreach (Body body in _order) if (!body.Gone) Leave(body, "disconnect");
                if (_everClient) Write(false, "disconnect");
            }
        }

        // ── Тела ─────────────────────────────────────────────────────────────

        private void Discover()
        {
            if (!_connected) return;
            foreach (PlayerSession session in FindObjectsByType<PlayerSession>())
            {
                if (session == null || session.isLocalPlayer) continue;
                PlayerController player = session.ActiveAvatar;
                if (player == null || player.netId == 0) continue;
                string token = session.DeviceToken ?? string.Empty;
                string kind = token.StartsWith(BotDirector.TokenPrefix, StringComparison.Ordinal) ? "bot" :
                    token.StartsWith(TargetTokenPrefix, StringComparison.Ordinal) ? "target" : "remote";
                string key = _epoch + ":" + player.netId;
                if (_bodies.TryGetValue(key, out Body known))
                {
                    if (known.Player != player)
                    {
                        // Тот же netId в той же эпохе, но другой объект (повторный спавн): ссылки обновить, запись продолжить.
                        known.Player = player;
                        known.Avatar = player.GetComponent<UxrAvatar>();
                        known.Transforms = player.GetComponentsInChildren<NetworkTransformBase>(true);
                        if (known.Gone) { known.Gone = false; Event(known, "respawnedSameNetId", player.name, null); }
                    }
                    continue;
                }
                // Ключ уже выбывшего тела: тот же netId в той же эпохе — повторный спавн; запись начинается заново.
                bool reborn = _retiredKeys.Remove(key);
                var body = new Body
                {
                    Key = key, Kind = kind, Token = token, Name = session.PlayerName, Epoch = _epoch, AvatarNetId = player.netId,
                    SessionNetId = session.netId, FirstSeen = Time.unscaledTime, FirstSeenUtc = DateTime.UtcNow.ToString("o"),
                    Player = player, Session = session, Avatar = player.GetComponent<UxrAvatar>(),
                    Transforms = player.GetComponentsInChildren<NetworkTransformBase>(true)
                };
                body.LastSeen = body.FirstSeen;
                body.LastSeenUtc = body.FirstSeenUtc;
                // Новое тело того же бота в той же эпохе (смерть → призрак → возрождение): у клиента прежнее тело
                // уничтожается без видимого IsAlive=false — оружие прежнего тела фиксируется здесь.
                if (kind == "bot")
                    foreach (Body previous in _order)
                    {
                        if (previous.Kind != "bot" || previous.Token != token || previous.Epoch != _epoch || previous.AvatarNetId == player.netId) continue;
                        body.PreviousAvatarNetId = previous.AvatarNetId;
                        RememberWeapons(previous, "replacedByNewBody");
                    }
                _bodies[key] = body;
                _order.Add(body);
                ScanBody(body);
                if (kind != "remote") body.Inventory = BotStandNetPoses.Inventory(body.Avatar);
                Event(body, "firstSeen", player.name, body.PreviousAvatarNetId != 0 ? (object)body.PreviousAvatarNetId : null);
                if (reborn) Event(body, "respawnedSameNetId", player.name, null);
            }
            foreach (Body body in _order)
                if (!body.Gone && body.Player == null) Leave(body, "destroyed");
        }

        private void Leave(Body body, string why)
        {
            // Тело бота исчезло в своей эпохе (смерть/смена тела/конец case) — что стало с его оружием.
            if (why == "destroyed" && body.Kind == "bot") RememberWeapons(body, "bodyDestroyed");
            body.Gone = true;
            Event(body, "gone", why, null);
            body.Player = null;
            body.Avatar = null;
        }

        /// <summary>
        /// Ушедшее тело без незавершённых замеров (оружие после смерти, выстрелы в ожидании позы) — строкой в
        /// .bodies.jsonl и из памяти: из списков, ключей и привязок оружия.
        /// </summary>
        private void RetireGoneBodies()
        {
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                Body body = _order[i];
                if (!body.Gone || body.PendingWeapons.Count > 0 || HasPendingShot(body)) continue;
                Retire(body);
                _order.RemoveAt(i);
            }
        }

        private bool HasPendingShot(Body body)
        {
            foreach (PendingShot pending in _pendingShots) if (pending.Body == body) return true;
            return false;
        }

        private void Retire(Body body)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_retiredPath));
                File.AppendAllText(_retiredPath, JsonConvert.SerializeObject(Describe(body), Formatting.None) + "\n",
                    new System.Text.UTF8Encoding(false));
                _retiredCount++;
            }
            catch (Exception e) { GameLog.Debug.Warning("[BotStandNetObserver] Выбытие тела: " + e.Message); }
            _bodies.Remove(body.Key);
            _retiredKeys.Add(body.Key);
            var sources = new List<UxrProjectileSource>();
            foreach (var pair in _sources) if (pair.Value == body || pair.Key == null) sources.Add(pair.Key);
            foreach (UxrProjectileSource source in sources)
            {
                if (source != null) source.StateChanged -= OnSourceStateChanged;
                _sources.Remove(source);
            }
            var firearms = new List<UxrFirearmWeapon>();
            foreach (var pair in _firearms) if (pair.Value == body || pair.Key == null) firearms.Add(pair.Key);
            foreach (UxrFirearmWeapon firearm in firearms)
            {
                if (firearm != null)
                {
                    firearm.StateChanged -= OnFirearmStateChanged;
                    if (_divergenceHandlers.TryGetValue(firearm, out var handler)) firearm.ReadinessReplayDiverged -= handler;
                }
                _divergenceHandlers.Remove(firearm);
                _firearms.Remove(firearm);
            }
        }

        private static void AddSample(List<float> series, float value)
        {
            if (series.Count >= MaxSeriesSamples)
            {
                // Прореживание через один: распределение сохраняется, память и сортировка в Stats ограничены.
                int write = 0;
                for (int read = 0; read < series.Count; read += 2) series[write++] = series[read];
                series.RemoveRange(write, series.Count - write);
            }
            series.Add(value);
        }

        /// <summary>Бот-компоненты на теле и в сцене клиента (ожидается 0): пространства имён ботов и Blaze.</summary>
        private void Scan()
        {
            foreach (Body body in _order) if (!body.Gone && body.Player != null) ScanBody(body);
            int count = 0;
            Transform local = LocalAvatarRoot();
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour == null || !IsBotType(behaviour.GetType())) continue;
                // Свой аватар клиента-игрока — не тело бота: отдельной справкой, в счёт не идёт.
                if (local != null && behaviour.transform.IsChildOf(local)) { _localAvatarBotTypes.Add(behaviour.GetType().FullName); continue; }
                count++;
                _globalBotTypes.Add(behaviour.GetType().FullName);
            }
            _globalBotComponentsMax = Mathf.Max(_globalBotComponentsMax, count);
            if (BotDirector.Instance != null) _botDirectorSeen = true;

            // Дубли оружия: два экземпляра с одним UniqueId (повторный спавн/снимок).
            var ids = new HashSet<Guid>();
            int duplicates = 0;
            foreach (UxrFirearmWeapon weapon in FindObjectsByType<UxrFirearmWeapon>())
                if (weapon != null && !ids.Add(weapon.UniqueId)) duplicates++;
            _duplicateWeaponIdsMax = Mathf.Max(_duplicateWeaponIdsMax, duplicates);
        }

        private static void ScanBody(Body body)
        {
            foreach (Component component in body.Player.GetComponentsInChildren<Component>(true))
                if (component != null && IsBotType(component.GetType())) body.BotComponents.Add(component.GetType().FullName);
        }

        private static bool IsBotType(Type type)
        {
            string ns = type.Namespace ?? string.Empty;
            // Фикстура стенда (BotCombatStand в сцене стенда) — не бот-компонент: её пространство имён не считается.
            return ns.StartsWith("VrBattlegrounds.Bots", StringComparison.Ordinal) || ns.StartsWith("BlazeAISpace", StringComparison.Ordinal);
        }

        /// <summary>Корень своего аватара (локальная сессия клиента-игрока); null — зритель или аватара нет.</summary>
        private Transform LocalAvatarRoot()
        {
            PlayerSession local = PlayerSession.LocalSession;
            PlayerController avatar = local != null ? local.ActiveAvatar : null;
            if (avatar == null) return null;
            if (avatar.netId != _localAvatarNetId)
            {
                _localAvatarNetId = avatar.netId;
                Vector3 position = avatar.transform.position;
                _localAvatars.Add(new Dictionary<string, object>
                {
                    ["epoch"] = _epoch, ["avatarNetId"] = avatar.netId, ["teamIndex"] = local.TeamIndex, ["role"] = local.Role.ToString(),
                    ["position"] = new[] { position.x, position.y, position.z }, ["utc"] = DateTime.UtcNow.ToString("o")
                });
            }
            return avatar.transform;
        }

        private void SampleTiming()
        {
            AddSample(_rttMs, (float)(NetworkTime.rtt * 1000.0));
            AddSample(_bufferTimeMs, (float)(NetworkClient.bufferTime * 1000.0));
        }

        /// <summary>A: запястья бота сразу после применения снимков NT (Update наблюдателя идёт после Update NT).</summary>
        private void CaptureAfterNt()
        {
            if (!_connected) return;
            _receive.OnUpdate();
            foreach (Body body in _order)
            {
                if (body.Kind != "bot" || body.Gone || body.Avatar == null) continue;
                if (_request.ClientNoRecoil) ApplyClientNoRecoil(body);
                body.AfterNtL = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Left));
                body.AfterNtR = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Right));
                body.AfterNtFrame = Time.frameCount;
                CheckNtApplied(body);
            }
        }

        /// <summary>
        /// ClientNoRecoil: политика запрета корневой отдачи на корневой grabbable копии оружия в руке бота — с первого кадра,
        /// когда оружие в руке (до повтора выстрела этим оружием), на каждое новое оружие/тело. Компонент локальный: не
        /// <c>UxrComponent</c>, не синхронизируется. shotsSeenBefore/commitsBefore — повторы выстрела тела к этому кадру.
        /// </summary>
        private void ApplyClientNoRecoil(Body body)
        {
            if (!UxrGrabManager.HasInstance) return;
            for (int i = 0; i < 2; i++)
            {
                UxrHandSide side = i == 0 ? UxrHandSide.Right : UxrHandSide.Left;
                UxrGrabber grabber = body.Avatar.GetGrabber(side);
                UxrGrabbableObject item = grabber != null ? grabber.GrabbedObject : null;
                if (item == null) continue;
                UxrFirearmWeapon firearm = item.GetComponentInParent<UxrFirearmWeapon>();
                if (firearm == null) firearm = item.GetComponentInChildren<UxrFirearmWeapon>();
                if (firearm == null || _noRecoilInstalled.Contains(firearm)) continue;
                UxrGrabbableObject root = BotStandShotConditions.RootGrabbable(firearm);
                if (root == null) continue;
                bool existed = root.GetComponent<BotStandNoRecoilPolicy>() != null;
                if (!existed) root.gameObject.AddComponent<BotStandNoRecoilPolicy>();
                _noRecoilInstalled.Add(firearm);
                body.NoRecoilInstalls.Add(new Dictionary<string, object>
                {
                    ["weapon"] = firearm.name, ["weaponUniqueId"] = firearm.UniqueId.ToString(), ["side"] = side.ToString(),
                    ["root"] = root.name, ["alreadyPresent"] = existed, ["frame"] = Time.frameCount,
                    ["t"] = Time.unscaledTime - body.FirstSeen, ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["shotsSeenBefore"] = body.Shots, ["commitsBefore"] = body.CommitsApplied
                });
                Event(body, "clientNoRecoil", firearm.name, Time.frameCount);
            }
        }

        private static void CheckNtApplied(Body body)
        {
            if (body.Transforms == null) return;
            double now = NetworkTime.time;
            foreach (NetworkTransformBase nt in body.Transforms)
            {
                if (nt == null) continue;
                Transform target = nt.target != null ? nt.target : nt.transform;
                if (!body.NtChecks.TryGetValue(target.name, out NtCheck check))
                {
                    check = new NtCheck { SyncDirection = nt.syncDirection.ToString(), CoordinateSpace = nt.coordinateSpace.ToString(), IsOwned = nt.isOwned };
                    body.NtChecks[target.name] = check;
                }
                check.Frames++;
                bool local = nt.coordinateSpace == CoordinateSpace.Local;
                Vector3 position = local ? target.localPosition : target.position;
                Quaternion rotation = local ? target.localRotation : target.rotation;
                if (check.HasLast) check.MotionMm.Add((position - check.LastPosition).magnitude * 1000f);
                check.LastPosition = position;
                check.HasLast = true;
                SortedList<double, TransformSnapshot> snapshots = nt.clientSnapshots;
                check.LastSnapshotCount = snapshots.Count;
                if (snapshots.Count == 0) { check.FramesWithoutSnapshots++; continue; }
                // Интерполяция буфера на NetworkTime.time (до первого — первый, после последнего — последний).
                TransformSnapshot expected = snapshots.Values[snapshots.Count - 1];
                if (now <= snapshots.Keys[0]) expected = snapshots.Values[0];
                else
                    for (int i = 0; i + 1 < snapshots.Count; i++)
                    {
                        if (now < snapshots.Keys[i] || now > snapshots.Keys[i + 1]) continue;
                        double span = snapshots.Keys[i + 1] - snapshots.Keys[i];
                        expected = TransformSnapshot.Interpolate(snapshots.Values[i], snapshots.Values[i + 1],
                            span > 0 ? (now - snapshots.Keys[i]) / span : 1.0);
                        break;
                    }
                if (nt.syncPosition) check.ErrorMm.Add((position - expected.position).magnitude * 1000f);
                if (nt.syncRotation) check.ErrorDeg.Add(Quaternion.Angle(rotation, expected.rotation));
            }
        }

        /// <summary>B: начало PostUpdate UXR (после Update и Animator кадра).</summary>
        private void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.AvatarUsingTracking || !_connected) return;
            foreach (Body body in _order)
            {
                if (body.Kind != "bot" || body.Gone || body.Avatar == null) continue;
                body.PreUxrL = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Left));
                body.PreUxrR = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Right));
                body.PreUxrFrame = Time.frameCount;
            }
        }

        /// <summary>C против A и B этого кадра: перезаписано ли запястье после применения NT (Animator, UXR/IK, хват).</summary>
        private static void WristOverwrite(Body body)
        {
            if (body.AfterNtFrame != Time.frameCount) return;
            float[] finalR = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Right));
            float[] finalL = BotStandNetPoses.Pose(body.Avatar.GetHandBone(UxrHandSide.Left));
            BotStandNetPoses.Delta(body.AfterNtR, finalR, out float mmR, out float degR);
            BotStandNetPoses.Delta(body.AfterNtL, finalL, out float mmL, out float degL);
            if (!float.IsNaN(mmR)) { body.WristNtToFinalMm.Add(Mathf.Max(mmR, float.IsNaN(mmL) ? 0f : mmL)); body.WristNtToFinalDeg.Add(Mathf.Max(degR, float.IsNaN(degL) ? 0f : degL)); }
            AddDelta(body.RightWrist.NtToFinalMm, body.RightWrist.NtToFinalDeg, mmR, degR);
            AddDelta(body.LeftWrist.NtToFinalMm, body.LeftWrist.NtToFinalDeg, mmL, degL);
            if (body.PreUxrFrame != Time.frameCount) return;
            BotStandNetPoses.Delta(body.AfterNtR, body.PreUxrR, out float mmA, out float degA);
            BotStandNetPoses.Delta(body.PreUxrR, finalR, out float mmB, out float degB);
            if (!float.IsNaN(mmA)) body.WristNtToPreUxrMm.Add(mmA);
            if (!float.IsNaN(mmB)) body.WristPreUxrToFinalMm.Add(mmB);
            AddDelta(body.RightWrist.NtToPreUxrMm, body.RightWrist.NtToPreUxrDeg, mmA, degA);
            AddDelta(body.RightWrist.PreUxrToFinalMm, body.RightWrist.PreUxrToFinalDeg, mmB, degB);
            BotStandNetPoses.Delta(body.AfterNtL, body.PreUxrL, out float mmAL, out float degAL);
            BotStandNetPoses.Delta(body.PreUxrL, finalL, out float mmBL, out float degBL);
            AddDelta(body.LeftWrist.NtToPreUxrMm, body.LeftWrist.NtToPreUxrDeg, mmAL, degAL);
            AddDelta(body.LeftWrist.PreUxrToFinalMm, body.LeftWrist.PreUxrToFinalDeg, mmBL, degBL);
        }

        private static void AddDelta(List<float> mm, List<float> degrees, float valueMm, float valueDeg)
        {
            if (float.IsNaN(valueMm)) return;
            mm.Add(valueMm);
            degrees.Add(valueDeg);
        }

        // ── Покадрово после всех стадий UXR ─────────────────────────────────

        private void OnAvatarsUpdated()
        {
            if (!_connected) return;
            try
            {
                _receive.OnFrameEnd();
                foreach (Body body in _order)
                {
                    if (body.Gone || body.Player == null || body.Avatar == null) continue;
                    body.Frames++;
                    body.LastSeen = Time.unscaledTime;
                    body.LastSeenUtc = DateTime.UtcNow.ToString("o");
                    if (body.Kind != "remote") SampleBody(body);
                    // Сетка серверного времени: клиент показывает позу сервера на NetworkTime.time − сдвиг снимков NT.
                    if (body.Kind == "bot")
                        body.Grid.Feed(NetworkTime.time - SnapshotShift(body), BotStandPoseSet.Capture(body.Avatar), body.AvatarNetId);
                }
                ResolveShots();
            }
            catch (Exception e) { GameLog.Debug.Warning("[BotStandNetObserver] Кадр: " + e.Message); }
        }

        private void SampleBody(Body body)
        {
            if (body.Kind == "bot") WristOverwrite(body);
            bool alive = body.Player.IsAlive;
            if (!body.AliveKnown) { body.AliveKnown = true; body.Alive = alive; Event(body, alive ? "alive" : "bornDead", null, null); }
            else if (body.Alive != alive)
            {
                body.Alive = alive;
                Event(body, alive ? "revived" : "death", null, null);
                if (!alive) RememberWeapons(body, "death");
            }
            body.LastRootY = body.Player.transform.position.y;
            if (!UxrGrabManager.HasInstance) { body.NotSampledFrames++; return; }

            UxrGrabber right = body.Avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = body.Avatar.GetGrabber(UxrHandSide.Left);
            UxrGrabbableObject rightItem = right != null ? right.GrabbedObject : null;
            UxrGrabbableObject leftItem = left != null ? left.GrabbedObject : null;
            body.LastRight = rightItem;
            body.LastLeft = leftItem;
            string rightName = rightItem != null ? rightItem.name : null;
            string leftName = leftItem != null ? leftItem.name : null;
            if (rightName != body.RightHeld) { body.RightHeld = rightName; Event(body, "right", rightName ?? "-", null); }
            if (leftName != body.LeftHeld) { body.LeftHeld = leftName; Event(body, "left", leftName ?? "-", null); }

            Gap(right, rightItem, body.RightGapMm, body.RightGapRawMm);
            Gap(left, leftItem, body.LeftGapMm, body.LeftGapRawMm);
            Holders(body, right, rightItem);
            if (leftItem != rightItem) Holders(body, left, leftItem);
            if (rightItem != null && rightItem == leftItem && !TwoPointGrab(rightItem, right, left)) body.DoubleSideFrames++;
            WatchShots(body, rightItem);
            WatchShots(body, leftItem);
            SampleAnchors(body, rightItem, leftItem);
            Snapshots(body);
        }

        /// <summary>Граббер↔снап SDK, мм (оружие в руке, не отстаёт). Кадры плавного перехода хвата — только в сырой ряд.</summary>
        private static void Gap(UxrGrabber grabber, UxrGrabbableObject item, List<float> gaps, List<float> raw)
        {
            if (grabber == null || item == null) return;
            int point = GrabbedPoint(item, grabber);
            if (point < 0 || !item.ComputeRequiredGrabberTransform(grabber, point, out Vector3 snap, out Quaternion _, false)) return;
            float gap = (grabber.transform.position - snap).magnitude * 1000f;
            raw.Add(gap);
            if (!item.IsInSmoothTransition) gaps.Add(gap);
        }

        private static int GrabbedPoint(UxrGrabbableObject item, UxrGrabber grabber)
        {
            for (int i = 0; i < item.GrabPointCount; i++)
                if (UxrGrabManager.Instance.GetGrabbingHand(item, i, out UxrGrabber holder) && holder == grabber) return i;
            return -1;
        }

        private static bool TwoPointGrab(UxrGrabbableObject item, UxrGrabber right, UxrGrabber left) =>
            GrabbedPoint(item, right) >= 0 && GrabbedPoint(item, left) >= 0 && GrabbedPoint(item, right) != GrabbedPoint(item, left);

        /// <summary>Дубли хвата: предмет бота держит чужой граббер; граббер бота держит предмет, о котором не знает менеджер.</summary>
        private static void Holders(Body body, UxrGrabber grabber, UxrGrabbableObject item)
        {
            if (grabber == null || item == null) return;
            if (!UxrGrabManager.Instance.IsBeingGrabbedBy(item, grabber)) body.ManagerMismatchFrames++;
            foreach (UxrGrabber other in UxrGrabber.AllComponents)
            {
                if (other == null || other.GrabbedObject != item) continue;
                if (other.Avatar != body.Avatar) { body.ForeignHolderFrames++; break; }
            }
        }

        private static bool InHand(UxrGrabbableObject item, UxrGrabbableObject held) =>
            held != null && (item == held || item.transform.IsChildOf(held.transform));

        /// <summary>Якоря тела (кобура): какие предметы лежат; предмет, ушедший из якоря не в руку, — событие с высотой.</summary>
        private static void SampleAnchors(Body body, UxrGrabbableObject rightItem, UxrGrabbableObject leftItem)
        {
            if (body.Player == null) return;
            body.Player.GetComponentsInChildren(true, body.Anchors);
            var current = new List<UxrGrabbableObject>();
            string label = null;
            foreach (UxrGrabbableObjectAnchor anchor in body.Anchors)
            {
                if (anchor == null || anchor.CurrentPlacedObject == null) continue;
                current.Add(anchor.CurrentPlacedObject);
                label = (label == null ? "" : label + ", ") + anchor.CurrentPlacedObject.name + "@" + anchor.name;
            }
            foreach (UxrGrabbableObject was in body.AnchoredItems)
            {
                if (current.Contains(was)) continue;
                if (was == null) { Event(body, "anchoredItemDestroyed", null, null); continue; }
                // Вложенный предмет (магазин в MagAnchor пистолета) уходит из иерархии тела вместе с предметом в руке —
                // это не «выпал»: считается взятым, если он сам или его предок-предмет в руке.
                bool held = InHand(was, rightItem) || InHand(was, leftItem);
                Event(body, held ? "anchoredItemTakenToHand" : "anchoredItemFreed", was.name,
                      held ? null : (object)(was.transform.position.y - body.Player.transform.position.y));
            }
            body.AnchoredItems.Clear();
            foreach (UxrGrabbableObject item in current) body.AnchoredItems.Add(item);
            if (label != body.Anchored)
            {
                body.Anchored = label;
                Event(body, "anchors", label ?? "-", null);
            }
        }

        /// <summary>Снимки через 1 и 3 с после первого появления тела в эпохе (поздний вход/reconnect: что пришло).</summary>
        private static void Snapshots(Body body)
        {
            if (body.SnapshotIndex >= SnapshotDelays.Length) return;
            float age = Time.unscaledTime - body.FirstSeen;
            if (age < SnapshotDelays[body.SnapshotIndex]) return;
            body.SnapshotIndex++;
            body.Snapshots.Add(new Dictionary<string, object>
            {
                ["afterSeconds"] = age, ["alive"] = body.Alive, ["right"] = body.RightHeld, ["left"] = body.LeftHeld,
                ["anchored"] = body.Anchored, ["foreignHolderFrames"] = body.ForeignHolderFrames,
                ["managerMismatchFrames"] = body.ManagerMismatchFrames, ["doubleSideFrames"] = body.DoubleSideFrames,
                ["utc"] = DateTime.UtcNow.ToString("o")
            });
        }

        // ── Выстрел у наблюдателя ────────────────────────────────────────────

        private void WatchShots(Body body, UxrGrabbableObject item)
        {
            if (item == null || body.Kind != "bot") return;
            UxrFirearmWeapon firearm = item.GetComponentInParent<UxrFirearmWeapon>();
            if (firearm == null) firearm = item.GetComponentInChildren<UxrFirearmWeapon>();
            UxrProjectileSource source = firearm != null ? firearm.GetComponent<UxrProjectileSource>() : null;
            if (source == null) return;
            bool known = _sources.ContainsKey(source);
            // Купленное/подобранное заново оружие относится к телу, которое держит его сейчас.
            _sources[source] = body;
            if (!known) source.StateChanged += OnSourceStateChanged;
            bool knownFirearm = _firearms.ContainsKey(firearm);
            _firearms[firearm] = body;
            if (knownFirearm) return;
            // Выстрел AK105/PPK у клиента — повтор UxrFirearmWeapon.CommitShotSynced (учёт готовности, патч SDK); он
            // вызывает UxrProjectileSource.TryShootWithOutcome только если фиксация учёта принята. Отказ (форк ревизии)
            // выстрел у клиента не воспроизводит — считаем и принятые фиксации, и расхождения.
            firearm.StateChanged += OnFirearmStateChanged;
            UxrFirearmWeapon captured = firearm;
            Action<UxrFirearmReplayDivergence> handler = divergence => OnDivergence(captured, divergence);
            _divergenceHandlers[firearm] = handler;
            firearm.ReadinessReplayDiverged += handler;
        }

        private void OnFirearmStateChanged(object sender, UxrSyncEventArgs e)
        {
            if (!(e is UxrMethodInvokedSyncEventArgs call) || call.MethodName != "CommitShotSynced") return;
            if (sender is UxrFirearmWeapon firearm && _firearms.TryGetValue(firearm, out Body body)) body.CommitsApplied++;
        }

        private void OnDivergence(UxrFirearmWeapon firearm, UxrFirearmReplayDivergence divergence)
        {
            if (divergence == null || !_firearms.TryGetValue(firearm, out Body body)) return;
            string key = divergence.Operation + ":" + divergence.Kind;
            body.Divergences.TryGetValue(key, out int count);
            body.Divergences[key] = count + 1;
            if (count == 0)
                Event(body, "ledgerDivergence", key, new[] { divergence.ExpectedRevision, divergence.NextRevision, divergence.LocalRevision });
        }

        /// <summary>
        /// Повтор синхронизированного <c>Shoot(index, position, orientation)</c> у клиента: луч — поворот стрелка (сервер),
        /// ствол — <c>ShotSource</c> этого процесса в этот момент (поза от NetworkTransform + IK + хват клиента).
        /// </summary>
        private void OnSourceStateChanged(object sender, UxrSyncEventArgs e)
        {
            try
            {
                if (!(e is UxrMethodInvokedSyncEventArgs call) || call.MethodName != "Shoot" || call.Parameters == null || call.Parameters.Length < 3) return;
                if (!(sender is UxrProjectileSource source) || !_sources.TryGetValue(source, out Body body)) return;
                if (!(call.Parameters[0] is int index) || !(call.Parameters[1] is Vector3 position) || !(call.Parameters[2] is Quaternion rotation)) return;
                if (index < 0 || index >= source.ShotTypes.Count || source.ShotTypes[index].ShotSource == null) return;
                Transform barrel = source.ShotTypes[index].ShotSource;
                // В момент приёма поза бота на экране клиента старше выстрела на буфер интерполяции (~bufferTime + RTT/2):
                // угол «сейчас» включает поворот ствола за это время (attempt-2: медианы 24–54°, смещение 0,2–0,4 м).
                float receiptAngle = Vector3.Angle(barrel.forward, rotation * Vector3.forward);
                float receiptOffset = (barrel.position - position).magnitude * 1000f;
                body.Shots++;
                body.ShotReceiptAngleDeg.Add(receiptAngle);
                body.ShotReceiptOffsetMm.Add(receiptOffset);
                if (body.Shots <= 3) Event(body, "shot", source.name, receiptAngle);
                // Основной замер — на позе той же серверной метки времени: событие пришло в батче с меткой сервера
                // (connection.remoteTimeStamp); NT кистей бота показывают кадр сервера с этой меткой, когда шкала клиента
                // (NetworkTime.time) дойдёт до метки + сдвиг снимков NT (timeStampAdjustment + offset).
                NetworkConnection connection = NetworkClient.connection;
                if (connection == null || connection.remoteTimeStamp <= 0)
                {
                    body.ShotsWithoutTimestamp++;
                    return;
                }
                _pendingShots.Add(new PendingShot
                {
                    Body = body, Barrel = barrel, RayPosition = position, RayRotation = rotation,
                    Stamp = connection.remoteTimeStamp, Target = connection.remoteTimeStamp + SnapshotShift(body), ReceivedAt = Time.unscaledTime,
                    ReceiptAngle = receiptAngle, ReceiptOffset = receiptOffset, ReceivedUtc = DateTime.UtcNow.ToString("o"),
                    PreviousPosition = barrel.position, PreviousRotation = barrel.rotation, PreviousTime = NetworkTime.time
                });
            }
            catch (Exception ex) { GameLog.Debug.Warning("[BotStandNetObserver] Выстрел: " + ex.Message); }
        }

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            if (e == null || e.ActorSource == null) return;
            foreach (Body body in _order)
                if (!body.Gone && body.Player != null && body.Player._actor == e.ActorSource) { body.Hits++; return; }
        }

        // ── Смерть: судьба оружия у клиента ─────────────────────────────────

        /// <summary>
        /// Оружие тела в момент смерти/исчезновения тела/появления нового тела того же бота (что первое — один раз на
        /// тело): последнее известное (руки, якоря тела) и высота корня. Замеры — в <see cref="PendingChecks"/>.
        /// </summary>
        private static void RememberWeapons(Body body, string reason)
        {
            if (body.WeaponsRemembered) return;
            body.WeaponsRemembered = true;
            var items = new List<UxrGrabbableObject>();
            var where = new List<string>();
            if (body.LastRight != null) { items.Add(body.LastRight); where.Add("hand"); }
            if (body.LastLeft != null && !items.Contains(body.LastLeft)) { items.Add(body.LastLeft); where.Add("hand"); }
            foreach (UxrGrabbableObject anchored in body.AnchoredItems)
                if (anchored != null && !items.Contains(anchored)) { items.Add(anchored); where.Add("anchor"); }
            Event(body, "weaponsRemembered", reason, items.Count);
            for (int i = 0; i < items.Count; i++)
                body.PendingWeapons.Add(new PendingWeapon
                {
                    Grip = items[i], Name = items[i].name, Where = where[i], Reason = reason, DeathTime = Time.unscaledTime,
                    RootY = body.LastRootY
                });
        }

        /// <summary>
        /// Поза бота у клиента на кадре разрешения выстрела (шкала дошла до метки): ствол (интерполирован на метку), луч
        /// из события, корень, камера, запястья — C (после UXR), A (сразу после NT), B (начало PostUpdate), локальные позы
        /// запястий. Сверка с сервером — по <c>stamp</c> (= <c>t</c> серверной записи) в analyze.py.
        /// </summary>
        private void RecordShotPose(Body body, PendingShot shot, Vector3 barrelPosition, Quaternion barrelRotation, double now)
        {
            if (body.ShotPoses.Count >= MaxShotsKept / 2 || body.Avatar == null) return;
            UxrFirearmWeapon firearm = shot.Barrel != null ? shot.Barrel.GetComponentInParent<UxrFirearmWeapon>() : null;
            bool sameFrameA = body.AfterNtFrame == Time.frameCount, sameFrameB = body.PreUxrFrame == Time.frameCount;
            body.ShotPoses.Add(new Dictionary<string, object>
            {
                ["stamp"] = shot.Stamp, ["target"] = shot.Target, ["now"] = now,
                ["barrel"] = BotStandNetPoses.Pose(barrelPosition, barrelRotation),
                ["ray"] = BotStandNetPoses.Pose(shot.RayPosition, shot.RayRotation),
                ["rig"] = BotStandNetPoses.Rig(body.Avatar),
                ["afterNtWristL"] = sameFrameA ? body.AfterNtL : null, ["afterNtWristR"] = sameFrameA ? body.AfterNtR : null,
                ["preUxrWristL"] = sameFrameB ? body.PreUxrL : null, ["preUxrWristR"] = sameFrameB ? body.PreUxrR : null,
                ["weaponUniqueId"] = firearm != null ? firearm.UniqueId.ToString() : null,
                ["avatarMode"] = body.Avatar.AvatarMode.ToString()
            });
        }

        /// <summary>Сдвиг ключа снимка NT относительно метки батча (как в NetworkTransformBase): по NT правой кисти бота.</summary>
        private static double SnapshotShift(Body body)
        {
            NetworkTransformBase chosen = null;
            Transform hand = body.Avatar != null ? body.Avatar.GetHandBone(UxrHandSide.Right) : null;
            if (body.Transforms != null)
                foreach (NetworkTransformBase nt in body.Transforms)
                {
                    if (nt == null) continue;
                    if (chosen == null) chosen = nt;
                    if (hand != null && nt.target == hand) { chosen = nt; break; }
                }
            if (chosen == null) return 0.0;
            double interval = NetworkServer.sendInterval;
            return interval * (chosen.sendIntervalMultiplier - 1) + (chosen.timelineOffset ? interval * chosen.sendIntervalMultiplier : 0.0);
        }

        /// <summary>
        /// После всех стадий UXR кадра ствол показывает позу сервера на момент <c>NetworkTime.time</c>. Когда шкала
        /// переходит метку выстрела, поза ствола интерполируется между прошлым и этим кадром точно на метку.
        /// </summary>
        private void ResolveShots()
        {
            double now = NetworkTime.time;
            for (int i = _pendingShots.Count - 1; i >= 0; i--)
            {
                PendingShot shot = _pendingShots[i];
                Body body = shot.Body;
                if (shot.Barrel == null || Time.unscaledTime - shot.ReceivedAt > ShotResolveTimeoutSeconds)
                {
                    body.ShotsUnresolved++;
                    _pendingShots.RemoveAt(i);
                    continue;
                }
                Vector3 position = shot.Barrel.position;
                Quaternion rotation = shot.Barrel.rotation;
                if (now < shot.Target)
                {
                    shot.PreviousPosition = position;
                    shot.PreviousRotation = rotation;
                    shot.PreviousTime = now;
                    continue;
                }
                if (shot.PreviousTime < shot.Target && now > shot.PreviousTime)
                {
                    float s = (float)((shot.Target - shot.PreviousTime) / (now - shot.PreviousTime));
                    position = Vector3.Lerp(shot.PreviousPosition, position, s);
                    rotation = Quaternion.Slerp(shot.PreviousRotation, rotation, s);
                }
                float angle = Vector3.Angle(rotation * Vector3.forward, shot.RayRotation * Vector3.forward);
                float offset = (position - shot.RayPosition).magnitude * 1000f;
                body.ShotAngleDeg.Add(angle);
                body.ShotOffsetMm.Add(offset);
                body.ShotResolveWaitMs.Add((Time.unscaledTime - shot.ReceivedAt) * 1000f);
                body.ShotResolveSlackMs.Add((float)((now - shot.Target) * 1000.0));
                if (body.ShotSamples.Count < MaxShotsKept)
                    body.ShotSamples.Add(new object[] { shot.ReceivedUtc, angle, offset, shot.ReceiptAngle, shot.ReceiptOffset });
                RecordShotPose(body, shot, position, rotation, now);
                _pendingShots.RemoveAt(i);
            }
        }

        private void PendingChecks(float now)
        {
            foreach (Body body in _order)
            {
                for (int i = body.PendingWeapons.Count - 1; i >= 0; i--)
                {
                    PendingWeapon pending = body.PendingWeapons[i];
                    if (now - pending.DeathTime < DeathWeaponDelays[pending.Index]) continue;
                    UxrGrabbableObject grip = pending.Grip;
                    bool exists = grip != null;
                    Rigidbody rigidbody = exists ? grip.GetComponent<Rigidbody>() : null;
                    body.DeathWeapons.Add(new Dictionary<string, object>
                    {
                        ["item"] = pending.Name, ["atDeath"] = pending.Where, ["reason"] = pending.Reason,
                        ["afterSeconds"] = now - pending.DeathTime, ["exists"] = exists,
                        ["grabbed"] = exists && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(grip),
                        ["anchor"] = exists && grip.CurrentAnchor != null ? grip.CurrentAnchor.name : null,
                        ["heightAboveDeathRootM"] = exists ? (object)(grip.transform.position.y - pending.RootY) : null,
                        ["rigidbodyDynamic"] = rigidbody != null && !rigidbody.isKinematic
                    });
                    pending.Index++;
                    if (!exists || pending.Index >= DeathWeaponDelays.Length) body.PendingWeapons.RemoveAt(i);
                }
            }
        }

        // ── Задержка позы ────────────────────────────────────────────────────

        /// <summary>
        /// Оценка задержки отображаемой позы относительно «сейчас» сервера: новейший снимок NT (время сервера) − шкала
        /// интерполяции клиента (<c>NetworkTime.time</c>) + RTT/2. Берётся максимум по NT тела (корень/камера/кисти).
        /// </summary>
        private void SamplePoseDelay()
        {
            if (!_connected) return;
            double halfRtt = NetworkTime.rtt * 0.5;
            foreach (Body body in _order)
            {
                if (body.Gone || body.Player == null || body.Transforms == null) continue;
                double newest = double.NegativeInfinity;
                foreach (NetworkTransformBase nt in body.Transforms)
                    if (nt != null && nt.clientSnapshots.Count > 0)
                        newest = Math.Max(newest, nt.clientSnapshots.Keys[nt.clientSnapshots.Count - 1]);
                if (double.IsNegativeInfinity(newest)) continue;
                body.PoseDelayMs.Add((float)((newest - NetworkTime.time + halfRtt) * 1000.0));
            }
        }

        // ── Сеть и консоль ───────────────────────────────────────────────────

        private void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            // Сообщения нового подключения приходят в EarlyUpdate раньше Update наблюдателя — эпоха считается здесь же.
            if (!_connected && NetworkClient.isConnected && !NetworkServer.active) TrackConnection(true);
            _receive.OnMessage(info, _epoch);
            long bytes = (long)info.bytes * info.count;
            _bytesTotal += bytes;
            _messagesTotal += info.count;
            uint netId;
            if (info.message is RpcMessage rpc) netId = rpc.netId;
            else if (info.message is EntityStateMessage state) netId = state.netId;
            else return;
            _bytesByNetId.TryGetValue(netId, out long known);
            _bytesByNetId[netId] = known + bytes;
            _messagesByNetId.TryGetValue(netId, out int messages);
            _messagesByNetId[netId] = messages + info.count;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            bool relay = condition != null && condition.IndexOf("Relay", StringComparison.OrdinalIgnoreCase) >= 0;
            lock (_logLock)
            {
                if (type == LogType.Warning) { if (relay) _relayWarnings++; return; }
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                _errors++;
                if (relay) _relayErrors++;
                if (_errorSamples.Count < MaxErrorSamples) _errorSamples.Add(DateTime.UtcNow.ToString("o") + " " + type + ": " + condition);
            }
        }

        // ── Отчёт ────────────────────────────────────────────────────────────

        private void Write(bool final, string reason)
        {
            _lastWriteReason = reason;
            if (!_everClient || string.IsNullOrEmpty(_reportPath)) return;
            try
            {
                long relayBytes = 0;
                foreach (var pair in _bytesByNetId)
                    if (NetworkClient.spawned.TryGetValue(pair.Key, out NetworkIdentity identity) && identity != null &&
                        identity.GetComponent<NetworkStateRelay>() != null) relayBytes += pair.Value;
                var bodies = new List<object>();
                foreach (Body body in _order) bodies.Add(Describe(body));
                Dictionary<string, object> console;
                lock (_logLock)
                {
                    console = new Dictionary<string, object>
                    {
                        ["errors"] = _errors, ["relayErrors"] = _relayErrors, ["relayWarnings"] = _relayWarnings,
                        ["samples"] = new List<string>(_errorSamples)
                    };
                }
                var report = new Dictionary<string, object>
                {
                    ["schema"] = "bot-stand-net-observer@3", ["runTag"] = _request.RunTag, ["participant"] = _participant,
                    ["clientNoRecoil"] = _request.ClientNoRecoil, ["receiveTraceError"] = _receive.Error,
                    ["pid"] = Process.GetCurrentProcess().Id, ["dataPath"] = Application.dataPath, ["startedUtc"] = _startedUtc,
                    ["updatedUtc"] = DateTime.UtcNow.ToString("o"), ["final"] = final, ["writeReason"] = _lastWriteReason,
                    ["role"] = new Dictionary<string, object>
                    {
                        ["client"] = NetworkClient.active, ["server"] = NetworkServer.active, ["connected"] = NetworkClient.isConnected, ["epoch"] = _epoch
                    },
                    ["network"] = new Dictionary<string, object>
                    {
                        ["inBytesTotal"] = _bytesTotal, ["inMessagesTotal"] = _messagesTotal, ["connectedSeconds"] = _clientSeconds,
                        ["inBytesPerSecondTotal"] = _clientSeconds > 0 ? _bytesTotal / _clientSeconds : 0.0,
                        ["relayRpcBytes"] = relayBytes,
                        ["rttMs"] = Stats(_rttMs), ["bufferTimeMs"] = Stats(_bufferTimeMs), ["clientFrameMs"] = Stats(_frameMs),
                        ["sendIntervalMs"] = NetworkServer.sendInterval * 1000.0,
                        ["timingNote"] = "Пока подключён: NetworkTime.rtt (с → мс), NetworkClient.bufferTime = sendInterval × bufferTimeMultiplier " +
                                         "(динамическая подстройка Mirror по джиттеру снимков). Длинный кадр клона (фоновый редактор MPPM) " +
                                         "раздувает и RTT, и буфер — сравнить с clientFrameMs.",
                        ["note"] = "Байты — входящие RpcMessage/EntityStateMessage по netId (NetworkDiagnostics.InMessageEvent). Ретранслятор " +
                                   "UXR-событий (NetworkStateRelay) общий для всех тел — отдельной строкой."
                    },
                    ["connections"] = _connections,
                    ["botComponentsOnClient"] = new Dictionary<string, object>
                    {
                        ["sceneTypes"] = new List<string>(_globalBotTypes), ["sceneMaxCount"] = _globalBotComponentsMax,
                        ["botDirectorSeen"] = _botDirectorSeen,
                        ["localAvatarTypesExcluded"] = new List<string>(_localAvatarBotTypes),
                        ["note"] = "Пространства имён VrBattlegrounds.Bots* и BlazeAISpace* (фикстура стенда не считается); ожидается 0."
                    },
                    ["duplicateWeaponIdsMax"] = _duplicateWeaponIdsMax,
                    ["localAvatars"] = _localAvatars,
                    ["console"] = console,
                    ["retiredBodies"] = new Dictionary<string, object>
                    {
                        ["count"] = _retiredCount, ["file"] = Path.GetFileName(_retiredPath),
                        ["note"] = "Ушедшие тела с завершёнными замерами — по строке JSON в этом файле (та же схема, что bodies[])."
                    },
                    ["bodies"] = bodies
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                string temp = _reportPath + ".tmp";
                // Периодическая запись — компактная; с отступами только итоговая.
                File.WriteAllText(temp, JsonConvert.SerializeObject(report, final ? Formatting.Indented : Formatting.None),
                    new System.Text.UTF8Encoding(false));
                if (File.Exists(_reportPath)) File.Delete(_reportPath);
                File.Move(temp, _reportPath);
            }
            catch (Exception e) { GameLog.Debug.Warning("[BotStandNetObserver] Отчёт: " + e.Message); }
        }

        private Dictionary<string, object> Describe(Body body)
        {
            long bytes = 0;
            int messages = 0;
            foreach (uint id in new[] { body.AvatarNetId, body.SessionNetId })
            {
                if (_bytesByNetId.TryGetValue(id, out long b)) bytes += b;
                if (_messagesByNetId.TryGetValue(id, out int m)) messages += m;
            }
            float seconds = Mathf.Max(1e-3f, body.LastSeen - body.FirstSeen);
            var directions = new List<string>();
            var multipliers = new List<uint>();
            if (body.Transforms != null)
                foreach (NetworkTransformBase nt in body.Transforms)
                {
                    if (nt == null) continue;
                    directions.Add(nt.syncDirection.ToString());
                    multipliers.Add(nt.sendIntervalMultiplier);
                }
            return new Dictionary<string, object>
            {
                ["kind"] = body.Kind, ["token"] = body.Token, ["name"] = body.Name, ["epoch"] = body.Epoch,
                ["avatarNetId"] = body.AvatarNetId, ["sessionNetId"] = body.SessionNetId,
                ["firstSeenUtc"] = body.FirstSeenUtc, ["lastSeenUtc"] = body.LastSeenUtc, ["seconds"] = seconds, ["gone"] = body.Gone,
                ["frames"] = body.Frames, ["framesWithoutGrabManager"] = body.NotSampledFrames,
                ["network"] = new Dictionary<string, object>
                {
                    ["inBytes"] = bytes, ["inMessages"] = messages, ["inBytesPerSecond"] = bytes / seconds,
                    ["networkTransforms"] = directions.Count, ["syncDirections"] = directions, ["sendIntervalMultipliers"] = multipliers
                },
                ["poseDelayMs"] = Stats(body.PoseDelayMs),
                ["grabberToSnapMm"] = new Dictionary<string, object>
                {
                    ["right"] = Stats(body.RightGapMm), ["left"] = Stats(body.LeftGapMm),
                    ["rightRaw"] = Stats(body.RightGapRawMm), ["leftRaw"] = Stats(body.LeftGapRawMm)
                },
                ["shots"] = body.Shots, ["hitsSeen"] = body.Hits,
                ["shotBarrelToRayDeg"] = Stats(body.ShotAngleDeg), ["shotBarrelToRayOriginMm"] = Stats(body.ShotOffsetMm),
                ["shotAtReceipt"] = new Dictionary<string, object>
                {
                    ["barrelToRayDeg"] = Stats(body.ShotReceiptAngleDeg), ["barrelToRayOriginMm"] = Stats(body.ShotReceiptOffsetMm),
                    ["note"] = "Поза ствола в момент приёма события (старше выстрела на буфер интерполяции) — справочно."
                },
                ["shotResolve"] = new Dictionary<string, object>
                {
                    ["waitMs"] = Stats(body.ShotResolveWaitMs), ["slackMs"] = Stats(body.ShotResolveSlackMs),
                    ["unresolved"] = body.ShotsUnresolved, ["withoutTimestamp"] = body.ShotsWithoutTimestamp,
                    ["note"] = "Основной угол — поза ствола на шкале клиента на серверной метке выстрела (метка батча + сдвиг снимков NT " +
                               "правой кисти), интерполяция между соседними кадрами; waitMs — сколько ждали эту позу после приёма."
                },
                ["ledger"] = new Dictionary<string, object>
                {
                    ["commitShotApplied"] = body.CommitsApplied, ["divergences"] = body.Divergences,
                    ["note"] = "Выстрел у клиента = повтор CommitShotSynced; при форке ревизии учёта фиксация не применяется и снаряд " +
                               "у клиента не выпускается (shots < выстрелов сервера)."
                },
                ["previousAvatarNetId"] = body.PreviousAvatarNetId,
                ["wristOverwrite"] = new Dictionary<string, object>
                {
                    ["afterNtToFinalMm"] = Stats(body.WristNtToFinalMm), ["afterNtToFinalDeg"] = Stats(body.WristNtToFinalDeg),
                    ["afterNtToPreUxrMm"] = Stats(body.WristNtToPreUxrMm), ["preUxrToFinalMm"] = Stats(body.WristPreUxrToFinalMm),
                    ["right"] = DescribeWrist(body.RightWrist), ["left"] = DescribeWrist(body.LeftWrist),
                    ["note"] = "Запястья бота у клиента за кадр: сразу после NT (Update 32000) → начало PostUpdate UXR (после Animator) → " +
                               "после всех стадий. Ненулевое afterNtToFinal = поза из сети перезаписана на клиенте. Агрегаты верхнего " +
                               "уровня (совместимость с attempt-5): afterNtToFinalMm/Deg — max по двум рукам; afterNtToPreUxrMm и " +
                               "preUxrToFinalMm — только ПРАВАЯ рука, мм. Единицы верны: близость preUxrToFinalMm (мм) и afterNtToFinalDeg " +
                               "(°) в attempt-5 — геометрия (граббер ~57 мм от кости кисти: поворот кисти вокруг граббера на θ° сдвигает " +
                               "кость на ≈θ мм) и разные руки в агрегатах, не перепутанные поля. right/left — каждый отрезок по руке, мм и °."
                },
                ["netReceive"] = _receive.Describe(body.Epoch, body.AvatarNetId),
                ["clientNoRecoil"] = body.NoRecoilInstalls,
                ["inventory"] = body.Inventory,
                ["poseSamples"] = body.Grid.Samples,
                ["ntApplied"] = NtReport(body),
                ["shotPoses"] = body.ShotPoses,
                ["shotSamples"] = body.ShotSamples,
                ["duplicates"] = new Dictionary<string, object>
                {
                    ["foreignHolderFrames"] = body.ForeignHolderFrames, ["managerMismatchFrames"] = body.ManagerMismatchFrames,
                    ["sameItemBothHandsSamePointFrames"] = body.DoubleSideFrames
                },
                ["final"] = new Dictionary<string, object> { ["alive"] = body.Alive, ["right"] = body.RightHeld, ["left"] = body.LeftHeld, ["anchored"] = body.Anchored },
                ["snapshots"] = body.Snapshots,
                ["deathWeapons"] = body.DeathWeapons,
                ["botComponents"] = new List<string>(body.BotComponents),
                ["timeline"] = body.Timeline
            };
        }

        private static Dictionary<string, object> DescribeWrist(WristSeries series) => new Dictionary<string, object>
        {
            ["afterNtToFinalMm"] = Stats(series.NtToFinalMm), ["afterNtToFinalDeg"] = Stats(series.NtToFinalDeg),
            ["afterNtToPreUxrMm"] = Stats(series.NtToPreUxrMm), ["afterNtToPreUxrDeg"] = Stats(series.NtToPreUxrDeg),
            ["preUxrToFinalMm"] = Stats(series.PreUxrToFinalMm), ["preUxrToFinalDeg"] = Stats(series.PreUxrToFinalDeg)
        };

        private static Dictionary<string, object> NtReport(Body body)
        {
            var report = new Dictionary<string, object>();
            foreach (var pair in body.NtChecks)
            {
                NtCheck check = pair.Value;
                report[pair.Key] = new Dictionary<string, object>
                {
                    ["syncDirectionOnClient"] = check.SyncDirection, ["coordinateSpace"] = check.CoordinateSpace, ["isOwned"] = check.IsOwned,
                    ["frames"] = check.Frames, ["framesWithoutSnapshots"] = check.FramesWithoutSnapshots, ["lastSnapshotCount"] = check.LastSnapshotCount,
                    ["errorToSnapshotMm"] = Stats(check.ErrorMm), ["errorToSnapshotDeg"] = Stats(check.ErrorDeg), ["motionPerFrameMm"] = Stats(check.MotionMm)
                };
            }
            report["note"] = "Поза цели NT сразу после Update NT против интерполяции буфера снимков на NetworkTime.time. Снимки " +
                             "применяются — ошибка ≈0 при движении (motionPerFrameMm > 0); нет снимков или большая ошибка — NT позу не ставит. " +
                             "syncDirection на клиенте — как в префабе (ServerAuthoredAvatar.Prepare меняет его только на сервере).";
            return report;
        }

        private static void Event(Body body, string kind, string what, object value)
        {
            if (body.Timeline.Count >= MaxTimeline) return;
            body.Timeline.Add(new Dictionary<string, object>
            {
                ["event"] = kind, ["what"] = what, ["value"] = value, ["t"] = Time.unscaledTime - body.FirstSeen, ["utc"] = DateTime.UtcNow.ToString("o")
            });
        }

        private static Dictionary<string, object> Stats(List<float> values)
        {
            var result = new Dictionary<string, object> { ["count"] = values.Count };
            if (values.Count == 0) return result;
            var sorted = new List<float>(values);
            sorted.Sort();
            double sum = 0;
            foreach (float value in sorted) sum += value;
            result["median"] = sorted[sorted.Count / 2];
            result["p95"] = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))];
            result["max"] = sorted[sorted.Count - 1];
            result["min"] = sorted[0];
            result["mean"] = (float)(sum / sorted.Count);
            return result;
        }

        /// <summary>Имя участника MPPM из аргумента <c>-name</c> (главный редактор — "main").</summary>
        private static string ParticipantName()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-name", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return "main";
        }

        private static string Sanitize(string value)
        {
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Mirror;
using Newtonsoft.Json;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Заявка на пробу матча ботов (подэтап cc-0 clip-cutover): куда писать отчёты и до какого времени.</summary>
    [Serializable]
    public sealed class BotStandMatchProbeRequest
    {
        /// <summary>Метка прогона (пакет/прогон) — попадает в отчёт каждого процесса.</summary>
        public string RunTag;
        /// <summary>Абсолютная папка отчётов (вне Assets). Клоны MPPM пишут сюда же.</summary>
        public string OutputDirectory;
        /// <summary>Срок действия заявки (UTC, ISO 8601). Просроченная заявка пробу не создаёт.</summary>
        public string ExpiresUtc;
        /// <summary>Период промежуточной записи отчёта, секунды.</summary>
        public float WriteSeconds = 10f;
    }

    /// <summary>
    /// Включение <see cref="BotStandMatchProbe"/> в процессах мультипроцессного запуска по файлу заявки в <c>Temp</c>
    /// исходного checkout (как <see cref="BotStandNetObserverGate"/>): payload главного редактора пишет её до
    /// <c>PlayLaunch.Play</c> и удаляет после остановки. Без заявки ничего не создаётся.
    /// </summary>
    public static class BotStandMatchProbeGate
    {
        public const string RequestRelativePath = "Temp/BotStandMatchProbe/request.json";

        public static string RequestPath => Path.Combine(BotStandNetObserverGate.ProjectRoot, RequestRelativePath).Replace('\\', '/');

        /// <summary>Проба этого процесса (null — заявки не было).</summary>
        public static BotStandMatchProbe Active { get; internal set; }

        public static string WriteRequest(BotStandMatchProbeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.OutputDirectory) || !Path.IsPathRooted(request.OutputDirectory))
                throw new ArgumentException("Нужна абсолютная папка отчётов пробы матча.");
            string assets = Path.GetFullPath(Path.Combine(BotStandNetObserverGate.ProjectRoot, "Assets")) + Path.DirectorySeparatorChar;
            if ((Path.GetFullPath(request.OutputDirectory) + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Отчёты пробы матча нельзя сохранять в Assets.");
            Directory.CreateDirectory(Path.GetDirectoryName(RequestPath));
            File.WriteAllText(RequestPath, JsonConvert.SerializeObject(request, Formatting.Indented), new System.Text.UTF8Encoding(false));
            return RequestPath;
        }

        public static bool ClearRequest()
        {
            if (!File.Exists(RequestPath)) return false;
            File.Delete(RequestPath);
            return true;
        }

        public static bool TryReadRequest(out BotStandMatchProbeRequest request, out string reason)
        {
            request = null;
            reason = null;
            if (!File.Exists(RequestPath)) { reason = "нет заявки"; return false; }
            try { request = JsonConvert.DeserializeObject<BotStandMatchProbeRequest>(File.ReadAllText(RequestPath)); }
            catch (Exception e) { reason = "заявка не читается: " + e.Message; return false; }
            if (request == null || string.IsNullOrEmpty(request.OutputDirectory) || !Path.IsPathRooted(request.OutputDirectory))
            {
                reason = "в заявке нет абсолютной папки отчётов";
                return false;
            }
            if (!DateTime.TryParse(request.ExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime expires) || expires < DateTime.UtcNow)
            {
                reason = "заявка просрочена";
                return false;
            }
            return true;
        }

        /// <summary>Финальная запись и остановка пробы этого процесса (B6 до Cancel). Путь отчёта или null.</summary>
        public static string FinishActive(string reason)
        {
            BotStandMatchProbe probe = Active;
            if (probe == null) return null;
            probe.Finish(reason);
            return probe.ReportPath;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = null;
            if (!TryReadRequest(out BotStandMatchProbeRequest request, out string _)) return;
            if (UnityEngine.Object.FindAnyObjectByType<BotStandMatchProbe>() != null) return;
            var go = new GameObject("BotStandMatchProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var probe = go.AddComponent<BotStandMatchProbe>();
            probe.Configure(request);
            Active = probe;
            GameLog.Debug.Info("[BotStandMatchProbe] Включена по заявке " + request.RunTag + "; отчёт — " + probe.ReportPath);
        }
    }

    /// <summary>
    /// Проба матча ботов (cc-0 clip-cutover): только чтение состояния игры, поведение не меняет.
    /// <list type="bullet">
    /// <item>Сервер/хост: таймлайн матча (фазы и раунды Elimination, закупки и все денежные операции, выстрелы
    ///       <c>UxrFirearmWeapon.ProjectileShot</c>, попадания <c>UxrWeaponManager.DamageReceived</c>, гибели
    ///       <c>MapReferee.PlayerKilled</c>, тела <c>AvatarManager.AvatarSpawned</c> — живое/призрак, конец карты),
    ///       снимок ботов на каждой смене фазы (команда, жив, деньги, оружие в руках); стоимость кадра по окнам фаз
    ///       (время кадра, «GC Allocated In Frame», сборки GC, маркеры бот-кода через <see cref="ProfilerRecorder"/>);
    ///       исходящие байты Mirror по категориям netId (тела/сессии ботов, людей, ретранслятор).</item>
    /// <item>Чистый клиент: входящие байты по тем же категориям, кадр клиента.</item>
    /// <item>Все процессы: ошибки/исключения консоли, ошибки и предупреждения Relay.</item>
    /// </list>
    /// В кадре — только накопление в предвыделенные списки и счётчики; разовые события (смена фазы, выстрел, гибель)
    /// пишут запись таймлайна. Отчёт — JSON раз в <see cref="BotStandMatchProbeRequest.WriteSeconds"/> и финальный.
    /// </summary>
    public sealed class BotStandMatchProbe : MonoBehaviour
    {
        private const int MaxTimeline = 6000;
        private const int MaxErrorSamples = 24;
        private const int MaxWindows = 600;
        private const int FrameCapacity = 16384;

        private static readonly string[] MarkerNames =
        {
            "BotDirector.Think", "BotRuntimeController.Update", "BotRuntimeController.LateUpdate", "BotAvatarDriver.Apply",
            "BotHands.Tick", "BotWeaponAim.Solve", "BotGunner.Sees", "BotRuntimeController.Shot", "BotRuntimeController.Equipment",
            "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate"
        };

        // Коды окна: фаза Elimination (0..5) или состояние карты.
        private const int LabelNoMap = 6, LabelWarmup = 7, LabelLiveOther = 8, LabelEnded = 9, LabelCount = 10;
        private static readonly string[] LabelNames =
            { "Setup", "Equipment", "Countdown", "Combat", "Resolution", "Scoreboard", "NoMap", "Warmup", "LiveOther", "Ended" };

        // Категории netId для байтов.
        private const int CatBotAvatar = 0, CatBotSession = 1, CatHumanAvatar = 2, CatHumanSession = 3, CatRelay = 4, CatOther = 5, CatCount = 6;
        private static readonly string[] CatNames = { "botAvatar", "botSession", "humanAvatar", "humanSession", "relay", "other" };

        private BotStandMatchProbeRequest _request;
        private string _participant;
        private string _startedUtc;
        private float _start;
        private bool _finished;
        private string _finishReason;
        private float _nextWrite;
        private bool _everServer, _everClient;

        public string ReportPath { get; private set; }

        // ── Кадр и окна ──────────────────────────────────────────────────────
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[MarkerNames.Length];
        private ProfilerRecorder _gcRecorder;
        private readonly List<float>[] _frameByLabel = new List<float>[LabelCount];
        private readonly List<float>[] _gcByLabel = new List<float>[LabelCount];
        private readonly double[][] _markerSumByLabel = new double[LabelCount][];
        private readonly float[][] _markerMaxByLabel = new float[LabelCount][];
        private readonly int[] _gcCollectionsByLabel = new int[LabelCount];
        private readonly List<float> _windowFrames = new List<float>(FrameCapacity);
        private readonly List<float> _windowGc = new List<float>(FrameCapacity);
        private readonly double[] _windowMarkerSum = new double[MarkerNames.Length];
        private readonly float[] _windowMarkerMax = new float[MarkerNames.Length];
        private readonly long[] _windowOutBytes = new long[CatCount];
        private readonly List<Dictionary<string, object>> _windows = new List<Dictionary<string, object>>();
        private int _label = -1, _labelRound = -1;
        private float _windowStart;
        private int _windowGen0;
        private int _windowMaxBots, _windowMinBots = int.MaxValue;

        // ── Таймлайн ─────────────────────────────────────────────────────────
        private readonly List<Dictionary<string, object>> _timeline = new List<Dictionary<string, object>>();
        private int _timelineDropped;
        private MapReferee _referee;
        private GameMode _mode;
        private EliminationMode _elimination;
        private UxrWeaponManager _weaponManager;
        private readonly Dictionary<UxrFirearmWeapon, Action<int>> _shotHandlers = new Dictionary<UxrFirearmWeapon, Action<int>>();
        private readonly Dictionary<string, int[]> _shotsBySession = new Dictionary<string, int[]>(); // [выстрелы, попадания, урон×10]
        private int _shotsBots, _shotsHumans, _shotsUnknown, _hitsByBots, _hitsOnBots, _kills;
        private readonly Dictionary<string, object> _matchEnd = new Dictionary<string, object>();

        // ── Сеть ─────────────────────────────────────────────────────────────
        private readonly Dictionary<uint, int> _category = new Dictionary<uint, int>();
        private readonly long[] _outBytes = new long[CatCount];
        private readonly long[] _outMessages = new long[CatCount];
        private readonly long[] _outBytesByLabel = new long[LabelCount * CatCount];
        private readonly long[] _inBytes = new long[CatCount];
        private readonly long[] _inMessages = new long[CatCount];
        private readonly Dictionary<uint, long> _bytesByNetId = new Dictionary<uint, long>();
        private readonly HashSet<uint> _botAvatarIds = new HashSet<uint>();
        private readonly HashSet<uint> _humanAvatarIds = new HashSet<uint>();
        private long _outBytesTotal, _inBytesTotal;
        private float _serverSeconds, _clientSeconds;
        private float _connectionSecondsSum;
        private bool _netSubscribed;

        // ── Консоль ──────────────────────────────────────────────────────────
        private readonly object _logLock = new object();
        private int _errors, _exceptions, _asserts, _relayErrors, _relayWarnings, _warnings;
        private readonly List<string> _errorSamples = new List<string>();

        public void Configure(BotStandMatchProbeRequest request)
        {
            _request = request;
            _participant = ParticipantName();
            _startedUtc = DateTime.UtcNow.ToString("o");
            _start = Time.realtimeSinceStartup;
            ReportPath = Path.Combine(request.OutputDirectory, Sanitize(_participant) + "-pid" + Process.GetCurrentProcess().Id + ".json");
            for (int i = 0; i < LabelCount; i++)
            {
                _frameByLabel[i] = new List<float>(i == 3 ? FrameCapacity : 2048);
                _gcByLabel[i] = new List<float>(i == 3 ? FrameCapacity : 2048);
                _markerSumByLabel[i] = new double[MarkerNames.Length];
                _markerMaxByLabel[i] = new float[MarkerNames.Length];
            }
        }

        private float Now => Time.realtimeSinceStartup - _start;

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += OnQuitting;
            for (int i = 0; i < MarkerNames.Length; i++) _recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerNames[i]);
            _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            EliminationMode.RoundPhaseChangedServer += OnPhaseServer;
            MatchEconomy.TransactionServer += OnTransaction;
            MatchEconomy.MoneyChangedLocal += OnMoneyChanged;
            AvatarManager.AvatarSpawned += OnAvatarSpawned;
            PlayersManager.SessionConnected += OnSessionConnected;
            PlayersManager.SessionDisconnected += OnSessionDisconnected;
            MapLoader.MapLoadStarted += OnMapLoadStarted;
            MapLoader.MapLoadCompleted += OnMapLoadCompleted;
            GameMode.ModeStartedLocal += OnModeStarted;
            GameMode.ModeFinishedLocal += OnModeFinished;
            UxrComponent.GlobalEnabled += OnUxrComponentEnabled;
        }

        private void OnDisable()
        {
            if (!_finished) Finish("disable");
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= OnQuitting;
            Teardown();
        }

        private void OnQuitting()
        {
            if (!_finished) Finish("quit");
        }

        private void Teardown()
        {
            EliminationMode.RoundPhaseChangedServer -= OnPhaseServer;
            MatchEconomy.TransactionServer -= OnTransaction;
            MatchEconomy.MoneyChangedLocal -= OnMoneyChanged;
            AvatarManager.AvatarSpawned -= OnAvatarSpawned;
            PlayersManager.SessionConnected -= OnSessionConnected;
            PlayersManager.SessionDisconnected -= OnSessionDisconnected;
            MapLoader.MapLoadStarted -= OnMapLoadStarted;
            MapLoader.MapLoadCompleted -= OnMapLoadCompleted;
            GameMode.ModeStartedLocal -= OnModeStarted;
            GameMode.ModeFinishedLocal -= OnModeFinished;
            UxrComponent.GlobalEnabled -= OnUxrComponentEnabled;
            if (_netSubscribed)
            {
                NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
                NetworkDiagnostics.InMessageEvent -= OnInMessage;
                _netSubscribed = false;
            }
            BindReferee(null);
            BindMode(null);
            if (_weaponManager != null) { _weaponManager.DamageReceived -= OnDamage; _weaponManager = null; }
            foreach (var pair in _shotHandlers) if (pair.Key != null) pair.Key.ProjectileShot -= pair.Value;
            _shotHandlers.Clear();
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
            _gcRecorder.Dispose();
        }

        /// <summary>Закрыть окно, записать финальный отчёт и перестать накапливать (B6 — до Cancel).</summary>
        public void Finish(string reason)
        {
            if (_finished) return;
            CloseWindow();
            _finishReason = reason;
            _finished = true;
            Write(true, reason);
        }

        private void Update()
        {
            if (_finished || _request == null) return;
            bool server = NetworkServer.active;
            bool clientOnly = NetworkClient.active && !server;
            if (server) _everServer = true;
            if (clientOnly) _everClient = true;
            if ((server || clientOnly) && !_netSubscribed)
            {
                NetworkDiagnostics.OutMessageEvent += OnOutMessage;
                NetworkDiagnostics.InMessageEvent += OnInMessage;
                _netSubscribed = true;
                ScanFirearms();
            }

            float dt = Time.unscaledDeltaTime;
            if (server)
            {
                _serverSeconds += dt;
                _connectionSecondsSum += NetworkServer.connections.Count * dt;
                if (_weaponManager == null && UxrWeaponManager.HasInstance)
                {
                    _weaponManager = UxrWeaponManager.Instance;
                    _weaponManager.DamageReceived += OnDamage;
                }
                if (MapReferee.Instance != _referee) BindReferee(MapReferee.Instance);
                GameMode mode = _referee != null ? _referee.ActiveGameMode : null;
                if (mode != _mode) BindMode(mode);
            }
            else if (clientOnly && NetworkClient.isConnected) _clientSeconds += dt;

            // Окно кадра: фаза/раунд (сервер) или «нет карты»/клиент.
            int label = CurrentLabel(out int round);
            if (label != _label || round != _labelRound) { CloseWindow(); OpenWindow(label, round); }

            float frameMs = dt * 1000f;
            float gc = _gcRecorder.Valid ? _gcRecorder.LastValue : 0f;
            _windowFrames.Add(frameMs);
            _windowGc.Add(gc);
            _frameByLabel[label].Add(frameMs);
            _gcByLabel[label].Add(gc);
            for (int i = 0; i < _recorders.Length; i++)
            {
                if (!_recorders[i].Valid) continue;
                float ms = _recorders[i].LastValue * 1e-6f;
                _windowMarkerSum[i] += ms;
                if (ms > _windowMarkerMax[i]) _windowMarkerMax[i] = ms;
                _markerSumByLabel[label][i] += ms;
                if (ms > _markerMaxByLabel[label][i]) _markerMaxByLabel[label][i] = ms;
            }
            if (server)
            {
                int bots = BotDirector.Instance != null ? BotDirector.Instance.Bots.Count : 0;
                if (bots > _windowMaxBots) _windowMaxBots = bots;
                if (bots < _windowMinBots) _windowMinBots = bots;
            }

            float now = Time.unscaledTime;
            if (now >= _nextWrite)
            {
                _nextWrite = now + Mathf.Max(2f, _request.WriteSeconds);
                Write(false, "periodic");
            }
        }

        private int CurrentLabel(out int round)
        {
            round = -1;
            if (!NetworkServer.active) return LabelNoMap;
            if (_referee == null) return LabelNoMap;
            if (_matchEnd.Count > 0 && !_referee.IsLiveOrPaused) return LabelEnded;
            if (!_referee.IsLiveOrPaused) return LabelWarmup;
            if (_elimination == null) return LabelLiveOther;
            round = _elimination.CurrentRoundNumber;
            int phase = (int)_elimination.CurrentRoundPhase;
            return phase >= 0 && phase < 6 ? phase : LabelLiveOther;
        }

        private void OpenWindow(int label, int round)
        {
            _label = label;
            _labelRound = round;
            _windowStart = Now;
            _windowGen0 = GC.CollectionCount(0);
            _windowFrames.Clear();
            _windowGc.Clear();
            Array.Clear(_windowMarkerSum, 0, _windowMarkerSum.Length);
            Array.Clear(_windowMarkerMax, 0, _windowMarkerMax.Length);
            Array.Clear(_windowOutBytes, 0, _windowOutBytes.Length);
            _windowMaxBots = 0;
            _windowMinBots = int.MaxValue;
        }

        private void CloseWindow()
        {
            if (_label < 0) return;
            int collections = GC.CollectionCount(0) - _windowGen0;
            _gcCollectionsByLabel[_label] += collections;
            if (_windowFrames.Count == 0) return;
            if (_windows.Count >= MaxWindows) return;
            float seconds = Mathf.Max(1e-3f, Now - _windowStart);
            var markers = new Dictionary<string, object>();
            int frames = _windowFrames.Count;
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                if (!_recorders[i].Valid) continue;
                markers[MarkerNames[i]] = new Dictionary<string, object>
                {
                    ["meanMs"] = Round3(_windowMarkerSum[i] / frames), ["maxMs"] = Round3(_windowMarkerMax[i])
                };
            }
            var outBytes = new Dictionary<string, object>();
            for (int c = 0; c < CatCount; c++) outBytes[CatNames[c]] = _windowOutBytes[c];
            double gcSum = 0;
            foreach (float v in _windowGc) gcSum += v;
            _windows.Add(new Dictionary<string, object>
            {
                ["label"] = LabelNames[_label], ["round"] = _labelRound, ["t0"] = Round3(_windowStart), ["t1"] = Round3(Now),
                ["seconds"] = Round3(seconds), ["frames"] = frames, ["frameMs"] = Stats(_windowFrames), ["gcBytesPerFrame"] = Stats(_windowGc),
                ["gcBytesTotal"] = (long)gcSum, ["gcCollections"] = collections,
                ["bots"] = new Dictionary<string, object> { ["min"] = _windowMinBots == int.MaxValue ? 0 : _windowMinBots, ["max"] = _windowMaxBots },
                ["markers"] = markers, ["outBytes"] = outBytes
            });
        }

        // ── Привязка к судье и режиму ────────────────────────────────────────

        private void BindReferee(MapReferee referee)
        {
            if (_referee != null)
            {
                _referee.PlayerKilled -= OnKilled;
                _referee.Finished -= OnRefereeFinished;
                _referee.RoundWon -= OnRoundWon;
            }
            _referee = referee;
            if (_referee == null) return;
            _referee.PlayerKilled += OnKilled;
            _referee.Finished += OnRefereeFinished;
            _referee.RoundWon += OnRoundWon;
            AddEvent("referee", new Dictionary<string, object> { ["scene"] = _referee.SceneName, ["state"] = _referee.CurrentState.ToString() });
            ScanFirearms();
        }

        private void BindMode(GameMode mode)
        {
            if (_mode != null) _mode.Finished -= OnModeInstanceFinished;
            if (_elimination != null)
            {
                _elimination.RoundBeganServer -= OnRoundBegan;
                _elimination.RoundScoredServer -= OnRoundScored;
            }
            _mode = mode;
            _elimination = mode as EliminationMode;
            if (_mode == null) return;
            _mode.Finished += OnModeInstanceFinished;
            if (_elimination != null)
            {
                _elimination.RoundBeganServer += OnRoundBegan;
                _elimination.RoundScoredServer += OnRoundScored;
            }
            var teams = new List<object>();
            if (_mode.Teams != null)
                foreach (TeamData team in _mode.Teams)
                    if (team != null) teams.Add(new Dictionary<string, object> { ["index"] = team.teamIndex, ["name"] = team.displayName });
            var data = new Dictionary<string, object>
            {
                ["type"] = _mode.GetType().Name, ["teams"] = teams, ["teamAssignment"] = _mode.TeamAssignment.ToString()
            };
            if (_elimination != null)
            {
                data["roundsPerHalf"] = _elimination.RoundsPerHalf;
                data["roundsToWin"] = _elimination.RoundsToWin;
            }
            AddEvent("mode", data);
            data["sessions"] = Roster();
        }

        // ── События матча (сервер) ───────────────────────────────────────────

        private void OnPhaseServer(RoundPhase phase)
        {
            if (_finished) return;
            var data = new Dictionary<string, object>
            {
                ["phase"] = phase.ToString(), ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["scores"] = Scores(), ["sessions"] = Roster()
            };
            AddEvent("phase", data);
        }

        private void OnRoundBegan(int round, bool firstOfHalf) =>
            AddEvent("roundBegan", new Dictionary<string, object> { ["round"] = round, ["firstOfHalf"] = firstOfHalf });

        private void OnRoundScored(TeamData winner) =>
            AddEvent("roundScored", new Dictionary<string, object>
            {
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["winner"] = winner != null ? winner.teamIndex : 0, ["scores"] = Scores()
            });

        private void OnRoundWon(TeamData winner) =>
            AddEvent("roundWon", new Dictionary<string, object> { ["winner"] = winner != null ? winner.teamIndex : 0 });

        private void OnModeInstanceFinished(TeamData winner) =>
            AddEvent("modeFinished", new Dictionary<string, object> { ["winner"] = winner != null ? winner.teamIndex : 0, ["scores"] = Scores() });

        private void OnRefereeFinished(TeamData winner)
        {
            _matchEnd["t"] = Round3(Now);
            _matchEnd["winner"] = winner != null ? winner.teamIndex : 0;
            _matchEnd["scores"] = Scores();
            _matchEnd["rounds"] = _elimination != null ? _elimination.CurrentRoundNumber : -1;
            AddEvent("matchFinished", new Dictionary<string, object>(_matchEnd));
        }

        private void OnModeStarted() { if (NetworkServer.active) AddEvent("modeStartedLocal", null); }

        private void OnModeFinished(TeamData winner)
        {
            if (NetworkServer.active) AddEvent("modeFinishedLocal", new Dictionary<string, object> { ["winner"] = winner != null ? winner.teamIndex : 0 });
        }

        private void OnMapLoadStarted(string scene) => AddEvent("mapLoadStarted", new Dictionary<string, object> { ["scene"] = scene });

        private void OnMapLoadCompleted(string scene)
        {
            AddEvent("mapLoadCompleted", new Dictionary<string, object> { ["scene"] = scene });
            ScanFirearms();
        }

        private void OnSessionConnected(PlayerSession session)
        {
            if (!NetworkServer.active || session == null) return;
            AddEvent("sessionConnected", Describe(session));
        }

        private void OnSessionDisconnected(PlayerSession session)
        {
            if (!NetworkServer.active || session == null) return;
            AddEvent("sessionDisconnected", Describe(session));
        }

        private void OnTransaction(EconomyTransaction tx)
        {
            var data = new Dictionary<string, object>
            {
                ["player"] = tx.Player, ["delta"] = tx.Delta, ["money"] = tx.Money, ["reason"] = tx.Reason.ToString(), ["detail"] = tx.Detail,
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["phase"] = _elimination != null ? _elimination.CurrentRoundPhase.ToString() : null
            };
            if (tx.Reason == EconomyReason.Purchase)
            {
                WeaponRegistry registry = WeaponRegistry.Instance;
                WeaponInfo info = registry != null && !string.IsNullOrEmpty(tx.Detail) ? registry.GetByName(tx.Detail) : null;
                data["price"] = info != null ? info.Price : -1;
                data["category"] = info != null ? info.Category.ToString() : null;
            }
            AddEvent("money", data);
        }

        private void OnMoneyChanged(string player, int money)
        {
            if (!NetworkServer.active) return;
            AddEvent("balance", new Dictionary<string, object> { ["player"] = player, ["money"] = money });
        }

        private void OnAvatarSpawned(PlayerController avatar)
        {
            if (!NetworkServer.active || avatar == null) return;
            PlayerSession session = avatar.Session;
            bool bot = session != null && BotDirector.IsBotToken(session.DeviceToken);
            uint netId = avatar.netId;
            if (netId != 0)
            {
                _category[netId] = bot ? CatBotAvatar : CatHumanAvatar;
                (bot ? _botAvatarIds : _humanAvatarIds).Add(netId);
            }
            AddEvent("body", new Dictionary<string, object>
            {
                ["session"] = session != null ? session.PlayerName : null, ["bot"] = bot, ["netId"] = netId,
                ["ghost"] = session != null && session.IsEliminated, ["alive"] = avatar.IsAlive, ["team"] = session != null ? session.TeamIndex : 0,
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["phase"] = _elimination != null ? _elimination.CurrentRoundPhase.ToString() : null
            });
            ScanFirearms();
        }

        private void OnKilled(PlayerSession victim, PlayerSession killer, IReadOnlyList<PlayerSession> assists)
        {
            _kills++;
            AddEvent("kill", new Dictionary<string, object>
            {
                ["victim"] = victim != null ? victim.PlayerName : null, ["victimBot"] = victim != null && BotDirector.IsBotToken(victim.DeviceToken),
                ["victimTeam"] = victim != null ? victim.TeamIndex : 0,
                ["killer"] = killer != null ? killer.PlayerName : null, ["killerBot"] = killer != null && BotDirector.IsBotToken(killer.DeviceToken),
                ["killerTeam"] = killer != null ? killer.TeamIndex : 0, ["assists"] = assists != null ? assists.Count : 0,
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["phase"] = _elimination != null ? _elimination.CurrentRoundPhase.ToString() : null
            });
        }

        // ── Выстрелы и попадания (сервер) ────────────────────────────────────

        private void OnUxrComponentEnabled(UxrComponent component)
        {
            if (component is UxrFirearmWeapon firearm) Track(firearm);
        }

        private void ScanFirearms()
        {
            foreach (UxrFirearmWeapon firearm in FindObjectsByType<UxrFirearmWeapon>(FindObjectsInactive.Include)) Track(firearm);
        }

        private void Track(UxrFirearmWeapon firearm)
        {
            if (firearm == null || _shotHandlers.ContainsKey(firearm)) return;
            Action<int> handler = trigger => OnShot(firearm);
            _shotHandlers[firearm] = handler;
            firearm.ProjectileShot += handler;
        }

        private void OnShot(UxrFirearmWeapon firearm)
        {
            if (_finished || !NetworkServer.active || firearm == null) return;
            PlayerSession shooter = SessionOf(firearm.Owner);
            bool bot = shooter != null && BotDirector.IsBotToken(shooter.DeviceToken);
            if (shooter == null) _shotsUnknown++;
            else if (bot) _shotsBots++;
            else _shotsHumans++;
            int[] row = Row(shooter != null ? shooter.PlayerName : "?");
            row[0]++;
            AddEvent("shot", new Dictionary<string, object>
            {
                ["shooter"] = shooter != null ? shooter.PlayerName : null, ["bot"] = bot, ["weapon"] = firearm.name,
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["phase"] = _elimination != null ? _elimination.CurrentRoundPhase.ToString() : null
            });
        }

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            if (_finished || !NetworkServer.active || e == null) return;
            PlayerSession source = SessionOf(e.ActorSource);
            PlayerSession target = SessionOf(e.ActorTarget);
            bool sourceBot = source != null && BotDirector.IsBotToken(source.DeviceToken);
            bool targetBot = target != null && BotDirector.IsBotToken(target.DeviceToken);
            if (sourceBot) _hitsByBots++;
            if (targetBot) _hitsOnBots++;
            if (source != null)
            {
                int[] row = Row(source.PlayerName);
                row[1]++;
                row[2] += Mathf.RoundToInt(e.Damage * 10f);
            }
            AddEvent("hit", new Dictionary<string, object>
            {
                ["source"] = source != null ? source.PlayerName : null, ["sourceBot"] = sourceBot,
                ["target"] = target != null ? target.PlayerName : null, ["targetBot"] = targetBot,
                ["damage"] = Round3(e.Damage), ["dies"] = e.Dies, ["type"] = e.DamageType.ToString(),
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1
            });
        }

        private int[] Row(string name)
        {
            if (!_shotsBySession.TryGetValue(name, out int[] row)) { row = new int[3]; _shotsBySession[name] = row; }
            return row;
        }

        private static PlayerSession SessionOf(UxrActor actor)
        {
            if (actor == null) return null;
            PlayerController controller = actor.GetComponentInParent<PlayerController>();
            return controller != null ? controller.Session : null;
        }

        // ── Сеть ─────────────────────────────────────────────────────────────

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            if (_finished || !NetworkServer.active) return;
            long bytes = (long)info.bytes * info.count;
            _outBytesTotal += bytes;
            int category = CatOther;
            uint netId = 0;
            if (info.message is RpcMessage rpc) netId = rpc.netId;
            else if (info.message is EntityStateMessage state) netId = state.netId;
            if (netId != 0) category = Categorize(netId, true);
            if (category != CatOther && category != CatRelay) AddNetIdBytes(netId, bytes);
            _outBytes[category] += bytes;
            _outMessages[category] += info.count;
            _windowOutBytes[category] += bytes;
            if (_label >= 0) _outBytesByLabel[_label * CatCount + category] += bytes;
        }

        private void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            if (_finished || NetworkServer.active) return;
            long bytes = (long)info.bytes * info.count;
            _inBytesTotal += bytes;
            int category = CatOther;
            uint netId = 0;
            if (info.message is RpcMessage rpc) netId = rpc.netId;
            else if (info.message is EntityStateMessage state) netId = state.netId;
            if (netId != 0) category = Categorize(netId, false);
            if (category != CatOther && category != CatRelay) AddNetIdBytes(netId, bytes);
            _inBytes[category] += bytes;
            _inMessages[category] += info.count;
        }

        private void AddNetIdBytes(uint netId, long bytes)
        {
            _bytesByNetId.TryGetValue(netId, out long known);
            _bytesByNetId[netId] = known + bytes;
        }

        private List<object> NetIdBytes()
        {
            var list = new List<object>();
            foreach (var pair in _bytesByNetId)
            {
                _category.TryGetValue(pair.Key, out int category);
                list.Add(new Dictionary<string, object> { ["netId"] = pair.Key, ["category"] = CatNames[category], ["bytes"] = pair.Value });
            }
            return list;
        }

        /// <summary>Категория netId; вычисляется один раз (GetComponent только при первом сообщении объекта).</summary>
        private int Categorize(uint netId, bool server)
        {
            if (_category.TryGetValue(netId, out int known)) return known;
            NetworkIdentity identity;
            bool found = server ? NetworkServer.spawned.TryGetValue(netId, out identity) : NetworkClient.spawned.TryGetValue(netId, out identity);
            if (!found || identity == null) return CatOther;
            int category = CatOther;
            PlayerController avatar = identity.GetComponent<PlayerController>();
            if (avatar != null)
            {
                PlayerSession owner = avatar.Session;
                if (owner == null || string.IsNullOrEmpty(owner.DeviceToken)) return CatOther; // сессия ещё не разрешена — без кеша
                category = BotDirector.IsBotToken(owner.DeviceToken) ? CatBotAvatar : CatHumanAvatar;
                (category == CatBotAvatar ? _botAvatarIds : _humanAvatarIds).Add(netId);
            }
            else
            {
                PlayerSession session = identity.GetComponent<PlayerSession>();
                if (session != null)
                {
                    if (string.IsNullOrEmpty(session.DeviceToken)) return CatOther;
                    category = BotDirector.IsBotToken(session.DeviceToken) ? CatBotSession : CatHumanSession;
                }
                else if (identity.GetComponent<NetworkStateRelay>() != null) category = CatRelay;
            }
            _category[netId] = category;
            return category;
        }

        // ── Консоль ──────────────────────────────────────────────────────────

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            bool relay = condition != null && condition.IndexOf("Relay", StringComparison.OrdinalIgnoreCase) >= 0;
            lock (_logLock)
            {
                if (type == LogType.Warning)
                {
                    _warnings++;
                    if (relay) _relayWarnings++;
                    return;
                }
                if (type == LogType.Log) return;
                if (type == LogType.Exception) _exceptions++;
                else if (type == LogType.Assert) _asserts++;
                else _errors++;
                if (relay) _relayErrors++;
                if (_errorSamples.Count < MaxErrorSamples)
                {
                    string text = condition ?? "";
                    if (text.Length > 400) text = text.Substring(0, 400);
                    _errorSamples.Add(Round3(Now) + " " + type + ": " + text);
                }
            }
        }

        // ── Снимки ───────────────────────────────────────────────────────────

        private void AddEvent(string kind, Dictionary<string, object> data)
        {
            if (_finished && kind != "matchFinished") return;
            if (_timeline.Count >= MaxTimeline) { _timelineDropped++; return; }
            var entry = data ?? new Dictionary<string, object>();
            entry["t"] = Round3(Now);
            entry["kind"] = kind;
            _timeline.Add(entry);
        }

        private Dictionary<string, object> Scores()
        {
            var scores = new Dictionary<string, object>();
            if (_mode == null || _mode.Teams == null) return scores;
            foreach (TeamData team in _mode.Teams)
                if (team != null) scores[team.teamIndex.ToString(CultureInfo.InvariantCulture)] = _mode.GetScore(team);
            return scores;
        }

        private List<object> Roster()
        {
            var list = new List<object>();
            PlayersManager players = PlayersManager.Instance;
            if (players == null) return list;
            foreach (PlayerSession session in players.Sessions)
                if (session != null) list.Add(Describe(session));
            return list;
        }

        private Dictionary<string, object> Describe(PlayerSession session)
        {
            bool bot = BotDirector.IsBotToken(session.DeviceToken);
            PlayerController avatar = session.ActiveAvatar;
            MatchEconomy economy = MatchEconomy.Current;
            bool inModeTeam = false;
            if (_mode != null && _mode.Teams != null)
                foreach (TeamData team in _mode.Teams) if (team != null && team.teamIndex == session.TeamIndex) inModeTeam = true;
            var data = new Dictionary<string, object>
            {
                ["name"] = session.PlayerName, ["bot"] = bot, ["role"] = session.Role.ToString(), ["team"] = session.TeamIndex,
                ["inModeTeam"] = inModeTeam, ["eliminated"] = session.IsEliminated, ["ready"] = session.ReadyState,
                ["inZone"] = session.IsInSpawnZone, ["avatarNetId"] = session.ActiveAvatarNetId,
                ["alive"] = avatar != null && avatar.IsAlive
            };
            if (economy != null && economy.TryGetMoney(session, out int money)) data["money"] = money;
            if (avatar != null && UxrGrabManager.HasInstance)
            {
                UxrAvatar uxr = avatar.GetComponent<UxrAvatar>();
                if (uxr != null)
                {
                    data["right"] = UxrGrabManager.Instance.GetObjectBeingGrabbed(uxr, UxrHandSide.Right, out UxrGrabbableObject right) && right != null ? right.name : null;
                    data["left"] = UxrGrabManager.Instance.GetObjectBeingGrabbed(uxr, UxrHandSide.Left, out UxrGrabbableObject left) && left != null ? left.name : null;
                }
            }
            return data;
        }

        // ── Отчёт ────────────────────────────────────────────────────────────

        private void Write(bool final, string reason)
        {
            if (string.IsNullOrEmpty(ReportPath) || (!_everServer && !_everClient)) return;
            try
            {
                var report = new Dictionary<string, object>
                {
                    ["schema"] = "bot-stand-match-probe@1", ["runTag"] = _request.RunTag, ["participant"] = _participant,
                    ["pid"] = Process.GetCurrentProcess().Id, ["startedUtc"] = _startedUtc, ["updatedUtc"] = DateTime.UtcNow.ToString("o"),
                    ["final"] = final, ["writeReason"] = reason, ["finishReason"] = _finishReason, ["elapsedSeconds"] = Round3(Now),
                    ["role"] = new Dictionary<string, object>
                    {
                        ["everServer"] = _everServer, ["everClient"] = _everClient, ["server"] = NetworkServer.active,
                        ["client"] = NetworkClient.active, ["connected"] = NetworkClient.isConnected
                    },
                    ["frame"] = FrameReport(),
                    ["console"] = ConsoleReport()
                };
                if (_everServer)
                {
                    report["match"] = MatchReport();
                    report["serverNetwork"] = ServerNetworkReport();
                }
                if (_everClient) report["clientNetwork"] = ClientNetworkReport();
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                string temp = ReportPath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(report, Formatting.Indented), new System.Text.UTF8Encoding(false));
                if (File.Exists(ReportPath)) File.Delete(ReportPath);
                File.Move(temp, ReportPath);
            }
            catch (Exception e) { GameLog.Debug.Warning("[BotStandMatchProbe] отчёт не записан: " + e.Message); }
        }

        private Dictionary<string, object> FrameReport()
        {
            var byLabel = new Dictionary<string, object>();
            for (int l = 0; l < LabelCount; l++)
            {
                if (_frameByLabel[l].Count == 0) continue;
                int frames = _frameByLabel[l].Count;
                var markers = new Dictionary<string, object>();
                for (int i = 0; i < MarkerNames.Length; i++)
                {
                    if (!_recorders[i].Valid) continue;
                    markers[MarkerNames[i]] = new Dictionary<string, object>
                    {
                        ["meanMs"] = Round3(_markerSumByLabel[l][i] / frames), ["maxMs"] = Round3(_markerMaxByLabel[l][i])
                    };
                }
                double gcSum = 0;
                foreach (float v in _gcByLabel[l]) gcSum += v;
                byLabel[LabelNames[l]] = new Dictionary<string, object>
                {
                    ["frames"] = frames, ["frameMs"] = Stats(_frameByLabel[l]), ["gcBytesPerFrame"] = Stats(_gcByLabel[l]),
                    ["gcBytesTotal"] = (long)gcSum, ["gcCollections"] = _gcCollectionsByLabel[l], ["markers"] = markers
                };
            }
            var valid = new Dictionary<string, object>();
            for (int i = 0; i < MarkerNames.Length; i++) valid[MarkerNames[i]] = _recorders[i].Valid;
            return new Dictionary<string, object>
            {
                ["note"] = "frameMs — Time.unscaledDeltaTime процесса редактора (включая отрисовку редактора); gcBytesPerFrame — " +
                           "ProfilerRecorder «GC Allocated In Frame»; gcCollections — прирост GC.CollectionCount(0) за окна метки; " +
                           "маркеры — LastValue ProfilerRecorder (нс→мс) каждого кадра, вложенность не вычитается.",
                ["gcRecorderValid"] = _gcRecorder.Valid, ["markerValid"] = valid, ["byLabel"] = byLabel, ["windows"] = _windows
            };
        }

        private Dictionary<string, object> ConsoleReport()
        {
            lock (_logLock)
            {
                return new Dictionary<string, object>
                {
                    ["errors"] = _errors, ["exceptions"] = _exceptions, ["asserts"] = _asserts, ["warnings"] = _warnings,
                    ["relayErrors"] = _relayErrors, ["relayWarnings"] = _relayWarnings, ["samples"] = new List<string>(_errorSamples)
                };
            }
        }

        private Dictionary<string, object> MatchReport()
        {
            var weapons = new List<object>();
            WeaponRegistry registry = WeaponRegistry.Instance;
            if (registry != null)
                foreach (WeaponInfo info in registry.Weapons)
                    if (info != null)
                        weapons.Add(new Dictionary<string, object>
                        {
                            ["name"] = info.DisplayName, ["price"] = info.Price, ["category"] = info.Category.ToString(),
                            ["firearm"] = info.WeaponPrefab != null && info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() != null
                        });
            var shots = new Dictionary<string, object>();
            foreach (var pair in _shotsBySession)
                shots[pair.Key] = new Dictionary<string, object> { ["shots"] = pair.Value[0], ["hits"] = pair.Value[1], ["damage"] = pair.Value[2] / 10f };
            return new Dictionary<string, object>
            {
                ["bots"] = BotDirector.Instance != null ? BotDirector.Instance.Bots.Count : 0,
                ["scene"] = _referee != null ? _referee.SceneName : null,
                ["mapState"] = _referee != null ? _referee.CurrentState.ToString() : null,
                ["round"] = _elimination != null ? _elimination.CurrentRoundNumber : -1,
                ["phase"] = _elimination != null ? _elimination.CurrentRoundPhase.ToString() : null,
                ["scores"] = Scores(), ["matchEnd"] = new Dictionary<string, object>(_matchEnd),
                ["shotsBots"] = _shotsBots, ["shotsHumans"] = _shotsHumans, ["shotsUnknown"] = _shotsUnknown,
                ["hitsByBots"] = _hitsByBots, ["hitsOnBots"] = _hitsOnBots, ["kills"] = _kills, ["firearmsTracked"] = _shotHandlers.Count,
                ["bySession"] = shots, ["weaponCatalog"] = weapons, ["sessions"] = Roster(),
                ["timelineDropped"] = _timelineDropped, ["timeline"] = _timeline
            };
        }

        private Dictionary<string, object> ServerNetworkReport()
        {
            var bytes = new Dictionary<string, object>();
            for (int c = 0; c < CatCount; c++) bytes[CatNames[c]] = new Dictionary<string, object> { ["bytes"] = _outBytes[c], ["messages"] = _outMessages[c] };
            var byLabel = new Dictionary<string, object>();
            for (int l = 0; l < LabelCount; l++)
            {
                var row = new Dictionary<string, object>();
                bool any = false;
                for (int c = 0; c < CatCount; c++) { long v = _outBytesByLabel[l * CatCount + c]; if (v != 0) any = true; row[CatNames[c]] = v; }
                if (any) byLabel[LabelNames[l]] = row;
            }
            return new Dictionary<string, object>
            {
                ["note"] = "Исход сервера: NetworkDiagnostics.OutMessageEvent, bytes × count (число получателей, у хоста — включая локальное " +
                           "соединение). Категория по netId RpcMessage/EntityStateMessage; прочие сообщения — other.",
                ["serverSeconds"] = Round3(_serverSeconds), ["meanConnections"] = _serverSeconds > 0 ? Round3(_connectionSecondsSum / _serverSeconds) : 0,
                ["connectionsNow"] = NetworkServer.active ? NetworkServer.connections.Count : 0,
                ["sendIntervalMs"] = Round3(NetworkServer.sendInterval * 1000.0), ["outBytesTotal"] = _outBytesTotal,
                ["byCategory"] = bytes, ["byLabel"] = byLabel, ["botAvatars"] = _botAvatarIds.Count, ["humanAvatars"] = _humanAvatarIds.Count,
                ["byNetId"] = NetIdBytes()
            };
        }

        private Dictionary<string, object> ClientNetworkReport()
        {
            var bytes = new Dictionary<string, object>();
            for (int c = 0; c < CatCount; c++) bytes[CatNames[c]] = new Dictionary<string, object> { ["bytes"] = _inBytes[c], ["messages"] = _inMessages[c] };
            uint own = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0;
            PlayerSession local = PlayerSession.LocalSession;
            return new Dictionary<string, object>
            {
                ["note"] = "Вход клиента: NetworkDiagnostics.InMessageEvent по netId. humanAvatar включает свой аватар (его состояние тоже " +
                           "приходит от сервера); удалённые люди — humanAvatarIds без своего.",
                ["connectedSeconds"] = Round3(_clientSeconds), ["inBytesTotal"] = _inBytesTotal, ["byCategory"] = bytes, ["byNetId"] = NetIdBytes(),
                ["botAvatarIds"] = new List<uint>(_botAvatarIds), ["humanAvatarIds"] = new List<uint>(_humanAvatarIds),
                ["ownSessionNetId"] = own, ["ownAvatarNetId"] = local != null ? local.ActiveAvatarNetId : 0,
                ["ownTeam"] = local != null ? local.TeamIndex : -1, ["ownRole"] = local != null ? local.Role.ToString() : null
            };
        }

        // ── Утилиты ──────────────────────────────────────────────────────────

        private static Dictionary<string, object> Stats(List<float> values)
        {
            var result = new Dictionary<string, object> { ["count"] = values.Count };
            if (values.Count == 0) return result;
            var sorted = new List<float>(values);
            sorted.Sort();
            double sum = 0;
            foreach (float v in sorted) sum += v;
            result["p50"] = Round3(sorted[(int)(0.50 * (sorted.Count - 1))]);
            result["p95"] = Round3(sorted[(int)(0.95 * (sorted.Count - 1))]);
            result["p99"] = Round3(sorted[(int)(0.99 * (sorted.Count - 1))]);
            result["max"] = Round3(sorted[sorted.Count - 1]);
            result["mean"] = Round3(sum / sorted.Count);
            return result;
        }

        private static double Round3(double value) => Math.Round(value, 3);

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

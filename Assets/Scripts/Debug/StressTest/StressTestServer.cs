using System.Collections;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Серверная половина стресс-теста. По запросу игрока (<see cref="StressTestRequestMessage"/>)
    /// спавнит <see cref="StressTestConfig.puppetCount"/> remote-аватаров (<see cref="StressPuppet"/>),
    /// которые повторяют движения этого игрока с задержкой, затем россыпь предметов.
    ///
    /// <para>
    /// <b>Почему на сервере.</b> Куклы — обычные сетевые аватары без владельца, их поза
    /// рассылается всем клиентам штатным <c>NetworkTransform</c>, позы кистей — каналом
    /// состояния UltimateXR. Шлем получает их ровно так, как получал бы 9 живых игроков:
    /// десериализация, снапшот-интерполяция, IK remote-аватаров. На выделенном сервере это
    /// полный путь прода; на шлеме-хосте сеть внутри процесса, зато шлем несёт и сервер.
    /// </para>
    ///
    /// <para>
    /// <b>NetworkTransform куклы переключается на ServerToClient.</b> У аватара он
    /// <c>ClientToServer</c>, а для объекта без владельца Mirror в таком режиме позу не
    /// рассылает вовсе (<c>NetworkTransformUnreliable.UpdateServerBroadcast</c>) —
    /// у клиентов куклы стояли бы замороженными.
    /// </para>
    ///
    /// <para>
    /// <b>Фазы.</b> Обычный прогон: разгон → «база» → «куклы» → «куклы+хлам» → «куклы: по карте».
    /// Прогон по скинам (<see cref="StressTestConfig.perSkinPhases"/>): разгон → «база» →
    /// «куклы: &lt;префаб&gt;» на каждый скин. Раскладка «арена» (<see cref="StressTestConfig.arenaLayout"/>):
    /// разгон → «база» → «куклы: арена» — куклы на полу физической арены карты с закреплёнными корнями.
    /// Обход точек обзора (<see cref="StressTestConfig.viewNames"/>, только с «ареной»): разгон → «база: &lt;точка&gt;»
    /// по всем точкам → спавн кукол на арене → «куклы: &lt;точка&gt;» по всем точкам; переносит аватар клиент.
    /// Сервер шлёт инициатору
    /// начало каждой фазы (<see cref="StressTestStatusMessage"/>), клиент по ним открывает
    /// фазы в своём логе. Выделенный сервер пишет и свой лог (время тика); на хосте
    /// второй рекордер не заводится — процесс один, его меряет клиентская половина.
    /// </para>
    ///
    /// <para>
    /// Только в разминке: в режиме с уроном куклы мешали бы матчу.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-10000)] // Tick серверного рекордера — первым в кадре.
    public sealed class StressTestServer : MonoBehaviour
    {
        public static StressTestServer Current { get; private set; }

        /// <summary>
        /// Итог последнего прогона на этой машине; null — прогонов с запуска не было. Пишет только
        /// <see cref="Finish"/>; читают наблюдатели (сценарий e2e <c>perf-stress</c>).
        /// </summary>
        public static StressTestRunRecord LastRun { get; private set; }

        /// <summary>Каталог отчёта этого прогона; пусто на хосте — там свой лог только у клиентской половины.</summary>
        public string RunDirectory => _report != null ? _report.Directory : string.Empty;

        private NetworkConnectionToClient _initiator;
        private PlayerSession _session;
        private StressTestConfig _config;
        private PerfFrameRecorder _recorder;   // только выделенный сервер
        private PerfRunReport _report;
        private readonly PoseDelayBuffer _poses = new PoseDelayBuffer(512);
        private readonly List<StressPuppet> _puppets = new List<StressPuppet>();
        private readonly List<GameObject> _clutter = new List<GameObject>();
        private List<GameObject> _prefabs = new List<GameObject>();
        private string _currentSkins = "";

        private Vector3 _startRootPosition;
        private Quaternion _startRootRotation;
        private bool _finished;
        private string _abortReason = "прерван";
        private int _phaseNumber;   // сколько фаз открыто — для LastRun
        private ArenaFloor _arena;  // раскладка «арена»: найдена при приёме запроса
        private string _placement = string.Empty;   // итог раскладки «арена» — для заметки фазы и LastRun
        private List<Vector2> _viewFloors;          // обход точек обзора: точки пола (мировые X/Z) — запретные зоны арены

        /// <summary>
        /// Точки обзора, которые серверу передал его сценарий e2e (<see cref="ProvideViewTour"/>): имена по порядку и
        /// точки пола. Запрос обхода от клиента сверяется с ними по именам — оба получают один файл прогона.
        /// Пишут только <see cref="ProvideViewTour"/> и <see cref="ClearViewTour"/>.
        /// </summary>
        private static string[] s_viewTourNames;
        private static Vector2[] s_viewTourFloors;

        // ── Запросы ─────────────────────────────────────────────────────────

        /// <summary>
        /// Точки обзора для запросов обхода на этом сервере: имена по порядку и точки пола (мировые X/Z — Vector2.y это
        /// мировой Z). Зовёт серверный сценарий e2e, прочитав тот же файл точек, что и клиент; без них сервер в обходе
        /// отказывает. Стресс-тест типов e2e не знает — только имена и точки.
        /// </summary>
        public static void ProvideViewTour(IReadOnlyList<string> names, IReadOnlyList<Vector2> floorPoints)
        {
            if (names == null || floorPoints == null || names.Count != floorPoints.Count)
            {
                GameLog.Perf.Error("[StressTest] Точки обзора не приняты: имён и точек пола должно быть поровну.");
                ClearViewTour();
                return;
            }

            s_viewTourNames = new string[names.Count];
            s_viewTourFloors = new Vector2[floorPoints.Count];
            for (int i = 0; i < names.Count; i++)
            {
                s_viewTourNames[i] = names[i];
                s_viewTourFloors[i] = floorPoints[i];
            }
        }

        public static void ClearViewTour()
        {
            s_viewTourNames = null;
            s_viewTourFloors = null;
        }

        /// <summary>Обход из запроса против точек сервера: null — годится, иначе причина отказа.</summary>
        private static string CheckViewTour(StressTestConfig config)
        {
            if (!config.arenaLayout)
                return "обход точек обзора — только с раскладкой «арена»: ряды кукол перед игроком после переноса стояли бы где попало";
            if (config.viewNames.Length > StressTestConfig.MaxViews)
                return $"точек обзора {config.viewNames.Length}, наибольшее {StressTestConfig.MaxViews}";
            if (s_viewTourNames == null)
                return "обход точек обзора: серверу не переданы точки (сценарий perf-stress на сервере с -e2eArg views=...)";
            if (s_viewTourNames.Length != config.viewNames.Length)
                return $"точки обзора клиента и сервера разошлись: у клиента {config.viewNames.Length}, у сервера {s_viewTourNames.Length}";
            for (int i = 0; i < s_viewTourNames.Length; i++)
            {
                if (s_viewTourNames[i] != config.viewNames[i])
                    return $"точки обзора клиента и сервера разошлись: №{i + 1} «{config.viewNames[i]}» у клиента, «{s_viewTourNames[i]}» у сервера";
            }
            return null;
        }

        internal static void HandleRequest(NetworkConnectionToClient conn, StressTestRequestMessage msg)
        {
            if (!msg.start)
            {
                if (Current != null && Current._initiator == conn) Abort("остановлен игроком");
                return;
            }

            if (!CanStart(conn, out PlayerSession session, out string reason))
            {
                Refuse(conn, reason);
                return;
            }

            StressTestConfig config = msg.ToConfig();
            ArenaFloor arena = null;
            if (config.arenaLayout)
            {
                if (config.perSkinPhases)
                {
                    Refuse(conn, "раскладка «арена» несовместима с прогоном по скинам");
                    return;
                }
                // Без арены — отказ, а не тихий откат на другую раскладку: замер был бы не тем, что просили.
                if (!ArenaFloor.TryFind(out arena, out reason))
                {
                    Refuse(conn, reason);
                    return;
                }
            }

            List<Vector2> viewFloors = null;
            if (config.HasViewTour)
            {
                string tourError = CheckViewTour(config);
                if (tourError != null)
                {
                    Refuse(conn, tourError);
                    return;
                }
                viewFloors = new List<Vector2>(s_viewTourFloors);
            }

            // Не DontDestroyOnLoad: смена сцены уничтожает кукол вместе с ней, и прогон
            // обязан закончиться тут же, а не мерить пустую новую карту.
            var go = new GameObject("StressTestServer");
            var server = go.AddComponent<StressTestServer>();
            server._initiator = conn;
            server._session = session;
            server._config = config;
            server._arena = arena;
            server._viewFloors = viewFloors;
        }

        private static void Refuse(NetworkConnectionToClient conn, string reason)
        {
            GameLog.Perf.Warning($"[StressTest] Отказ в старте (conn {conn.connectionId}): {reason}.");
            conn.Send(new StressTestStatusMessage { kind = StressTestStatusKind.Rejected, text = reason });
        }

        private static bool CanStart(NetworkConnectionToClient conn, out PlayerSession session, out string reason)
        {
            session = conn.identity != null ? conn.identity.GetComponent<PlayerSession>() : null;

            if (Current != null)                                     { reason = "стресс-тест уже идёт"; return false; }
            if (session == null || session.ActiveAvatar == null)    { reason = "у игрока нет аватара"; return false; }

            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (mode == null || !mode.IsWarmup)                      { reason = "только в разминке — в матче куклы мешали бы игре"; return false; }

            reason = null;
            return true;
        }

        public static void Abort(string reason)
        {
            if (Current == null) return;
            Current._abortReason = reason;
            Destroy(Current.gameObject);
        }

        // ── Жизненный цикл ──────────────────────────────────────────────────

        private void Awake()
        {
            Current = this;
        }

        private void Start()
        {
            Transform leaderRoot = _session.ActiveAvatar.transform;
            _startRootPosition = leaderRoot.position;
            _startRootRotation = Quaternion.Euler(0f, leaderRoot.eulerAngles.y, 0f);
            _prefabs = CollectAvatarPrefabs();

            if (_config.puppetSkin != StressTestLayout.MixedSkins && !StressTestLayout.IsSingleSkin(_prefabs.Count, _config.puppetSkin))
            {
                GameLog.Perf.Warning($"[StressTest] Скина {_config.puppetSkin} нет (скинов {_prefabs.Count}) — куклы вперемешку.");
            }

            // Хост меряет себя клиентской половиной; свой лог — только у выделенного сервера.
            if (!NetworkClient.active)
            {
                _report = PerfRunReport.Create("выделенный сервер", _config.puppetCount, _config.clutterCount);
                _report.AddSkins(PlannedSkins());
                _recorder = new PerfFrameRecorder(_report.Directory, _report.BudgetMs, _report.BuildHeader());
            }

            GameLog.Perf.Info($"[StressTest] Сервер: старт для {_session.PlayerName}, кукол {_config.puppetCount}, предметов {_config.clutterCount}, " +
                              $"{(_config.perSkinPhases ? "по скинам" : _config.arenaLayout ? "арена" : _config.mapOnly ? "только по карте" : "обычный")}, " +
                              (_config.HasViewTour ? $"обход точек обзора: {_config.viewNames.Length}, " : "") +
                              $"скины: {PlannedSkins()}.");

            UxrManager.StageUpdated += OnUxrStageUpdated;
            StartCoroutine(Run());
        }

        private void Update()
        {
            _recorder?.Tick();

            if (!_finished && (_initiator == null || _initiator.identity == null))
            {
                Abort("инициатор отключился");
            }
        }

        private void OnDestroy()
        {
            UxrManager.StageUpdated -= OnUxrStageUpdated;

            if (!_finished)
            {
                GameLog.Perf.Warning($"[StressTest] Сервер: прогон прерван — {_abortReason}.");
                Finish(completed: false, _abortReason);
            }

            DespawnAll();
            _recorder?.Dispose();
            _recorder = null;

            if (Current == this) Current = null;
        }

        // ── Сценарий ────────────────────────────────────────────────────────

        private IEnumerator Run()
        {
            yield return Phase("разгон", _config.warmupSeconds, measured: false, note: null);

            if (_config.HasViewTour)
            {
                yield return RunViewTour();
            }
            else
            {
                yield return Phase("база", _config.phaseSeconds, measured: true, note: null);

                if (_config.perSkinPhases)    yield return RunPerSkin();
                else if (_config.arenaLayout) yield return RunArena();
                else                          yield return RunStandard();
            }

            Finish(completed: true, "полный прогон");
            Destroy(gameObject);
        }

        /// <summary>
        /// Обход точек обзора: пара фаз на точку без кукол, затем куклы на арене (запретные зоны — и точки обзора)
        /// и пара фаз на точку с ними. Аватар в точку переносит клиент в начале каждой «~успокоение»; сервер
        /// только держит фазы. Порядок фаз повторяет <see cref="StressTestPlan.Phases"/>.
        /// </summary>
        private IEnumerator RunViewTour()
        {
            foreach (string view in _config.viewNames)
                yield return ViewPhases(StressTestPlan.ViewPhaseName(view, withPuppets: false), note: null);

            if (_config.puppetCount <= 0) yield break;

            // Спавн — в начале первого успокоения с куклами: рывок спавна в замер не попадает.
            SpawnPuppets(PuppetLayout.Arena, _config.puppetSkin);
            string spawnNote = _placement.Length > 0 ? SpawnNote() + "; " + _placement : SpawnNote();
            for (int i = 0; i < _config.viewNames.Length; i++)
                yield return ViewPhases(StressTestPlan.ViewPhaseName(_config.viewNames[i], withPuppets: true), i == 0 ? spawnNote : null);
        }

        private IEnumerator ViewPhases(string name, string note)
        {
            yield return Phase(name + StressTestPlan.SettleSuffix, _config.settleSeconds, measured: false, note: note);
            yield return Phase(name, _config.phaseSeconds, measured: true, note: null);
        }

        private IEnumerator RunStandard()
        {
            // Порядок фаз повторяет StressTestPlan.Phases (экран «Перф-тесты» показывает по нему
            // «фаза i из N») — меняешь здесь, меняй и там.
            if (_config.mapOnly)
            {
                if (_config.puppetCount <= 0) yield break;
                SpawnPuppets(PuppetLayout.MapRing, _config.puppetSkin);
                yield return Phase("куклы: по карте~успокоение", _config.settleSeconds, measured: false, note: SpawnNote());
                yield return Phase("куклы: по карте", _config.phaseSeconds, measured: true, note: null);
                yield break;
            }

            SpawnPuppets(PuppetLayout.Rows, _config.puppetSkin);
            yield return Phase("куклы~успокоение", _config.settleSeconds, measured: false, note: SpawnNote());
            yield return Phase("куклы", _config.phaseSeconds, measured: true, note: null);

            if (_config.clutterCount > 0)
            {
                SpawnClutter();
                yield return Phase("куклы+хлам~успокоение", _config.settleSeconds, measured: false, note: $"высыпано предметов: {_clutter.Count}");
                yield return Phase("куклы+хлам", _config.phaseSeconds, measured: true, note: null);
            }

            if (_config.mapSpreadPhase && _config.puppetCount > 0)
            {
                // Хлам и ряды убираются: фаза сравнивается с «куклы» — то же число кукол,
                // другое расположение.
                int removed = DespawnClutter() + DespawnPuppets();
                SpawnPuppets(PuppetLayout.MapRing, _config.puppetSkin);
                yield return Phase("куклы: по карте~успокоение", _config.settleSeconds, measured: false,
                                   note: $"убрано объектов: {removed}; " + SpawnNote());
                yield return Phase("куклы: по карте", _config.phaseSeconds, measured: true, note: null);
            }
        }

        /// <summary>
        /// Раскладка «арена»: одна фаза нагрузки — куклы на полу физической арены, корни закреплены.
        /// Порядок фаз повторяет <see cref="StressTestPlan.Phases"/>.
        /// </summary>
        private IEnumerator RunArena()
        {
            if (_config.puppetCount <= 0) yield break;
            SpawnPuppets(PuppetLayout.Arena, _config.puppetSkin);
            yield return Phase(StressTestPlan.ArenaPhaseName + "~успокоение", _config.settleSeconds, measured: false,
                               note: _placement.Length > 0 ? SpawnNote() + "; " + _placement : SpawnNote());
            yield return Phase(StressTestPlan.ArenaPhaseName, _config.phaseSeconds, measured: true, note: null);
        }

        /// <summary>
        /// Каждый скин — своя фаза с одинаковой расстановкой: цифры фаз напрямую сравнимы
        /// между собой и с «базой». Куклы прошлого скина убираются в начале успокоения
        /// следующего — рывок уборки попадает в неизмеряемую фазу.
        /// </summary>
        private IEnumerator RunPerSkin()
        {
            if (_prefabs.Count == 0)
            {
                GameLog.Perf.Warning("[StressTest] Прогон по скинам: в TeamRegistry нет ни одного префаба аватара.");
                yield break;
            }

            for (int skin = 0; skin < _prefabs.Count; skin++)
            {
                int removed = DespawnPuppets();
                SpawnPuppets(PuppetLayout.Rows, skin);

                string name = "куклы: " + _prefabs[skin].name;
                yield return Phase(name + "~успокоение", _config.settleSeconds, measured: false,
                                   note: (removed > 0 ? $"убрано кукол: {removed}; " : "") + SpawnNote());
                yield return Phase(name, _config.phaseSeconds, measured: true, note: null);
            }
        }

        private string SpawnNote() => $"заспавнено кукол: {_puppets.Count}, скины: {(_currentSkins.Length > 0 ? _currentSkins : "-")}";

        /// <summary>Скины, которые пойдут в прогон, — для заголовка лога сервера и строки старта.</summary>
        private string PlannedSkins()
        {
            if (_prefabs.Count == 0) return "-";
            if (_config.perSkinPhases) return JoinNames(_prefabs, -1, _prefabs.Count);
            return JoinNames(_prefabs, _config.puppetSkin, Mathf.Max(0, _config.puppetCount));
        }

        /// <summary>Имена префабов, которые получат <paramref name="count"/> кукол, без повторов, по порядку.</summary>
        private static string JoinNames(List<GameObject> prefabs, int puppetSkin, int count)
        {
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int index = StressTestLayout.SkinIndex(prefabs.Count, puppetSkin, i);
                if (index < 0) break;
                string name = prefabs[index].name;
                if (!names.Contains(name)) names.Add(name);
            }
            return string.Join(", ", names);
        }

        private IEnumerator Phase(string name, float seconds, bool measured, string note)
        {
            if (_recorder != null)
            {
                ClosePhase();
                _recorder.BeginPhase(name, measured, seconds);
                if (note != null) PerfEvents.Note(note);
            }

            _phaseNumber++;
            Send(new StressTestStatusMessage
            {
                kind         = StressTestStatusKind.Phase,
                phase        = name,
                measured     = measured,
                seconds      = seconds,
                puppetCount  = _puppets.Count,
                clutterCount = _clutter.Count,
                skins        = _currentSkins,
                text         = note,
            });

            float endsAt = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < endsAt) yield return null;
        }

        private void ClosePhase()
        {
            PerfPhaseReport phase = _recorder?.EndPhase();
            if (phase != null) _report.phases.Add(phase);
        }

        private void Finish(bool completed, string reason)
        {
            _finished = true;

            if (_recorder != null)
            {
                ClosePhase();
                _report.completed = completed;
                _report.endReason = reason;
                _report.Save();
                _recorder.WriteLine("=== итог прогона (сервер): " + reason + "\n" + _report.BuildResultText());
            }

            LastRun = new StressTestRunRecord(RunDirectory, completed, reason, _phaseNumber, _placement);

            Send(new StressTestStatusMessage
            {
                kind      = StressTestStatusKind.Finished,
                completed = completed,
                text      = reason,
            });
        }

        private void Send(StressTestStatusMessage msg)
        {
            if (_initiator != null && _initiator.isReady) _initiator.Send(msg);
        }

        // ── Куклы ───────────────────────────────────────────────────────────

        private enum PuppetLayout
        {
            /// <summary>Рядами перед игроком, лицом туда же, куда он.</summary>
            Rows,
            /// <summary>Кольцом 8–15 м вокруг игрока, лицом в случайную сторону.</summary>
            MapRing,
            /// <summary>По полу физической арены, лицом в случайную сторону, корни закреплены.</summary>
            Arena,
        }

        /// <summary>
        /// <see cref="PuppetLayout.Rows"/> — куклы встают рядами перед игроком, лицом туда же,
        /// куда он: весь прогон в поле зрения (худший случай для рендера), выстрелы уйдут от
        /// игрока. <see cref="PuppetLayout.MapRing"/> — вокруг игрока по карте, как в живом
        /// матче: большинство за укрытиями или вне поля зрения.
        /// Скин — <paramref name="puppetSkin"/> (см. <see cref="StressTestConfig.puppetSkin"/>).
        /// </summary>
        private void SpawnPuppets(PuppetLayout layout, int puppetSkin)
        {
            _currentSkins = "";
            if (_prefabs.Count == 0)
            {
                GameLog.Perf.Warning("[StressTest] Куклы не созданы: в TeamRegistry нет ни одного префаба аватара.");
                return;
            }

            int count = Mathf.Max(0, _config.puppetCount);
            List<StressTestLayout.ArenaSpot> spots = null;
            if (layout == PuppetLayout.Arena)
            {
                spots = PlaceOnArena(count);
                count = spots.Count;
            }
            _currentSkins = JoinNames(_prefabs, puppetSkin, count);

            var rng = new System.Random(unchecked(System.Environment.TickCount * 31 + count));
            float ringPhase = (float)rng.NextDouble() * 360f;
            int offMap = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 position;
                Quaternion rotation;

                if (layout == PuppetLayout.Arena)
                {
                    StressTestLayout.ArenaSpot spot = spots[i];
                    position = new Vector3(spot.Position.x, ArenaFloorY(spot.Position), spot.Position.y);
                    rotation = Quaternion.Euler(0f, spot.YawDeg, 0f);
                }
                else if (layout == PuppetLayout.MapRing)
                {
                    StressTestLayout.RingPlacement(i, count, _config.mapMinRadius, _config.mapMaxRadius, ringPhase, rng,
                                                   out Vector3 offset, out float yaw);
                    if (!TryPlaceOnFloor(offset, out position)) offMap++;
                    rotation = _startRootRotation * Quaternion.Euler(0f, yaw, 0f);
                }
                else
                {
                    Vector3 offset = StressTestLayout.RowOffset(i, count, _config.puppetsPerRow, _config.puppetSpacing, _config.firstRowDistance);
                    position = _startRootPosition + _startRootRotation * offset;
                    rotation = _startRootRotation;
                }

                GameObject prefab = _prefabs[StressTestLayout.SkinIndex(_prefabs.Count, puppetSkin, i)];
                GameObject go = Instantiate(prefab, position, rotation);
                go.name = $"StressPuppet {i + 1} ({prefab.name})";

                PlayerController pc = go.GetComponent<PlayerController>();
                if (pc != null) pc.AvatarPlayerName = $"Кукла {i + 1}";

                // Без владельца ClientToServer не рассылается — см. описание класса.
                VrBattlegrounds.Player.Avatars.ServerAuthoredAvatar.Prepare(go);

                float delay = count > 1 ? Mathf.Lerp(0.1f, _config.maxDelaySeconds, (float)i / (count - 1)) : 0.1f;
                StressPuppet puppet = go.AddComponent<StressPuppet>();
                puppet.Initialize(delay, position, rotation, fixedRoot: layout == PuppetLayout.Arena);

                NetworkServer.Spawn(go);
                _puppets.Add(puppet);
                PerfEvents.Count(PerfEventKind.Spawn);
            }

            if (offMap > 0)
            {
                GameLog.Perf.Warning($"[StressTest] По карте: для {offMap} из {count} кукол не нашлось свободного пола — " +
                                     "стоят на высоте игрока в исходной точке кольца.");
            }

            // Отчёт выделенного сервера — фактическое число кукол на арене, а не запрошенное.
            if (layout == PuppetLayout.Arena && _report != null) _report.puppetCount = _puppets.Count;
        }

        // ── Арена ───────────────────────────────────────────────────────────

        /// <summary>
        /// Места кукол на полу арены (<see cref="StressTestLayout.ArenaPlacement"/>): запретные зоны — препятствия
        /// и станции арсенала арены плюс место игрока на старте; точка годна, если капсула роста человека там
        /// свободна от геометрии сцены (декор, не размеченный препятствием). Итог — в <see cref="_placement"/> и лог.
        /// </summary>
        private List<StressTestLayout.ArenaSpot> PlaceOnArena(int count)
        {
            var exclusions = new List<Rect>(_arena.Exclusions);
            // Игрок на старте: кукла вплотную к камере — не нагрузка лобби, а заслонённый кадр.
            float half = _config.puppetSpacing;
            exclusions.Add(new Rect(_startRootPosition.x - half, _startRootPosition.z - half, half * 2f, half * 2f));
            // Точки обзора: камера обхода не должна оказаться внутри куклы или вплотную к ней.
            int viewZones = _viewFloors != null ? _viewFloors.Count : 0;
            for (int i = 0; i < viewZones; i++) exclusions.Add(StressTestLayout.ViewPointExclusion(_viewFloors[i]));

            var spots = new List<StressTestLayout.ArenaSpot>(count);
            float floorY = _arena.FloorY;
            int placed = StressTestLayout.ArenaPlacement(_arena.Floor, StressTestLayout.ArenaWallMargin, exclusions,
                StressTestLayout.ArenaExclusionMargin, count, _config.puppetSpacing, StressTestLayout.ArenaDefaultSeed + count,
                spots, p => IsBodySpaceFree(new Vector3(p.x, floorY, p.y)));

            int shortfall = count - placed;
            _placement = $"арена «{_arena.Name}»: расставлено кукол {placed} из {count}" +
                         (shortfall > 0 ? $", не поместилось {shortfall}" : "");

            Rect f = _arena.Floor;
            string details = $"пол X {f.xMin:F2}..{f.xMax:F2}, Z {f.yMin:F2}..{f.yMax:F2}, высота {floorY:F2}; " +
                             $"отступ от стен {StressTestLayout.ArenaWallMargin:F1} м, шаг кукол {_config.puppetSpacing:F1} м; " +
                             $"запретных зон {exclusions.Count} (препятствий {_arena.ObstacleCount}, станций {_arena.StationCount}, игрок" +
                             (viewZones > 0 ? $", точек обзора {viewZones} — радиус {StressTestLayout.ViewPointClearance:F1} м)" : ")");
            if (_arena.Problems.Length > 0) details += $"; пропущены разметки: {_arena.Problems}";

            if (shortfall > 0) GameLog.Perf.Warning($"[StressTest] {_placement} — свободного пола не хватило. {details}.");
            else               GameLog.Perf.Info($"[StressTest] {_placement}. {details}.");
            return spots;
        }

        /// <summary>
        /// Высота пола под куклой: видимый пол сцены под точкой (не дальше <see cref="MaxFloorStep"/> от пола
        /// арены), иначе — верх пола арены.
        /// </summary>
        private float ArenaFloorY(Vector2 xz) =>
            TryFindFloor(new Vector3(xz.x, 0f, xz.y), _arena.FloorY, out float floorY) ? floorY : _arena.FloorY;

        /// <summary>
        /// Физическая арена загруженной карты (<see cref="PhysicalArenaDefinition"/>, зона level-design — только
        /// чтение): прямоугольник пола, высота пола и запретные зоны — препятствия арены
        /// (<see cref="PhysicalObstacleMarker"/>) и станции арсенала её сцены (поднятый корпус и место игрока у неё).
        /// </summary>
        private sealed class ArenaFloor
        {
            public string Name;
            public Rect Floor;
            public float FloorY;
            public readonly List<Rect> Exclusions = new List<Rect>();
            public int ObstacleCount;
            public int StationCount;
            /// <summary>Разметки, которые не прочитались (имя: причина), через «; »; пусто — все прочитаны.</summary>
            public string Problems = string.Empty;

            public static bool TryFind(out ArenaFloor arena, out string reason)
            {
                arena = null;
                PhysicalArenaDefinition[] found = FindObjectsByType<PhysicalArenaDefinition>();
                if (found.Length == 0)
                {
                    reason = "раскладка «арена»: в загруженной сцене нет физической арены (PhysicalArenaDefinition)";
                    return false;
                }
                if (found.Length > 1)
                {
                    reason = $"раскладка «арена»: в загруженных сценах арен {found.Length} — какую мерить, неясно";
                    return false;
                }

                PhysicalArenaDefinition definition = found[0];
                if (!definition.Valid(out string invalid))
                {
                    reason = $"раскладка «арена»: арена «{definition.name}» не прошла проверку — {invalid}";
                    return false;
                }
                if (!definition.TryFloorBounds(out Bounds floor))
                {
                    reason = $"раскладка «арена»: у арены «{definition.name}» нет габаритов пола";
                    return false;
                }

                arena = new ArenaFloor
                {
                    Name   = $"{definition.gameObject.scene.name}/{definition.name}",
                    Floor  = StressTestLayout.FloorRect(floor),
                    FloorY = floor.max.y,
                };

                var problems = new List<string>();
                foreach (PhysicalObstacleMarker marker in definition.GetComponentsInChildren<PhysicalObstacleMarker>(true))
                {
                    if (marker.TryBounds(definition, out Bounds bounds, out string why))
                    {
                        arena.Exclusions.Add(StressTestLayout.FloorRect(bounds));
                        arena.ObstacleCount++;
                    }
                    else problems.Add($"{marker.name}: {why}");
                }

                foreach (ArsenalStationAnchor station in FindObjectsByType<ArsenalStationAnchor>())
                {
                    if (station.gameObject.scene != definition.gameObject.scene) continue;
                    Bounds footprint = station.RaisedBoundsWorld;
                    footprint.Encapsulate(station.StandingPosition);
                    arena.Exclusions.Add(StressTestLayout.FloorRect(footprint));
                    arena.StationCount++;
                }

                if (problems.Count > 0)
                {
                    arena.Problems = string.Join("; ", problems);
                    GameLog.Perf.Warning($"[StressTest] Арена «{arena.Name}»: не прочитаны разметки препятствий — {arena.Problems}. " +
                                         "Их место из раскладки не исключено; остаётся проверка капсулы роста по геометрии сцены.");
                }

                reason = null;
                return true;
            }
        }

        private const float FloorProbeUp   = 1.5f;  // луч — с высоты груди: ниже большинства потолков
        private const float FloorProbeDown = 4f;
        private const float MaxFloorStep   = 0.5f;  // выше — это крыша укрытия, а не пол
        private static readonly float[] RadiusTry = new float[8];

        /// <summary>
        /// Точка кольца на полу карты. Луч вниз с высоты груди; попадания по динамике
        /// (предметы, аватары) пропускаются. Пол должен быть не выше/ниже старта игрока больше
        /// чем на <see cref="MaxFloorStep"/>, а капсула роста человека — свободной. Не вышло —
        /// ближе к игроку (<see cref="StressTestLayout.FallbackRadii"/>); совсем не вышло —
        /// исходная точка на высоте старта, false.
        /// </summary>
        private bool TryPlaceOnFloor(Vector3 offset, out Vector3 position)
        {
            Vector3 flat = new Vector3(offset.x, 0f, offset.z);
            float radius = flat.magnitude;
            Vector3 dir  = radius > 1e-3f ? flat / radius : Vector3.forward;
            float refY   = _startRootPosition.y;

            int tries = StressTestLayout.FallbackRadii(radius, 2f, RadiusTry);
            for (int t = 0; t < tries; t++)
            {
                Vector3 xz = _startRootPosition + _startRootRotation * (dir * RadiusTry[t]);
                if (TryFindFloor(xz, refY, out float floorY) && IsBodySpaceFree(new Vector3(xz.x, floorY, xz.z)))
                {
                    position = new Vector3(xz.x, floorY, xz.z);
                    return true;
                }
            }

            position = _startRootPosition + _startRootRotation * flat;
            position.y = refY;
            return false;
        }

        private static bool TryFindFloor(Vector3 xz, float refY, out float floorY)
        {
            floorY = refY;
            var origin = new Vector3(xz.x, refY + FloorProbeUp, xz.z);
            // Аллоцирующие версии сознательно: вызывается раз на куклу при спавне, в фазе успокоения.
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, FloorProbeUp + FloorProbeDown,
                                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (hit.distance >= best) continue;
                best   = hit.distance;
                floorY = hit.point.y;
                found  = true;
            }

            return found && Mathf.Abs(floorY - refY) <= MaxFloorStep;
        }

        private static bool IsBodySpaceFree(Vector3 feet)
        {
            const float radius = 0.25f;
            Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * (0.3f + radius), feet + Vector3.up * (1.7f - radius), radius,
                                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (hit != null && hit.attachedRigidbody == null) return false;
            }
            return true;
        }

        /// <summary>
        /// Скины кукол по порядку: префабы аватаров всех команд <c>TeamRegistry</c>, без повторов.
        /// Публичный — тот же список показывает выбор скина на планшете (экран «Отладка»): индекс
        /// в нём и есть <see cref="StressTestConfig.puppetSkin"/>.
        /// </summary>
        public static List<GameObject> CollectAvatarPrefabs()
        {
            var result = new List<GameObject>();
            TeamRegistry registry = TeamRegistry.Instance;
            if (registry == null || registry.teams == null) return result;

            foreach (TeamData team in registry.teams)
            {
                if (team == null) continue;
                for (int i = 0; i < team.avatars.Count; i++)
                {
                    GameObject prefab = team.GetAvatarPrefab(i);
                    if (prefab != null && !result.Contains(prefab)) result.Add(prefab);
                }
            }
            return result;
        }

        /// <summary>
        /// Поза ведущего снимается в конце стадии <see cref="UxrUpdateStage.Update"/>:
        /// на хосте трекинг локального аватара уже применён, на выделенном сервере его
        /// <c>NetworkTransform</c> отработал в своём <c>Update</c> (не позже чем кадр назад).
        /// IK кукол (<see cref="UxrUpdateStage.PostProcess"/>) ещё впереди.
        ///
        /// <para>
        /// Ведущий берётся из сессии каждый кадр: смена скина пересоздаёт аватар, и куклы
        /// должны пойти за новым.
        /// </para>
        /// </summary>
        private void OnUxrStageUpdated(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || _session == null) return;

            PlayerController leader = _session.ActiveAvatar;
            UxrAvatar leaderAvatar = leader != null ? leader.GetComponent<UxrAvatar>() : null;

            if (StressPuppet.TryCapture(leaderAvatar, _startRootPosition, _startRootRotation, out AvatarPoseSample sample))
            {
                _poses.Add(Time.time, sample);
            }

            for (int i = _puppets.Count - 1; i >= 0; i--)
            {
                StressPuppet puppet = _puppets[i];
                if (puppet == null)
                {
                    _puppets.RemoveAt(i);
                    continue;
                }

                if (_poses.TrySample(Time.time - puppet.Delay, out AvatarPoseSample delayed))
                {
                    puppet.Apply(delayed);
                }
            }
        }

        // ── Хлам ────────────────────────────────────────────────────────────

        /// <summary>
        /// Лежащее оружие и магазины между игроком и куклами. Спавн — через
        /// <see cref="NetworkUxrIdentity"/>, как у стены арсенала: сырой Spawn оставил бы
        /// UniqueId UltimateXR невыровненным (NET-16). Предметы роняются с высоты —
        /// первые секунды физика их укладывает, это и есть фаза успокоения.
        /// </summary>
        private void SpawnClutter()
        {
            var prefabs = new List<GameObject>();
            WeaponRegistry registry = WeaponRegistry.Instance;
            if (registry != null)
            {
                foreach (WeaponInfo info in registry.Weapons)
                {
                    if (info == null) continue;
                    if (info.WeaponPrefab != null)   prefabs.Add(info.WeaponPrefab);
                    if (info.MagazinePrefab != null) prefabs.Add(info.MagazinePrefab);
                }
            }

            if (prefabs.Count == 0)
            {
                GameLog.Perf.Warning("[StressTest] Хлам не создан: WeaponRegistry пуст.");
                return;
            }

            const int perRow = 8;
            for (int i = 0; i < _config.clutterCount; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                var offset = new Vector3((col - (perRow - 1) * 0.5f) * 0.45f,
                                         0.8f + (row % 3) * 0.25f,
                                         1.0f + (row / 3) * 0.35f + (row % 3) * 0.1f);

                GameObject instance = NetworkUxrIdentity.CreateInstance(prefabs[i % prefabs.Count]);
                if (instance == null) continue;

                instance.transform.SetPositionAndRotation(_startRootPosition + _startRootRotation * offset,
                                                          _startRootRotation * Quaternion.Euler(0f, i * 37f, 0f));
                instance.SetActive(true);
                NetworkUxrIdentity.SpawnServerObject(instance);

                _clutter.Add(instance);
                PerfEvents.Count(PerfEventKind.Spawn);
            }
        }

        private void DespawnAll()
        {
            DespawnPuppets();
            DespawnClutter();
        }

        /// <summary>Убирает кукол, возвращает, сколько убрано.</summary>
        private int DespawnPuppets()
        {
            int removed = 0;
            if (NetworkServer.active)
            {
                foreach (StressPuppet puppet in _puppets)
                {
                    if (puppet == null) continue;
                    NetworkServer.Destroy(puppet.gameObject);
                    removed++;
                }
            }

            _puppets.Clear();
            _currentSkins = "";
            return removed;
        }

        /// <summary>Убирает лежащие предметы, возвращает, сколько убрано.</summary>
        private int DespawnClutter()
        {
            int removed = 0;
            if (NetworkServer.active)
            {
                foreach (GameObject item in _clutter)
                {
                    if (item == null) continue;
                    NetworkServer.Destroy(item);
                    removed++;
                }
            }

            _clutter.Clear();
            return removed;
        }
    }
}

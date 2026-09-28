using System.Collections;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
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
    /// <b>Фазы.</b> Разгон → «база» → «куклы» → «куклы+хлам». Сервер шлёт инициатору
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

        private NetworkConnectionToClient _initiator;
        private PlayerSession _session;
        private StressTestConfig _config;
        private PerfFrameRecorder _recorder;   // только выделенный сервер
        private PerfRunReport _report;
        private readonly PoseDelayBuffer _poses = new PoseDelayBuffer(512);
        private readonly List<StressPuppet> _puppets = new List<StressPuppet>();
        private readonly List<GameObject> _clutter = new List<GameObject>();

        private Vector3 _startRootPosition;
        private Quaternion _startRootRotation;
        private bool _finished;
        private string _abortReason = "прерван";

        // ── Запросы ─────────────────────────────────────────────────────────

        internal static void HandleRequest(NetworkConnectionToClient conn, StressTestRequestMessage msg)
        {
            if (!msg.start)
            {
                if (Current != null && Current._initiator == conn) Abort("остановлен игроком");
                return;
            }

            if (!CanStart(conn, out PlayerSession session, out string reason))
            {
                GameLog.Perf.Warning($"[StressTest] Отказ в старте (conn {conn.connectionId}): {reason}.");
                conn.Send(new StressTestStatusMessage { kind = StressTestStatusKind.Rejected, text = reason });
                return;
            }

            // Не DontDestroyOnLoad: смена сцены уничтожает кукол вместе с ней, и прогон
            // обязан закончиться тут же, а не мерить пустую новую карту.
            var go = new GameObject("StressTestServer");
            var server = go.AddComponent<StressTestServer>();
            server._initiator = conn;
            server._session = session;
            server._config = msg.ToConfig();
        }

        private static bool CanStart(NetworkConnectionToClient conn, out PlayerSession session, out string reason)
        {
            session = conn.identity != null ? conn.identity.GetComponent<PlayerSession>() : null;

            if (Current != null)                                     { reason = "стресс-тест уже идёт"; return false; }
            if (session == null || session.ActiveAvatar == null)    { reason = "у игрока нет аватара"; return false; }

            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
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

            // Хост меряет себя клиентской половиной; свой лог — только у выделенного сервера.
            if (!NetworkClient.active)
            {
                _report = PerfRunReport.Create("выделенный сервер", _config.puppetCount, _config.clutterCount);
                _recorder = new PerfFrameRecorder(_report.Directory, _report.BudgetMs, _report.BuildHeader());
            }

            GameLog.Perf.Info($"[StressTest] Сервер: старт для {_session.PlayerName}, кукол {_config.puppetCount}, предметов {_config.clutterCount}.");

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
            yield return Phase("база", _config.phaseSeconds, measured: true, note: null);

            SpawnPuppets();
            yield return Phase("куклы~успокоение", _config.settleSeconds, measured: false, note: $"заспавнено кукол: {_puppets.Count}");
            yield return Phase("куклы", _config.phaseSeconds, measured: true, note: null);

            if (_config.clutterCount > 0)
            {
                SpawnClutter();
                yield return Phase("куклы+хлам~успокоение", _config.settleSeconds, measured: false, note: $"высыпано предметов: {_clutter.Count}");
                yield return Phase("куклы+хлам", _config.phaseSeconds, measured: true, note: null);
            }

            Finish(completed: true, "полный прогон");
            Destroy(gameObject);
        }

        private IEnumerator Phase(string name, float seconds, bool measured, string note)
        {
            if (_recorder != null)
            {
                ClosePhase();
                _recorder.BeginPhase(name, measured, seconds);
                if (note != null) PerfEvents.Note(note);
            }

            Send(new StressTestStatusMessage
            {
                kind         = StressTestStatusKind.Phase,
                phase        = name,
                measured     = measured,
                seconds      = seconds,
                puppetCount  = _puppets.Count,
                clutterCount = _clutter.Count,
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

        /// <summary>
        /// Куклы встают рядами перед игроком, лицом туда же, куда он: весь прогон в поле
        /// зрения (худший случай для рендера), выстрелы уйдут от игрока. Скины чередуются
        /// по реестру команд — как в живом матче.
        /// </summary>
        private void SpawnPuppets()
        {
            List<GameObject> prefabs = CollectAvatarPrefabs();
            if (prefabs.Count == 0)
            {
                GameLog.Perf.Warning("[StressTest] Куклы не созданы: в TeamRegistry нет ни одного префаба аватара.");
                return;
            }

            int count  = Mathf.Max(0, _config.puppetCount);
            int perRow = Mathf.Max(1, _config.puppetsPerRow);

            for (int i = 0; i < count; i++)
            {
                int row   = i / perRow;
                int col   = i % perRow;
                int inRow = Mathf.Min(perRow, count - row * perRow);

                var offset = new Vector3((col - (inRow - 1) * 0.5f) * _config.puppetSpacing, 0f,
                                         _config.firstRowDistance + row * _config.puppetSpacing);
                Vector3 position = _startRootPosition + _startRootRotation * offset;

                GameObject prefab = prefabs[i % prefabs.Count];
                GameObject go = Instantiate(prefab, position, _startRootRotation);
                go.name = $"StressPuppet {i + 1} ({prefab.name})";

                PlayerController pc = go.GetComponent<PlayerController>();
                if (pc != null) pc.AvatarPlayerName = $"Кукла {i + 1}";

                // Без владельца ClientToServer не рассылается — см. описание класса.
                foreach (NetworkTransformBase nt in go.GetComponentsInChildren<NetworkTransformBase>(true))
                {
                    nt.syncDirection = SyncDirection.ServerToClient;
                }

                float delay = count > 1 ? Mathf.Lerp(0.1f, _config.maxDelaySeconds, (float)i / (count - 1)) : 0.1f;
                StressPuppet puppet = go.AddComponent<StressPuppet>();
                puppet.Initialize(delay, position, _startRootRotation);

                NetworkServer.Spawn(go);
                _puppets.Add(puppet);
                PerfEvents.Count(PerfEventKind.Spawn);
            }
        }

        private static List<GameObject> CollectAvatarPrefabs()
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
            if (NetworkServer.active)
            {
                foreach (StressPuppet puppet in _puppets)
                {
                    if (puppet != null) NetworkServer.Destroy(puppet.gameObject);
                }
                foreach (GameObject item in _clutter)
                {
                    if (item != null) NetworkServer.Destroy(item);
                }
            }

            _puppets.Clear();
            _clutter.Clear();
        }
    }
}

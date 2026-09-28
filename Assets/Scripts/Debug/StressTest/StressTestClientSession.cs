using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Клиентская половина стресс-теста: меряет кадры этого шлема, пока сервер гоняет
    /// нагрузку. Фазы открываются по <see cref="StressTestStatusMessage"/> от сервера —
    /// он знает, когда заспавнил кукол, клиент знает только свои кадры.
    ///
    /// <para>
    /// Живёт с первого сообщения о фазе до сообщения о конце прогона (или обрыва сети),
    /// переживает смену сцены: лог должен дописаться, даже если прогон прервала карта.
    /// Результат — <c>persistentDataPath/perf/&lt;время&gt;/</c> на этом устройстве.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-10000)] // Tick рекордера — первым в кадре, см. PerfFrameRecorder.
    public sealed class StressTestClientSession : MonoBehaviour
    {
        public static StressTestClientSession Current { get; private set; }
        public static bool IsRunning => Current != null;

        private PerfFrameRecorder _recorder;
        private PerfRunReport _report;
        private bool   _phaseOpen;
        private string _phaseName = "-";
        private float  _phaseEndsAt;
        private int    _puppets;
        private int    _clutter;
        private float  _nextOverlayAt;
        private bool   _finished;

        // ── Сообщения сервера ───────────────────────────────────────────────

        internal static void HandleStatus(StressTestStatusMessage msg)
        {
            switch (msg.kind)
            {
                case StressTestStatusKind.Rejected:
                    GameLog.Perf.Warning($"[StressTest] Сервер отказал: {msg.text}.");
                    PerfOverlay.Show("Стресс-тест не запущен:\n" + msg.text, 6f);
                    break;

                case StressTestStatusKind.Phase:
                    EnsureCurrent().BeginPhase(msg);
                    break;

                case StressTestStatusKind.Finished:
                    if (Current != null) Current.Finish(msg.completed, msg.text);
                    break;
            }
        }

        private static StressTestClientSession EnsureCurrent()
        {
            if (Current != null) return Current;

            var go = new GameObject(nameof(StressTestClientSession));
            DontDestroyOnLoad(go);
            return go.AddComponent<StressTestClientSession>();
        }

        // ── Жизненный цикл ──────────────────────────────────────────────────

        private void Awake()
        {
            Current = this;

            string role = NetworkServer.active ? "хост (сервер в этом же процессе)" : "клиент";
            _report = PerfRunReport.Create(role, 0, 0);
            _recorder = new PerfFrameRecorder(_report.Directory, _report.BudgetMs, _report.BuildHeader());
            GameLog.Perf.Info($"[StressTest] Клиент: запись начата. Лог: {_recorder.LogPath}");
        }

        private void BeginPhase(StressTestStatusMessage msg)
        {
            ClosePhase();

            _recorder.BeginPhase(msg.phase, msg.measured);
            if (!string.IsNullOrEmpty(msg.text)) PerfEvents.Note(msg.text);

            _phaseOpen   = true;
            _phaseName   = msg.phase;
            _phaseEndsAt = Time.realtimeSinceStartup + msg.seconds;
            _puppets     = msg.puppetCount;
            _clutter     = msg.clutterCount;
            _report.puppetCount  = Mathf.Max(_report.puppetCount, msg.puppetCount);
            _report.clutterCount = Mathf.Max(_report.clutterCount, msg.clutterCount);
            _nextOverlayAt = 0f;
        }

        private void ClosePhase()
        {
            if (!_phaseOpen) return;
            _phaseOpen = false;

            PerfPhaseReport phase = _recorder.EndPhase();
            if (phase != null) _report.phases.Add(phase);
        }

        private void Update()
        {
            if (_finished) return;

            _recorder.Tick();

            if (!NetworkClient.active)
            {
                Finish(false, "соединение с сервером потеряно");
                return;
            }

            // Табличка — два раза в секунду: сборка строки каждый кадр сама давала бы
            // GC-аллокации и портила бы замер, который показывает.
            if (Time.unscaledTime >= _nextOverlayAt)
            {
                _nextOverlayAt = Time.unscaledTime + 0.5f;
                float left  = Mathf.Max(0f, _phaseEndsAt - Time.realtimeSinceStartup);
                float level = _recorder.LevelMs;
                float fps   = level > 0f ? 1000f / level : 0f;

                PerfOverlay.Show(
                    $"Стресс-тест · {_phaseName} · {left:F0} с\n" +
                    $"кадр {level:F1} мс ({fps:F0} FPS), бюджет {_recorder.BudgetMs:F1} мс\n" +
                    $"кукол {_puppets} · предметов {_clutter}\n" +
                    "Двигайтесь — куклы повторяют. Оба стика 2 с — прервать.");
            }
        }

        private void Finish(bool completed, string reason)
        {
            if (_finished) return;
            _finished = true;

            ClosePhase();
            _report.completed = completed;
            _report.endReason = reason;
            _report.Save();

            string result = _report.BuildResultText();
            _recorder.WriteLine("=== итог прогона: " + reason + "\n" + result);
            PerfOverlay.Show($"Стресс-тест {(completed ? "завершён" : "прерван: " + reason)}\n{result}\nЛог: perf/{_report.startedAt}", 30f);

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!_finished) Finish(false, "сессия уничтожена");

            _recorder?.Dispose();
            _recorder = null;

            if (Current == this) Current = null;
        }
    }
}

using System.Collections.Generic;
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

        /// <summary>Текущая фаза и сколько секунд до её конца — для строки статуса на планшете.</summary>
        public string PhaseName => _phaseName;
        public float PhaseSecondsLeft => Mathf.Max(0f, _phaseEndsAt - Time.realtimeSinceStartup);

        /// <summary>Номер текущей фазы с 1 (сколько фаз сервер уже открыл).</summary>
        public int PhaseNumber => _phaseNumber;

        /// <summary>
        /// perf.log последнего прогона на этой машине — переживает конец прогона, чтобы экран
        /// «Перф-тесты» показал путь для <c>adb pull</c>. Пусто — прогонов с запуска не было.
        /// </summary>
        public static string LastLogPath { get; private set; } = string.Empty;

        /// <summary>Последнее слово сервера: отказ в старте или итог прогона.</summary>
        public static string LastServerText { get; private set; } = string.Empty;

        /// <summary>
        /// Сколько отказов в старте пришло от сервера с запуска процесса. Наблюдатель (сценарий e2e) отличает
        /// по нему новый отказ от старого текста <see cref="LastServerText"/>.
        /// </summary>
        public static int RejectionCount { get; private set; }

        /// <summary>Итог последнего прогона на этой машине; null — прогонов с запуска не было. Пишет только <see cref="Finish"/>.</summary>
        public static StressTestRunRecord LastRun { get; private set; }

        /// <summary>Каталог отчёта этого прогона (<see cref="PerfRunReport.Directory"/>).</summary>
        public string RunDirectory => _report != null ? _report.Directory : string.Empty;

        private PerfFrameRecorder _recorder;
        private PerfRunReport _report;
        private bool   _phaseOpen;
        private string _phaseName = "-";
        private float  _phaseEndsAt;
        private int    _puppets;
        private int    _clutter;
        private string _skins = "-";
        private float  _nextOverlayAt;
        private bool   _finished;
        private int    _phaseNumber;
        // Точки обзора измеряемых фаз, ещё не закрытых: имя фазы → снимок (SetPhaseView).
        private readonly Dictionary<string, PerfViewReport> _pendingViews = new Dictionary<string, PerfViewReport>();

        // ── Сообщения сервера ───────────────────────────────────────────────

        internal static void HandleStatus(StressTestStatusMessage msg)
        {
            // Обработчик сообщения Mirror: исключение отсюда Mirror считает «битыми данными» и рвёт
            // соединение клиента — замер не должен ронять игру. Ошибку пишем, сессию закрываем.
            try
            {
                HandleStatusUnsafe(msg);
            }
            catch (System.Exception e)
            {
                GameLog.Perf.Error($"[StressTest] Клиент: сбой обработки статуса «{msg.kind}» — запись прервана. {e}");
                LastServerText = "сбой записи: " + e.GetType().Name;
                if (Current != null) Destroy(Current.gameObject);
            }
        }

        private static void HandleStatusUnsafe(StressTestStatusMessage msg)
        {
            switch (msg.kind)
            {
                case StressTestStatusKind.Rejected:
                    GameLog.Perf.Warning($"[StressTest] Сервер отказал: {msg.text}.");
                    LastServerText = "отказ: " + msg.text;
                    RejectionCount++;
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

            try
            {
                _recorder = new PerfFrameRecorder(_report.Directory, _report.BudgetMs, _report.BuildHeader());
            }
            catch (System.Exception e)
            {
                // Без рекордера сессия бесполезна; «полуживая» роняла бы Update и следующий статус.
                GameLog.Perf.Error($"[StressTest] Клиент: не открыть лог {_report.Directory}: {e.Message}");
                LastServerText = "сбой записи: " + e.GetType().Name;
                _finished = true;
                Current = null;
                Destroy(gameObject);
                throw;
            }

            GameLog.Perf.Info($"[StressTest] Клиент: запись начата. Лог: {_recorder.LogPath}. {_report.display}");
            LastLogPath = _recorder.LogPath;
            LastServerText = "прогон идёт";
        }

        private void BeginPhase(StressTestStatusMessage msg)
        {
            ClosePhase();

            _recorder.BeginPhase(msg.phase, msg.measured, msg.seconds);
            if (!string.IsNullOrEmpty(msg.text)) PerfEvents.Note(msg.text);

            _phaseOpen   = true;
            _phaseName   = msg.phase;
            _phaseNumber++;
            _phaseEndsAt = Time.realtimeSinceStartup + msg.seconds;
            _puppets     = msg.puppetCount;
            _clutter     = msg.clutterCount;
            _skins       = string.IsNullOrEmpty(msg.skins) ? "-" : msg.skins;
            _report.puppetCount  = Mathf.Max(_report.puppetCount, msg.puppetCount);
            _report.clutterCount = Mathf.Max(_report.clutterCount, msg.clutterCount);
            _report.AddSkins(msg.skins);
            _nextOverlayAt = 0f;
        }

        private void ClosePhase()
        {
            if (!_phaseOpen) return;
            _phaseOpen = false;

            PerfPhaseReport phase = _recorder.EndPhase();
            if (phase == null) return;

            if (_pendingViews.TryGetValue(phase.name, out PerfViewReport view))
            {
                phase.view = view;
                _pendingViews.Remove(phase.name);
            }
            _report.phases.Add(phase);
        }

        /// <summary>
        /// Точка обзора измеряемой фазы <paramref name="phaseName"/> и фактическая поза камеры — в отчёт этой фазы
        /// (<c>summary.json</c>, поле <c>view</c>) и строкой в perf.log. Зовёт наблюдатель, который переносит аватар
        /// (сценарий e2e обхода), обычно в её «~успокоение»; фаза уже закрыта — пишется в её отчёт сразу. Отчёт
        /// по-прежнему пишет только эта сессия.
        /// </summary>
        public void SetPhaseView(string phaseName, PerfViewReport view)
        {
            if (_finished || string.IsNullOrEmpty(phaseName) || view == null) return;

            _recorder.WriteLine(view.ok
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "точка обзора «{0}» для фазы «{1}»: камера ({2:F2}, {3:F2}, {4:F2}), курс {5:F0}°, тангаж {6:F1}°, " +
                    "глаза {7:F2} м, отклонение {8:F2} м",
                    view.name, phaseName, view.camera.x, view.camera.y, view.camera.z, view.yaw, view.pitch,
                    view.eyeHeight, view.deviationXZ)
                : $"точка обзора «{view.name}» для фазы «{phaseName}»: перенос не удался — {view.error}",
                warning: !view.ok);

            foreach (PerfPhaseReport closed in _report.phases)
            {
                if (closed.name != phaseName) continue;
                closed.view = view;
                return;
            }
            _pendingViews[phaseName] = view;
        }

        private void Update()
        {
            if (_finished || _recorder == null) return;

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
                    $"скины: {_skins}\n" +
                    "Двигайтесь — куклы повторяют. Прервать — планшет: «Отладка» · «Перф-тесты» · «Стоп».");
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
            LastServerText = completed ? "прогон завершён: " + reason : "прогон прерван: " + reason;
            LastRun = new StressTestRunRecord(_report.Directory, completed, reason, _phaseNumber);
            PerfOverlay.Show($"Стресс-тест {(completed ? "завершён" : "прерван: " + reason)}\n{result}\nЛог: perf/{_report.startedAt}", 30f);

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Рекордер мог не создаться (Awake упал) — тогда и дописывать нечего.
            if (!_finished && _recorder != null && _report != null) Finish(false, "сессия уничтожена");
            _finished = true;

            _recorder?.Dispose();
            _recorder = null;

            if (Current == this) Current = null;
        }
    }
}

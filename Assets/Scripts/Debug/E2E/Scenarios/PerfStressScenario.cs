// Ярус C (два процесса) — сценарий perf-stress: стресс-тест на шлеме без рук (задача perf-stress-scenario).
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>perf-stress</c>: запускает существующий стресс-тест (<see cref="StressTestLauncher"/>) со шлема
    /// без человека и ждёт его конца. Измерительный, а не сторожевой: бюджетных порогов нет, вердикт — «прогон
    /// состоялся полностью и отчёт записан»; числа оценивает performance по <c>summary.json</c>/<c>perf.log</c>.
    ///
    /// <para>
    /// <b>Топология.</b> Шлем — клиент <c>client-1</c>, выделенный Windows-плеер на ПК — <c>server</c>
    /// (<c>Tools/e2e/Run-QuestE2E.ps1 -Scenario perf-stress</c>). Параметры (<c>-Arg</c>) приходят обеим сторонам;
    /// обе разбирают их одним <see cref="StressTestArgs"/> и одинаково отказывают до старта.
    /// </para>
    ///
    /// <para>
    /// <b>Клиент.</b> Подключение и свой аватар → разминка (реплицированный режим судьи карты) → <see cref="StressTestLauncher.TryStart"/>.
    /// Отказ сервера (например, «только в разминке» при гонке репликации) повторяется до срока старта. Конец прогона
    /// и его итог — <see cref="StressTestClientSession.LastRun"/>; все фазы плана (<see cref="StressTestPlan.Phases"/>)
    /// должны быть открыты и прогон не прерван. Каждый кадр прогона проверяется <see cref="Application.isFocused"/>
    /// (плюс событие <see cref="Application.focusChanged"/> — на паузе кадров нет): потеря фокуса — прогон не замер.
    /// </para>
    ///
    /// <para>
    /// <b>Сервер.</b> Ждёт клиента, начало и конец своего прогона (<see cref="StressTestServer.LastRun"/>), объявляет
    /// свой каталог отчёта и держит соединение, пока клиент не запишет вердикт.
    /// </para>
    ///
    /// <para>
    /// Раскладка <c>layout=arena</c> (<see cref="StressTestArgs"/>) — куклы на полу физической арены карты; нет арены —
    /// сервер отказывает в старте (проверка «старт принят сервером»). Сколько кукол поместилось — в summary сервера.
    /// </para>
    ///
    /// <para>
    /// FFR, уровни CPU/GPU и настройки шлема сценарий не трогает. Поза лежащего шлема статична — это условие прогона
    /// (<c>pose=static</c> в summary вердикта).
    /// </para>
    /// </summary>
    public class PerfStressScenario : IE2EScenario
    {
        public string Name => "perf-stress";

        // ── Проверки ────────────────────────────────────────────────────────

        private const string CheckArgs    = "параметры прогона разобраны";
        private const string CheckTimeout = "время плана помещается в таймаут сценария";

        private const string CheckJoined    = "клиент подключился, есть своя сессия с аватаром";
        private const string CheckWarmup    = "карта в разминке";
        private const string CheckStart     = "старт принят сервером";
        private const string CheckCompleted = "все фазы плана пройдены, прогон не прерван";
        private const string CheckReport    = "отчёт записан (summary.json и perf.log)";
        private const string CheckFocus     = "приложение в фокусе весь прогон";

        private const string CheckServerJoined    = "клиент подключился к серверу";
        private const string CheckServerStarted   = "прогон начат на сервере";
        private const string CheckServerCompleted = "все фазы плана пройдены на сервере, прогон не прерван";
        private const string CheckServerReport    = "отчёт сервера записан (summary.json и perf.log)";

        // ── Сроки ───────────────────────────────────────────────────────────

        /// <summary>Запас до общего таймаута исполнителя: вердикт сценария должен успеть записаться раньше.</summary>
        private const float TimeoutMargin = 10f;

        /// <summary>Подключение (Discovery до 30 с, затем прямое), аватар, разминка, ответ сервера на старт.</summary>
        private const float StartReserve = 60f;

        /// <summary>Конец прогона: сообщение «прогон окончен», запись отчёта, вердикт клиента до выхода сервера.</summary>
        private const float FinishReserve = 15f;

        /// <summary>
        /// Сервер запускается раньше шлема (дирижёр ждёт его, потом будит шлем и стартует приложение), а таймаут у обоих
        /// одинаковый от своего старта: клиент обязан кончить прогон раньше, чем истечёт срок сервера.
        /// </summary>
        private const float ServerLead = 30f;

        /// <summary>Весь запас сверх плана фаз — одинаковый для обеих ролей.</summary>
        private const float Reserve = StartReserve + FinishReserve + ServerLead;

        /// <summary>На сколько таймаут сценария меньше <c>-Timeout</c> дирижёра (Run-QuestE2E.ps1).</summary>
        private const int ConductorReserve = 30;

        private const float DiscoveryWait   = 30f;
        private const float StartAnswerWait = 10f;
        private const float RetryDelay      = 2f;

        /// <summary>Сколько сервер ждёт отключения клиента после конца прогона: клиент пишет вердикт и выходит.</summary>
        private const float ServerHold = 10f;

        private static float Now => E2EWait.Now;

        public IEnumerator Run(E2EContext context, E2EResult result) =>
            context.IsServerRole ? RunServer(context, result) : RunClient(context, result);

        // ── Общее: параметры и время ────────────────────────────────────────

        /// <summary>
        /// Разбирает параметры и сверяет план с таймаутом; ставит <see cref="CheckArgs"/> и <see cref="CheckTimeout"/>.
        /// Ложь — прогон запускать нельзя, summary уже заполнен.
        /// </summary>
        private static bool Prepare(E2EContext context, E2EResult result, out PerfRunMode mode, out StressTestConfig config,
                                    out int plannedPhases, out float planSeconds)
        {
            int skinCount = StressTestServer.CollectAvatarPrefabs().Count;
            string error = StressTestArgs.Parse(context.Args, skinCount, out mode, out config);
            plannedPhases = 0;
            planSeconds = 0f;

            if (error != null)
            {
                result.Set(CheckArgs, false, error);
                result.Set(CheckTimeout, false, "не проверялось: параметры не разобраны");
                result.Summary = "параметры perf-stress: " + error;
                GameLog.Perf.Error($"[E2E perf-stress] Параметры не разобраны: {error}");
                return false;
            }

            List<StressTestPlannedPhase> phases = StressTestPlan.Phases(config, skinCount);
            plannedPhases = phases.Count;
            planSeconds = StressTestPlan.TotalSeconds(phases);
            result.Set(CheckArgs, true, $"{StressTestArgs.Describe(mode, config)}; скинов {skinCount}; фаз по плану {plannedPhases}");

            float available = context.Timeout - TimeoutMargin;
            string timeError = StressTestArgs.TimeoutError(planSeconds, Reserve, available);
            if (timeError != null)
            {
                int need = Mathf.CeilToInt(planSeconds + Reserve + TimeoutMargin) + ConductorReserve;
                string detail = $"{timeError}. Подними -Timeout дирижёра (Run-QuestE2E.ps1) минимум до {need} с " +
                                $"(сценарию уходит на {ConductorReserve} с меньше)";
                result.Set(CheckTimeout, false, detail);
                result.Summary = "план не помещается в таймаут: " + detail;
                GameLog.Perf.Error($"[E2E perf-stress] {detail}");
                return false;
            }

            result.Set(CheckTimeout, true, string.Format(CultureInfo.InvariantCulture,
                "план {0} + запас {1:F0} с ≤ {2:F0} с", StressTestPlan.FormatDuration(planSeconds), Reserve, available));
            return true;
        }

        private static string Conditions(PerfRunMode mode, StressTestConfig config) =>
            StressTestArgs.Describe(mode, config) + " pose=static";

        /// <summary>Итоговая строка: вердикт, условия прогона и имя каталога отчёта.</summary>
        private static string Summarize(E2EResult result, string conditions, string runDirectory)
        {
            string verdict = result.AllChecksGreen ? "прогон состоялся полностью" : "прогон не замер: " + FirstFailure(result);
            string dir = string.IsNullOrEmpty(runDirectory) ? "-" : Path.GetFileName(runDirectory);
            return $"{verdict}. Условия: {conditions}. Каталог: {dir}";
        }

        private static string FirstFailure(E2EResult result)
        {
            foreach (E2ECheck check in result.Checks)
            {
                if (!check.Passed) return $"«{check.Name}» — {check.Detail}";
            }
            return "нет проверок";
        }

        /// <summary>Отчёт прогона на диске: <c>summary.json</c> и <c>perf.log</c> в каталоге.</summary>
        private static bool ReportWritten(string directory, out string detail)
        {
            if (string.IsNullOrEmpty(directory))
            {
                detail = "каталог прогона неизвестен";
                return false;
            }

            bool summary = File.Exists(Path.Combine(directory, "summary.json"));
            bool log = File.Exists(Path.Combine(directory, "perf.log"));
            detail = $"{directory}: summary.json {(summary ? "есть" : "нет")}, perf.log {(log ? "есть" : "нет")}";
            return summary && log;
        }

        /// <summary>Итог прогона против плана: прогон не прерван и открыто ровно столько фаз, сколько в плане.</summary>
        private static bool RunCompleted(StressTestRunRecord run, int plannedPhases, out string detail)
        {
            bool allPhases = run.PhasesSeen == plannedPhases;
            detail = $"фаз открыто {run.PhasesSeen} из {plannedPhases} по плану; конец: {(run.Completed ? "полный" : "прерван")} — {run.EndReason}";
            if (run.Completed && !allPhases)
                detail += ". План (StressTestPlan.Phases) и сервер (StressTestServer.Run) разошлись";
            return run.Completed && allPhases;
        }

        private static float Remaining(float deadline) => Mathf.Max(0f, deadline - Now);

        // ── Клиент ──────────────────────────────────────────────────────────

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckArgs, CheckTimeout, CheckJoined, CheckWarmup, CheckStart, CheckCompleted, CheckReport, CheckFocus);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий perf-stress");
            float deadline = Now + context.Timeout - TimeoutMargin;

            if (!Prepare(context, result, out PerfRunMode mode, out StressTestConfig config, out int plannedPhases, out float planSeconds))
                yield break;

            string conditions = Conditions(mode, config);
            // Прогон обязан кончиться за FinishReserve + ServerLead до своего срока — см. ServerLead.
            float startDeadline = deadline - planSeconds - FinishReserve - ServerLead;
            GameLog.Perf.Info($"[E2E perf-stress] Клиент: {conditions}; фаз {plannedPhases}, план {StressTestPlan.FormatDuration(planSeconds)}.");

            // ── подключение и свой аватар ──
            var discovered = new E2EWaitOutcome();
            yield return E2EWait.Until(discovered, "подключение через Discovery", Mathf.Min(DiscoveryWait, Remaining(startDeadline)),
                () => NetworkClient.isConnected, DescribeClient);

            string via = "Discovery";
            if (!discovered.Succeeded && !string.IsNullOrEmpty(context.ServerAddress))
            {
                via = $"прямое подключение к {context.ServerAddress}";
                GameLog.Perf.Info($"[E2E perf-stress] Discovery молчит {DiscoveryWait:F0} с, подключаюсь напрямую к {context.ServerAddress}");
                ConnectDirectly(context.ServerAddress);
            }

            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, "своя PlayerSession с аватаром", Remaining(startDeadline),
                () => NetworkClient.isConnected && HasOwnAvatar(), DescribeClient);
            result.Set(CheckJoined, joined.Succeeded, $"путь: {via}. {joined.Diagnosis}");
            if (!joined.Succeeded)
            {
                result.Summary = Summarize(result, conditions, null);
                yield break;
            }

            // ── разминка: реплицированный режим судьи карты, своего состояния не заводим ──
            var warm = new E2EWaitOutcome();
            yield return E2EWait.Until(warm, "режим карты — разминка", Remaining(startDeadline),
                IsWarmup, DescribeMode, () => NetworkClient.isConnected ? null : "соединение с сервером потеряно");
            result.Set(CheckWarmup, warm.Succeeded, warm.Diagnosis);
            if (!warm.Succeeded)
            {
                result.Summary = Summarize(result, conditions, null);
                yield break;
            }

            // ── старт: отказ сервера (гонка репликации разминки/аватара) повторяем до срока старта ──
            int attempts = 0;
            string lastRefusal = "срок старта истёк до первой попытки";
            while (Remaining(startDeadline) > 0f && !StressTestClientSession.IsRunning)
            {
                attempts++;
                int rejectionsBefore = StressTestClientSession.RejectionCount;
                if (!StressTestLauncher.TryStart(config, out string message))
                {
                    lastRefusal = "клиент: " + message;
                }
                else
                {
                    var answer = new E2EWaitOutcome();
                    yield return E2EWait.Until(answer, "ответ сервера на запрос старта", Mathf.Min(StartAnswerWait, Remaining(startDeadline)),
                        () => StressTestClientSession.IsRunning || StressTestClientSession.RejectionCount != rejectionsBefore,
                        () => $"последнее слово сервера: «{StressTestClientSession.LastServerText}»",
                        () => NetworkClient.isConnected ? null : "соединение с сервером потеряно");
                    if (StressTestClientSession.IsRunning) break;
                    lastRefusal = StressTestClientSession.RejectionCount != rejectionsBefore
                        ? "сервер: " + StressTestClientSession.LastServerText
                        : answer.Diagnosis;
                }

                if (!NetworkClient.isConnected) break;
                GameLog.Perf.Warning($"[E2E perf-stress] Старт не принят (попытка {attempts}): {lastRefusal}. Повтор через {RetryDelay:F0} с.");
                yield return E2EWait.Hold(Mathf.Min(RetryDelay, Remaining(startDeadline)));
            }

            StressTestClientSession session = StressTestClientSession.Current;
            if (session == null)
            {
                result.Set(CheckStart, false, $"попыток {attempts}, старт не принят до срока; последний отказ — {lastRefusal}");
                result.Summary = Summarize(result, conditions, null);
                yield break;
            }

            string runDirectory = session.RunDirectory;
            result.Set(CheckStart, true, $"попыток {attempts}; каталог {runDirectory}");
            GameLog.Perf.Info($"[E2E perf-stress] Прогон начат: {runDirectory}.");

            // ── прогон: ждём конца, фокус — каждый кадр ──
            float runStarted = Now;
            var focus = new FocusWatch();
            focus.Begin();
            try
            {
                while (StressTestClientSession.IsRunning && Now < deadline)
                {
                    focus.Sample();
                    yield return null;
                }
            }
            finally
            {
                focus.End();
            }

            StressTestRunRecord run = StressTestClientSession.LastRun;
            if (run == null || run.Directory != runDirectory)
            {
                result.Set(CheckCompleted, false, StressTestClientSession.IsRunning
                    ? $"прогон не кончился за срок сценария: идёт {Now - runStarted:F0} с при плане {planSeconds:F0} с"
                    : $"итог прогона не записан; последнее слово сервера: «{StressTestClientSession.LastServerText}»");
            }
            else
            {
                bool completed = RunCompleted(run, plannedPhases, out string detail);
                result.Set(CheckCompleted, completed, detail);
            }

            bool written = ReportWritten(runDirectory, out string reportDetail);
            result.Set(CheckReport, written, reportDetail);
            result.Set(CheckFocus, focus.Losses == 0, focus.Describe());

            if (!string.IsNullOrEmpty(runDirectory) && Directory.Exists(runDirectory))
                result.AddArtifact(runDirectory);
            string history = Path.Combine(Application.persistentDataPath, "perf", "history.log");
            if (File.Exists(history))
                result.AddArtifact(history);

            result.Summary = Summarize(result, conditions, runDirectory);
            GameLog.Perf.Info($"[E2E perf-stress] Клиент: {result.Summary}");
        }

        private static bool HasOwnAvatar()
        {
            PlayerSession session = PlayerSession.LocalSession;
            return session != null && session.ActiveAvatar != null;
        }

        private static bool IsWarmup()
        {
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;
            return mode != null && mode.IsWarmup;
        }

        private static string DescribeMode()
        {
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;
            return $"сцена «{SceneManager.GetActiveScene().name}», судья карты {(referee != null ? "есть" : "нет")}, " +
                   $"режим {(mode != null ? mode.GetType().Name + (mode.IsWarmup ? " (разминка)" : " (не разминка)") : "не назначен")}";
        }

        /// <summary>
        /// Прямое подключение мимо Discovery — через владельца сети (<see cref="GameNetworkDiscovery.RequestClientConnect"/>),
        /// чтобы шов подготовки сети применил пару портов прогона. Прямой StartClient ушёл бы на порт сцены.
        /// </summary>
        private static void ConnectDirectly(string address)
        {
            GameNetworkDiscovery owner = NetworkManager.singleton != null ? NetworkManager.singleton.GetComponent<GameNetworkDiscovery>() : null;
            if (owner == null || !GameNetworkDiscovery.ClientManagerReady)
            {
                GameLog.Error("[E2E perf-stress] Владелец сети не готов к подключению — прямой путь недоступен.");
                return;
            }

            if (NetworkClient.active)
                return;

            try { owner.RequestClientConnect(address); }
            catch (InvalidOperationException e) { GameLog.Error($"[E2E perf-stress] Прямое подключение отклонено владельцем сети: {e.Message}"); }
        }

        private static string DescribeClient()
        {
            NetworkManager manager = NetworkManager.singleton;
            PlayerSession session = PlayerSession.LocalSession;
            return $"адрес менеджера={(manager != null ? manager.networkAddress : "менеджера нет")}, " +
                   $"NetworkClient.active={NetworkClient.active}, isConnected={NetworkClient.isConnected}, " +
                   $"своя сессия={(session != null ? "есть" : "нет")}, аватар={(session != null && session.ActiveAvatar != null ? "есть" : "нет")}";
        }

        /// <summary>
        /// Фокус приложения за прогон: опрос каждый кадр и событие <see cref="Application.focusChanged"/> — на паузе
        /// (шлем снят, системное окно) кадров нет, и одного опроса мало. Длительность — по монотонным часам системы.
        /// </summary>
        private sealed class FocusWatch
        {
            private bool _active;
            private bool _lost;
            private long _lostAt;
            private double _lostSeconds;
            private int _losses;
            private int _frames;

            public int Losses => _losses;

            public void Begin()
            {
                _active = true;
                Application.focusChanged += Apply;
                Apply(Application.isFocused);
            }

            public void Sample()
            {
                _frames++;
                Apply(Application.isFocused);
            }

            public void End()
            {
                if (!_active) return;
                Application.focusChanged -= Apply;
                if (_lost) _lostSeconds += Elapsed(_lostAt);
                _lost = false;
                _active = false;
            }

            public string Describe() => _losses == 0
                ? $"фокус не терялся; кадров проверено {_frames}"
                : string.Format(CultureInfo.InvariantCulture,
                    "фокус терялся {0} раз, всего {1:F1} с — прогон не замер (системное окно, снятый шлем, сон); кадров проверено {2}",
                    _losses, _lostSeconds, _frames);

            private void Apply(bool focused)
            {
                if (!_active) return;
                if (!focused && !_lost)
                {
                    _lost = true;
                    _losses++;
                    _lostAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    GameLog.Perf.Warning($"[E2E perf-stress] Приложение потеряло фокус во время прогона (раз {_losses}).");
                }
                else if (focused && _lost)
                {
                    _lost = false;
                    _lostSeconds += Elapsed(_lostAt);
                }
            }

            private static double Elapsed(long since) =>
                (System.Diagnostics.Stopwatch.GetTimestamp() - since) / (double)System.Diagnostics.Stopwatch.Frequency;
        }

        // ── Сервер ──────────────────────────────────────────────────────────

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckArgs, CheckTimeout, CheckServerJoined, CheckServerStarted, CheckServerCompleted, CheckServerReport);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий perf-stress");
            float deadline = Now + context.Timeout - TimeoutMargin;

            if (!Prepare(context, result, out PerfRunMode mode, out StressTestConfig config, out int plannedPhases, out float planSeconds))
                yield break;

            string conditions = Conditions(mode, config);
            float startDeadline = deadline - planSeconds - FinishReserve;
            StressTestRunRecord before = StressTestServer.LastRun;
            GameLog.Perf.Info($"[E2E perf-stress] Сервер: {conditions}; фаз {plannedPhases}, план {StressTestPlan.FormatDuration(planSeconds)}.");

            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, "клиентское подключение с сессией", Remaining(startDeadline),
                () => NetworkServer.active && ClientConnections() >= 1 && SessionCount() >= 1, DescribeServer);
            result.Set(CheckServerJoined, joined.Succeeded, joined.Diagnosis);
            if (!joined.Succeeded)
            {
                result.Summary = Summarize(result, conditions, null);
                yield break;
            }

            var started = new E2EWaitOutcome();
            yield return E2EWait.Until(started, "сервер начал прогон стресс-теста", Remaining(startDeadline),
                () => StressTestServer.Current != null || !ReferenceEquals(StressTestServer.LastRun, before),
                DescribeServer,
                () => !NetworkServer.active ? "сервер погас"
                    : ClientConnections() == 0 ? "клиент отключился до старта прогона — причина в его вердикте (client-1.json)"
                    : null);
            result.Set(CheckServerStarted, started.Succeeded, started.Diagnosis);
            if (!started.Succeeded)
            {
                result.Summary = Summarize(result, conditions, null);
                yield break;
            }

            var ended = new E2EWaitOutcome();
            yield return E2EWait.Until(ended, "конец прогона на сервере", Remaining(deadline),
                () => StressTestServer.Current == null && !ReferenceEquals(StressTestServer.LastRun, before),
                DescribeServer);

            // Каталог — из итога: отчёт сервер заводит в Start, кадром позже появления Current.
            StressTestRunRecord run = StressTestServer.LastRun;
            bool ours = ended.Succeeded && run != null && !ReferenceEquals(run, before);
            StressTestServer current = StressTestServer.Current;
            string runDirectory = ours ? run.Directory : current != null ? current.RunDirectory : string.Empty;
            if (!ours)
            {
                result.Set(CheckServerCompleted, false, ended.Diagnosis);
            }
            else
            {
                bool completed = RunCompleted(run, plannedPhases, out string detail);
                result.Set(CheckServerCompleted, completed, detail);

                // Раскладка «арена»: фактическое число кукол и недостача — в условия прогона. Не проверка:
                // бюджета на недостачу нет, её оценивает performance по отчёту.
                if (run.Placement.Length > 0)
                {
                    conditions += "; " + run.Placement;
                    GameLog.Perf.Info($"[E2E perf-stress] Сервер: {run.Placement}.");
                }
            }

            bool written = ReportWritten(runDirectory, out string reportDetail);
            result.Set(CheckServerReport, written, reportDetail);
            if (!string.IsNullOrEmpty(runDirectory) && Directory.Exists(runDirectory))
                result.AddArtifact(runDirectory);

            result.Summary = Summarize(result, conditions, runDirectory);
            GameLog.Perf.Info($"[E2E perf-stress] Сервер: {result.Summary}");

            // Сервер выходит по вердикту: дать клиенту получить «прогон окончен», записать отчёт и свой вердикт.
            var released = new E2EWaitOutcome();
            yield return E2EWait.Until(released, "клиент записал вердикт и отключился", Mathf.Min(ServerHold, Remaining(deadline)),
                () => ClientConnections() == 0, DescribeServer);
        }

        /// <summary>Подключения удалённых клиентов (на выделенном сервере локального соединения нет).</summary>
        private static int ClientConnections()
        {
            int count = 0;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection != null && connection != NetworkServer.localConnection)
                    count++;
            }
            return count;
        }

        private static int SessionCount() => PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;

        private static string DescribeServer()
        {
            StressTestRunRecord last = StressTestServer.LastRun;
            return $"NetworkServer.active={NetworkServer.active}, подключений={ClientConnections()}, сессий={SessionCount()}, " +
                   $"прогон {(StressTestServer.Current != null ? "идёт" : "не идёт")}, последний итог: {(last != null ? last.ToString() : "нет")}";
        }
    }
}
#endif

// Ярус C (два процесса) — точка входа харнесса внутри плеера.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E.Scenarios;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Поднимает и прокручивает e2e-сценарий внутри собранного плеера.
    ///
    /// Ставится в игру не через сцену и не через префаб, а через
    /// <see cref="RuntimeInitializeOnLoadMethod"/>: сцены и префабы в этом проекте —
    /// общий ресурс, и харнесс, который их правит, ломает обычный запуск игры.
    /// Без аргумента <c>-e2eScenario</c> компонент не создаётся вообще, поэтому
    /// в обычном билде код полностью инертен.
    ///
    /// Гарантия для дирижёра: файл результата пишется <b>всегда</b> —
    /// заглушка на старте, вердикт по завершении, отдельный статус на таймаут,
    /// исключение и досрочный выход процесса.
    /// </summary>
    public class E2ERunner : MonoBehaviour
    {
        private E2EContext _context;
        private E2EResult _result;
        private float _startedRealtime;
        private bool _finished;
        private bool _quitServerOnFinish;
        private IDisposable _identityScope;
        private int _writtenStepsVersion;
        private float _nextInterimWrite;
        private static E2ERunner _active;
        private Func<Mirror.NetworkManager, Mirror.Discovery.NetworkDiscovery, Network.GameNetworkDiscovery.AppRole, string> _diagnosticRefusal;
        private Func<Mirror.NetworkManager, Mirror.Discovery.NetworkDiscovery, Network.GameNetworkDiscovery.AppRole, string> _previousPlayerPreparation;
        private UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode> _serverStartup;

        /// <summary>Проверка, которую ставит abort сервера у клиента.</summary>
        public const string CheckServerAbort = "сервер не прерывал прогон";
        /// <summary>Не чаще, чем раз в столько секунд, исполнитель переписывает файл результата ради новых шагов.</summary>
        private const float InterimWriteSeconds = 1f;
        public bool Finished => _finished;
        public E2EResult Result => _result;


        // ── Бутстрап ───────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            E2EContext context = E2EContext.FromCommandLine();
            if (context == null)
                return;

            // Порты прогона ставятся до первой сцены: её GameNetworkDiscovery.Start уже поднимает сеть.
            if (!Application.isEditor)
                E2EPlayerPorts.Install(context);

            Begin(context);
        }

        /// <summary>Один исполнитель для CLI и Editor. Профиль и запуск сети ведёт вызывающий адаптер.</summary>
        public static E2ERunner Begin(E2EContext context, bool quitServerOnFinish = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.ExternallyLaunched != DebugBootstrapGate.ManagedLaunchActive && Application.isEditor)
                throw new InvalidOperationException("E2E bootstrap конфликтует с владельцем Play Launch.");
            if (_active != null) throw new InvalidOperationException("E2E-сценарий уже запущен в этом процессе.");
            GameObject host = new GameObject("E2ERunner");
            DontDestroyOnLoad(host);
            var runner = host.AddComponent<E2ERunner>();
            _active = runner;
            try
            {
                if (!string.IsNullOrEmpty(context.DeviceToken))
                {
                    if (ClientDeviceIdentity.HasTemporaryToken)
                    {
                        if (ClientDeviceIdentity.BaseToken != context.DeviceToken)
                            throw new InvalidOperationException("Идентичность E2E отличается от идентичности текущего стенда.");
                    }
                    else runner._identityScope = ClientDeviceIdentity.BeginTemporaryToken(context.DeviceToken);
                }
                runner._quitServerOnFinish = quitServerOnFinish && !Application.isEditor;
                runner.Initialize(context);
                return runner;
            }
            catch
            {
                runner._identityScope?.Dispose();
                runner._identityScope = null;
                _active = null;
                Destroy(host);
                throw;
            }
        }

        private void Initialize(E2EContext context)
        {
            _context = context;

            _result = new E2EResult
            {
                Scenario = context.Scenario,
                Role = context.Role,
                Status = E2EResult.StatusStarted,
                Summary = "сценарий запущен, вердикта ещё нет"
            };
            // Условия сборки — уже в заглушке: дирижёр сверяет стороны и по оборванному прогону.
            _result.CaptureBuildConditions();
            _startedRealtime = Time.realtimeSinceStartup;
            if (_context.DiagnosticError == null && _context.DiagnosticInput != null && !IsRegistered(_context.Scenario))
                _context.DiagnosticError = "invalid-input: неизвестный diagnostic scenario " + _context.Scenario;
            if (_context.DiagnosticError == null && _context.DiagnosticInput != null)
            {
                _context.DiagnosticError = E2EDiagnosticSession.TryCreate(_context, _result, out E2EDiagnosticSession diagnostics);
                _context.AttachDiagnostics(diagnostics);
            }
            if (_context.DiagnosticError != null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = "diagnostic inputs: " + _context.DiagnosticError;
                _result.Set("diagnostic inputs приняты", false, _context.DiagnosticError);
                // Запрещаем автоматический Start сети тем же player-швом, до первой сцены.
                if (!Application.isEditor)
                {
                    _previousPlayerPreparation = DebugBootstrapGate.PlayerBeforeNetworkStart;
                    _diagnosticRefusal = (manager, discovery, role) => "DiagnosticInputInvalid: " + _context.DiagnosticError;
                    DebugBootstrapGate.PlayerBeforeNetworkStart = _diagnosticRefusal;
                }
                _result.WriteTo(_context.ResultPath);
                GameLog.Error("[E2E] " + _result.Summary);
                Finish(2);
                return;
            }

            // Заглушка пишется немедленно: если процесс упадёт или его убьют
            // по таймауту, дирижёр всё равно увидит файл и отличит падение
            // от «плеер не стартовал».
            _result.WriteTo(_context.ResultPath);

            GameLog.Debug.Info($"[E2E] Старт харнесса: {_context}");

            // Барьер шагов: обработчики e2e-сообщений встанут при старте сети (опрос в Update), не раньше.
            E2EBarrier.Activate(_context, _result);

            // Сервер до старта сети: первая сцена и прочее, что нельзя сделать из Run (он начнётся в Start, порядок
            // относительно GameNetworkDiscovery.Start не гарантирован). Окно — загрузка первой сцены (Offline):
            // менеджеры уже прошли Awake (каталог карт доступен), а Start, который поднимает сеть, ещё не вызван.
            // При ошибке портов или параметров прогон валится до сценария (DriveScenario) — подготовку сервера не запускаем.
            if (_context.PortError == null && _context.ArgError == null && _context.IsServerRole &&
                Resolve(_context.Scenario) is IE2EServerStartup startup)
            {
                _serverStartup = (scene, mode) =>
                {
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded -= _serverStartup;
                    _serverStartup = null;
                    string refusal = startup.BeforeNetworkStart(_context);
                    if (refusal != null) GameLog.Debug.Error($"[E2E] Подготовка сервера отказала: {refusal}");
                };
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += _serverStartup;
            }
        }

        private void Start()
        {
            if (_context != null && !_finished) StartCoroutine(DriveScenario());
        }

        private void Update()
        {
            if (_context == null) return;
            _context.Diagnostics?.Poll();
            if (_context.DiagnosticError != null) return;
            // Барьер опрашивает сеть и после вердикта: сервер ещё принимает шаги клиентов, клиент досылает свой failed.
            E2EBarrier.Poll();

            // Шаги — в файл и до вердикта: убитый дирижёром процесс оставит журнал, до какого шага дошёл.
            if (!_finished && _result.Status == E2EResult.StatusStarted && _result.StepsVersion != _writtenStepsVersion &&
                Time.realtimeSinceStartup >= _nextInterimWrite)
            {
                _writtenStepsVersion = _result.StepsVersion;
                _nextInterimWrite = Time.realtimeSinceStartup + InterimWriteSeconds;
                _result.WriteTo(_context.ResultPath);
            }
        }

        // ── Прокрутка сценария ─────────────────────────────────────────────

        private IEnumerator DriveScenario()
        {
            _startedRealtime = Time.realtimeSinceStartup;

            // Неверная пара портов: сценарий не запускаем вовсе, иначе его фоллбэки подключатся на порты по умолчанию.
            if (_context.PortError != null)
            {
                FailOnPorts(_context.PortError);
                yield break;
            }

            // Неверные -e2eArg: сценарий с частью параметров или с умолчаниями вместо заданных не запускаем.
            if (_context.ArgError != null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = $"параметры e2e: {_context.ArgError}";
                _result.AbortPending("прогон остановлен: параметры -e2eArg не разобраны");
                GameLog.Error($"[E2E] Сценарий '{_context.Scenario}' остановлен: параметры e2e: {_context.ArgError}");
                Finish(2);
                yield break;
            }

            // Неверная топология (-e2eLate): барьер ждал бы не тех клиентов.
            if (_context.TopologyError != null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = $"топология e2e: {_context.TopologyError}";
                _result.AbortPending("прогон остановлен: топология e2e не разобрана");
                GameLog.Error($"[E2E] Сценарий '{_context.Scenario}' остановлен: топология e2e: {_context.TopologyError}");
                Finish(2);
                yield break;
            }

            IE2EScenario scenario = Resolve(_context.Scenario);
            if (scenario == null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = $"неизвестный сценарий '{_context.Scenario}'. Доступные: {string.Join(", ", Names())}";
                Finish(2);
                yield break;
            }

            // Сценарий прокручивается вручную, а не через yield return StartCoroutine, и вместе с вложенными
            // итераторами (E2EWait.Until, барьер): исключение любого уровня ловится здесь и попадает в файл результата,
            // а общий таймаут и abort сервера проверяются каждый кадр, в том числе внутри длинного ожидания.
            var steps = new E2EScenarioStepper(scenario.Run(_context, _result));
            float deadline = _startedRealtime + _context.Timeout;

            while (true)
            {
                object current;

                // Отказ шва портов в рантайме: дальше сценарий ушёл бы в фоллбэк StartClient на порт сцены.
                if (E2EPlayerPorts.LastRefusal != null)
                {
                    steps.Dispose();
                    FailOnPorts(E2EPlayerPorts.LastRefusal);
                    yield break;
                }

                // Abort сервера: клиент завершается сразу, именованным провалом.
                string abort = _context.IsServerSide ? null : E2EBarrierCore.Shared.AbortReason;
                if (abort != null)
                {
                    steps.Dispose();
                    FailNamed(CheckServerAbort, "сервер прервал прогон: " + abort);
                    yield break;
                }

                try
                {
                    if (!steps.MoveNext(out current))
                    {
                        _result.Status = E2EResult.StatusCompleted;
                        break;
                    }
                }
                catch (E2EScenarioFailure failure)
                {
                    steps.Dispose();
                    FailNamed(failure.Check, failure.Detail);
                    yield break;
                }
                catch (Exception e)
                {
                    steps.Dispose();
                    _result.Status = E2EResult.StatusError;
                    _result.Summary = $"исключение в сценарии: {e.GetType().Name}: {e.Message}";
                    _result.AbortPending("сценарий упал с исключением");
                    GameLog.Error($"[E2E] Исключение в сценарии '{_context.Scenario}':\n{e}");
                    Finish(2);
                    yield break;
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    // Текущее ожидание снимаем до закрытия итераторов: их finally снимают отметку.
                    string waiting = E2EWait.CurrentWait;
                    float waited = Time.realtimeSinceStartup - E2EWait.CurrentWaitStarted;
                    steps.Dispose();
                    _result.Status = E2EResult.StatusTimeout;
                    _result.Summary = $"сценарий не уложился в общий таймаут {_context.Timeout:F0} с" +
                                      (waiting != null ? $"; шло ожидание {waiting}, прошло {waited:F0} с" : string.Empty);
                    _result.AbortPending($"общий таймаут {_context.Timeout:F0} с истёк");
                    Finish(3);
                    yield break;
                }

                yield return current;
            }

            if (string.IsNullOrEmpty(_result.Summary) || _result.Summary.StartsWith("сценарий запущен"))
                _result.Summary = _result.Passed ? "все проверки зелёные" : "есть красные проверки";

            Finish(_result.Passed ? 0 : 1);
        }

        /// <summary>
        /// Именованный провал (барьер, abort сервера): красная проверка с диагнозом, статус completed — вердикт RED
        /// «сценарий поймал отказ», а не «прогон не состоялся».
        /// </summary>
        private void FailNamed(string check, string detail)
        {
            _result.Set(check, false, detail);
            _result.AbortPending($"прогон остановлен: {check}");
            _result.Summary = $"{check}: {detail}";
            _result.Status = E2EResult.StatusCompleted;
            GameLog.Debug.Warning($"[E2E] Сценарий '{_context.Scenario}' остановлен: {check}: {detail}");
            Finish(1);
        }

        /// <summary>Прогон не состоялся из-за портов: вердикт «ошибка», сценарий дальше не крутится.</summary>
        private void FailOnPorts(string reason)
        {
            _result.Status = E2EResult.StatusError;
            _result.Summary = $"порты e2e: {reason}";
            _result.AbortPending("прогон остановлен: порты e2e не применены");
            GameLog.Error($"[E2E] Сценарий '{_context.Scenario}' остановлен: порты e2e: {reason}");
            Finish(2);
        }

        /// <summary>
        /// Записывает вердикт и гасит процесс. Код возврата дублирует статус,
        /// но дирижёр опирается на файл: убитый по таймауту процесс кода не вернёт.
        /// </summary>
        private void Finish(int exitCode)
        {
            if (_finished)
                return;

            _finished = true;
            _result.DurationSeconds = Time.realtimeSinceStartup - _startedRealtime;
            _context.Diagnostics?.End("scenario-finished");
            ClearServerStartup();

            // Провал виден другой стороне барьера: сервер рассылает abort, клиент шлёт шаг failed (до записи — в журнал).
            if (!_result.Passed && _context.DiagnosticError == null)
            {
                string reason = string.IsNullOrEmpty(_result.Summary) ? _result.Status : _result.Summary;
                if (_context.IsServerSide) E2EBarrier.FailServer(reason);
                else E2EBarrier.ReportClientFailure(_context, reason);
            }

            _result.WriteTo(_context.ResultPath);

            GameLog.Debug.Info($"[E2E] Вердикт: passed={_result.Passed}, status={_result.Status}, {_result.Summary}");
            foreach (E2ECheck check in _result.Checks)
                GameLog.Debug.Info($"[E2E]   [{(check.Passed ? "OK  " : "FAIL")}] {check.Name} — {check.Detail}");

            // Клиентские роли без -e2eQuitOnFinish держат процесс живым до конца прогона: если клиент
            // выйдет раньше сервера, сервер потеряет игрока и его вердикт станет
            // невалидным. Гасит такие процессы дирижёр. С -e2eQuitOnFinish (шлем: дирижёр не гасит
            // процесс по PID, а приложение не должно висеть после вердикта) выходит любая роль.
            bool quit = (_context.IsServerRole && _quitServerOnFinish) ||
                        (_context.QuitOnFinish && !Application.isEditor);
            if (quit)
                Application.Quit(exitCode);
        }

        private void OnApplicationQuit()
        {
            ClearServerStartup();
            _identityScope?.Dispose();
            _identityScope = null;
            if (_finished || _result == null)
                return;

            _result.Status = E2EResult.StatusError;
            _result.Summary = "процесс завершился раньше, чем сценарий вынес вердикт";
            _result.AbortPending("процесс завершился досрочно");
            _result.DurationSeconds = Time.realtimeSinceStartup - _startedRealtime;
            _context?.Diagnostics?.End("application-quit", false);
            _result.WriteTo(_context.ResultPath);
        }

        private void OnDestroy()
        {
            ClearServerStartup();
            if (!_finished && _result != null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = "исполнитель уничтожен до завершения сценария";
                _result.AbortPending(_result.Summary);
                _result.DurationSeconds = Time.realtimeSinceStartup - _startedRealtime;
                _context?.Diagnostics?.End("runner-destroyed", false);
                _result.WriteTo(_context.ResultPath);
            }
            _context?.Diagnostics?.End("runner-destroyed", false);
            _identityScope?.Dispose();
            _identityScope = null;
            if (_context != null) E2EBarrier.Deactivate(_context);
            if (_active == this) _active = null;
            if (_diagnosticRefusal != null && DebugBootstrapGate.PlayerBeforeNetworkStart == _diagnosticRefusal)
                DebugBootstrapGate.PlayerBeforeNetworkStart = _previousPlayerPreparation;
        }

        private void OnApplicationFocus(bool focused) => _context?.Diagnostics?.FocusChanged(focused);
        private void OnApplicationPause(bool paused)
        { if (paused) _context?.Diagnostics?.FocusChanged(false); }
        private void ClearServerStartup()
        {
            if (_serverStartup == null) return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= _serverStartup;
            _serverStartup = null;
        }

        // ── Реестр сценариев ───────────────────────────────────────────────

        private static IEnumerable<IE2EScenario> All()
        {
            yield return new DedicatedServerArsenalScenario();
            yield return new AvatarSwapDeathReplicationScenario();
            yield return new CalibrationScaleReplicationScenario();
            yield return new SessionRecoveryOnReconnectScenario();
            yield return new ArsenalItemGrabScenario();
            yield return new PlayerDeathSignalScenario();
            yield return new ShotPipelineBudgetScenario();
            yield return new RoundReadinessMatchScenario();
            yield return new WeaponHitDamageScenario();
            yield return new AvatarSpawnPointScenario();
            yield return new CalibratedPositionPersistsScenario();
            yield return new ConnectPlaceAcrossMapScenario();
            yield return new MapRunRelayBarrierScenario();
            yield return new MapRunRelayBarrierScenario(startupRoute: true);
            yield return new E2EKitSelfTestScenario();
            yield return new DeviceSmokeScenario();
            yield return new PerfStressScenario();
            yield return new ScriptedClientScenario(ScriptedClientScenario.SmokeName, "input-smoke");
            yield return new ScriptedClientScenario(ScriptedClientScenario.PurchaseName, "wall-purchase");
            yield return new ScriptedClientScenario(ScriptedClientScenario.CombatHitName, "combat-hit");
            yield return new ScriptedClientScenario(ScriptedClientScenario.CombatRoundName, "combat-round");
            yield return new ScriptedClientScenario(ScriptedClientScenario.CombatSeriesName, "combat-next-round");
            yield return new ScriptedClientScenario(ScriptedClientScenario.AdminPauseName, "admin-pause");
            yield return new ScriptedClientScenario(ScriptedClientScenario.SeriesNextMapName, "series-next-map");
            yield return new ScriptedClientScenario(ScriptedClientScenario.SeriesStopName, "series-stop");
        }

        /// <summary>Сценарий с таким именем зарегистрирован (регистр не важен).</summary>
        public static bool IsRegistered(string name) => !string.IsNullOrEmpty(name) && Resolve(name) != null;

        private static IE2EScenario Resolve(string name)
        {
            foreach (IE2EScenario scenario in All())
            {
                if (string.Equals(scenario.Name, name, StringComparison.OrdinalIgnoreCase))
                    return scenario;
            }

            return null;
        }

        private static List<string> Names()
        {
            List<string> names = new List<string>();
            foreach (IE2EScenario scenario in All())
                names.Add(scenario.Name);

            return names;
        }
    }
}
#endif

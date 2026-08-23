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


        // ── Бутстрап ───────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            E2EContext context = E2EContext.FromCommandLine();
            if (context == null)
                return;

            // DeviceToken должен быть переопределён до того, как GameNetworkManager
            // отправит GamePlayerConnectMessage — то есть до любого подключения.
            if (!string.IsNullOrEmpty(context.DeviceToken))
            {
                PlayerPrefs.SetString("DeviceToken", context.DeviceToken);
                PlayerPrefs.Save();
            }

            GameObject host = new GameObject("E2ERunner");
            DontDestroyOnLoad(host);
            host.AddComponent<E2ERunner>().Initialize(context);
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

            // Заглушка пишется немедленно: если процесс упадёт или его убьют
            // по таймауту, дирижёр всё равно увидит файл и отличит падение
            // от «плеер не стартовал».
            _result.WriteTo(_context.ResultPath);

            GameLog.Debug.Info($"[E2E] Старт харнесса: {_context}");
        }

        private void Start()
        {
            StartCoroutine(DriveScenario());
        }

        // ── Прокрутка сценария ─────────────────────────────────────────────

        private IEnumerator DriveScenario()
        {
            _startedRealtime = Time.realtimeSinceStartup;

            IE2EScenario scenario = Resolve(_context.Scenario);
            if (scenario == null)
            {
                _result.Status = E2EResult.StatusError;
                _result.Summary = $"неизвестный сценарий '{_context.Scenario}'. Доступные: {string.Join(", ", Names())}";
                Finish(2);
                yield break;
            }

            // Сценарий прокручивается вручную, а не через yield return StartCoroutine:
            // так исключение внутри него ловится здесь и попадает в файл результата,
            // а не теряется в консоли Unity.
            IEnumerator steps = scenario.Run(_context, _result);
            float deadline = _startedRealtime + _context.Timeout;

            while (true)
            {
                object current;

                try
                {
                    if (!steps.MoveNext())
                    {
                        _result.Status = E2EResult.StatusCompleted;
                        break;
                    }

                    current = steps.Current;
                }
                catch (Exception e)
                {
                    _result.Status = E2EResult.StatusError;
                    _result.Summary = $"исключение в сценарии: {e.GetType().Name}: {e.Message}";
                    _result.AbortPending("сценарий упал с исключением");
                    GameLog.Error($"[E2E] Исключение в сценарии '{_context.Scenario}':\n{e}");
                    Finish(2);
                    yield break;
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    _result.Status = E2EResult.StatusTimeout;
                    _result.Summary = $"сценарий не уложился в общий таймаут {_context.Timeout:F0} с";
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
        /// Записывает вердикт и гасит процесс. Код возврата дублирует статус,
        /// но дирижёр опирается на файл: убитый по таймауту процесс кода не вернёт.
        /// </summary>
        private void Finish(int exitCode)
        {
            if (_finished)
                return;

            _finished = true;
            _result.DurationSeconds = Time.realtimeSinceStartup - _startedRealtime;
            _result.WriteTo(_context.ResultPath);

            GameLog.Debug.Info($"[E2E] Вердикт: passed={_result.Passed}, status={_result.Status}, {_result.Summary}");
            foreach (E2ECheck check in _result.Checks)
                GameLog.Debug.Info($"[E2E]   [{(check.Passed ? "OK  " : "FAIL")}] {check.Name} — {check.Detail}");

            // Клиентские роли держат процесс живым до конца прогона: если клиент
            // выйдет раньше сервера, сервер потеряет игрока и его вердикт станет
            // невалидным. Гасит такие процессы дирижёр.
            if (_context.IsServerRole)
                Application.Quit(exitCode);
        }

        private void OnApplicationQuit()
        {
            if (_finished || _result == null)
                return;

            _result.Status = E2EResult.StatusError;
            _result.Summary = "процесс завершился раньше, чем сценарий вынес вердикт";
            _result.AbortPending("процесс завершился досрочно");
            _result.DurationSeconds = Time.realtimeSinceStartup - _startedRealtime;
            _result.WriteTo(_context.ResultPath);
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
        }

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

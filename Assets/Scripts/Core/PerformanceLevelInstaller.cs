using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if VRB_XR_OCULUS
using Unity.XR.Oculus;
#endif

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Поднимает уровень производительности CPU/GPU шлема (подсказка Oculus
    /// <c>Performance.TrySetCPULevel/TrySetGPULevel</c>) до
    /// <see cref="GameSettings.CpuPerformanceLevel"/> / <see cref="GameSettings.GpuPerformanceLevel"/>.
    ///
    /// <para>
    /// <b>Зачем.</b> Без подсказки Quest держит уровень 2 (замер стресс-теста: <c>ovr_cpu_lvl = 2</c>
    /// во всех прогонах), а игра упирается в главный поток: база ~13,8 мс при бюджете 13,9 мс.
    /// Уровень 4 поднимает частоту процессора; цена — батарея и нагрев.
    /// </para>
    ///
    /// <para>
    /// <b>Когда ставится.</b> Как только запущен XR-дисплей, и заново — после перезапуска
    /// дисплея. Раз в секунду сверяемся с уровнем, который отдаёт система: если он ниже
    /// запрошенного (сброс после паузы, перегрев), просьба повторяется, но не чаще
    /// <see cref="RetryIntervalSeconds"/> — решение в <see cref="PerformanceLevelPolicy.ShouldApply"/>.
    /// Только Android-плеер с XR (как <see cref="FoveatedRenderingInstaller"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceLevelInstaller : MonoBehaviour
    {
#if VRB_XR_OCULUS
        private const float PollIntervalSeconds = 1f;
        private const float RetryIntervalSeconds = 10f;

        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
        private XRDisplaySubsystem _appliedTo;
        private float _nextPollTime;
        private float _lastApplyTime = float.NegativeInfinity;
        private int _applyCount;
        private int _lastReportedCpu = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            if (Application.platform != RuntimePlatform.Android) return;

            var host = new GameObject(nameof(PerformanceLevelInstaller));
            DontDestroyOnLoad(host);
            host.AddComponent<PerformanceLevelInstaller>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + PollIntervalSeconds;

            XRDisplaySubsystem display = FindRunningDisplay();
            if (display == null)
            {
                _appliedTo = null;
                return;
            }

            int targetCpu = GameSettings.Instance.CpuPerformanceLevel;
            int reportedCpu = ReadCpuLevel();
            LogReportedLevel(reportedCpu, targetCpu);

            if (!PerformanceLevelPolicy.ShouldApply(display != _appliedTo, targetCpu, reportedCpu,
                    Time.unscaledTime, _lastApplyTime, RetryIntervalSeconds))
                return;

            Apply(display, targetCpu, GameSettings.Instance.GpuPerformanceLevel, reportedCpu);
        }

        private void Apply(XRDisplaySubsystem display, int cpu, int gpu, int reportedCpu)
        {
            bool cpuOk = Performance.TrySetCPULevel(cpu);
            bool gpuOk = Performance.TrySetGPULevel(gpu);
            _appliedTo = display;
            _lastApplyTime = Time.unscaledTime;
            _applyCount++;

            string message = $"[PerformanceLevelInstaller] Уровни шлема: CPU {cpu} ({(cpuOk ? "принят" : "отказ")}), " +
                             $"GPU {gpu} ({(gpuOk ? "принят" : "отказ")}); система сейчас держит CPU {reportedCpu}.";

            if (!cpuOk || !gpuOk) GameLog.Perf.Warning(message, this);
            else if (_applyCount == 1) GameLog.Perf.Info(message, this);
            else GameLog.Perf.Verbose($"{message} Повтор №{_applyCount}.", this);
        }

        // Смена уровня системой — событие (перегрев, сброс), пишем только изменения.
        private void LogReportedLevel(int reportedCpu, int targetCpu)
        {
            if (reportedCpu == _lastReportedCpu) return;
            bool first = _lastReportedCpu == int.MinValue;
            _lastReportedCpu = reportedCpu;
            if (first || reportedCpu < 0) return;

            GameLog.Perf.Info($"[PerformanceLevelInstaller] Система держит CPU уровень {reportedCpu} (запрошен {targetCpu}).", this);
        }

        private static int ReadCpuLevel()
        {
            try
            {
                return Stats.AdaptivePerformance.CPULevel;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private XRDisplaySubsystem FindRunningDisplay()
        {
            SubsystemManager.GetSubsystems(_displays);
            foreach (XRDisplaySubsystem display in _displays)
            {
                if (display.running) return display;
            }
            return null;
        }
#endif
    }
}

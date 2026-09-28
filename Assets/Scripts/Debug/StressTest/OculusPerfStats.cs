using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if VRB_XR_OCULUS
using Unity.XR.Oculus;
#endif

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>Снимок метрик рантайма Oculus за последний кадр.</summary>
    public struct OculusFrameStats
    {
        public float AppCpuMs;
        public float AppGpuMs;
        public float CompositorGpuMs;
        public float GpuUtilPercent;
        public float CpuUtilPercent;
        public float CpuLevel;
        public float GpuLevel;
    }

    /// <summary>
    /// Метрики кадра от рантайма Oculus (<c>Unity.XR.Oculus.Stats</c>): на Quest
    /// <c>FrameTimingManager</c> отдаёт GPU = 0, настоящее время GPU знает только рантайм.
    ///
    /// <para>
    /// Работает только на шлеме: Android и активное XR-устройство. В редакторе, на ПК и на
    /// выделенном сервере <see cref="TryEnable"/> возвращает false, и рекордер убирает
    /// колонки <c>ovr_*</c> в список недоступных. Любое исключение нативной части (нет
    /// плагина, другая версия) тоже выключает метрики, а не роняет прогон.
    /// </para>
    ///
    /// <para>
    /// Код под <c>VRB_XR_OCULUS</c> — versionDefine сборки по наличию пакета
    /// <c>com.unity.xr.oculus</c>: без пакета класс компилируется в заглушку.
    /// </para>
    /// </summary>
    public static class OculusPerfStats
    {
        private const float SecToMs = 1000f;

        public static bool   IsEnabled         { get; private set; }
        public static string UnavailableReason { get; private set; } = "не запрашивались";

        /// <summary>Включает сбор PerfMetrics в рантайме. Метрики по умолчанию выключены.</summary>
        public static bool TryEnable()
        {
#if VRB_XR_OCULUS
            if (Application.platform != RuntimePlatform.Android)
                return Fail("не Quest");
            if (!XRSettings.isDeviceActive)
                return Fail("XR-устройство не активно");

            try
            {
                Stats.PerfMetrics.EnablePerfMetrics(true);
                IsEnabled = true;
                UnavailableReason = null;
                return true;
            }
            catch (Exception e)
            {
                return Fail($"рантайм Oculus: {e.GetType().Name}");
            }
#else
            return Fail("пакет com.unity.xr.oculus не подключён");
#endif
        }

        public static void Disable()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
#if VRB_XR_OCULUS
            try { Stats.PerfMetrics.EnablePerfMetrics(false); }
            catch (Exception) { /* выключаем на выходе — ошибка здесь уже ничего не меняет */ }
#endif
        }

        /// <summary>
        /// Читает метрики последнего кадра. Времена рантайм отдаёт в секундах, загрузку —
        /// долей 0..1; здесь всё переводится в мс и проценты.
        /// </summary>
        public static bool TrySample(ref OculusFrameStats stats)
        {
            if (!IsEnabled) return false;
#if VRB_XR_OCULUS
            try
            {
                stats.AppCpuMs        = Stats.PerfMetrics.AppCPUTime * SecToMs;
                stats.AppGpuMs        = Stats.PerfMetrics.AppGPUTime * SecToMs;
                stats.CompositorGpuMs = Stats.PerfMetrics.CompositorGPUTime * SecToMs;
                stats.GpuUtilPercent  = Stats.PerfMetrics.GPUUtilization * 100f;
                stats.CpuUtilPercent  = Stats.PerfMetrics.CPUUtilizationAverage * 100f;
                stats.CpuLevel        = Stats.AdaptivePerformance.CPULevel;
                stats.GpuLevel        = Stats.AdaptivePerformance.GPULevel;
                return true;
            }
            catch (Exception e)
            {
                IsEnabled = false;
                UnavailableReason = $"рантайм Oculus: {e.GetType().Name}";
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Строка для заголовка perf.log: уровень Fixed Foveated Rendering, как его видит
        /// дисплей XR, и версия OVRPlugin. Только читает — FFR здесь не меняется.
        /// </summary>
        public static string DescribeDisplay()
        {
            string ffr = "нет активного XR-дисплея";
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem display in displays)
            {
                if (!display.running) continue;
                ffr = FormattableString.Invariant(
                    $"{display.foveatedRenderingLevel:F2} (0 — выкл, 1 — максимум), флаги {display.foveatedRenderingFlags}");
                break;
            }

            string plugin = "—";
#if VRB_XR_OCULUS
            if (Application.platform == RuntimePlatform.Android && XRSettings.isDeviceActive)
            {
                try { plugin = Stats.PluginVersion; }
                catch (Exception e) { plugin = $"недоступна ({e.GetType().Name})"; }
            }
#endif
            return $"FFR: {ffr}. OVRPlugin: {plugin}.";
        }

        private static bool Fail(string reason)
        {
            IsEnabled = false;
            UnavailableReason = reason;
            return false;
        }
    }
}

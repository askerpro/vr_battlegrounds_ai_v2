using System;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>Не меняет нагрузку; вызовы только на главном потоке Unity.</summary>
    public interface IPerfFrameObserver
    {
        void OnRawFrame(in PerfRawFrame value);
    }

    /// <summary>Наблюдатель локального получения фаз, а не часов сервера.</summary>
    public interface IStressTestDiagnosticObserver : IPerfFrameObserver
    {
        void OnPhase(in PerfPhaseObservation value);
        void OnStopped(string reason);
    }

    /// <summary>
    /// Сырые значения в момент чтения. PreviousUnityFrame относится к delta/UXR,
    /// но не устанавливает исходный кадр FrameTiming GPU и Oculus.
    /// </summary>
    public readonly struct PerfRawFrame
    {
        public readonly int ObservationUnityFrame;
        public readonly int PreviousUnityFrame;
        public readonly long ObservationTicks;
        public readonly long TickFrequency;
        public readonly double RealtimeSeconds;
        public readonly float DeltaMs;
        public readonly float UxrMs;
        public readonly bool HasFrameTiming;
        public readonly double UnityCpuFrameMs;
        public readonly double UnityGpuFrameMs;
        public readonly bool HasOculus;
        public readonly OculusFrameStats Oculus;

        public PerfRawFrame(int observationUnityFrame, long observationTicks, long tickFrequency,
                            double realtimeSeconds, float deltaMs, float uxrMs, bool hasFrameTiming,
                            double unityCpuFrameMs, double unityGpuFrameMs, bool hasOculus, OculusFrameStats oculus)
        {
            ObservationUnityFrame = observationUnityFrame;
            PreviousUnityFrame = observationUnityFrame - 1;
            ObservationTicks = observationTicks;
            TickFrequency = tickFrequency;
            RealtimeSeconds = realtimeSeconds;
            DeltaMs = deltaMs;
            UxrMs = uxrMs;
            HasFrameTiming = hasFrameTiming;
            UnityCpuFrameMs = unityCpuFrameMs;
            UnityGpuFrameMs = unityGpuFrameMs;
            HasOculus = hasOculus;
            Oculus = oculus;
        }
    }

    public readonly struct PerfPhaseObservation
    {
        public readonly string PhaseId;
        public readonly int PhaseNumber;
        public readonly bool IsBegin;
        public readonly bool IsMeasured;
        public readonly int UnityFrame;
        public readonly long MonotonicTicks;
        public readonly long TickFrequency;
        public readonly double RealtimeSeconds;
        public readonly float SecondsLeft;
        public readonly int PuppetCount;
        public readonly int ClutterCount;
        public readonly string Skins;

        public PerfPhaseObservation(string phaseId, int phaseNumber, bool isBegin, bool isMeasured,
                                    int unityFrame, long monotonicTicks, long tickFrequency, double realtimeSeconds,
                                    float secondsLeft, int puppetCount, int clutterCount, string skins)
        {
            PhaseId = phaseId;
            PhaseNumber = phaseNumber;
            IsBegin = isBegin;
            IsMeasured = isMeasured;
            UnityFrame = unityFrame;
            MonotonicTicks = monotonicTicks;
            TickFrequency = tickFrequency;
            RealtimeSeconds = realtimeSeconds;
            SecondsLeft = secondsLeft;
            PuppetCount = puppetCount;
            ClutterCount = clutterCount;
            Skins = skins;
        }
    }
}

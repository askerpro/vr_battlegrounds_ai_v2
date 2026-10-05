using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Личный параметр исполнения; не участвует в ассете замысла или hash геометрии.</summary>
    public sealed class MapGrowthExecutionOptions
    {
        public const string WorkerPreference = "VRBG.MapGrowth.MaxWorkers";
        public int maxWorkers;
        public bool backgroundPreparation = true;
        public bool batchLineOfSight;
        public int ResolveWorkers(int branchCount) => Math.Min(branchCount, maxWorkers > 0 ? maxWorkers : Math.Max(1, Environment.ProcessorCount - 2));
        public static MapGrowthExecutionOptions FromMachine() => new MapGrowthExecutionOptions { maxWorkers = Math.Max(0, EditorPrefs.GetInt(WorkerPreference, 0)) };
    }

    /// <summary>Измеренные затраты исполнения. Пики памяти выборочные, не полная трасса allocations Unity native.</summary>
    public sealed class MapGrowthPerformance
    {
        public int Workers { get; internal set; }
        public int PeakQueuedEvaluations { get; internal set; }
        public int PeakPreparations { get; internal set; }
        public double PreparationMilliseconds { get; internal set; }
        public double EvaluationStepMilliseconds { get; internal set; }
        public double QueueWaitMilliseconds { get; internal set; }
        public double RoutePreparationMilliseconds { get; internal set; }
        public long RouteAllocatedBytes { get; internal set; }
        public long PreparationAllocatedBytes { get; internal set; }
        public long MainThreadAllocatedBytes { get; internal set; }
        public long PeakManagedBytes { get; internal set; }
        public long PeakWorkingSetBytes { get; internal set; }
        public long LogicalQueryBudget { get; internal set; }
        public bool AllocationCounterAvailable { get; }
        public bool WorkingSetAvailable => PeakWorkingSetBytes > 0;
        public string SamplingProblem { get; private set; }
        private readonly Dictionary<string, double> stages = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, double> StageElapsedMilliseconds => stages;
        private readonly Dictionary<string, double> executing = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, double> StageExecutingMilliseconds => executing;
        private long sampled;
        public MapGrowthPerformance()
        {
            long before = GC.GetAllocatedBytesForCurrentThread(); var calibration = new byte[256];
            GC.KeepAlive(calibration); AllocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > before;
        }
        internal void Sample(bool force = false)
        {
            long now = Stopwatch.GetTimestamp(); if (!force && now - sampled < Stopwatch.Frequency) return; sampled = now;
            // Необязательная диагностика памяти не должна прерывать поиск или мешать Dispose.
            try
            {
                PeakManagedBytes = Math.Max(PeakManagedBytes, GC.GetTotalMemory(false));
                using (var process = Process.GetCurrentProcess()) PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, process.WorkingSet64);
            }
            catch (Exception ex) { SamplingProblem = ex.GetType().Name + ": " + ex.Message; }
        }
        internal void Prepared(MapGrowthPreparedAttempt prepared)
        { PreparationMilliseconds += prepared.Milliseconds; PreparationAllocatedBytes += prepared.AllocatedBytes; }
        internal void Measured(MapGrowthCandidateResult result)
        {
            foreach (var row in result.TimingsMilliseconds)
            { stages.TryGetValue(row.Key, out double old); stages[row.Key] = old + row.Value; }
            foreach (var row in result.ExecutingMilliseconds)
            { executing.TryGetValue(row.Key, out double old); executing[row.Key] = old + row.Value; }
        }
    }
}

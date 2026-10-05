using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Пул только числовых работ; не удерживает сцены, окно или UnityEngine.Object.</summary>
    internal sealed class MapGrowthNumericPool
    {
        internal sealed class Measurement { internal string Kind; internal double Milliseconds; internal long Bytes; }
        private readonly SemaphoreSlim gate;
        private readonly ConcurrentDictionary<int, Task> tasks = new ConcurrentDictionary<int, Task>();
        private readonly ConcurrentQueue<Measurement> measurements = new ConcurrentQueue<Measurement>();
        private int active, peak;
        internal int Pending => tasks.Values.Count(t => !t.IsCompleted);
        internal int Peak => Volatile.Read(ref peak);
        internal MapGrowthNumericPool(int workers) { gate = new SemaphoreSlim(workers, workers); }
        internal Task<T> Schedule<T>(Func<T> work, CancellationToken token, string kind)
        {
            var task = Task.Run(async () =>
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                int count = Interlocked.Increment(ref active), seen;
                do { seen = Volatile.Read(ref peak); if (seen >= count) break; } while (Interlocked.CompareExchange(ref peak, count, seen) != seen);
                var clock = Stopwatch.StartNew(); long bytes = GC.GetAllocatedBytesForCurrentThread();
                try { token.ThrowIfCancellationRequested(); var result = work(); token.ThrowIfCancellationRequested(); return result; }
                finally
                {
                    measurements.Enqueue(new Measurement { Kind = kind, Milliseconds = clock.Elapsed.TotalMilliseconds, Bytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
                    Interlocked.Decrement(ref active); gate.Release();
                }
            }, token);
            tasks.TryAdd(task.Id, task);
            // Не держим runner в callback и наблюдаем исключение после Cancel/закрытия окна.
            var registry = tasks;
            task.ContinueWith(t => { var observed = t.Exception; registry.TryRemove(t.Id, out _); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
        internal void Drain(MapGrowthPerformance performance)
        {
            performance.PeakPreparations = Peak;
            while (measurements.TryDequeue(out var row))
                if (row.Kind == "routes") { performance.RoutePreparationMilliseconds += row.Milliseconds; performance.RouteAllocatedBytes += row.Bytes; }
        }
        internal Task<ImpactRoute> Route(MapGrid source, Vector2 from, Vector2 to, Vector2[] via, float radius, float speed,
            bool[] allowed, float[] clearance, bool direct, CancellationToken token)
        {
            // Отделяем массивы от главного потока и особенно от LOS/ShotLine с native PhysicsScene.
            var numeric = new MapGrid(source.Width, source.Depth, source.Cell, source.Origin);
            Array.Copy(source.Blocked, numeric.Blocked, source.Count);
            var points = via?.ToArray(); var mask = allowed?.ToArray(); var clear = clearance?.ToArray();
            return Schedule(() => PositionRouteAnalyzer.Build(numeric, from, to, points, radius, speed, mask, clear, direct), token, "routes");
        }
    }
}

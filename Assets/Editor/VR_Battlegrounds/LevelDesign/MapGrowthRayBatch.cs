using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public readonly struct MapGrowthRayRequest
    {
        public readonly int Id;
        public readonly Vector3 From, To;
        public MapGrowthRayRequest(int id, Vector3 from, Vector3 to) { Id = id; From = from; To = to; }
    }
    public readonly struct MapGrowthRayResult
    {
        public readonly int Id;
        public readonly bool Visible;
        public MapGrowthRayResult(int id, bool visible) { Id = id; Visible = visible; }
    }
    /// <summary>Пакеты только LOS. ShotLine сохраняет общий последовательный backend пробития.</summary>
    public static class MapGrowthRayBatch
    {
        private static readonly HashSet<MapGrowthRayBatchHandle> Live = new HashSet<MapGrowthRayBatchHandle>();
        public static MapGrowthRayBatchHandle Schedule(PhysicsScene physics, MapGrowthRayRequest[] requests,
            IEnumerable<Collider> obstacles, CancellationToken token = default, int initialHits = 8, int maximumHits = 4096)
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); token.ThrowIfCancellationRequested();
            if (!physics.IsValid() || requests == null || requests.Length == 0 || requests.Length > 128 ||
                requests.Select(r => r.Id).Distinct().Count() != requests.Length || initialHits < 1 || maximumHits < initialHits)
                throw new ArgumentException("Нужен валидный пакет 1–128 уникальных LOS и размер буфера.");
            if (requests.Any(r => !Finite(r.From) || !Finite(r.To))) throw new ArgumentException("Нечисловой LOS.");
            var handle = new MapGrowthRayBatchHandle(physics, requests.ToArray(), new HashSet<Collider>(obstacles.Where(c => c != null)), token, initialHits, maximumHits);
            Live.Add(handle); return handle;
        }
        private static bool Finite(Vector3 p) => PositionImpactValidation.Finite(p.x) && PositionImpactValidation.Finite(p.y) && PositionImpactValidation.Finite(p.z);
        internal static void Released(MapGrowthRayBatchHandle handle) => Live.Remove(handle);
        internal static void CompleteSceneJobs(PhysicsScene physics)
        { foreach (var handle in Live.Where(h => h.Physics.Equals(physics)).ToArray()) handle.Dispose(); }
    }
    /// <summary>Scene остаётся живой до Dispose. Poll/снятие Collider/освобождение — только на главном потоке.</summary>
    public sealed class MapGrowthRayBatchHandle : IDisposable
    {
        internal PhysicsScene Physics { get; }
        private readonly MapGrowthRayRequest[] requests;
        private readonly HashSet<Collider> obstacles;
        private readonly CancellationToken token;
        private readonly int maximumHits;
        private NativeArray<RaycastCommand> commands;
        private NativeArray<RaycastHit> hits;
        private JobHandle job;
        private MapGrowthRayResult[] results;
        private int capacity;
        private bool scheduled, disposed;
        public int Reschedules { get; private set; }
        public bool Ready
        {
            get
            {
                MapGrowthSnapshotBuilder.RequireMainThread(); ThrowDisposed(); token.ThrowIfCancellationRequested();
                if (results != null) return true;
                if (!job.IsCompleted) return false;
                job.Complete(); scheduled = false;
                var rows = new MapGrowthRayResult[requests.Length]; bool full = false;
                for (int i = 0; i < requests.Length; i++)
                {
                    bool visible = true; int count = 0;
                    for (int h = 0; h < capacity; h++)
                    {
                        var hit = hits[i * capacity + h]; if (hit.collider == null) break; count++;
                        if (obstacles.Contains(hit.collider)) visible = false;
                    }
                    if (count == capacity) full = true;
                    rows[i] = new MapGrowthRayResult(requests[i].Id, visible);
                }
                if (full)
                {
                    if (capacity >= maximumHits) throw new InvalidOperationException("LOS-пакет неполон: заполнен предельный буфер " + capacity + ". Луч не объявлен свободным.");
                    hits.Dispose(); capacity = Math.Min(maximumHits, checked(capacity * 2)); Reschedules++;
                    hits = new NativeArray<RaycastHit>(checked(requests.Length * capacity), Allocator.Persistent);
                    job = RaycastCommand.ScheduleBatch(commands, hits, 16, capacity); scheduled = true; return false;
                }
                results = rows; return true;
            }
        }
        internal MapGrowthRayBatchHandle(PhysicsScene physics, MapGrowthRayRequest[] requests, HashSet<Collider> obstacles,
            CancellationToken token, int capacity, int maximumHits)
        {
            Physics = physics; this.requests = requests; this.obstacles = obstacles; this.token = token; this.capacity = capacity; this.maximumHits = maximumHits;
            try
            {
                commands = new NativeArray<RaycastCommand>(requests.Length, Allocator.Persistent);
                hits = new NativeArray<RaycastHit>(checked(requests.Length * capacity), Allocator.Persistent);
                var query = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, UnityEngine.Physics.queriesHitBackfaces);
                for (int i = 0; i < requests.Length; i++)
                {
                    var direction = requests[i].To - requests[i].From;
                    commands[i] = new RaycastCommand(physics, requests[i].From, direction.normalized, query, direction.magnitude);
                }
                job = RaycastCommand.ScheduleBatch(commands, hits, 16, capacity); scheduled = true;
            }
            catch { Dispose(); throw; }
        }
        public MapGrowthRayResult[] Complete()
        {
            if (!Ready) throw new InvalidOperationException("LOS-пакет ещё исполняется; вызывайте Poll без ожидания главного потока.");
            return results.ToArray();
        }
        private void ThrowDisposed() { if (disposed) throw new ObjectDisposedException(nameof(MapGrowthRayBatchHandle)); }
        public void Dispose()
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); if (disposed) return; disposed = true;
            try { if (scheduled) job.Complete(); }
            finally { scheduled = false; if (hits.IsCreated) hits.Dispose(); if (commands.IsCreated) commands.Dispose(); MapGrowthRayBatch.Released(this); }
        }
    }
}

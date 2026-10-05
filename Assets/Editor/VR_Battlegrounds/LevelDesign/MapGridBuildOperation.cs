using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Один общий захват сетки; каждая порция ограничивает число логических запросов Physics.</summary>
    public sealed class MapGridBuildOperation : IDisposable
    {
        private IEnumerator<int> steps;
        private readonly MapGridBuilder.Result result = new MapGridBuilder.Result();
        private readonly CancellationToken token;
        private bool completed, disposed;
        public bool Complete => completed;
        public int LastStepQueries { get; private set; }
        public long TotalQueries { get; private set; }
        public MapGridBuilder.Result Result => completed ? result : throw new InvalidOperationException("Захват сетки ещё не завершён.");
        internal MapGridBuildOperation(Scene scene, float cell, byte[] capturedZones, CancellationToken token)
        {
            this.token = token;
            steps = MapGridBuilder.BuildSteps(scene, cell, capturedZones == null ? null : (byte[])capturedZones.Clone(), result).GetEnumerator();
        }
        public bool Step(int maximumQueries = 256)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (disposed) throw new ObjectDisposedException(nameof(MapGridBuildOperation));
            if (maximumQueries < 1) throw new ArgumentOutOfRangeException(nameof(maximumQueries));
            LastStepQueries = 0; if (completed) return true;
            try
            {
                token.ThrowIfCancellationRequested();
                while (LastStepQueries < maximumQueries)
                {
                    token.ThrowIfCancellationRequested();
                    if (!steps.MoveNext()) { completed = true; steps.Dispose(); steps = null; return true; }
                    LastStepQueries += steps.Current; TotalQueries += steps.Current;
                }
                return false;
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        { if (disposed) return; disposed = true; steps?.Dispose(); steps = null; }
    }
}

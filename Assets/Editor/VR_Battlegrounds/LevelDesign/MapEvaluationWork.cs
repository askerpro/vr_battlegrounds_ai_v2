using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Одна неделимая группа общего оценщика. Бюджет — верхняя граница вызовов LOS/ShotLine.</summary>
    public sealed class MapEvaluationWork
    {
        public int MaximumQueries { get; }
        private readonly Action action;
        private readonly Func<bool> ready;
        public string Category { get; }
        public bool Ready => ready == null || ready();
        public MapEvaluationWork(int maximumQueries, Action action, string category = null, Func<bool> ready = null)
        {
            if (maximumQueries < 0 || action == null) throw new ArgumentException("Нужны действие и неотрицательный бюджет.");
            MaximumQueries = maximumQueries; this.action = action; Category = category; this.ready = ready;
        }
        internal MapEvaluationWork InCategory(string fallback) => Category == null ? new MapEvaluationWork(MaximumQueries, action, fallback, ready) : this;
        public void Execute() => action();
    }

    /// <summary>Единственный исполнитель бюджета для общего отчёта и целого кандидата.</summary>
    internal sealed class MapEvaluationWorkQueue : IDisposable
    {
        private IEnumerator<MapEvaluationWork> steps;
        private readonly CancellationToken token;
        private MapEvaluationWork pending;
        private bool disposed;
        public bool Complete { get; private set; }
        public int LastStepQueryBudget { get; private set; }
        public long TotalQueryBudget { get; private set; }
        internal readonly Dictionary<string, double> ExecutingMilliseconds = new Dictionary<string, double>();
        private string lastCategory = "overhead";
        private void Record(string category, long started)
        {
            category = category ?? "overhead"; ExecutingMilliseconds.TryGetValue(category, out double old);
            ExecutingMilliseconds[category] = old + (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }
        public MapEvaluationWorkQueue(IEnumerator<MapEvaluationWork> steps, CancellationToken token)
        { this.steps = steps ?? throw new ArgumentNullException(nameof(steps)); this.token = token; }
        public bool Step(int maximumQueries)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (disposed) throw new ObjectDisposedException(nameof(MapEvaluationWorkQueue));
            if (maximumQueries < MapEvaluationRunner.MinimumQueryBudget)
                throw new ArgumentOutOfRangeException(nameof(maximumQueries), "Бюджет должен вмещать пару поз: минимум " + MapEvaluationRunner.MinimumQueryBudget + ".");
            LastStepQueryBudget = 0; if (Complete) return true;
            try
            {
                token.ThrowIfCancellationRequested(); int workCount = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (pending == null)
                    {
                        long started = Stopwatch.GetTimestamp(); bool moved = steps.MoveNext();
                        if (!moved) { Record(lastCategory, started); Complete = true; steps.Dispose(); steps = null; return true; }
                        pending = steps.Current; lastCategory = pending?.Category ?? "overhead"; Record(lastCategory, started);
                    }
                    if (pending == null || pending.MaximumQueries > maximumQueries)
                        throw new InvalidOperationException("Недопустимая группа запросов или группа больше бюджета.");
                    if (pending.MaximumQueries > maximumQueries - LastStepQueryBudget) return false;
                    if (!pending.Ready) return false;
                    long executing = Stopwatch.GetTimestamp(); pending.Execute(); Record(pending.Category, executing);
                    LastStepQueryBudget += pending.MaximumQueries; TotalQueryBudget += pending.MaximumQueries;
                    pending = null; if (++workCount >= 256) return false;
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { if (disposed) return; disposed = true; steps?.Dispose(); steps = null; pending = null; }
    }
}

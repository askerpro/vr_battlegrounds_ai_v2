using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ограниченные числовые ветки и единственная очередь native-оценки главного потока.</summary>
    [InitializeOnLoad]
    public sealed class MapGrowthScheduler : IDisposable
    {
        private sealed class Slot
        {
            internal MapGrowthBranchSearch Branch;
            internal Task<MapGrowthPreparedAttempt> Preparation;
            internal bool WaitingPhysics;
        }
        private sealed class Job
        {
            internal MapGrowthBranchSearch Branch;
            internal Slot Slot;
            internal MapGrowthCandidate Candidate;
            internal bool Final;
            internal long Ready;
        }
        private static readonly HashSet<MapGrowthScheduler> Live = new HashSet<MapGrowthScheduler>();
        private readonly MapGrowthSceneCapture capture;
        private readonly MapGrowthSnapshot snapshot;
        private readonly MapGrowthSearchParameters search;
        private readonly CancellationTokenSource cancel;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly bool background;
        private readonly bool batchLineOfSight;
        private readonly List<Slot> active = new List<Slot>();
        private readonly List<MapGrowthBranchSearch> branches = new List<MapGrowthBranchSearch>();
        private readonly Queue<Job> queue = new Queue<Job>();
        private readonly HashSet<string> published = new HashSet<string>(StringComparer.Ordinal);
        private readonly MapGrowthNumericPool pool;
        private MapGrowthCandidateEvaluation evaluation;
        private Job evaluating;
        private int nextBranch, nextPublish;
        private bool publicationQueued, disposed, automaticUpdate;
        public MapGrowthReport Report { get; }
        public MapGrowthPerformance Performance { get; }
        public bool Finished { get; private set; }
        public string Phase { get; private set; } = "Подготовка веток";
        public int BranchId => evaluating?.Branch.Report.branchId ?? active.FirstOrDefault()?.Branch.Report.branchId ?? Math.Min(nextPublish, search.branchCount - 1);
        public int AttemptId => evaluating?.Branch.Attempt ?? active.FirstOrDefault()?.Branch.Attempt ?? 0;
        public int PendingPreparations => pool.Pending;
        public int QueuedEvaluations => queue.Count;
        static MapGrowthScheduler() => AssemblyReloadEvents.beforeAssemblyReload += () => { foreach (var run in Live.ToArray()) run.Dispose(); };

        public static MapGrowthScheduler Start(MapGrowthSceneCapture capture, MapGrowthExecutionOptions execution = null,
            CancellationToken token = default, bool automaticUpdate = false) => new MapGrowthScheduler(capture, execution, token, automaticUpdate);

        private MapGrowthScheduler(MapGrowthSceneCapture capture, MapGrowthExecutionOptions execution, CancellationToken token, bool automaticUpdate)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (capture == null || !capture.CaptureComplete || !capture.IsCurrent()) throw new ArgumentException("Нужен завершённый актуальный захват.");
            this.capture = capture; snapshot = capture.Snapshot; search = snapshot.CopySearch();
            if (search.branchCount < 1 || search.attemptsPerBranch < 1 || search.targetCandidates < 1 || (long)search.branchCount * search.attemptsPerBranch > int.MaxValue)
                throw new ArgumentException("Неверный бюджет поиска.");
            execution = execution ?? MapGrowthExecutionOptions.FromMachine();
            if (execution.maxWorkers < 0) throw new ArgumentOutOfRangeException(nameof(execution.maxWorkers));
            background = execution.backgroundPreparation; batchLineOfSight = execution.batchLineOfSight;
            Performance = new MapGrowthPerformance { Workers = execution.ResolveWorkers(search.branchCount) };
            pool = new MapGrowthNumericPool(Performance.Workers);
            cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            Report = new MapGrowthReport { InputVersion = snapshot.InputVersion, Seed = search.seed, AttemptBudget = search.branchCount * search.attemptsPerBranch,
                TargetCandidates = search.targetCandidates, Performance = Performance };
            this.automaticUpdate = automaticUpdate; Live.Add(this); if (automaticUpdate) EditorApplication.update += Tick;
            Performance.Sample(true);
        }
        private void Tick() { try { Step(); } catch (Exception ex) { Fail(ex); } }

        public bool Step(int maximumQueries = 256)
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); if (disposed) throw new ObjectDisposedException(nameof(MapGrowthScheduler));
            if (Finished) return true; if (maximumQueries < MapEvaluationRunner.MinimumQueryBudget) throw new ArgumentOutOfRangeException(nameof(maximumQueries));
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                if (cancel.IsCancellationRequested) { Stop(false, true); return true; }
                if (evaluation != null)
                {
                    Phase = evaluating.Final ? "Полный отчёт выбранного варианта" : "Точная оценка";
                    var timing = Stopwatch.StartNew(); bool done = evaluation.Step(maximumQueries);
                    Performance.EvaluationStepMilliseconds += timing.Elapsed.TotalMilliseconds;
                    Performance.LogicalQueryBudget += evaluation.LastStepQueryBudget;
                    if (!done) return false;
                    var measured = evaluation.Result; evaluation.Dispose(); evaluation = null;
                    Performance.Measured(measured);
                    if (measured.GeometryIssues.Any(i => i.kind == MapGrowthGeometryDefect.StaleInput)) { Report.Stale = true; Stop(false, false); return true; }
                    if (evaluating.Final)
                    {
                        if (measured.AutomaticRequirementsSatisfied && published.Add(MapGrowthSearch.GeometrySignature(measured.Recipes))) Report.candidates.Add(measured);
                        else evaluating.Branch.Report.Refuse("Полный отчёт не подтвердил автоматические требования или геометрия повторяется.");
                        FinishPublication(evaluating.Branch);
                    }
                    else { evaluating.Branch.ReceiveMeasurement(measured); evaluating.Slot.WaitingPhysics = false; }
                    evaluating = null; return false;
                }
                if (!capture.IsCurrent()) { Report.Stale = true; Stop(false, false); return true; }
                active.RemoveAll(s => s.Branch.CoreComplete && !s.WaitingPhysics && s.Preparation == null);
                PreparePublication();
                while (nextBranch < search.branchCount && active.Count < Performance.Workers)
                {
                    var branch = new MapGrowthBranchSearch(snapshot, nextBranch++); branches.Add(branch); Report.branches.Add(branch.Report);
                    active.Add(new Slot { Branch = branch });
                }
                foreach (var slot in active)
                {
                    if (slot.WaitingPhysics || slot.Branch.CoreComplete) continue;
                    if (slot.Preparation == null)
                    {
                        // Замыкание содержит только числовые данные, номер попытки и CancellationToken.
                        var numericSnapshot = snapshot; var current = slot.Branch.Current; int attempt = slot.Branch.Attempt; var token = cancel.Token;
                        slot.Preparation = background ? pool.Schedule(() => MapGrowthPreparedAttempt.Build(numericSnapshot, current, attempt, token), token, "proposal")
                            : Task.FromResult(MapGrowthPreparedAttempt.Build(numericSnapshot, current, attempt, token));
                    }
                    if (!slot.Preparation.IsCompleted || queue.Count >= Performance.Workers) continue;
                    var prepared = slot.Preparation.GetAwaiter().GetResult(); slot.Preparation = null; Performance.Prepared(prepared);
                    if (!slot.Branch.Receive(prepared)) continue;
                    slot.WaitingPhysics = true; queue.Enqueue(new Job { Branch = slot.Branch, Slot = slot, Candidate = prepared.Candidate, Ready = prepared.ReadyTimestamp });
                }
                pool.Drain(Performance);
                Performance.PeakQueuedEvaluations = Math.Max(Performance.PeakQueuedEvaluations, queue.Count);
                if (queue.Count > 0)
                {
                    evaluating = queue.Dequeue(); Performance.QueueWaitMilliseconds += (Stopwatch.GetTimestamp() - evaluating.Ready) * 1000.0 / Stopwatch.Frequency;
                    evaluation = MapGrowthCandidateEvaluator.Begin(capture, evaluating.Candidate, cancel.Token, evaluating.Final, background ? pool : null, batchLineOfSight); return false;
                }
                if (nextPublish >= search.branchCount) { Stop(true, false); return true; }
                Phase = PendingPreparations > 0 ? "Числовые предложения в фоне" : "Следующая попытка"; return false;
            }
            catch (OperationCanceledException) { Stop(false, true); return true; }
            catch (Exception ex) { Fail(ex); return true; }
            finally { pool.Drain(Performance); Performance.MainThreadAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated; Performance.Sample(); if (!Finished) Report.ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds; }
        }
        private void PreparePublication()
        {
            if (publicationQueued) return;
            while (nextPublish < branches.Count && branches[nextPublish].CoreComplete)
            {
                var branch = branches[nextPublish];
                var candidate = Report.candidates.Count < search.targetCandidates ? branch.ForPublication(published) : null;
                if (candidate == null) { FinishPublication(branch); continue; }
                if (queue.Count >= Performance.Workers) return;
                queue.Enqueue(new Job { Branch = branch, Candidate = candidate, Final = true, Ready = Stopwatch.GetTimestamp() }); publicationQueued = true; return;
            }
        }
        private void FinishPublication(MapGrowthBranchSearch branch)
        { branch.ReleaseCandidates(); nextPublish++; publicationQueued = false; }
        private void Fail(Exception ex)
        { Report.Problem = ex.GetType().Name + ": " + ex.Message; if (evaluating != null) evaluating.Branch.Report.problem = Report.Problem; Stop(false, false); }
        private void Stop(bool complete, bool cancelled)
        {
            if (Finished) return; cancel.Cancel(); evaluation?.Dispose(); evaluation = null; evaluating = null; queue.Clear();
            foreach (var branch in branches) branch.ReleaseCandidates();
            // Оставшиеся Tasks владеют лишь числовым аргументом. Cancellation завершает их безопасно;
            // окно/сцена/Collider не удерживаются и в worker недоступны. Главный поток не ждёт Task.Wait.
            active.Clear(); clock.Stop(); Finished = true; Report.Complete = complete; Report.Cancelled = cancelled;
            Report.ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds; Performance.Sample(true);
            Phase = cancelled ? "Отменено" : Report.Stale ? "Вход устарел" : complete ? "Бюджет завершён" : "Ошибка";
            if (automaticUpdate) { EditorApplication.update -= Tick; automaticUpdate = false; }
        }
        public void Cancel() { MapGrowthSnapshotBuilder.RequireMainThread(); if (!disposed && !Finished) cancel.Cancel(); }
        public void Dispose()
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); if (disposed) return;
            if (!Finished) Stop(false, true); disposed = true; cancel.Dispose(); Live.Remove(this);
        }
    }
}

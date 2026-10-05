using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public sealed class MapGrowthCandidateResult
    {
        public string CandidateId { get; internal set; }
        public string InputVersion { get; internal set; }
        public int BranchId { get; internal set; }
        public int Seed { get; internal set; }
        public ReadOnlyCollection<MapGrowthBlockRecipe> Recipes { get; internal set; }
        public MapEvaluationResult Evaluation { get; internal set; }
        public MapGrowthIntentResult Intent { get; internal set; }
        internal readonly List<MapGrowthGeometryIssue> issues = new List<MapGrowthGeometryIssue>();
        public ReadOnlyCollection<MapGrowthGeometryIssue> GeometryIssues => issues.AsReadOnly();
        internal readonly Dictionary<string, double> timings = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, double> TimingsMilliseconds => new ReadOnlyDictionary<string, double>(timings);
        internal readonly Dictionary<string, double> executing = new Dictionary<string, double>();
        public IReadOnlyDictionary<string, double> ExecutingMilliseconds => new ReadOnlyDictionary<string, double>(executing);
        private MapGrowthViolationVector verdict;
        private bool geometryComplete, automaticSatisfied;
        public bool Complete => verdict != null;
        public bool GeometryComplete => Complete && geometryComplete;
        public bool AutomaticRequirementsSatisfied => Complete && automaticSatisfied;
        internal MapGrowthViolationVector Verdict => verdict ?? throw new InvalidOperationException("Оценка ещё не завершена.");

        // Единственная запись решения — при завершении оценки. Публичные отчёты служат отображению.
        internal void Seal()
        {
            if (Complete) throw new InvalidOperationException("Решение завершённой оценки неизменно.");
            geometryComplete = issues.All(v => !v.IsUnchecked);
            automaticSatisfied = geometryComplete && issues.Count == 0 && Evaluation != null && Evaluation.measurementsComplete
                && Evaluation.violations.Count == 0 && Intent != null && Intent.AutomaticRequirementsSatisfied;
            verdict = MapGrowthViolationVector.Measure(this);
        }
    }

    [InitializeOnLoad]
    public static class MapGrowthCandidateEvaluator
    {
        private static readonly HashSet<MapGrowthCandidateEvaluation> Live = new HashSet<MapGrowthCandidateEvaluation>();
        static MapGrowthCandidateEvaluator() => AssemblyReloadEvents.beforeAssemblyReload += () => { foreach (var evaluation in Live.ToArray()) evaluation.Dispose(); };
        public static MapGrowthCandidateEvaluation Begin(MapGrowthSceneCapture capture, MapGrowthCandidate candidate, CancellationToken token = default,
            bool includeVisibility = true)
            => Begin(capture, candidate, token, includeVisibility, null);
        internal static MapGrowthCandidateEvaluation Begin(MapGrowthSceneCapture capture, MapGrowthCandidate candidate, CancellationToken token,
            bool includeVisibility, MapGrowthNumericPool pool, bool batchLineOfSight = false)
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (capture == null || !capture.CaptureComplete || candidate == null || !candidate.BelongsTo(capture.Snapshot))
                throw new ArgumentException("Кандидат должен принадлежать завершённому захвату.");
            if (!capture.IsCurrent()) throw new InvalidOperationException("Вход выращивателя устарел; повторите захват.");
            var evaluation = new MapGrowthCandidateEvaluation(capture, candidate, token, includeVisibility, pool, batchLineOfSight);
            Live.Add(evaluation); return evaluation;
        }
        public static MapGrowthCandidateResult Evaluate(MapGrowthSceneCapture capture, MapGrowthCandidate candidate, CancellationToken token = default,
            bool includeVisibility = true)
        {
            using (var evaluation = Begin(capture, candidate, token, includeVisibility))
            { while (!evaluation.Step()) { } return evaluation.Result; }
        }
        internal static void Released(MapGrowthCandidateEvaluation evaluation) => Live.Remove(evaluation);
    }

    /// <summary>Одна собственная оценка. Результат не удерживает Collider/Scene/делегаты живой Physics.</summary>
    public sealed class MapGrowthCandidateEvaluation : IDisposable
    {
        private readonly MapGrowthSceneCapture capture;
        private readonly MapGrowthSnapshot snapshot;
        private readonly MapGrowthCandidateResult result;
        private readonly MapEvaluationWorkQueue queue;
        private readonly bool includeVisibility;
        private readonly bool batchLineOfSight;
        private readonly CancellationToken token;
        private readonly MapGrowthNumericPool pool;
        private MapGrowthPreview preview;
        private MapGrowthCollisionGeometry collisions;
        private MapGridBuildOperation gridOperation;
        private bool disposed;
        public bool Complete => queue.Complete;
        public int LastStepQueryBudget => queue.LastStepQueryBudget;
        public long TotalQueryBudget => queue.TotalQueryBudget;
        public MapGrowthCandidateResult Result => Complete ? result : throw new InvalidOperationException("Кандидат ещё не оценён.");
        internal MapGrowthCandidateEvaluation(MapGrowthSceneCapture capture, MapGrowthCandidate candidate, CancellationToken token, bool includeVisibility, MapGrowthNumericPool pool = null, bool batchLineOfSight = false)
        {
            this.capture = capture; snapshot = capture.Snapshot; this.token = token; this.includeVisibility = includeVisibility; this.pool = pool;
            this.batchLineOfSight = batchLineOfSight;
            result = new MapGrowthCandidateResult { CandidateId = candidate.CandidateId, BranchId = candidate.BranchId, Seed = candidate.Seed,
                InputVersion = candidate.InputVersion, Recipes = Array.AsReadOnly(candidate.Recipes.ToArray()) };
            queue = new MapEvaluationWorkQueue(Stages().GetEnumerator(), token);
        }
        public bool Step(int maximumQueries = 256)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MapGrowthCandidateEvaluation));
            try
            {
                bool complete = queue.Step(maximumQueries);
                if (complete) foreach (var row in queue.ExecutingMilliseconds) result.executing[row.Key] = row.Value;
                return complete;
            }
            catch { Dispose(); throw; }
        }
        private IEnumerable<MapEvaluationWork> Stages()
        {
            var total = Stopwatch.StartNew(); var stage = Stopwatch.StartNew();
            try
            {
                yield return new MapEvaluationWork(0, () => preview = capture.CreateFixedPreview(), "preview");
                var generated = new Dictionary<GameObject, MapGrowthBlockRecipe>();
                foreach (var recipe in result.Recipes)
                    yield return new MapEvaluationWork(0, () => {
                        var definition = BlockoutRegistryFactory.Current.Definitions.Single(d => d != null && d.shapeId == recipe.ShapeId);
                        var root = BlockoutRegistryFactory.CreatePreview(definition, preview.Scene, recipe, capture.AuthorCell);
                        root.name = "Grower_" + recipe.RecipeId; generated.Add(root, recipe);
                    }, "preview");
                result.timings["preview"] = stage.Elapsed.TotalMilliseconds; stage.Restart();
                collisions = new MapGrowthCollisionGeometry(preview, snapshot, capture.FloorY, generated, result.issues);
                foreach (var work in collisions.CheckSteps()) yield return work.InCategory("geometry");
                collisions.Dispose(); collisions = null;
                result.timings["geometry"] = stage.Elapsed.TotalMilliseconds; stage.Restart();
                if (result.issues.Count == 0)
                {
                    var original = snapshot.CreateMovementGrid();
                    gridOperation = MapGridBuilder.Begin(preview.Scene, snapshot.Cell, original.Zone, token);
                    do { yield return new MapEvaluationWork(1, () => gridOperation.Step(1), "footprint"); } while (!gridOperation.Complete);
                    var built = gridOperation.Result; gridOperation.Dispose(); gridOperation = null;
                    result.timings["grid"] = stage.Elapsed.TotalMilliseconds; stage.Restart();
                    var layout = snapshot.CopyLayout(); result.Evaluation = new MapEvaluationResult();
                    MapRouteBuilder routeBuilder = pool == null ? null : (grid, from, to, via, radius, speed, allowed, clear, direct) => pool.Route(grid, from, to, via, radius, speed, allowed, clear, direct, token);
                    foreach (var work in MapEvaluation.EvaluateSteps(layout.map, built, layout, snapshot.Profile, includeVisibility,
                        PositionImpactAnalysis.MaximumInfluenceIntervals, preview.FixedColliderIds.Keys.Concat(generated.Keys.SelectMany(r => r.GetComponentsInChildren<Collider>(false)))
                            .Where(BlockoutSupportSurfaces.IsActiveSolid).Where(c => c.gameObject.layer != LayerMask.NameToLayer("Ground")).Select(c => c.bounds).ToArray(), result.Evaluation, routeBuilder, batchLineOfSight))
                        yield return work.InCategory("spatial-report");
                    result.Intent = MapGrowthIntentEvaluation.Evaluate(new MapGrowthEvaluationInput { grid = built.Grid, layout = layout,
                        contacts = snapshot.CopyContacts(), validation = snapshot.CopyValidation() }, result.Evaluation, snapshot.CopySearch());
                    result.timings["report"] = stage.Elapsed.TotalMilliseconds;
                    if (result.Evaluation.grid != null)
                    { result.Evaluation.grid.LineOfSight = Detached; result.Evaluation.grid.ShotLine = Detached; result.Evaluation.grid.BeginLineBatch = null; }
                }
                if (!capture.IsCurrent()) result.issues.Add(new MapGrowthGeometryIssue { kind = MapGrowthGeometryDefect.StaleInput,
                    recipeId = result.CandidateId, otherId = result.InputVersion, message = "Исходная карта или настройки изменились во время оценки; нужен новый захват." });
                result.timings["total"] = total.Elapsed.TotalMilliseconds;
                result.Seal();
            }
            finally { Cleanup(); }
        }
        private static bool Detached(Vector3 a, Vector3 b) => throw new InvalidOperationException("Готовый результат содержит числовую сетку; повторная Physics требует нового preview.");
        private void Cleanup()
        {
            gridOperation?.Dispose(); gridOperation = null;
            try { collisions?.Dispose(); }
            finally { collisions = null; preview?.Dispose(); preview = null; }
        }
        public void Dispose()
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); if (disposed) return; disposed = true;
            try { queue.Dispose(); }
            finally { Cleanup(); MapGrowthCandidateEvaluator.Released(this); }
        }
    }
}

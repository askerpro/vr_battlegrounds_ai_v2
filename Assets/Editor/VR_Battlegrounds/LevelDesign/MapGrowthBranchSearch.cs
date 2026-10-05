using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Передаваемое числовое предложение. Worker не читает branch, отчёт или Unity-объекты.</summary>
    internal sealed class MapGrowthPreparedAttempt
    {
        internal MapGrowthCandidate Candidate;
        internal MapGrowthMutationProposal Proposal;
        internal MapGrowthFilterResult Filter;
        internal double Milliseconds;
        internal long AllocatedBytes;
        internal long ReadyTimestamp;

        internal static MapGrowthPreparedAttempt Build(MapGrowthSnapshot snapshot, MapGrowthCandidate current, int attempt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
            var prepared = new MapGrowthPreparedAttempt { Candidate = current };
            if (attempt != 0)
            {
                prepared.Proposal = MapGrowthMutations.ProposeDetailed(snapshot, current, attempt);
                prepared.Candidate = prepared.Proposal.Candidate;
                token.ThrowIfCancellationRequested();
                if (prepared.Proposal.Changed && prepared.Proposal.ChangedRecipe != null)
                {
                    var without = current.Copy(current.CandidateId + "-filter");
                    without.SetRecipes(current.Recipes.Where(r => r.RecipeId != prepared.Proposal.RecipeId));
                    prepared.Filter = MapGrowthCellFilter.Check(snapshot, without, prepared.Proposal.ChangedRecipe);
                }
            }
            token.ThrowIfCancellationRequested(); prepared.Milliseconds = watch.Elapsed.TotalMilliseconds;
            prepared.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated; prepared.ReadyTimestamp = Stopwatch.GetTimestamp();
            return prepared;
        }
    }

    /// <summary>Один владелец состояния ветки на главном потоке. Worker получает только неизменяемый вход попытки.</summary>
    internal sealed class MapGrowthBranchSearch
    {
        private readonly int budget;
        private readonly Dictionary<string, (int attempt, MapGrowthCandidate candidate)> valid = new Dictionary<string, (int, MapGrowthCandidate)>(StringComparer.Ordinal);
        private MapGrowthCandidate pending;
        private MapGrowthCandidateResult measuredCurrent;
        internal MapGrowthCandidate Current { get; private set; }
        internal int Attempt { get; private set; }
        internal MapGrowthBranchReport Report { get; }
        internal bool CoreComplete => Attempt >= budget;
        internal MapGrowthBranchSearch(MapGrowthSnapshot snapshot, int id)
        {
            Current = MapGrowthSearch.CreateBranch(snapshot, id); budget = snapshot.CopySearch().attemptsPerBranch;
            Report = new MapGrowthBranchReport { branchId = id, seed = Current.Seed };
        }
        /// <returns>Точное измерение нужно; либо попытка завершена консервативным отсевом.</returns>
        internal bool Receive(MapGrowthPreparedAttempt prepared)
        {
            Report.attempts++; Report.lastOperation = prepared.Proposal?.Reason ?? "Исходная фиксированная карта";
            if (prepared.Proposal != null && !prepared.Proposal.Changed)
            { Report.Refuse(prepared.Proposal.Reason); Attempt++; return false; }
            if (prepared.Filter?.status == MapGrowthFilterStatus.Impossible)
            { Report.filtered++; foreach (string reason in prepared.Filter.reasons) Report.Refuse(reason); Attempt++; return false; }
            pending = prepared.Candidate; return true;
        }
        internal void ReceiveMeasurement(MapGrowthCandidateResult measured)
        {
            Report.evaluated++;
            if (measuredCurrent == null) { Current = pending; measuredCurrent = measured; }
            else
            {
                var decision = MapGrowthSearch.Accept(Current, pending, measuredCurrent, measured);
                if (decision.Accepted) { Current = pending; measuredCurrent = measured; Report.accepted++; }
                else Report.Refuse(decision.Reason);
            }
            if (measured.AutomaticRequirementsSatisfied) valid[MapGrowthSearch.GeometrySignature(pending)] = (Attempt, pending);
            foreach (var issue in measured.GeometryIssues) Report.Refuse(issue.kind + "/" + issue.otherId + ": " + issue.message);
            foreach (var issue in measured.Evaluation?.violations ?? new List<MapEvaluationIssue>()) Report.Refuse(issue.rule + ": " + issue.message);
            foreach (var issue in measured.Intent?.checks ?? new List<MapGrowthIntentCheck>())
                if (issue.kind == MapGrowthIntentCheckKind.Automatic && issue.status != MapGrowthIntentStatus.Satisfied)
                    Report.Refuse(issue.ownerId + "/" + issue.field + ": " + issue.message);
            Report.best = MapGrowthViolationVector.From(measuredCurrent); pending = null; Attempt++;
        }
        internal MapGrowthCandidate ForPublication(HashSet<string> published)
            => valid.Where(pair => !published.Contains(pair.Key)).OrderByDescending(pair => pair.Value.attempt).Select(pair => pair.Value.candidate).FirstOrDefault();
        internal void ReleaseCandidates() { valid.Clear(); Current = pending = null; measuredCurrent = null; }
    }
}

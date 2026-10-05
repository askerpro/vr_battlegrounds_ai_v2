using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Вектор отказов, а не универсальная оценка качества карты.</summary>
    public sealed class MapGrowthViolationVector : IComparable<MapGrowthViolationVector>
    {
        public int InvalidGeometry { get; private set; }
        public int UncheckedAutomatic { get; private set; }
        public int P0 { get; private set; }
        public int MandatoryIntent { get; private set; }
        public int MapRanges { get; private set; }
        public int ManualAcceptance { get; private set; }
        public static MapGrowthViolationVector From(MapGrowthCandidateResult result)
        {
            if (result == null || !result.Complete) throw new ArgumentException("Вектор строится только по завершённой оценке.");
            return result.Verdict;
        }
        internal static MapGrowthViolationVector Measure(MapGrowthCandidateResult result)
        {
            var checks = result.Intent?.checks ?? new List<MapGrowthIntentCheck>();
            return new MapGrowthViolationVector {
                InvalidGeometry = result.GeometryIssues.Count(v => !v.IsUnchecked),
                UncheckedAutomatic = result.GeometryIssues.Count(v => v.IsUnchecked) + (result.Evaluation == null || !result.Evaluation.measurementsComplete ? 1 : 0)
                    + (result.Intent == null || !result.Intent.measurementsComplete ? 1 : 0)
                    + checks.Count(c => c.kind == MapGrowthIntentCheckKind.Automatic && c.status == MapGrowthIntentStatus.Unmeasured),
                P0 = result.Evaluation?.violations.Count ?? 0,
                MandatoryIntent = checks.Count(c => c.kind == MapGrowthIntentCheckKind.Automatic && c.status == MapGrowthIntentStatus.Violated && c.ownerId != "map"),
                MapRanges = checks.Count(c => c.kind == MapGrowthIntentCheckKind.Automatic && c.status == MapGrowthIntentStatus.Violated && c.ownerId == "map"),
                ManualAcceptance = checks.Count(c => c.kind == MapGrowthIntentCheckKind.ManualAcceptance)
            };
        }
        public int CompareTo(MapGrowthViolationVector other)
        {
            if (other == null) return -1;
            // Геометрически неподходящие и неполные результаты не улучшают карту отсутствием измерений.
            int[] a = { InvalidGeometry == 0 ? 0 : 1, UncheckedAutomatic, P0, MandatoryIntent, MapRanges, InvalidGeometry };
            int[] b = { other.InvalidGeometry == 0 ? 0 : 1, other.UncheckedAutomatic, other.P0, other.MandatoryIntent, other.MapRanges, other.InvalidGeometry };
            for (int i = 0; i < a.Length; i++) { int comparison = a[i].CompareTo(b[i]); if (comparison != 0) return comparison; }
            return 0;
        }
        public override string ToString() => "Геометрия " + InvalidGeometry + "; непроверено " + UncheckedAutomatic + "; P0 " + P0 + "; контакты/пути " + MandatoryIntent + "; диапазоны " + MapRanges;
    }
    public sealed class MapGrowthBranchReport
    {
        public int branchId, seed, attempts, filtered, evaluated, accepted;
        public string lastOperation, problem;
        public MapGrowthViolationVector best;
        public readonly SortedDictionary<string, int> reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        internal void Refuse(string reason)
        { if (string.IsNullOrWhiteSpace(reason)) reason = "Нет улучшения обязательных требований."; reasons.TryGetValue(reason, out int count); reasons[reason] = count + 1; }
    }
    public sealed class MapGrowthReport
    {
        public string InputVersion { get; internal set; }
        public int Seed { get; internal set; }
        public int AttemptBudget { get; internal set; }
        public int TargetCandidates { get; internal set; }
        public bool Complete { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool Stale { get; internal set; }
        public string Problem { get; internal set; }
        public double ElapsedMilliseconds { get; internal set; }
        public MapGrowthPerformance Performance { get; internal set; }
        internal readonly List<MapGrowthCandidateResult> candidates = new List<MapGrowthCandidateResult>();
        internal readonly List<MapGrowthBranchReport> branches = new List<MapGrowthBranchReport>();
        public ReadOnlyCollection<MapGrowthCandidateResult> Candidates => candidates.AsReadOnly();
        public ReadOnlyCollection<MapGrowthBranchReport> Branches => branches.AsReadOnly();
        public int Attempts => branches.Sum(b => b.attempts);
        public int DistinctGeometryCount => candidates.Count;
        public string ShortageReason => candidates.Count >= TargetCandidates ? null
            : "Найдено " + candidates.Count + " из " + TargetCandidates + ". " + (Cancelled ? "Поиск отменён." : Stale ? "Источник изменился." : Complete ? "Бюджет исчерпан; см. причины веток." : Problem ?? "Поиск продолжается.");
    }
    public sealed class MapGrowthDecision
    {
        public bool Accepted { get; internal set; }
        public MapGrowthViolationVector Vector { get; internal set; }
        public string Reason { get; internal set; }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Детерминированные чистые решения поиска; главный поток лишь поставляет точные измерения.</summary>
    public static class MapGrowthSearch
    {
        public static MapGrowthCandidate CreateBranch(MapGrowthSnapshot snapshot, int branchId)
        {
            if (snapshot == null || branchId < 0) throw new ArgumentException("Нужен снимок и номер ветки.");
            int seed = unchecked((int)MapGrowthRandom.Mix(unchecked((uint)snapshot.CopySearch().seed) ^ MapGrowthRandom.Mix((uint)branchId + 1)));
            return new MapGrowthCandidate("b" + branchId + "-a0", branchId, seed, snapshot);
        }
        public static MapGrowthDecision Accept(MapGrowthCandidate current, MapGrowthCandidate proposed,
            MapGrowthCandidateResult currentMeasured, MapGrowthCandidateResult measured)
        {
            Match(current, currentMeasured); Match(proposed, measured);
            if (current.InputVersion != proposed.InputVersion || current.BranchId != proposed.BranchId) throw new ArgumentException("Сравниваются результаты одной ветки/версии.");
            var before = MapGrowthViolationVector.From(currentMeasured); var after = MapGrowthViolationVector.From(measured);
            int comparison = after.CompareTo(before);
            bool duplicate = GeometrySignature(current) == GeometrySignature(proposed);
            return new MapGrowthDecision { Vector = after, Accepted = comparison < 0 || comparison == 0 && !duplicate,
                Reason = duplicate ? "Та же геометрия и секции." : comparison > 0 ? "Вектор обязательных нарушений ухудшился: " + after : comparison < 0 ? "Обязательные требования улучшены." : "Равные нарушения; исследуется другая геометрия." };
        }
        private static void Match(MapGrowthCandidate candidate, MapGrowthCandidateResult result)
        {
            if (candidate == null || result == null || !result.Complete || candidate.CandidateId != result.CandidateId
                || candidate.InputVersion != result.InputVersion || candidate.BranchId != result.BranchId || candidate.Seed != result.Seed
                || GeometrySignature(candidate.Recipes) != GeometrySignature(result.Recipes)) throw new ArgumentException("Измерение не соответствует кандидату.");
        }
        public static string GeometrySignature(MapGrowthCandidate candidate) => GeometrySignature(candidate.Recipes);
        public static string GeometrySignature(System.Collections.Generic.IEnumerable<MapGrowthBlockRecipe> recipes)
        {
            var rows = recipes.Select(r => {
                var row = new StringBuilder(); row.Append(r.ShapeId.Length).Append(':').Append(r.ShapeId);
                Add(r.BottomCenter.x); Add(r.BottomCenter.y); Add(r.BottomCenter.z); Add((r.Yaw % 360 + 360) % 360);
                Add(r.Dimensions.x); Add(r.Dimensions.y); Add(r.Dimensions.z); Add(r.TopSize.x); Add(r.TopSize.y);
                foreach (var section in r.CopySections())
                { row.Append('|').Append((int)section.material).Append('|').Append(section.openings.enabled ? '1' : '0');
                    if (section.openings.enabled) { Add(section.openings.spacing); Add(section.openings.width); Add(section.openings.sillHeight); Add(section.openings.lintelHeight); } }
                return row.ToString();
                void Add(float value) => row.Append('|').Append((value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture));
            }).OrderBy(row => row, StringComparer.Ordinal);
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "").ToLowerInvariant();
        }
    }
}

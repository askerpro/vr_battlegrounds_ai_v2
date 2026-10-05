using System;
using System.Collections.Generic;
using System.Linq;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum MapGrowthIntentStatus { Unmeasured, Satisfied, Violated }
    public enum MapGrowthIntentCheckKind { Automatic, ManualAcceptance }
    public sealed class MapGrowthIntentCheck
    {
        public string ownerId, field, intended, message;
        public MapGrowthContactSource[] sources = Array.Empty<MapGrowthContactSource>();
        public float? measured;
        public MapGrowthIntentStatus status;
        public MapGrowthIntentCheckKind kind;
    }
    public sealed class MapGrowthIntentResult
    {
        public readonly List<MapGrowthIntentCheck> checks = new List<MapGrowthIntentCheck>();
        public ImpactPairState[] undeclaredContacts = Array.Empty<ImpactPairState>();
        public bool measurementsComplete;
        public bool RequirementsSatisfied => measurementsComplete && checks.All(c => c.status == MapGrowthIntentStatus.Satisfied);
        public bool AutomaticRequirementsSatisfied => measurementsComplete && checks.All(c => c.kind == MapGrowthIntentCheckKind.ManualAcceptance || c.status == MapGrowthIntentStatus.Satisfied);
    }

    /// <summary>Сопоставляет замысел с готовыми строками общего ядра; не выполняет собственные лучи или формулы пробития.</summary>
    public static class MapGrowthIntentEvaluation
    {
        public static MapGrowthIntentResult Evaluate(MapGrowthEvaluationInput input, MapEvaluationResult measured, MapGrowthSearchParameters search = null)
        {
            var result = new MapGrowthIntentResult();
            if (input == null || !input.CanEvaluate)
            {
                result.checks.Add(new MapGrowthIntentCheck { ownerId = "map", field = "input", intended = "Валидный вход",
                    message = "Вход не готов к измерению: " + string.Join("; ", input?.validation?.errors.Select(e => e.ownerId + ": " + e.message) ?? Array.Empty<string>()) });
                return result;
            }
            bool sameGrid = measured != null && ReferenceEquals(input.grid, measured.grid);
            bool sameInput = sameGrid && measured.contacts != null && measured.contacts.inputFingerprint != null
                && measured.contacts.inputFingerprint == PositionImpactFingerprint.Compute(input.grid, input.layout);
            var contacts = sameInput ? measured.contacts : null;
            var rows = contacts?.pairs ?? Array.Empty<ImpactPairState>();
            result.measurementsComplete = sameInput && measured.measurementsComplete && contacts.complete;
            var declared = new HashSet<(string from, string to, string fromState, string toState)>();
            foreach (var pair in input.contacts)
            foreach (var condition in pair.cases)
            {
                string owner = pair.positionAId + " ↔ " + pair.positionBId + "/" + condition.stateAId + " ↔ " + condition.stateBId;
                var abKey = (pair.positionAId, pair.positionBId, condition.stateAId, condition.stateBId);
                var baKey = (pair.positionBId, pair.positionAId, condition.stateBId, condition.stateAId);
                declared.Add(abKey); declared.Add(baKey);
                var ab = rows.FirstOrDefault(row => (row.from, row.to, row.fromState, row.toState) == abKey);
                var ba = rows.FirstOrDefault(row => (row.from, row.to, row.fromState, row.toState) == baKey);
                Direction(result, owner, "A→B", condition.aToB, ab, condition.sources);
                Direction(result, owner, "B→A", condition.bToA, ba, condition.sources);
                if (condition.advantage != ExpectedAdvantage.Unspecified)
                    result.checks.Add(new MapGrowthIntentCheck { ownerId = owner, field = "advantage", sources = condition.sources,
                        kind = MapGrowthIntentCheckKind.ManualAcceptance,
                        intended = condition.advantage == ExpectedAdvantage.A ? pair.positionAId : pair.positionBId,
                        message = "Критерий преимущества ещё не откалиброван; геометрические доли не доказывают доминирование." });
            }
            foreach (var spec in input.layout.routes)
            {
                var route = contacts?.routes?.FirstOrDefault(r => r.id == spec.id);
                result.checks.Add(new MapGrowthIntentCheck { ownerId = spec.id, field = "route", intended = spec.requireDirect ? "Прямой проход тела в коридоре" : "Доступный проход тела в коридоре",
                    status = route == null ? MapGrowthIntentStatus.Unmeasured : route.reachable ? MapGrowthIntentStatus.Satisfied : MapGrowthIntentStatus.Violated,
                    measured = route != null && route.reachable ? (float?)route.length : null,
                    message = route == null ? "Нет измерения маршрута." : route.reachable ? "Длина прохода, м." : route.problem });
            }
            foreach (var issue in input.validation.uncheckedRequirements)
                result.checks.Add(new MapGrowthIntentCheck { ownerId = issue.ownerId, field = issue.field, intended = issue.code, message = issue.message,
                    kind = issue.code == "PROFILE_UNCALIBRATED" || issue.code == "ADVANTAGE_UNCALIBRATED" ? MapGrowthIntentCheckKind.ManualAcceptance : MapGrowthIntentCheckKind.Automatic });
            if (search != null)
            {
                var space = measured?.space;
                Range(result, "map", "density", search.density, space != null && space.complete ? (float?)space.movementObstacleShare : null, Array.Empty<MapGrowthContactSource>());
                Range(result, "map", "closureStanding", search.closureStanding, space != null && space.complete ? (float?)space.standingClosure : null, Array.Empty<MapGrowthContactSource>());
                Range(result, "map", "closureCrouching", search.closureCrouching, space != null && space.complete ? (float?)space.crouchingClosure : null, Array.Empty<MapGrowthContactSource>());
            }
            result.undeclaredContacts = rows.Where(row => (row.visibleShare > 0 || row.shotShare > 0)
                && !declared.Contains((row.from, row.to, row.fromState, row.toState))).ToArray();
            if (!result.measurementsComplete)
                result.checks.Add(new MapGrowthIntentCheck { ownerId = "map", field = "completeness", intended = "Полный расчёт",
                    message = !sameGrid ? "Результат отсутствует или относится к другой сетке."
                        : !sameInput ? "Позы, маршруты, профиль или сетка изменились после измерения; нужен новый расчёт."
                        : string.Join("; ", contacts?.problems ?? new[] { "Контакты не измерены." }) });
            return result;
        }

        private static void Direction(MapGrowthIntentResult result, string owner, string side, MapGrowthContactDirection intended,
            ImpactPairState row, MapGrowthContactSource[] sources)
        {
            if (intended.vision != ContactRequirement.Unspecified)
                Check(result, owner, side + ".vision", intended.vision.ToString(), row == null ? null : (float?)row.visibleShare,
                    value => intended.vision == ContactRequirement.Required ? value > 0 : value == 0, sources);
            Range(result, owner, side + ".anyShotShare", intended.anyShotShare, row == null ? null : (float?)row.shotShare, sources);
            Range(result, owner, side + ".visibleShotShare", intended.visibleShotShare, row == null ? null : (float?)row.visibleShotShare, sources);
            Range(result, owner, side + ".sourceExposure", intended.sourceExposure, row == null ? null : (float?)row.sourceExposure, sources);
        }
        private static void Range(MapGrowthIntentResult result, string owner, string field, MapGrowthMetricRange range, float? value, MapGrowthContactSource[] sources)
        {
            if (range != null && range.enabled)
                Check(result, owner, field, "[" + range.min + "; " + range.max + "]", value, number => number >= range.min && number <= range.max, sources);
        }
        private static void Check(MapGrowthIntentResult result, string owner, string field, string intended, float? value,
            Func<float, bool> accepted, MapGrowthContactSource[] sources)
        {
            bool valid = value.HasValue && PositionImpactValidation.Finite(value.Value) && value.Value >= 0 && value.Value <= 1;
            result.checks.Add(new MapGrowthIntentCheck { ownerId = owner, field = field, intended = intended, sources = sources,
                measured = valid ? value : null, status = !valid ? MapGrowthIntentStatus.Unmeasured : accepted(value.Value) ? MapGrowthIntentStatus.Satisfied : MapGrowthIntentStatus.Violated,
                message = valid ? (owner == "map" ? "Числовое измерение; не рейтинг баланса." : "Доля образцов тела; не вероятность попадания.")
                    : owner == "map" ? "Пространственное измерение не выполнено." : "Соответствующая пара поз не измерена." });
        }
    }
}

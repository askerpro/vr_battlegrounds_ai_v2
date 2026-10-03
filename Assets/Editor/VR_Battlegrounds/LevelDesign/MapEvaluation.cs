using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum MapEvaluationProfile { Unspecified, Open, Closed }
    [Serializable] public sealed class MapEvaluationIssue { public string rule; public string message; }
    [Serializable] public sealed class MapEvaluationResult
    {
        public string map; public MapEvaluationProfile profile;
        public bool measurementsComplete; public bool readyForPlaytest;
        public List<MapEvaluationIssue> violations = new List<MapEvaluationIssue>();
        public List<MapEvaluationIssue> uncheckedRequirements = new List<MapEvaluationIssue>();
        public List<MapEvaluationIssue> diagnostics = new List<MapEvaluationIssue>();
        public PositionImpactResult contacts;
        public MapSpatialMetricsResult space;
        // Рабочие данные для картинок; сцена должна оставаться открытой до конца оценки.
        [NonSerialized] public MapGrid grid;
        [NonSerialized] public float[] clearance;
        [NonSerialized] public List<MapAnalyzer.NarrowPassage> passages;
        [NonSerialized] public List<MapAnalyzer.Pocket> pockets;
        [NonSerialized] public List<MapAnalyzer.Sightline> sightlines;
        [NonSerialized] public MapAnalyzer.VisibilityStats visibility;
        public bool visibilityMeasured;
        public int closePairs, mediumPairs, longPairs, throughOpeningPairs, blindShotPairs;
        public float maxEnemyShare, zoneAExposure, zoneBExposure;
    }
    public static class MapEvaluation
    {
        public static MapEvaluationResult Evaluate(string map, MapGridBuilder.Result built, PositionImpactLayout layout,
            MapEvaluationProfile profile, bool includeVisibility = true,
            int intervalBudget = PositionImpactAnalysis.MaximumInfluenceIntervals, IEnumerable<Bounds> footprints = null)
        {
            if (profile == MapEvaluationProfile.Unspecified && layout != null) profile = layout.profile;
            var r = new MapEvaluationResult { map = map, profile = profile };
            Add(r.uncheckedRequirements, "ARENA", "Сверить пол, столбы, якоря и отсутствие смещения физической арены.");
            Add(r.uncheckedRequirements, "BOUNDARY", "Проверить защиту столбов и границ, запрет выхода за арену.");
            Add(r.uncheckedRequirements, "POSE", "Проверить полный объём тела/оружия и комфорт в шлеме.");
            Add(r.uncheckedRequirements, "LD-49", "Вручную подтвердить три независимых подхода с каждой базы; три полосы — эвристика.");
            Add(r.uncheckedRequirements, "LD-50", "Осмотреть остаточные зазоры, диагонали, составные формы и намеренные бойницы.");
            Add(r.uncheckedRequirements, "BRIEF", "Сверить заявленные контакты и разные роли сторон с паспортом карты.");
            if (profile == MapEvaluationProfile.Unspecified || !Enum.IsDefined(typeof(MapEvaluationProfile), profile))
                Add(r.uncheckedRequirements, "PROFILE", "Профиль карты не выбран или неизвестен.");
            Add(r.uncheckedRequirements, "PROFILE-REVIEW", "Сверить заявленный Open/Closed с паспортом и плейтестом; универсальных порогов нет.");
            if (built == null || built.Problems.Count > 0 || built.Grid == null)
            {
                Add(r.uncheckedRequirements, "GEOMETRY", built == null ? "Нет геометрии." : string.Join("; ", built.Problems));
                return r;
            }
            r.grid = built.Grid;
            r.clearance = MapAnalyzer.Clearance(r.grid);
            r.space = MapSpatialMetrics.Measure(r.grid, r.clearance);
            if (!r.space.complete) Add(r.uncheckedRequirements, "SPACE", r.space.problem);
            AddMany(r.violations, "LD-20", MapAnalyzer.CheckCoverHeights(built.BlockoutTops));
            AddMany(r.violations, "LD-48", MapAnalyzer.CheckVaultables(built.Vaultables));
            r.passages = MapAnalyzer.FindNarrowPassages(r.grid, r.clearance);
            r.pockets = MapAnalyzer.FindUnreachable(r.grid, r.clearance);
            r.sightlines = MapAnalyzer.FindBaseToBaseSightlines(r.grid, r.clearance);
            AddMany(r.violations, "LD-23", r.passages.Select(p => p.ToString()));
            AddMany(r.violations, "LD-25", r.pockets.Select(p => p.ToString()));
            // Стартовые дуэли допустимы по паспорту; лучи остаются измерением, не запретом.
            AddMany(r.diagnostics, "LD-15", r.sightlines.Select(p => "Стартовая видимость база—база: " + p));
            AddMany(r.diagnostics, "LD-15", MapAnalyzer.FindBaseToBaseShotlines(r.grid, r.clearance).Select(p => "Стартовый прострел вслепую база—база: " + p));
            if (!MapAnalyzer.TryHalves(r.grid, out _, out _))
                Add(r.uncheckedRequirements, "BASES", "Нет двух размеченных баз; пустые результаты не подтверждают защиту баз.");
            AddMany(r.diagnostics, "LD-49", MapFeedbackRules.CheckApproaches(r.grid, r.clearance));
            if (footprints != null) AddMany(r.diagnostics, "LD-50", MapFeedbackRules.CheckResidualGaps(footprints));
            else Add(r.diagnostics, "LD-50", "Кандидаты зазоров не вычислены: нет Bounds препятствий.");
            if (profile == MapEvaluationProfile.Open)
                AddMany(r.diagnostics, "LD-51", MapFeedbackRules.CheckOpenCover(r.grid));
            if (includeVisibility)
            {
                r.visibility = MapAnalyzer.Visibility(r.grid, r.clearance); r.visibilityMeasured = true;
                r.closePairs = r.visibility.Close; r.mediumPairs = r.visibility.Medium; r.longPairs = r.visibility.Long;
                r.throughOpeningPairs = r.visibility.ThroughOpenings; r.blindShotPairs = r.visibility.BlindShots;
                r.maxEnemyShare = r.visibility.MaxEnemyShare; r.zoneAExposure = r.visibility.ZoneAExposure; r.zoneBExposure = r.visibility.ZoneBExposure;
            }
            else Add(r.diagnostics, "VISIBILITY", "Общая тепловая карта не запрошена; метрики видимости отсутствуют.");
            if (layout == null)
            { Add(r.uncheckedRequirements, "CONTACTS", "Нет JSON разметки; контакты и маршруты не измерены."); return r; }
            if (layout.map != map)
            { Add(r.violations, "LAYOUT", "Имя map в разметке не совпадает с картой."); return r; }
            r.contacts = PositionImpactAnalysis.Analyze(r.grid, layout, intervalBudget);
            if (!r.contacts.complete)
                Add(r.uncheckedRequirements, "CONTACTS", "Расчёт отказал или неполон; отсутствующие строки не равны нулю.");
            AddMany(r.diagnostics, "CONTACTS", r.contacts.problems);
            foreach (var route in r.contacts.routes.Where(p => !p.reachable))
                Add(r.violations, "ROUTE", route.id + ": " + route.problem);
            if (r.contacts.complete)
            {
                foreach (var from in layout.positions)
                foreach (var to in layout.positions.Where(p => p.id != from.id))
                {
                    var rows = r.contacts.pairs.Where(p => p.from == from.id && p.to == to.id).ToArray();
                    if (rows.Length != from.states.Length * to.states.Length)
                        Add(r.uncheckedRequirements, "CONTACTS", from.id + " → " + to.id + ": отсутствуют сочетания поз.");
                    else if (rows.All(p => p.shotShare == 0 && p.visibleShare == 0))
                        Add(r.diagnostics, "ZERO-CONTACT", from.id + " → " + to.id + ": контакт отсутствует во всех размеченных позах.");
                }
            }
            r.measurementsComplete = r.contacts.complete && r.contacts.routes.Length == layout.routes.Length &&
                !r.uncheckedRequirements.Any(p => p.rule == "CONTACTS") && r.space.complete;
            // Ручные обязательные пункты не снимаются автоматически или устаревшим подтверждением.
            r.readyForPlaytest = r.measurementsComplete && r.violations.Count == 0 && r.uncheckedRequirements.Count == 0;
            return r;
        }

        private static void Add(List<MapEvaluationIssue> list, string rule, string message)
            => list.Add(new MapEvaluationIssue { rule = rule, message = message });
        private static void AddMany(List<MapEvaluationIssue> list, string rule, IEnumerable<string> messages)
        { foreach (string message in messages) Add(list, rule, message); }

        public static string Format(MapEvaluationResult r)
        {
            var text = new StringBuilder();
            text.AppendLine($"══ {r.map}; профиль {r.profile} ══");
            text.AppendLine(r.measurementsComplete ? "Запрошенные контакты/маршруты измерены; это не доказательство баланса." : "Оценка неполная: готовность не подтверждена.");
            Section("Нарушения", r.violations);
            Section("Непроверенные обязательные пункты", r.uncheckedRequirements);
            text.AppendLine("Измеренные контакты и маршруты:");
            if (r.space != null)
                text.AppendLine(r.space.complete ? $"  Плотность преград для перемещения D={r.space.movementObstacleShare:F3}; свободная доля={r.space.freeShare:F3}; закрытость обзора стоя C={r.space.standingClosure:F3}, присев C={r.space.crouchingClosure:F3}; образцы={r.space.sampleCount}, LOS={r.space.lineQueries}." : "  Пространственные метрики неизвестны: " + r.space.problem);
            if (r.visibilityMeasured)
            {
                text.AppendLine($"  Видимые пары: ближние {r.closePairs}, средние {r.mediumPairs}, дальние {r.longPairs}; проёмы {r.throughOpeningPairs}; невидимый прострел {r.blindShotPairs}.");
                text.AppendLine($"  Максимальная доля чужой половины {r.maxEnemyShare:P0}; открытость баз A {r.zoneAExposure:P0}, B {r.zoneBExposure:P0}.");
            }
            if (r.contacts != null)
            {
                foreach (var p in r.contacts.pairs)
                    text.AppendLine($"  {p.from}/{p.fromState} → {p.to}/{p.toState}: видимость={p.visibleShare:F3}, огонь={p.shotShare:F3}, скрытый огонь={p.hiddenShotShare:F3}, открытость источника={p.sourceExposure:F3}.");
                foreach (var p in r.contacts.routes)
                    text.AppendLine($"  Маршрут {p.id}: " + (p.reachable ? $"{p.length:F2} м, {p.duration:F2} с, clearance={p.minimumClearance:F2} м" : p.problem));
                foreach (var p in r.contacts.influences)
                    text.AppendLine($"  {p.position}/{p.state} → маршрут {p.route}: огонь {p.shotLength:F2} м / {p.shotDuration:F2} с, непрерывно {p.longestShotLength:F2} м.");
                foreach (string a in r.contacts.assumptions ?? Array.Empty<string>()) text.AppendLine("  Допущение: " + a);
            }
            Section("Диагностика и пожелания для ревью", r.diagnostics);
            return text.ToString();
            void Section(string title, List<MapEvaluationIssue> issues)
            {
                text.AppendLine(title + ": " + issues.Count);
                foreach (var issue in issues) text.AppendLine("  " + issue.rule + ": " + issue.message);
            }
        }
    }
}

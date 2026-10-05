using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class PositionImpactResult
    {
        public int version = 1; public string map; public bool complete;
        public string inputFingerprint;
        public string[] problems = Array.Empty<string>();
        public string[] assumptions;
        public ImpactPairState[] pairs = Array.Empty<ImpactPairState>();
        public ImpactRoute[] routes = Array.Empty<ImpactRoute>();
        public ImpactRouteInfluence[] influences = Array.Empty<ImpactRouteInfluence>();
        public int walkableCells; public int positionCells; public int unassignedCells; public int overlappingPositionCells;
    }
    public static class PositionImpactAnalysis
    {
        // Лимит не является оценкой качества: при превышении отчёт честно помечается неполным.
        public const int MaximumInfluenceIntervals = 50000;

        public static PositionImpactResult Analyze(MapGrid grid, PositionImpactLayout layout,
            int intervalBudget = MaximumInfluenceIntervals, float[] clearance = null)
        {
            var result = new PositionImpactResult();
            foreach (var work in AnalyzeSteps(grid, layout, result, intervalBudget, clearance)) work.Execute();
            return result;
        }
        internal static IEnumerable<MapEvaluationWork> AnalyzeSteps(MapGrid grid, PositionImpactLayout layout,
            PositionImpactResult result, int intervalBudget = MaximumInfluenceIntervals, float[] clearance = null, MapRouteBuilder routeBuilder = null)
        {
                result.map = layout?.map;
                result.assumptions = new[]
                {
                    "Геометрические доли взвешенных образцов тела, не вероятность попадания или победы.",
                    "Явные состояния дизайнера. Проверяется горизонтальный диск, не полный 3D объём тела/оружия.",
                    "Видимость из глаз отдельно от ShotLine из ствола. Нет урона конкретного оружия.",
                    "visibleShot: доступная и видимая траектория ствола; hiddenShot: доступная невидимая траектория. Это не история пробитий.",
                    "Стрелок фиксирован на всём маршруте. Движущееся тело использует шаблон исходного состояния и поворачивается по пути.",
                    "Точки маршрута привязаны к центрам клеток; интервалы измерены в серединах рёбер. Скорость постоянна.",
                    "complete означает расчёт всех запрошенных данных, не доказанный баланс или полноту тактической разметки.",
                    "Клетки вне областей позиций считаются неразмеченными; автоматического поиска удержаний и независимых подходов ещё нет."
                };
            float[] clear = grid == null ? null : clearance ?? MapAnalyzer.Clearance(grid);
            List<string> problems = PositionImpactValidation.Validate(grid, layout, clear);
            if (problems.Count > 0) { result.problems = problems.ToArray(); yield break; }
            result.inputFingerprint = PositionImpactFingerprint.Compute(grid, layout);
            var pairs = new List<ImpactPairState>();
            foreach (ImpactPosition from in layout.positions)
            foreach (ImpactPosition to in layout.positions)
            {
                if (from.id == to.id) continue;
                var baseline = string.IsNullOrEmpty(from.protectedState) ? null : from.states.Single(s => s.id == from.protectedState);
                foreach (var a in from.states) foreach (var b in to.states)
                    yield return new MapEvaluationWork(3 * (a.body.Length + b.body.Length + (baseline?.body.Length ?? 0)),
                        () => pairs.Add(PositionImpactAnalyzer.EvaluatePairState(grid, from, to, a, b, baseline)), "contacts");
            }
            result.pairs = pairs.ToArray();

            for (int i = 0; i < grid.Count; i++)
            {
                if (grid.Blocked[i] || clear[i] < layout.radius) continue;
                result.walkableCells++;
                Vector2 c = grid.Center(i);
                int owners = layout.positions.Count(p => c.x >= p.min.x && c.x <= p.max.x && c.y >= p.min.y && c.y <= p.max.y);
                if (owners == 0) result.unassignedCells++;
                else result.positionCells++;
                if (owners > 1) result.overlappingPositionCells++;
            }
            if (result.overlappingPositionCells > 0)
                problems.Add("Области позиций перекрываются: " + result.overlappingPositionCells + " клеток; проверьте семантическую разметку.");

            var routes = new List<ImpactRoute>();
            var influences = new List<ImpactRouteInfluence>();
            int evaluatedIntervals = 0;
            bool budgetExceeded = false;
            foreach (ImpactRouteSpec spec in layout.routes)
            {
                ImpactState start = layout.positions.Single(p => p.id == spec.from).states.Single(s => s.id == spec.fromState);
                ImpactState end = layout.positions.Single(p => p.id == spec.to).states.Single(s => s.id == spec.toState);
                bool[] mask = null;
                if (spec.allowedCellIndices != null)
                { mask = new bool[grid.Count]; foreach (int cell in spec.allowedCellIndices) mask[cell] = true; }
                ImpactRoute route = null;
                var task = routeBuilder?.Invoke(grid, start.center, end.center, spec.via, layout.radius, layout.speed, mask, clear, spec.requireDirect);
                if (task == null)
                    yield return new MapEvaluationWork(0, () => route = PositionRouteAnalyzer.Build(grid, start.center, end.center, spec.via, layout.radius, layout.speed, mask, clear, spec.requireDirect), "routes");
                else
                    yield return new MapEvaluationWork(0, () => route = task.GetAwaiter().GetResult(), "routes", () => task.IsCompleted);
                route.id = spec.id; route.from = spec.from; route.to = spec.to;
                routes.Add(route);
                if (!route.reachable) { problems.Add(spec.id + ": " + route.problem); continue; }
                foreach (ImpactPosition p in layout.positions)
                foreach (ImpactState s in p.states)
                {
                    int count = Math.Max(0, route.points.Length - 1);
                    if (evaluatedIntervals + count > intervalBudget)
                    { budgetExceeded = true; continue; }
                    evaluatedIntervals += count;
                    var row = new ImpactRouteInfluence();
                    foreach (var work in PositionRouteAnalyzer.EvaluateSteps(grid, s, route, start, layout.speed, row)) yield return work.InCategory("contacts");
                    row.position = p.id; row.route = spec.id; row.targetTemplate = spec.from + "/" + start.id;
                    influences.Add(row);
                }
            }
            if (budgetExceeded) problems.Add("Превышен лимит интервалов влияния. Часть сочетаний отсутствует, а не равна нулю.");
            result.routes = routes.ToArray(); result.influences = influences.ToArray();
            result.problems = problems.ToArray(); result.complete = !budgetExceeded;
        }
    }
}

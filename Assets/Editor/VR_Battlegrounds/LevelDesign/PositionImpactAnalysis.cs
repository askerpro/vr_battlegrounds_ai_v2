using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class PositionImpactResult
    {
        public int version = 1; public string map; public bool complete;
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
            int intervalBudget = MaximumInfluenceIntervals)
        {
            var result = new PositionImpactResult
            {
                map = layout?.map,
                assumptions = new[]
                {
                    "Геометрические доли взвешенных образцов тела, не вероятность попадания или победы.",
                    "Явные состояния дизайнера. Проверяется горизонтальный диск, не полный 3D объём тела/оружия.",
                    "Видимость из глаз отдельно от ShotLine из ствола. Нет урона конкретного оружия.",
                    "visibleShot: доступная и видимая траектория ствола; hiddenShot: доступная невидимая траектория. Это не история пробитий.",
                    "Стрелок фиксирован на всём маршруте. Движущееся тело использует шаблон исходного состояния и поворачивается по пути.",
                    "Точки маршрута привязаны к центрам клеток; интервалы измерены в серединах рёбер. Скорость постоянна.",
                    "complete означает расчёт всех запрошенных данных, не доказанный баланс или полноту тактической разметки.",
                    "Клетки вне областей позиций считаются неразмеченными; автоматического поиска удержаний и независимых подходов ещё нет."
                }
            };
            List<string> problems = PositionImpactValidation.Validate(grid, layout);
            if (problems.Count > 0) { result.problems = problems.ToArray(); return result; }
            var pairs = new List<ImpactPairState>();
            foreach (ImpactPosition from in layout.positions)
            foreach (ImpactPosition to in layout.positions)
                if (from.id != to.id) pairs.AddRange(PositionImpactAnalyzer.EvaluatePair(grid, from, to));
            result.pairs = pairs.ToArray();

            float[] clear = MapAnalyzer.Clearance(grid);
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
                ImpactRoute route = PositionRouteAnalyzer.Build(grid, start.center, end.center, spec.via, layout.radius, layout.speed);
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
                    ImpactRouteInfluence row = PositionRouteAnalyzer.Evaluate(grid, s, route, start, layout.speed);
                    row.position = p.id; row.route = spec.id; row.targetTemplate = spec.from + "/" + start.id;
                    influences.Add(row);
                }
            }
            if (budgetExceeded) problems.Add("Превышен лимит интервалов влияния. Часть сочетаний отсутствует, а не равна нулю.");
            result.routes = routes.ToArray(); result.influences = influences.ToArray();
            result.problems = problems.ToArray(); result.complete = !budgetExceeded;
            return result;
        }
    }
}

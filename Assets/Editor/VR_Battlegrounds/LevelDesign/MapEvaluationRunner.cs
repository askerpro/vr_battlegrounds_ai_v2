using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Порционный маршрут общего отчёта без повторного расчёта пройденных стадий.</summary>
    public sealed class MapEvaluationRunner : IDisposable
    {
        public const int MinimumQueryBudget = 144;
        private readonly MapEvaluationResult result;
        private readonly MapEvaluationWorkQueue queue;
        public bool Complete => queue.Complete;
        public int LastStepQueryBudget => queue.LastStepQueryBudget;
        public long TotalQueryBudget => queue.TotalQueryBudget;
        public MapEvaluationResult Result => Complete ? result : throw new InvalidOperationException("Общий отчёт ещё не завершён.");
        internal MapEvaluationRunner(string map, MapGridBuilder.Result built, PositionImpactLayout layout,
            MapEvaluationProfile profile, bool includeVisibility, int intervalBudget, IEnumerable<Bounds> footprints, CancellationToken token)
        {
            result = new MapEvaluationResult { map = map, profile = profile };
            queue = new MapEvaluationWorkQueue(MapEvaluation.EvaluateSteps(map, built, layout, profile, includeVisibility, intervalBudget, footprints, result).GetEnumerator(), token);
        }
        public bool Step(int maximumQueries = 256)
            => queue.Step(maximumQueries);
        public void Dispose()
            => queue.Dispose();
    }
}

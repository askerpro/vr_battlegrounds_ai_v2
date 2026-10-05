using System;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Совместимая оболочка общего исполнительного цикла. Старый вызов — один поток без Task.</summary>
    public sealed class MapGrowthSearchRun : IDisposable
    {
        private readonly MapGrowthScheduler scheduler;
        public MapGrowthReport Report => scheduler.Report;
        public bool Finished => scheduler.Finished;
        public string Phase => scheduler.Phase;
        public int BranchId => scheduler.BranchId;
        public int AttemptId => scheduler.AttemptId;
        public int PendingPreparations => scheduler.PendingPreparations;
        public int QueuedEvaluations => scheduler.QueuedEvaluations;
        public MapGrowthSearchRun(MapGrowthSceneCapture capture, bool automaticUpdate = false)
            : this(capture, new MapGrowthExecutionOptions { maxWorkers = 1, backgroundPreparation = false }, automaticUpdate) { }
        public MapGrowthSearchRun(MapGrowthSceneCapture capture, MapGrowthExecutionOptions execution, bool automaticUpdate = false)
            => scheduler = MapGrowthScheduler.Start(capture, execution, automaticUpdate: automaticUpdate);
        public bool Step(int maximumQueries = 256) => scheduler.Step(maximumQueries);
        public void Cancel() => scheduler.Cancel();
        public void Dispose() => scheduler.Dispose();
    }
}

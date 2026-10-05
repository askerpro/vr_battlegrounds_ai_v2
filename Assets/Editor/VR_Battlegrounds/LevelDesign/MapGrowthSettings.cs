using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Flags] public enum MapGrowthOperation
    {
        None = 0, Add = 1, Remove = 2, Move = 4, Rotate = 8, ChangeShape = 16,
        ChangeHeight = 32, ChangeMaterial = 64, ChangeOpenings = 128, AddDetail = 256,
        All = Add | Remove | Move | Rotate | ChangeShape | ChangeHeight | ChangeMaterial | ChangeOpenings | AddDetail
    }

    [Serializable] public sealed class MapGrowthSearchParameters
    {
        public int seed = 1, targetCandidates = 5, branchCount = 20, attemptsPerBranch = 200;
        public int maximumGeneratedBlocks = 32;
        public MapGrowthOperation operations = MapGrowthOperation.All;
        public MapGrowthMetricRange density = new MapGrowthMetricRange();
        public MapGrowthMetricRange closureStanding = new MapGrowthMetricRange();
        public MapGrowthMetricRange closureCrouching = new MapGrowthMetricRange();
    }

    /// <summary>Переносимый замысел поиска; workers относятся к настройке исполнения машины.</summary>
    public sealed class MapGrowthSettings : ScriptableObject
    {
        public MapEvaluationProfile profile;
        public MapGrowthSearchParameters search = new MapGrowthSearchParameters();
        public List<string> fixedBlockGlobalObjectIds = new List<string>();
    }
}

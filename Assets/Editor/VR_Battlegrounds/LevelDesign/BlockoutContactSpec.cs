using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public enum BlockoutStance { Standing, Crouching }
    public enum ContactRequirement { Unspecified, Required, Forbidden }
    public enum ExpectedAdvantage { Unspecified, A, B }

    [Serializable] public sealed class BlockoutPositionState
    {
        public string id;
        public Vector2 centerOffset;
        public float yaw;
        public BlockoutStance stance;
    }

    [Serializable] public sealed class MapGrowthMetricRange
    {
        public bool enabled;
        public float min, max = 1;
        public MapGrowthMetricRange Copy() => new MapGrowthMetricRange { enabled = enabled, min = min, max = max };
    }

    [Serializable] public sealed class BlockoutContactDirection
    {
        public ContactRequirement vision, shot;
        public bool visibleShotOnly;
        public MapGrowthMetricRange shotShare = new MapGrowthMetricRange();
        public MapGrowthMetricRange sourceExposure = new MapGrowthMetricRange();

        public BlockoutContactDirection Copy() => new BlockoutContactDirection {
            vision = vision, shot = shot, visibleShotOnly = visibleShotOnly,
            shotShare = shotShare?.Copy(), sourceExposure = sourceExposure?.Copy() };
    }

    [Serializable] public sealed class BlockoutContactCase
    {
        public string id, stateAId, stateBId;
        public BlockoutContactDirection aToB = new BlockoutContactDirection();
        public BlockoutContactDirection bToA = new BlockoutContactDirection();
        public ExpectedAdvantage advantage;
    }

    /// <summary>Одна неупорядоченная пара позиций; условия и обе стороны замысла хранятся вместе.</summary>
    [Serializable] public sealed class BlockoutContactSpec
    {
        public string id, positionAId, positionBId, displayName, description;
        public BlockoutContactCase[] cases = Array.Empty<BlockoutContactCase>();

        /// <summary>Смена порядка ID переставляет позы, направления и преимущество, не изменяя исходный артефакт.</summary>
        public static BlockoutContactSpec CanonicalCopy(BlockoutContactSpec source)
        {
            if (source == null) return null;
            bool swap = string.CompareOrdinal(source.positionAId, source.positionBId) > 0;
            var result = new BlockoutContactSpec {
                id = source.id, displayName = source.displayName, description = source.description,
                positionAId = swap ? source.positionBId : source.positionAId,
                positionBId = swap ? source.positionAId : source.positionBId,
                cases = source.cases == null ? null : new BlockoutContactCase[source.cases.Length] };
            if (source.cases == null) return result;
            for (int i = 0; i < source.cases.Length; i++)
            {
                var c = source.cases[i];
                if (c == null) continue;
                result.cases[i] = new BlockoutContactCase {
                    id = c.id, stateAId = swap ? c.stateBId : c.stateAId, stateBId = swap ? c.stateAId : c.stateBId,
                    aToB = (swap ? c.bToA : c.aToB)?.Copy(), bToA = (swap ? c.aToB : c.bToA)?.Copy(),
                    advantage = swap && c.advantage != ExpectedAdvantage.Unspecified
                        ? c.advantage == ExpectedAdvantage.A ? ExpectedAdvantage.B
                        : c.advantage == ExpectedAdvantage.B ? ExpectedAdvantage.A : c.advantage : c.advantage };
            }
            return result;
        }
    }
}

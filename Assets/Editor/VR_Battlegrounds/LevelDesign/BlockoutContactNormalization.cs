using System;
using System.Linq;

namespace VrBattlegrounds.Editor.LevelDesign
{
    // Числовые данные оценки: исходные ID сохраняются отдельно от объединённых ограничений.
    public sealed class MapGrowthContactSource
    {
        public string contactId, caseId;
    }

    public sealed class MapGrowthContactDirection
    {
        public ContactRequirement vision;
        public MapGrowthMetricRange anyShotShare = new MapGrowthMetricRange();
        public MapGrowthMetricRange visibleShotShare = new MapGrowthMetricRange();
        public MapGrowthMetricRange sourceExposure = new MapGrowthMetricRange();
    }

    public sealed class MapGrowthContactCase
    {
        public string stateAId, stateBId;
        public ExpectedAdvantage advantage;
        public MapGrowthContactDirection aToB, bToA;
        public MapGrowthContactSource[] sources;
    }

    public sealed class MapGrowthContactPair
    {
        public string positionAId, positionBId;
        public string[] sourceContactIds;
        public MapGrowthContactCase[] cases;
    }

    /// <summary>Одна пара и один набор требований на пару поз. Объединение не изменяет авторские записи и их ссылки.</summary>
    public static class BlockoutContactNormalization
    {
        public static bool TryNormalize(BlockoutMarkup markup, out MapGrowthContactPair[] pairs, out MapGrowthInputValidation validation)
        {
            validation = MapGrowthValidation.ValidateContactInput(markup);
            pairs = Array.Empty<MapGrowthContactPair>();
            if (!validation.CanStart) return false;
            pairs = markup.contacts.Select(BlockoutContactSpec.CanonicalCopy)
                .GroupBy(c => (c.positionAId, c.positionBId))
                .OrderBy(g => g.Key.positionAId, StringComparer.Ordinal).ThenBy(g => g.Key.positionBId, StringComparer.Ordinal)
                .Select(pair => new MapGrowthContactPair {
                    positionAId = pair.Key.positionAId, positionBId = pair.Key.positionBId,
                    sourceContactIds = pair.Select(c => c.id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    cases = pair.SelectMany(c => c.cases.Select(condition => new { contact = c, condition }))
                        .GroupBy(entry => (entry.condition.stateAId, entry.condition.stateBId))
                        .OrderBy(g => g.Key.stateAId, StringComparer.Ordinal).ThenBy(g => g.Key.stateBId, StringComparer.Ordinal)
                        .Select(group => new MapGrowthContactCase {
                            stateAId = group.Key.stateAId, stateBId = group.Key.stateBId,
                            advantage = group.Select(e => e.condition.advantage).FirstOrDefault(a => a != ExpectedAdvantage.Unspecified),
                            aToB = MergeDirection(group.Select(e => e.condition.aToB).ToArray()),
                            bToA = MergeDirection(group.Select(e => e.condition.bToA).ToArray()),
                            sources = group.OrderBy(e => e.contact.id, StringComparer.Ordinal).ThenBy(e => e.condition.id, StringComparer.Ordinal)
                                .Select(e => new MapGrowthContactSource { contactId = e.contact.id, caseId = e.condition.id }).ToArray()
                        }).ToArray()
                }).ToArray();
            return true;
        }

        private static MapGrowthContactDirection MergeDirection(BlockoutContactDirection[] directions)
        {
            var result = new MapGrowthContactDirection();
            foreach (var d in directions)
            {
                if (d.vision != ContactRequirement.Unspecified) result.vision = d.vision;
                var shotRange = d.visibleShotOnly ? result.visibleShotShare : result.anyShotShare;
                if (d.shot != ContactRequirement.Unspecified)
                    Intersect(shotRange, d.shot == ContactRequirement.Required ? float.Epsilon : 0,
                        d.shot == ContactRequirement.Forbidden ? 0 : 1);
                if (d.shotShare.enabled) Intersect(shotRange, d.shotShare.min, d.shotShare.max);
                if (d.sourceExposure.enabled) Intersect(result.sourceExposure, d.sourceExposure.min, d.sourceExposure.max);
            }
            return result;
        }

        private static void Intersect(MapGrowthMetricRange range, float min, float max)
        {
            range.enabled = true; range.min = Math.Max(range.min, min); range.max = Math.Min(range.max, max);
        }
    }
}

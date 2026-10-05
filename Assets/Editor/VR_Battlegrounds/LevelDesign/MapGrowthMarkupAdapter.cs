using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public sealed class MapGrowthEvaluationInput
    {
        public PositionImpactLayout layout;
        public MapGrowthContactPair[] contacts = Array.Empty<MapGrowthContactPair>();
        public MapGrowthInputValidation validation;
        public MapGrid grid;
        public bool CanEvaluate => validation != null && validation.CanStart && layout != null;
    }

    /// <summary>Единственный перевод авторской разметки в общий измеритель; шаг кисти и шаг анализа независимы.</summary>
    public static class MapGrowthMarkupAdapter
    {
        public static MapGrowthEvaluationInput Build(BlockoutMarkup markup, MapGrid grid)
        {
            var body = markup == null || markup.bodyProfile == null ? null : markup.bodyProfile.data;
            var input = new MapGrowthEvaluationInput { grid = grid,
                validation = MapGrowthValidation.ValidateInput(markup, body, new MapGrowthSearchParameters()) };
            if (grid == null || grid.Width < 1 || grid.Depth < 1 || !PositionImpactValidation.Finite(grid.Cell) || grid.Cell <= 0
                || !PositionImpactValidation.Finite(grid.Origin) || !PositionImpactValidation.Finite(grid.Origin + new Vector2(grid.Width, grid.Depth) * grid.Cell))
                input.validation.errors.Add(new MapGrowthInputIssue { code = "MISSING_GRID", ownerId = "map", field = "grid", message = "Нет сетки измерителя." });
            if (!input.validation.CanStart) return input;
            if (!BlockoutContactNormalization.TryNormalize(markup, out input.contacts, out var contactIssues))
            { input.validation.errors.AddRange(contactIssues.errors); return input; }
            input.layout = new PositionImpactLayout { map = Path.GetFileNameWithoutExtension(markup.scenePath ?? ""), radius = body.radius, speed = body.speed,
                sceneGuid = markup.sceneGuid, bodyProfileId = body.id, bodyProfileVersion = body.version, bodyProfileCalibrated = body.calibrated,
                positions = markup.positions.Select(p => Position(markup, body, p)).ToArray(),
                routes = markup.routes.Select(r => new ImpactRouteSpec {
                    id = r.id, from = r.fromPositionId, to = r.toPositionId, fromState = r.fromStateId, toState = r.toStateId,
                    requireDirect = r.requireDirect, via = r.viaCells.Select(c => markup.origin + new Vector2(c.x + .5f, c.y + .5f) * markup.step).ToArray(),
                    allowedCellIndices = Corridor(markup, grid, r.id) }).ToArray() };
            foreach (var route in input.layout.routes)
                if (route.allowedCellIndices.Length == 0)
                    input.validation.errors.Add(new MapGrowthInputIssue { code = "EMPTY_ANALYSIS_CORRIDOR", ownerId = route.id,
                        field = "cells.routeIds", message = "В сетке измерителя нет целых клеток этого коридора." });
            return input;
        }

        private static ImpactPosition Position(BlockoutMarkup markup, MapBodyProfileData body, BlockoutMarkup.Position position)
        {
            Vector2 center = markup.origin + new Vector2(position.centerCell.x + .5f, position.centerCell.y + .5f) * markup.step;
            float low = (-(position.sizeCells / 2) - .5f) * markup.step;
            float high = (position.sizeCells - position.sizeCells / 2 - .5f) * markup.step;
            return new ImpactPosition { id = position.id, min = center + Vector2.one * low, max = center + Vector2.one * high,
                protectedState = position.protectedStateId, states = position.states.Select(s => {
                    MapBodyPoseTemplate template = s.stance == BlockoutStance.Standing ? body.standing : body.crouching;
                    return new ImpactState { id = s.id, center = center + s.centerOffset, yaw = position.mainThreatYaw + s.yaw,
                        eyeOffset = template.eyeOffset, muzzleOffset = template.muzzleOffset,
                        body = template.body.Select(sample => new ImpactBodySample { offset = sample.offset, weight = sample.weight }).ToArray() };
                }).ToArray() };
        }

        private static int[] Corridor(BlockoutMarkup markup, MapGrid grid, string id)
        {
            var author = new HashSet<Vector2Int>(markup.cells.Where(c => c.routeIds.Contains(id)).Select(c => new Vector2Int(c.x, c.z)));
            var allowed = new List<int>();
            // Разрешаем клетку анализа целиком, а не только её центр. Не создаём выход за авторскую полосу на несовпадающих сетках.
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 min = grid.Center(i) - Vector2.one * grid.Cell * .5f;
                Vector2 max = min + Vector2.one * grid.Cell;
                double left = Math.Floor(((double)min.x - markup.origin.x) / markup.step + 1e-5);
                double bottom = Math.Floor(((double)min.y - markup.origin.y) / markup.step + 1e-5);
                double right = Math.Ceiling(((double)max.x - markup.origin.x) / markup.step - 1e-5) - 1;
                double top = Math.Ceiling(((double)max.y - markup.origin.y) / markup.step - 1e-5) - 1;
                if (left < int.MinValue || bottom < int.MinValue || right >= int.MaxValue || top >= int.MaxValue
                    || (right - left + 1) * (top - bottom + 1) > author.Count) continue;
                int x0 = (int)left, z0 = (int)bottom, x1 = (int)right, z1 = (int)top;
                bool covered = true;
                for (int z = z0; covered && z <= z1; z++)
                for (int x = x0; x <= x1; x++) if (!author.Contains(new Vector2Int(x, z))) { covered = false; break; }
                if (covered) allowed.Add(i);
            }
            return allowed.ToArray();
        }
    }
}

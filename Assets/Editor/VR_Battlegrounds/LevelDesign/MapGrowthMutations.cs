using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public sealed class MapGrowthMutationProposal
    {
        public MapGrowthCandidate Candidate { get; internal set; }
        public MapGrowthOperation Operation { get; internal set; }
        public string RecipeId { get; internal set; }
        public string Reason { get; internal set; }
        public bool Changed { get; internal set; }
        public MapGrowthBlockRecipe ChangedRecipe { get; internal set; }
    }
    /// <summary>Чистые предложения ветки: только выращенные recipes, без Unity Random/ассетов/физики.</summary>
    public static class MapGrowthMutations
    {
        public static MapGrowthCandidate Propose(MapGrowthSnapshot snapshot, MapGrowthCandidate current, int attemptIndex)
            => ProposeDetailed(snapshot, current, attemptIndex).Candidate;

        public static MapGrowthMutationProposal ProposeDetailed(MapGrowthSnapshot snapshot, MapGrowthCandidate current, int attemptIndex)
        {
            if (snapshot == null || current == null || !current.BelongsTo(snapshot) || attemptIndex < 1) throw new ArgumentException("Нужна ветка данного снимка и номер попытки.");
            var result = new MapGrowthMutationProposal { Candidate = current.Copy("b" + current.BranchId + "-a" + attemptIndex), Reason = "Нет разрешённой операции." };
            var search = snapshot.CopySearch(); var random = new MapGrowthRandom(current.Seed, attemptIndex);
            var operations = Enum.GetValues(typeof(MapGrowthOperation)).Cast<MapGrowthOperation>()
                .Where(o => o != MapGrowthOperation.None && o != MapGrowthOperation.All && (search.operations & o) == o)
                .Where(o => current.Recipes.Count > 0 || o == MapGrowthOperation.Add || o == MapGrowthOperation.AddDetail).ToArray();
            if (operations.Length == 0) return result;
            result.Operation = operations[random.Next(operations.Length)];
            var recipes = current.Recipes.ToList(); MapGrowthBlockRecipe changed = null;
            if (result.Operation == MapGrowthOperation.Add || result.Operation == MapGrowthOperation.AddDetail)
            {
                if (recipes.Count >= search.maximumGeneratedBlocks) { result.Reason = "Достигнут предел выращенных блоков."; return result; }
                var layout = snapshot.CopyLayout();
                if (layout.positions.Length == 0 || snapshot.Variants.Count == 0 || snapshot.Anchors.Count != layout.positions.Length)
                { result.Reason = "Нужны авторские позиции, их направления угрозы и палитра."; return result; }
                var position = layout.positions[random.Next(layout.positions.Length)];
                var anchor = snapshot.Anchors.Single(a => a.PositionId == position.id);
                float yaw = Normalize((float)Math.Round(anchor.ThreatYaw / 15) * 15 + (random.Next(3) - 1) * 15);
                var options = snapshot.Variants.Where(v => Angle(v.Yaw, yaw) < 1e-4).ToArray();
                if (result.Operation == MapGrowthOperation.AddDetail)
                    options = options.Where(v => snapshot.Capabilities.Any(c => c.ShapeId == v.ShapeId && c.Detail)).ToArray();
                if (options.Length == 0) { result.Reason = "Нет подходящего рецепта палитры."; return result; }
                var variant = options[random.Next(options.Length)]; var dimensions = variant.Dimensions;
                if (result.Operation == MapGrowthOperation.AddDetail) { dimensions.x = snapshot.AuthorCell; dimensions.z = snapshot.AuthorCell; }
                double radians = anchor.ThreatYaw * Math.PI / 180;
                var direction = new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
                var half = (position.max - position.min) * .5f;
                double angle = (anchor.ThreatYaw - yaw) * Math.PI / 180;
                float blockRadius = (float)(Math.Abs(Math.Sin(angle)) * dimensions.x + Math.Abs(Math.Cos(angle)) * dimensions.z) * .5f;
                float distance = Math.Abs(direction.x) * half.x + Math.Abs(direction.y) * half.y + blockRadius + snapshot.AuthorCell;
                var center = (position.min + position.max) * .5f + direction * distance;
                // Все размеры берутся из варианта; якорь привязан к исходному выравниванию авторской сетки.
                var origin = snapshot.Variants[0].BottomCenter;
                var point = new Vector3(Aligned(center.x, origin.x, snapshot.AuthorCell), variant.BottomCenter.y, Aligned(center.y, origin.z, snapshot.AuthorCell));
                changed = new MapGrowthBlockRecipe("grown/" + current.BranchId + "/" + attemptIndex, variant.ShapeId, point, yaw, dimensions,
                    result.Operation == MapGrowthOperation.AddDetail ? Vector2.zero : variant.TopSize, variant.CopySections());
                recipes.Add(changed);
            }
            else
            {
                int index = random.Next(recipes.Count); var old = recipes[index]; result.RecipeId = old.RecipeId;
                if (result.Operation == MapGrowthOperation.Remove) recipes.RemoveAt(index);
                else
                {
                    var point = old.BottomCenter; float yaw = old.Yaw; var dimensions = old.Dimensions; var top = old.TopSize;
                    string shape = old.ShapeId; var sections = old.CopySections();
                    switch (result.Operation)
                    {
                        case MapGrowthOperation.Move:
                            var directions = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                            point += directions[random.Next(4)] * snapshot.AuthorCell; break;
                        case MapGrowthOperation.Rotate: yaw = Normalize(yaw + (random.Next(2) == 0 ? -15 : 15)); break;
                        case MapGrowthOperation.ChangeShape:
                        case MapGrowthOperation.ChangeHeight:
                            var variants = snapshot.Variants.Where(v => Angle(v.Yaw, yaw) < 1e-4
                                && (result.Operation == MapGrowthOperation.ChangeShape ? v.ShapeId != shape && Math.Abs(v.Dimensions.y - dimensions.y) < 1e-4
                                    : v.ShapeId == shape && Math.Abs(v.Dimensions.y - dimensions.y) > 1e-4)).ToArray();
                            if (variants.Length == 0) { result.Reason = "Нет другой формы/высоты в палитре."; return result; }
                            var variant = variants[random.Next(variants.Length)]; dimensions = variant.Dimensions; top = variant.TopSize; shape = variant.ShapeId;
                            var cap = snapshot.Capabilities.FirstOrDefault(c => c.ShapeId == shape);
                            var defaults = variant.CopySections();
                            for (int i = 0; i < sections.Length; i++)
                            {
                                if (cap == null || !cap.Materials.Contains(sections[i].material)) sections[i].material = defaults[i].material;
                                // Изменение высоты/формы не переносит непомещающийся проём; новый проём выбирается отдельной операцией.
                                sections[i].openings = defaults[i].openings;
                            }
                            break;
                        case MapGrowthOperation.ChangeMaterial:
                            var materialCap = snapshot.Capabilities.FirstOrDefault(c => c.ShapeId == shape);
                            if (materialCap == null || materialCap.Materials.Count < 2) { result.Reason = "У формы нет второго разрешённого материала."; return result; }
                            int section = random.Next(ActiveSections(dimensions.y));
                            var allowed = materialCap.Materials.Where(m => m != sections[section].material).ToArray();
                            sections[section].material = allowed[random.Next(allowed.Length)]; break;
                        case MapGrowthOperation.ChangeOpenings:
                            if (!snapshot.Capabilities.Any(c => c.ShapeId == shape && c.Openings) || dimensions.x <= .6001f)
                            { result.Reason = "Эта форма/размер не поддерживает настоящую щель."; return result; }
                            int slot = random.Next(ActiveSections(dimensions.y));
                            float height = Math.Min(dimensions.y, BlockoutSectionSettings.Top(slot)) - BlockoutSectionSettings.Bottom(slot);
                            var opening = BlockoutOpeningSettings.Default; opening.enabled = !sections[slot].openings.enabled;
                            opening.sillHeight = height * .2f; opening.lintelHeight = height * .2f;
                            if (random.Next(2) == 0) { opening.spacing = dimensions.x; opening.width = Math.Min(.6f, dimensions.x - .3f); }
                            sections[slot].openings = opening; break;
                    }
                    changed = new MapGrowthBlockRecipe(old.RecipeId, shape, point, yaw, dimensions, top, sections); recipes[index] = changed;
                }
            }
            result.Candidate.SetRecipes(recipes); result.Changed = true; result.ChangedRecipe = changed;
            result.RecipeId = changed?.RecipeId ?? result.RecipeId; result.Reason = result.Operation + ": " + result.RecipeId;
            return result;
        }
        private static int ActiveSections(float height) => height > 1.6001f ? 3 : height > 1.2001f ? 2 : 1;
        private static float Aligned(float value, float origin, float step) => origin + (float)Math.Round((value - origin) / step) * step;
        private static float Normalize(float yaw) => (yaw % 360 + 360) % 360;
        private static float Angle(float a, float b) { float d = Normalize(a - b); return Math.Min(d, 360 - d); }
    }
}

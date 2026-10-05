using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Числовой рецепт; разрешение shapeId в ассет выполняется только главным потоком.</summary>
    public sealed class MapGrowthBlockRecipe
    {
        public string RecipeId { get; }
        public string ShapeId { get; }
        public Vector3 BottomCenter { get; }
        public float Yaw { get; }
        public Vector3 Dimensions { get; }
        public Vector2 TopSize { get; }
        private readonly BlockoutSectionSettings[] sections;

        public MapGrowthBlockRecipe(string recipeId, string shapeId, Vector3 bottomCenter, float yaw,
            Vector3 dimensions, Vector2 topSize, BlockoutSectionSettings[] sections)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || string.IsNullOrWhiteSpace(shapeId)
                || !PositionImpactValidation.Finite(bottomCenter) || !PositionImpactValidation.Finite(yaw)
                || !PositionImpactValidation.Finite(dimensions) || dimensions.x <= 0 || dimensions.y <= 0 || dimensions.z <= 0
                || !PositionImpactValidation.Finite(topSize) || topSize.x < 0 || topSize.y < 0
                || (topSize.x == 0) != (topSize.y == 0)
                || sections == null || sections.Length != 3 || sections.Any(s => s == null))
                throw new ArgumentException("Нужен конечный рецепт формы с тремя секциями.");
            RecipeId = recipeId; ShapeId = shapeId; BottomCenter = bottomCenter;
            Yaw = yaw; Dimensions = dimensions; TopSize = topSize;
            this.sections = BlockoutSectionSettings.Copy(sections);
        }
        public BlockoutSectionSettings[] CopySections() => BlockoutSectionSettings.Copy(sections);
    }

    /// <summary>Одна ветка владеет занятостью и списком рецептов. Не содержит объектов Unity или физики.</summary>
    public sealed class MapGrowthCandidate
    {
        public string CandidateId { get; }
        public int BranchId { get; }
        public int Seed { get; }
        public string InputVersion => snapshot.InputVersion;
        private readonly MapGrowthSnapshot snapshot;
        private readonly int[] touchedCounts, bodyCounts;
        private readonly HashSet<string>[] owners;
        private MapGrowthBlockRecipe[] recipes = Array.Empty<MapGrowthBlockRecipe>();
        public bool OccupancyComplete { get; private set; } = true;
        public ReadOnlyCollection<MapGrowthBlockRecipe> Recipes => Array.AsReadOnly(recipes);
        internal bool BelongsTo(MapGrowthSnapshot owner) => ReferenceEquals(snapshot, owner);

        public MapGrowthCandidate(string candidateId, int branchId, int seed, MapGrowthSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(candidateId) || branchId < 0 || snapshot == null) throw new ArgumentException("Неверная ветка поиска.");
            CandidateId = candidateId; BranchId = branchId; Seed = seed; this.snapshot = snapshot;
            touchedCounts = new int[snapshot.Count]; bodyCounts = new int[snapshot.Count]; owners = new HashSet<string>[snapshot.Count];
        }
        public void SetRecipes(IEnumerable<MapGrowthBlockRecipe> value)
        {
            var copied = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
            var ids = new HashSet<string>(snapshot.FixedRecipes.Select(r => r.RecipeId), StringComparer.Ordinal);
            if (copied.Any(r => r == null || !ids.Add(r.RecipeId)))
                throw new ArgumentException("Кандидат не дублирует фиксированный блок или ID другого рецепта.");
            recipes = copied; ClearOccupancy(); OccupancyComplete = true;
            foreach (var recipe in recipes)
            {
                Vector2Int[] cells = null, body = null;
                foreach (var sample in snapshot.Footprints)
                    if (sample.TryPlacement(snapshot, recipe, out var touched, out var blocked)) { cells = touched; body = blocked; break; }
                if (cells == null) { OccupancyComplete = false; continue; }
                var blocking = new HashSet<Vector2Int>(body);
                foreach (var cell in cells)
                {
                    if (cell.x < 0 || cell.y < 0 || cell.x >= snapshot.Width || cell.y >= snapshot.Depth)
                    { OccupancyComplete = false; continue; }
                    Occupy(cell.y * snapshot.Width + cell.x, recipe.RecipeId, blocking.Contains(cell));
                }
            }
        }
        private void Occupy(int cell, string owner, bool blocksBody)
        {
            if (cell < 0 || cell >= snapshot.Count || string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Неверный след кандидата.");
            if (owners[cell] == null) owners[cell] = new HashSet<string>(StringComparer.Ordinal);
            if (!owners[cell].Add(owner)) throw new ArgumentException("След одного владельца учитывается один раз; пересоберите занятость после изменения рецепта.");
            touchedCounts[cell]++; if (blocksBody) bodyCounts[cell]++;
        }
        private void ClearOccupancy()
        { Array.Clear(touchedCounts, 0, touchedCounts.Length); Array.Clear(bodyCounts, 0, bodyCounts.Length); Array.Clear(owners, 0, owners.Length); }
        public int[] CopyTouchedCounts() => (int[])touchedCounts.Clone();
        public string[] OwnersAt(int cell) => owners[cell]?.OrderBy(id => id, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        public MapGrid CreateMovementGrid()
        {
            if (!OccupancyComplete) throw new InvalidOperationException("Занятость кандидата неполна: нужен захват геометрии отсутствующих следов.");
            var grid = snapshot.CreateMovementGrid();
            for (int i = 0; i < bodyCounts.Length; i++) if (bodyCounts[i] > 0) grid.Blocked[i] = true;
            return grid;
        }
        public MapGrowthCandidate Copy(string candidateId)
        {
            var result = new MapGrowthCandidate(candidateId, BranchId, Seed, snapshot);
            result.recipes = (MapGrowthBlockRecipe[])recipes.Clone();
            result.OccupancyComplete = OccupancyComplete;
            Array.Copy(touchedCounts, result.touchedCounts, touchedCounts.Length); Array.Copy(bodyCounts, result.bodyCounts, bodyCounts.Length);
            for (int i = 0; i < owners.Length; i++) if (owners[i] != null) result.owners[i] = new HashSet<string>(owners[i], StringComparer.Ordinal);
            return result;
        }
    }
}

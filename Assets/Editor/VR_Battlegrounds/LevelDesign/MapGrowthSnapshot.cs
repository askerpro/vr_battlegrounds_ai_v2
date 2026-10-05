using System;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public sealed class MapGrowthConstraintVolume
    {
        public string OwnerId { get; }
        public Bounds Bounds { get; }
        public bool PhysicalReserve { get; }
        public MapGrowthConstraintVolume(string ownerId, Bounds bounds, bool physicalReserve)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !PositionImpactValidation.Finite(bounds.center) || !PositionImpactValidation.Finite(bounds.size)
                || bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0) throw new ArgumentException("Нужен точный объём ограничения с ID.");
            OwnerId = ownerId; Bounds = bounds; PhysicalReserve = physicalReserve;
        }
    }
    /// <summary>Неизменяемый числовой вход. Геометрический захват и версия разрешаются главным потоком до конструктора.</summary>
    public sealed class MapGrowthSnapshot
    {
        public string InputVersion { get; }
        public int Width { get; }
        public int Depth { get; }
        public int Count => Width * Depth;
        public float Cell { get; }
        public Vector2 Origin { get; }
        public MapEvaluationProfile Profile { get; }
        public float AuthorCell { get; }
        private readonly MapGrowthPositionAnchor[] anchors;
        private readonly MapGrowthShapeCapabilities[] capabilities;
        public ReadOnlyCollection<MapGrowthPositionAnchor> Anchors => Array.AsReadOnly(anchors);
        public ReadOnlyCollection<MapGrowthShapeCapabilities> Capabilities => Array.AsReadOnly(capabilities);
        private readonly bool[] blocked, forbidden, reserved, fixedTouched;
        private readonly float[] heights;
        private readonly int[] owners;
        private readonly int[][] touchedOwners;
        private readonly byte[] zones;
        private readonly string[] obstacleNames;
        private readonly CoverClass[] obstacleCovers;
        private readonly bool[] mixedCovers;
        private readonly PositionImpactLayout layout;
        private readonly MapGrowthContactPair[] contacts;
        private readonly MapGrowthSearchParameters search;
        private readonly MapGrowthInputValidation validation;
        private readonly MapGrowthBlockRecipe[] fixedRecipes, variants;
        private readonly MapGrowthFootprintTemplate[] footprints;
        private readonly MapGrowthConstraintVolume[] constraints;
        public ReadOnlyCollection<MapGrowthConstraintVolume> Constraints => Array.AsReadOnly(constraints);
        public ReadOnlyCollection<MapGrowthFootprintTemplate> Footprints => Array.AsReadOnly(footprints);
        public ReadOnlyCollection<MapGrowthBlockRecipe> FixedRecipes => Array.AsReadOnly(fixedRecipes);
        public ReadOnlyCollection<MapGrowthBlockRecipe> Variants => Array.AsReadOnly(variants);

        public MapGrowthSnapshot(string version, MapGrowthEvaluationInput input, MapGrowthSearchParameters search,
            MapEvaluationProfile profile, bool[] forbidden, bool[] reserved, MapGrowthBlockRecipe[] fixedRecipes, MapGrowthBlockRecipe[] variants,
            MapGrowthFootprintTemplate[] footprints = null, MapGrowthConstraintVolume[] constraints = null,
            float authorCell = .3f, MapGrowthPositionAnchor[] anchors = null, MapGrowthShapeCapabilities[] capabilities = null)
        {
            if (string.IsNullOrWhiteSpace(version) || input == null || !input.CanEvaluate || input.grid == null || search == null
                || forbidden == null || reserved == null || forbidden.Length != input.grid.Count || reserved.Length != input.grid.Count
                || fixedRecipes == null || variants == null || fixedRecipes.Any(r => r == null) || variants.Any(r => r == null))
                throw new ArgumentException("Нужен валидный числовой вход и маски его сетки.");
            var problems = PositionImpactValidation.Validate(input.grid, input.layout);
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            InputVersion = version; Profile = profile;
            var grid = input.grid; Width = grid.Width; Depth = grid.Depth; Cell = grid.Cell; Origin = grid.Origin;
            blocked = (bool[])grid.Blocked.Clone(); heights = (float[])grid.Height.Clone(); owners = (int[])grid.Owner.Clone(); zones = (byte[])grid.Zone.Clone();
            fixedTouched = grid.Footprint != null ? (bool[])grid.Footprint.Touched.Clone() : grid.Owner.Select((owner, i) => owner >= 0 || grid.Blocked[i]).ToArray();
            touchedOwners = Enumerable.Range(0, Count).Select(i => grid.Footprint != null
                ? (int[])(grid.Footprint.Owners[i] ?? Array.Empty<int>()).Clone()
                : grid.Owner[i] >= 0 ? new[] { grid.Owner[i] } : Array.Empty<int>()).ToArray();
            obstacleNames = grid.Obstacles.ToArray(); obstacleCovers = grid.ObstacleCover.ToArray(); mixedCovers = grid.ObstacleMixedCover.ToArray();
            this.forbidden = (bool[])forbidden.Clone(); this.reserved = (bool[])reserved.Clone();
            layout = CloneLayout(input.layout); contacts = CloneContacts(input.contacts); this.search = CloneSearch(search);
            if (!PositionImpactValidation.Finite(authorCell) || authorCell <= 0) throw new ArgumentException("Нужен конечный шаг авторского размещения.");
            AuthorCell = authorCell;
            this.anchors = anchors == null ? Array.Empty<MapGrowthPositionAnchor>() : (MapGrowthPositionAnchor[])anchors.Clone();
            this.capabilities = capabilities == null ? Array.Empty<MapGrowthShapeCapabilities>() : (MapGrowthShapeCapabilities[])capabilities.Clone();
            if (this.anchors.Any(a => a == null || !layout.positions.Any(p => p.id == a.PositionId)) || this.anchors.GroupBy(a => a.PositionId).Any(g => g.Count() > 1)
                || this.capabilities.Any(c => c == null) || this.capabilities.GroupBy(c => c.ShapeId).Any(g => g.Count() > 1)) throw new ArgumentException("Неверные направления позиций или возможности палитры.");
            validation = CloneValidation(input.validation);
            this.fixedRecipes = (MapGrowthBlockRecipe[])fixedRecipes.Clone(); this.variants = (MapGrowthBlockRecipe[])variants.Clone();
            this.footprints = footprints == null ? Array.Empty<MapGrowthFootprintTemplate>() : (MapGrowthFootprintTemplate[])footprints.Clone();
            if (this.footprints.Any(f => f == null)) throw new ArgumentException("Пустая запись следа.");
            this.constraints = constraints == null ? Array.Empty<MapGrowthConstraintVolume>() : (MapGrowthConstraintVolume[])constraints.Clone();
            if (this.constraints.Any(c => c == null)) throw new ArgumentException("Пустой объём ограничения.");
        }
        public bool IsForbidden(int cell) => forbidden[cell];
        public bool IsReserved(int cell) => reserved[cell];
        public bool HasFixedTouch(int cell) => fixedTouched[cell];
        public string[] FixedOwnersAt(int cell) => touchedOwners[cell].Select(i => i >= 0 && i < obstacleNames.Length
            ? obstacleNames[i] : "Неизвестный фиксированный владелец #" + i).ToArray();
        public PositionImpactLayout CopyLayout() => CloneLayout(layout);
        public MapGrowthContactPair[] CopyContacts() => CloneContacts(contacts);
        public MapGrowthSearchParameters CopySearch() => CloneSearch(search);
        public MapGrowthInputValidation CopyValidation() => CloneValidation(validation);
        /// <summary>Только движение/маршруты. Контакты обязаны пройти общий физический оценщик.</summary>
        public MapGrid CreateMovementGrid()
        {
            var grid = new MapGrid(Width, Depth, Cell, Origin);
            Array.Copy(blocked, grid.Blocked, Count); Array.Copy(heights, grid.Height, Count);
            Array.Copy(owners, grid.Owner, Count); Array.Copy(zones, grid.Zone, Count);
            grid.Obstacles.AddRange(obstacleNames); grid.ObstacleCover.AddRange(obstacleCovers); grid.ObstacleMixedCover.AddRange(mixedCovers);
            grid.LineOfSight = Unavailable; grid.ShotLine = Unavailable;
            return grid;
        }
        private static bool Unavailable(Vector3 from, Vector3 to)
            => throw new InvalidOperationException("Числовой снимок движения не содержит физическую геометрию для видимости/прострела.");
        private static MapGrowthSearchParameters CloneSearch(MapGrowthSearchParameters s) => new MapGrowthSearchParameters {
            seed = s.seed, targetCandidates = s.targetCandidates, branchCount = s.branchCount, attemptsPerBranch = s.attemptsPerBranch,
            operations = s.operations, maximumGeneratedBlocks = s.maximumGeneratedBlocks,
            density = s.density?.Copy(), closureStanding = s.closureStanding?.Copy(), closureCrouching = s.closureCrouching?.Copy() };
        private static MapGrowthInputValidation CloneValidation(MapGrowthInputValidation source) => new MapGrowthInputValidation {
            errors = source.errors.Select(CloneIssue).ToList(), warnings = source.warnings.Select(CloneIssue).ToList(),
            uncheckedRequirements = source.uncheckedRequirements.Select(CloneIssue).ToList() };
        private static MapGrowthInputIssue CloneIssue(MapGrowthInputIssue source) => new MapGrowthInputIssue {
            code = source.code, ownerId = source.ownerId, field = source.field, message = source.message };
        private static PositionImpactLayout CloneLayout(PositionImpactLayout source) => new PositionImpactLayout {
            version = source.version, map = source.map, sceneGuid = source.sceneGuid, bodyProfileId = source.bodyProfileId,
            bodyProfileVersion = source.bodyProfileVersion, bodyProfileCalibrated = source.bodyProfileCalibrated,
            profile = source.profile, radius = source.radius, speed = source.speed,
            positions = source.positions.Select(p => new ImpactPosition { id = p.id, min = p.min, max = p.max, protectedState = p.protectedState,
                states = p.states.Select(s => new ImpactState { id = s.id, center = s.center, yaw = s.yaw,
                    eyeOffset = s.eyeOffset, muzzleOffset = s.muzzleOffset, body = s.body.Select(b => new ImpactBodySample { offset = b.offset, weight = b.weight }).ToArray() }).ToArray() }).ToArray(),
            routes = source.routes.Select(r => new ImpactRouteSpec { id = r.id, from = r.from, to = r.to,
                fromState = r.fromState, toState = r.toState, requireDirect = r.requireDirect,
                via = r.via == null ? null : (Vector2[])r.via.Clone(), allowedCellIndices = r.allowedCellIndices == null ? null : (int[])r.allowedCellIndices.Clone() }).ToArray() };
        private static MapGrowthContactPair[] CloneContacts(MapGrowthContactPair[] source) => (source ?? Array.Empty<MapGrowthContactPair>()).Select(p => new MapGrowthContactPair {
            positionAId = p.positionAId, positionBId = p.positionBId, sourceContactIds = (string[])p.sourceContactIds.Clone(),
            cases = p.cases.Select(c => new MapGrowthContactCase { stateAId = c.stateAId, stateBId = c.stateBId, advantage = c.advantage,
                aToB = CloneDirection(c.aToB), bToA = CloneDirection(c.bToA),
                sources = c.sources.Select(s => new MapGrowthContactSource { contactId = s.contactId, caseId = s.caseId }).ToArray() }).ToArray() }).ToArray();
        private static MapGrowthContactDirection CloneDirection(MapGrowthContactDirection d) => new MapGrowthContactDirection {
            vision = d.vision, anyShotShare = d.anyShotShare.Copy(), visibleShotShare = d.visibleShotShare.Copy(), sourceExposure = d.sourceExposure.Copy() };
    }
}

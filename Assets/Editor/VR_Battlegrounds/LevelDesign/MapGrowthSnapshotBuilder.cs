using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Главный поток захватывает штатные коллайдеры; результат содержит только числовой след.</summary>
    [InitializeOnLoad]
    public static class MapGrowthSnapshotBuilder
    {
        private static readonly int MainThread = Thread.CurrentThread.ManagedThreadId;
        public const int MaximumTemplateCells = 1000000;

        internal static void RequireMainThread()
        { if (Thread.CurrentThread.ManagedThreadId != MainThread) throw new InvalidOperationException("Захват геометрии разрешён только главному потоку Editor."); }

        /// <summary>Синхронная оболочка для харнесса. UI вызывает BeginCapture/StepCapture порциями.</summary>
        public static MapGrowthSceneCapture Capture(Scene scene, BlockoutMarkup markup, MapGrowthSettings settings, IEnumerable<GameObject> replacedRoots = null)
        {
            var capture = BeginCapture(scene, markup, settings, replacedRoots);
            try { while (!capture.StepCapture()) { } return capture; }
            catch { capture.Dispose(); throw; }
        }
        public static MapGrowthSceneCapture BeginCapture(Scene scene, BlockoutMarkup markup, MapGrowthSettings settings, IEnumerable<GameObject> replacedRoots = null)
        {
            RequireMainThread();
            if (!scene.IsValid() || !scene.isLoaded || markup == null || settings == null)
                throw new ArgumentException("Нужны загруженная сцена, разметка и настройки выращивания.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Захват карты доступен вне Play Mode.");
            string guid = string.IsNullOrEmpty(scene.path) ? "" : AssetDatabase.AssetPathToGUID(scene.path);
            if (!string.Equals(guid, markup.sceneGuid ?? "", StringComparison.Ordinal))
                throw new ArgumentException("Разметка принадлежит другой сцене: " + markup.sceneGuid);
            var validation = MapGrowthValidation.ValidateInput(markup, markup.bodyProfile != null ? markup.bodyProfile.data : null,
                settings.search, settings.fixedBlockGlobalObjectIds);
            if (!validation.CanStart) throw new ArgumentException(string.Join("; ", validation.errors.Select(e => e.ownerId + ": " + e.message)));
            var pinnedRoots = new HashSet<GameObject>();
            foreach (string id in settings.fixedBlockGlobalObjectIds)
            {
                if (!GlobalObjectId.TryParse(id, out var global)) throw new ArgumentException("Не распознан ID фиксированного блока: " + id);
                var resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(global);
                var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
                if (go == null || go.scene != scene || go.GetComponent<BlockoutBlockInstance>() == null)
                    throw new ArgumentException("Фиксированный блок не найден в исходной сцене: " + id);
                pinnedRoots.Add(go);
            }
            var replacement = MapGrowthGeneratedOwnership.Resolve(scene, replacedRoots);
            if (replacement.Any(pinnedRoots.Contains))
                throw new ArgumentException("Выбранный для замены блок явно закреплён в настройках поиска.");
            string version = MapGrowthSourceVersion.Compute(scene, markup, settings, replacement);
            var built = MapGridBuilder.Build(scene);
            if (built.Grid == null || built.Problems.Count > 0) throw new ArgumentException(string.Join("; ", built.Problems));
            if (replacement.Length > 0) built = BuildWithoutReplaced(scene, built.Grid, replacement);
            var input = MapGrowthMarkupAdapter.Build(markup, built.Grid);
            if (!input.CanEvaluate) throw new ArgumentException(string.Join("; ", input.validation.errors.Select(e => e.ownerId + ": " + e.message)));
            input.layout.map = scene.name; input.layout.profile = settings.profile;
            var sourceColliders = MapGrowthFrozenGeometry.SourceColliders(scene);
            int ground = LayerMask.NameToLayer("Ground");
            float floorY = sourceColliders.Where(c => c.gameObject.layer == ground).Max(c => c.bounds.max.y);
            var forbidden = new bool[built.Grid.Count]; var reserved = new bool[built.Grid.Count];
            var constraints = new List<MapGrowthConstraintVolume>();
            foreach (var authored in markup.cells.Where(c => c.constraintIds.Count > 0))
            {
                Vector2 min = markup.origin + new Vector2(authored.x, authored.z) * markup.step;
                Mark(built.Grid, min, min + Vector2.one * markup.step, forbidden);
                constraints.Add(new MapGrowthConstraintVolume("constraint:" + authored.x + ":" + authored.z + ":" + string.Join(",", authored.constraintIds.OrderBy(id => id, StringComparer.Ordinal)),
                    new Bounds(new Vector3(min.x + markup.step * .5f, floorY + MapGridBuilder.ObstacleCeiling * .5f, min.y + markup.step * .5f),
                        new Vector3(markup.step, MapGridBuilder.ObstacleCeiling, markup.step)), false));
            }
            var arenas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).ToArray();
            if (arenas.Length > 1) throw new ArgumentException("Нужен один однозначный паспорт физической арены.");
            if (arenas.Length == 1)
            {
                var arena = arenas[0];
                if (arena.arenaId != markup.arenaId || !arena.Valid(out string reason))
                    throw new ArgumentException("Разметка/паспорт арены не совпадают или паспорт невалиден: " + arena.arenaId);
                foreach (var marker in arena.GetComponentsInChildren<PhysicalObstacleMarker>(true))
                {
                    if (!marker.TryBounds(arena, out var bounds, out reason)) throw new ArgumentException(marker.markerId + ": " + reason);
                    Mark(built.Grid, new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.max.x, bounds.max.z), reserved);
                    constraints.Add(new MapGrowthConstraintVolume(marker.markerId, bounds, true));
                }
            }
            var fixedRecipes = sourceColliders.Where(c => !MapGrowthGeneratedOwnership.Excludes(c, replacement)).Select(c => c.GetComponentInParent<BlockoutBlockInstance>()).Where(b => b != null)
                .Distinct().Select(b => FixedRecipe(b)).OrderBy(r => r.RecipeId, StringComparer.Ordinal).ToArray();
            var variants = Variants(markup, floorY);
            var frozen = MapGrowthFrozenGeometry.Capture(scene, replacement);
            try { return new MapGrowthSceneCapture(scene, markup, settings, version, floorY, frozen, input, forbidden, reserved, fixedRecipes, variants, constraints.ToArray(), replacement); }
            catch { frozen.Dispose(); throw; }
        }
        private static MapGridBuilder.Result BuildWithoutReplaced(Scene source, MapGrid original, GameObject[] replacement)
        {
            using (var geometry = MapGrowthFrozenGeometry.Capture(source, replacement))
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                try
                {
                    geometry.Instantiate(scene);
                    var built = MapGridBuilder.Build(scene, original.Cell, original.Zone);
                    if (built.Grid == null || built.Problems.Count > 0) throw new ArgumentException(string.Join("; ", built.Problems));
                    // Разметка/снимок используют только числовые поля; Physics этой временной сцены больше не доступна.
                    built.Grid.LineOfSight = built.Grid.ShotLine = (a, b) => throw new InvalidOperationException("Для измерений нужен замороженный preview захвата.");
                    built.Grid.BeginLineBatch = null;
                    return built;
                }
                finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
        private static void Mark(MapGrid grid, Vector2 min, Vector2 max, bool[] mask)
        {
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 low = grid.Center(i) - Vector2.one * grid.Cell * .5f, high = low + Vector2.one * grid.Cell;
                if (high.x > min.x + .00001f && low.x < max.x - .00001f && high.y > min.y + .00001f && low.y < max.y - .00001f) mask[i] = true;
            }
        }
        private static MapGrowthBlockRecipe FixedRecipe(BlockoutBlockInstance block)
        {
            if (block.definition == null || string.IsNullOrWhiteSpace(block.definition.shapeId))
                throw new ArgumentException("Блок без формы реестра: " + block.name);
            var colliders = block.GetComponentsInChildren<Collider>(false).Where(BlockoutSupportSurfaces.IsActiveSolid).ToArray();
            var bounds = colliders[0].bounds; foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
            var step = block.GetComponent<BlockoutSteppedGeometry>();
            return new MapGrowthBlockRecipe(GlobalObjectId.GetGlobalObjectIdSlow(block.gameObject).ToString(), block.definition.shapeId,
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), block.transform.eulerAngles.y, block.dimensions,
                step != null ? step.topSize : Vector2.zero, BlockoutSectionFactory.Recipe(block.gameObject, block.dimensions.y, block.material, block.openings));
        }
        private static MapGrowthBlockRecipe[] Variants(BlockoutMarkup markup, float floorY)
        {
            var registry = BlockoutRegistryFactory.Current;
            var definitions = registry.Definitions.Where(d => d != null && d.gameplayGeometry && d.geometryPrefab != null)
                .OrderBy(d => d.shapeId, StringComparer.Ordinal).ToArray();
            if (definitions.Length == 0 || definitions.GroupBy(d => d.shapeId).Any(g => g.Count() != 1))
                throw new ArgumentException("Нужны непустые уникальные игровые формы реестра.");
            var variants = new List<MapGrowthBlockRecipe>();
            var center = new Vector3(markup.origin.x + markup.step * .5f, floorY, markup.origin.y + markup.step * .5f);
            foreach (var definition in definitions)
            {
                var defaults = BlockoutRegistryFactory.DefaultDimensions(definition);
                var step = definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>();
                var heights = definition.heightEditable ? new[] { 1.2f, 1.6f, 2.5f } : new[] { defaults.y };
                foreach (float height in heights) for (int yaw = 0; yaw < 360; yaw += 15)
                {
                    var dimensions = defaults; dimensions.y = height;
                    var sections = BlockoutSectionSettings.FromLegacy(height, definition.defaultMaterial, BlockoutOpeningSettings.Default);
                    if (!BlockoutSectionFactory.Validate(definition, dimensions, sections, out string reason))
                        throw new ArgumentException(definition.shapeId + ": " + reason);
                    variants.Add(new MapGrowthBlockRecipe(definition.shapeId + "/" + variants.Count, definition.shapeId, center, yaw,
                        dimensions, step != null ? step.topSize : Vector2.zero, sections));
                }
            }
            return variants.ToArray();
        }

        public static MapGrowthFootprintTemplate CaptureFootprint(string version, BlockoutBlockDefinition definition,
            MapGrowthBlockRecipe prototype, Vector2 sourceOrigin, float cell, float floorY = 0, float authorCell = .3f)
        {
            RequireMainThread();
            if (!PositionImpactValidation.Finite(sourceOrigin) || !PositionImpactValidation.Finite(cell) || cell <= 0
                || !PositionImpactValidation.Finite(floorY)) throw new ArgumentException("Нужны конечные параметры сетки/пола.");
            var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
            try
            {
                root = BlockoutRegistryFactory.CreatePreview(definition, scene, prototype, authorCell);
                Physics.SyncTransforms();
                var colliders = root.GetComponentsInChildren<Collider>(true)
                    .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.attachedRigidbody == null).ToArray();
                if (colliders.Length == 0) throw new InvalidOperationException("Штатная форма не содержит статических игровых коллайдеров.");
                Bounds bounds = colliders[0].bounds; foreach (var collider in colliders.Skip(1)) bounds.Encapsulate(collider.bounds);
                double left = Math.Floor(((double)bounds.min.x - sourceOrigin.x) / cell) - 1;
                double bottom = Math.Floor(((double)bounds.min.z - sourceOrigin.y) / cell) - 1;
                double right = Math.Ceiling(((double)bounds.max.x - sourceOrigin.x) / cell) + 1;
                double top = Math.Ceiling(((double)bounds.max.z - sourceOrigin.y) / cell) + 1;
                if (left < int.MinValue || bottom < int.MinValue || right > int.MaxValue || top > int.MaxValue
                    || right <= left || top <= bottom || (right - left) * (top - bottom) > MaximumTemplateCells)
                    throw new ArgumentException("След формы выходит за числовой бюджет сетки; уменьшите габариты или выберите больший шаг.");
                int x = (int)left, z = (int)bottom, width = (int)(right - left), depth = (int)(top - bottom);
                var grid = new MapGrid(width, depth, cell, sourceOrigin + new Vector2(x, z) * cell);
                var obstacles = colliders.ToDictionary(c => c, _ => 0);
                var vaultable = new HashSet<Collider>(colliders.Where(c => c.GetComponentInParent<VaultableObstacle>() != null));
                var footprint = MapCellFootprint.Capture(scene, grid, floorY, obstacles, vaultable);
                var touched = new List<Vector2Int>(); var body = new List<Vector2Int>();
                for (int i = 0; i < grid.Count; i++)
                {
                    var coordinate = new Vector2Int(x + grid.X(i), z + grid.Z(i));
                    if (footprint.Touched[i]) touched.Add(coordinate);
                    if (footprint.BodyBlocked[i]) body.Add(coordinate);
                }
                if (touched.Count == 0) throw new InvalidOperationException("Геометрия отсутствует в полосе игрового следа относительно пола.");
                return new MapGrowthFootprintTemplate(version, prototype, sourceOrigin, cell, touched.ToArray(), body.ToArray());
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}

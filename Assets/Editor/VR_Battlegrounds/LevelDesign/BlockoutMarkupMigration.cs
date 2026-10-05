using System;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Явная копия разметки v1/v2; неизвестные версии и отсутствующие позы не исправляются молча.</summary>
    public static class BlockoutMarkupMigration
    {
        public static BlockoutMarkup CreateVersion2Copy(BlockoutMarkup source)
        {
            var target = ScriptableObject.CreateInstance<BlockoutMarkup>();
            try { CopyToVersion2(source, target); return target; }
            catch { UnityEngine.Object.DestroyImmediate(target); throw; }
        }

        // Числовой маршрут также доступен автономному probe без создания Unity native object.
        public static void CopyToVersion2(BlockoutMarkup source, BlockoutMarkup target)
        {
            if (ReferenceEquals(source, null) || ReferenceEquals(target, null) || ReferenceEquals(source, target))
                throw new ArgumentException("Нужны разные исходная разметка и целевая копия.");
            if (source.schemaVersion != 1 && source.schemaVersion != BlockoutMarkup.CurrentSchemaVersion)
                throw new ArgumentException("Неизвестная версия разметки.");
            target.schemaVersion = BlockoutMarkup.CurrentSchemaVersion; target.needsReevaluation = true;
            target.sceneGuid = source.sceneGuid; target.scenePath = source.scenePath; target.arenaId = source.arenaId;
            target.origin = source.origin; target.step = source.step; target.module = source.module; target.bodyProfile = source.bodyProfile;
            target.cells = source.cells?.Select(c => c == null ? null : new BlockoutMarkup.Cell {
                x = c.x, z = c.z, positionIds = c.positionIds?.ToList(), routeIds = c.routeIds?.ToList(),
                coverIds = c.coverIds?.ToList(), constraintIds = c.constraintIds?.ToList() }).ToList();
            target.positions = source.positions?.Select(p => p == null ? null : new BlockoutMarkup.Position {
                id = p.id, displayName = p.displayName, centerCell = p.centerCell, sizeCells = p.sizeCells,
                mainThreatYaw = p.mainThreatYaw, confirmed = p.confirmed, protectedStateId = p.protectedStateId,
                supportingCoverIds = p.supportingCoverIds?.ToList(),
                states = p.states?.Select(s => s == null ? null : new BlockoutPositionState {
                    id = s.id, centerOffset = s.centerOffset, yaw = s.yaw, stance = s.stance }).ToArray() ?? Array.Empty<BlockoutPositionState>() }).ToList();
            target.routes = source.routes?.Select(r => r == null ? null : new BlockoutMarkup.Route {
                id = r.id, physicallyBidirectional = r.physicallyBidirectional,
                scenarioFromPositionId = r.scenarioFromPositionId, scenarioToPositionId = r.scenarioToPositionId,
                fromPositionId = r.fromPositionId, toPositionId = r.toPositionId,
                fromStateId = r.fromStateId, toStateId = r.toStateId, requireDirect = r.requireDirect,
                widthCells = r.widthCells, viaCells = r.viaCells?.ToList() ?? new System.Collections.Generic.List<UnityEngine.Vector2Int>() }).ToList();
            target.covers = source.covers?.Select(c => c == null ? null : new BlockoutMarkup.Cover {
                id = c.id, footprintSnapshot = c.footprintSnapshot, blockGlobalObjectIds = c.blockGlobalObjectIds?.ToList() }).ToList();
            target.contacts = source.contacts?.Select(BlockoutContactSpec.CanonicalCopy).ToList() ?? new System.Collections.Generic.List<BlockoutContactSpec>();
        }
    }
}

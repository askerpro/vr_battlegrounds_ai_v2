using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ручная гипотеза карты: независимые принадлежности клеток, без телеметрии и генератора.</summary>
    public sealed class BlockoutMarkup : ScriptableObject
    {
        [Serializable] public sealed class Cell
        {
            public int x, z;
            public List<string> positionIds = new List<string>();
            public List<string> routeIds = new List<string>();
            public List<string> coverIds = new List<string>();
            public List<string> constraintIds = new List<string>();
            public bool Conflicting => positionIds.Count > 0 && coverIds.Count > 0;
            public bool Empty => positionIds.Count + routeIds.Count + coverIds.Count + constraintIds.Count == 0;
        }
        [Serializable] public sealed class Route
        {
            public string id;
            public bool physicallyBidirectional = true;
            public string scenarioFromPositionId, scenarioToPositionId;
            public string fromPositionId, toPositionId, fromStateId, toStateId;
            public bool requireDirect;
            public int widthCells = 5;
            public List<Vector2Int> viaCells = new List<Vector2Int>();
        }
        [Serializable] public sealed class Position
        {
            public string id;
            public string displayName;
            public Vector2Int centerCell;
            public int sizeCells = 5;
            public float mainThreatYaw;
            public bool confirmed;
            public BlockoutPositionState[] states = Array.Empty<BlockoutPositionState>();
            public string protectedStateId;
            public List<string> supportingCoverIds = new List<string>();
        }
        [Serializable] public sealed class Cover
        {
            public string id;
            public List<string> blockGlobalObjectIds = new List<string>();
            public Bounds footprintSnapshot;
        }
        public const int CurrentSchemaVersion = 2;
        // Сохранённый schemaVersion=1 остаётся прежним; новые артефакты используют текущую схему.
        public int schemaVersion = CurrentSchemaVersion;
        public bool needsReevaluation = true;
        public string sceneGuid, scenePath, arenaId = "Environment";
        public Vector2 origin;
        public float step, module;
        public List<Cell> cells = new List<Cell>();
        public List<Route> routes = new List<Route>();
        public List<Position> positions = new List<Position>();
        public List<Cover> covers = new List<Cover>();
        public MapBodyProfile bodyProfile;
        public List<BlockoutContactSpec> contacts = new List<BlockoutContactSpec>();
        public enum Layer { Position, Route, HighCover, Constraint }

        public void Paint(Vector2Int coordinate, Layer layer, string id, bool erase)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            Cell cell = cells.FirstOrDefault(c => c.x == coordinate.x && c.z == coordinate.y);
            if (cell == null)
            {
                if (erase) return;
                cell = new Cell { x = coordinate.x, z = coordinate.y };
                cells.Add(cell);
            }
            var ids = layer == Layer.Position ? cell.positionIds : layer == Layer.Route ? cell.routeIds
                : layer == Layer.HighCover ? cell.coverIds : cell.constraintIds;
            if (erase) ids.Remove(id);
            else if (!ids.Contains(id)) ids.Add(id);
            if (cell.Empty) cells.Remove(cell);
            if (layer == Layer.Route && !erase && !routes.Any(r => r.id == id)) routes.Add(new Route { id = id });
        }
    }
}

using System;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Редактирование смысловой разметки; UI регистрирует Undo до вызова, геометрия сцены не меняется.</summary>
    public static class MapGrowthMarkupEditing
    {
        public static void DeletePosition(BlockoutMarkup markup, string id)
        {
            markup.positions.RemoveAll(p => p.id == id);
            markup.contacts.RemoveAll(c => c.positionAId == id || c.positionBId == id);
            foreach (var route in markup.routes.Where(r => r.fromPositionId == id || r.toPositionId == id
                || r.scenarioFromPositionId == id || r.scenarioToPositionId == id).ToArray()) DeleteRoute(markup, route.id);
            foreach (var cell in markup.cells) cell.positionIds.RemoveAll(value => value == id);
            RemoveEmptyCells(markup);
        }

        public static void DeleteState(BlockoutMarkup markup, string positionId, string stateId)
        {
            var position = markup.positions.Single(p => p.id == positionId);
            position.states = position.states.Where(s => s.id != stateId).ToArray();
            if (position.protectedStateId == stateId) position.protectedStateId = null;
            foreach (var contact in markup.contacts)
                contact.cases = contact.cases.Where(c => !(contact.positionAId == positionId && c.stateAId == stateId)
                    && !(contact.positionBId == positionId && c.stateBId == stateId)).ToArray();
            markup.contacts.RemoveAll(c => c.cases.Length == 0);
            foreach (var route in markup.routes.Where(r => r.fromPositionId == positionId && r.fromStateId == stateId
                || r.toPositionId == positionId && r.toStateId == stateId).ToArray()) DeleteRoute(markup, route.id);
            markup.needsReevaluation = true;
        }

        public static void DeleteRoute(BlockoutMarkup markup, string id)
        {
            markup.routes.RemoveAll(r => r.id == id);
            foreach (var cell in markup.cells) cell.routeIds.RemoveAll(value => value == id);
            RemoveEmptyCells(markup);
        }

        public static void MovePosition(BlockoutMarkup markup, BlockoutMarkup.Position position, Vector2Int center)
        {
            var area = markup.cells.Where(c => c.positionIds.Contains(position.id)).Select(c => new Vector2Int(c.x, c.z)).ToArray();
            Vector2Int offset = center - position.centerCell;
            foreach (var cell in markup.cells) cell.positionIds.RemoveAll(id => id == position.id);
            RemoveEmptyCells(markup); position.centerCell = center;
            if (area.Length == 0) PaintSquare(markup, position);
            else foreach (var cell in area) markup.Paint(cell + offset, BlockoutMarkup.Layer.Position, position.id, false);
            markup.needsReevaluation = true;
        }

        /// <summary>Явная перерисовка только выбранного коридора; чужие memberships сохраняются.</summary>
        public static void RepaintRoute(BlockoutMarkup markup, BlockoutMarkup.Route route)
        {
            if (route.widthCells < 1 || route.widthCells > 15 || route.viaCells == null || route.viaCells.Count > 16)
                throw new ArgumentException("Нужны ширина 1–15 клеток и не более 16 промежуточных точек.");
            Vector2Int Endpoint(string positionId, string stateId)
            {
                var p = markup.positions.Single(x => x.id == positionId); var state = p.states.Single(x => x.id == stateId);
                return p.centerCell + new Vector2Int(Mathf.RoundToInt(state.centerOffset.x / markup.step), Mathf.RoundToInt(state.centerOffset.y / markup.step));
            }
            var points = new[] { Endpoint(route.fromPositionId, route.fromStateId) }.Concat(route.viaCells)
                .Concat(new[] { Endpoint(route.toPositionId, route.toStateId) }).ToArray();
            // До изменения memberships ограничиваем объём интерактивной кисти и исключаем переполнение Stroke.
            long paintBudget = 0;
            for (int i = 1; i < points.Length; i++)
            {
                paintBudget += (Math.Abs((long)points[i].x - points[i - 1].x) + Math.Abs((long)points[i].y - points[i - 1].y) + 1) * route.widthCells * route.widthCells;
                if (paintBudget > 250000) throw new ArgumentException("Полоса слишком велика для одного жеста кисти (более 250000 записей). Разбейте её на отдельные проходы.");
            }
            foreach (var cell in markup.cells) cell.routeIds.RemoveAll(id => id == route.id);
            RemoveEmptyCells(markup); int half = route.widthCells / 2;
            for (int i = 1; i < points.Length; i++)
                foreach (var cell in BlockoutPainter.Stroke(points[i - 1], points[i], true))
                    for (int x = 0; x < route.widthCells; x++) for (int z = 0; z < route.widthCells; z++)
                        markup.Paint(cell + new Vector2Int(x - half, z - half), BlockoutMarkup.Layer.Route, route.id, false);
        }

        public static void ResizePosition(BlockoutMarkup markup, BlockoutMarkup.Position position, int size)
        {
            if (size < 1 || size > 15) throw new ArgumentOutOfRangeException(nameof(size), "Размер области: 1–15 клеток.");
            foreach (var cell in markup.cells) cell.positionIds.RemoveAll(id => id == position.id);
            RemoveEmptyCells(markup); position.sizeCells = size; PaintSquare(markup, position);
            markup.needsReevaluation = true;
        }

        private static void PaintSquare(BlockoutMarkup markup, BlockoutMarkup.Position p)
        {
            int half = p.sizeCells / 2;
            for (int z = 0; z < p.sizeCells; z++)
            for (int x = 0; x < p.sizeCells; x++)
                markup.Paint(p.centerCell + new Vector2Int(x - half, z - half), BlockoutMarkup.Layer.Position, p.id, false);
        }

        private static void RemoveEmptyCells(BlockoutMarkup markup)
        {
            markup.cells.RemoveAll(c => c.Empty); markup.needsReevaluation = true;
        }
    }
}

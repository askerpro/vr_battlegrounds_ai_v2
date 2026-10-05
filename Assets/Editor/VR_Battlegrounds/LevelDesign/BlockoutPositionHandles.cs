using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Жесты позиций и связей меняют только один BlockoutMarkup; игровых маркеров и коллайдеров нет.</summary>
    public sealed class BlockoutPositionHandles
    {
        private int undoGroup = -1;
        private string linkSource;
        private bool linkPassage, editPose;
        private BlockoutMarkup routeMarkup;
        private string editRouteId;
        private int corridorWidth = 5;
        public string Status { get; private set; }
        public bool Linking => linkSource != null;

        private static string Key(BlockoutMarkup markup, string suffix)
        {
            string path = AssetDatabase.GetAssetPath(markup);
            return "VRBG.MapGrowth." + (string.IsNullOrEmpty(path) ? markup.GetEntityId().ToString() : AssetDatabase.AssetPathToGUID(path)) + suffix;
        }
        public static string SelectedPosition(BlockoutMarkup markup) => markup == null ? "" : SessionState.GetString(Key(markup, ".position"), "");
        public static void SelectPosition(BlockoutMarkup markup, string id) { if (markup != null) SessionState.SetString(Key(markup, ".position"), id ?? ""); }
        public static string SelectedState(BlockoutMarkup markup) => SessionState.GetString(Key(markup, ".state"), "");
        public static void SelectState(BlockoutMarkup markup, string id) => SessionState.SetString(Key(markup, ".state"), id ?? "");
        public static string SelectedContact(BlockoutMarkup markup) => SessionState.GetString(Key(markup, ".contact"), "");
        public static void SelectContact(BlockoutMarkup markup, string id) => SessionState.SetString(Key(markup, ".contact"), id ?? "");
        public void EditState(BlockoutMarkup markup, string id) { Finish(); editRouteId = null; SelectState(markup, id); editPose = true; SceneView.RepaintAll(); }
        public void EditRoute(BlockoutMarkup markup, string id) { Finish(); linkSource = null; editPose = false; routeMarkup = markup; editRouteId = id; SceneView.RepaintAll(); }

        public void DrawControls(BlockoutMarkup markup)
        {
            if (markup == null || markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) return;
            editPose = GUILayout.Toolbar(editPose ? 1 : 0, new[] { "Область и угроза", "Поза в области" }) == 1;
            corridorWidth = EditorGUILayout.IntSlider("Ширина нового прохода, клеток", corridorWidth, 1, 15);
            using (new EditorGUI.DisabledScope(!markup.positions.Any(p => p.id == SelectedPosition(markup))))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Связать: контакт → клик позиции")) BeginLink(markup, false);
                if (GUILayout.Button("Связать: проход → клик позиции")) BeginLink(markup, true);
            }
            if (Linking) EditorGUILayout.HelpBox("Выберите вторую позицию в Scene View. Esc отменяет связь.", MessageType.Info);
            if (!string.IsNullOrEmpty(Status)) EditorGUILayout.HelpBox(Status, MessageType.Info);
        }

        public void BeginLink(BlockoutMarkup markup, bool passage)
        {
            Finish(); editRouteId = null; linkSource = SelectedPosition(markup); linkPassage = passage;
            if (!markup.positions.Any(p => p.id == linkSource)) linkSource = null;
            editPose = false; Status = null; SceneView.RepaintAll();
        }
        public bool Escape()
        {
            bool consumed = Linking || undoGroup >= 0 || editPose || editRouteId != null;
            Finish(); linkSource = null; editRouteId = null; routeMarkup = null; editPose = false; Status = null; return consumed;
        }
        public void Finish()
        {
            if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
        }

        public void Draw(BlockoutMarkup markup, float floorY, bool interactive, Func<string, bool, MapGrowthLinkLabel> label = null)
        {
            if (markup == null || markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) return;
            Event e = Event.current;
            if (e.type == EventType.MouseUp || e.type == EventType.Ignore) Finish();
            bool input = interactive && !e.alt && !e.control && !e.command && e.button == 0;
            var chosen = markup.positions.FirstOrDefault(p => p.id == SelectedPosition(markup));
            bool routeEditing = routeMarkup == markup && markup.routes.Any(r => r.id == editRouteId);
            if (input && !Linking && !routeEditing && chosen != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && !EditorGUIUtility.editingTextField)
            {
                Begin(markup, "Удалить позицию и её связи"); MapGrowthMarkupEditing.DeletePosition(markup, chosen.id);
                SelectPosition(markup, null); SelectState(markup, null); Changed(markup); Finish(); e.Use(); return;
            }
            using (new Handles.DrawingScope(Color.yellow))
            {
                foreach (var p in markup.positions)
                {
                    Vector3 center = Center(markup, p, floorY);
                    Handles.Label(center + Vector3.up * .1f, string.IsNullOrWhiteSpace(p.displayName) ? p.id : p.displayName);
                    if (input && (p != chosen || Linking) && Handles.Button(center, Quaternion.Euler(90, 0, 0), .12f, .18f, Handles.CircleHandleCap))
                    {
                        if (Linking) CompleteLink(markup, p.id);
                        SelectPosition(markup, p.id); SelectState(markup, p.protectedStateId); chosen = p;
                        GUI.changed = true;
                    }
                    DrawArrow(center, p.mainThreatYaw, Mathf.Max(.5f, p.sizeCells * markup.step), Color.yellow);
                }
            }
            DrawLinks(markup, floorY, label);
            if (routeEditing && !Linking)
            {
                var route = markup.routes.Single(r => r.id == editRouteId);
                for (int i = 0; i < route.viaCells.Count; i++)
                {
                    Vector2 cell = route.viaCells[i]; Vector3 point = new Vector3(markup.origin.x + (cell.x + .5f) * markup.step, floorY, markup.origin.y + (cell.y + .5f) * markup.step);
                    Handles.Label(point, "Точка прохода " + (i + 1));
                    if (!input) continue;
                    EditorGUI.BeginChangeCheck(); Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Begin(markup, "Переместить точку прохода");
                        route.viaCells[i] = new Vector2Int(Mathf.RoundToInt((moved.x - markup.origin.x) / markup.step - .5f), Mathf.RoundToInt((moved.z - markup.origin.y) / markup.step - .5f));
                        Changed(markup);
                    }
                }
                return;
            }
            if (chosen == null) return;
            Vector3 pivot = Center(markup, chosen, floorY);
            if (input && !Linking && !editPose)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(pivot, Quaternion.identity);
                Quaternion rotated = Handles.Disc(Quaternion.Euler(0, chosen.mainThreatYaw, 0), pivot, Vector3.up,
                    Mathf.Max(.5f, chosen.sizeCells * markup.step * .6f), false, 15);
                if (EditorGUI.EndChangeCheck())
                {
                    Begin(markup, "Изменить позицию на карте");
                    var cell = new Vector2Int(Mathf.RoundToInt((moved.x - markup.origin.x) / markup.step - .5f), Mathf.RoundToInt((moved.z - markup.origin.y) / markup.step - .5f));
                    MapGrowthMarkupEditing.MovePosition(markup, chosen, cell); chosen.mainThreatYaw = rotated.eulerAngles.y;
                    Changed(markup);
                }
            }
            foreach (var state in chosen.states ?? Array.Empty<BlockoutPositionState>())
            {
                if (state == null) continue;
                Vector3 foot = pivot + new Vector3(state.centerOffset.x, 0, state.centerOffset.y);
                bool selected = state.id == SelectedState(markup);
                using (new Handles.DrawingScope(selected ? Color.cyan : Color.white))
                {
                    Handles.DrawWireDisc(foot, Vector3.up, .09f);
                    if (input && editPose && !selected && Handles.Button(foot, Quaternion.Euler(90, 0, 0), .09f, .12f, Handles.CircleHandleCap))
                        SelectState(markup, state.id);
                }
                if (!selected) continue;
                DrawBody(markup, chosen, state, foot);
                if (input && !Linking && editPose)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(foot, Quaternion.identity);
                    Quaternion rotated = Handles.Disc(Quaternion.Euler(0, chosen.mainThreatYaw + state.yaw, 0), foot, Vector3.up, .35f, false, 15);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Begin(markup, "Изменить позу на карте");
                        state.centerOffset = new Vector2(moved.x - pivot.x, moved.z - pivot.z);
                        state.yaw = Mathf.DeltaAngle(chosen.mainThreatYaw, rotated.eulerAngles.y); Changed(markup);
                    }
                }
            }
        }

        private void Begin(BlockoutMarkup markup, string label)
        {
            if (undoGroup >= 0) return;
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(markup, label);
        }
        private static void Changed(BlockoutMarkup markup) { markup.needsReevaluation = true; EditorUtility.SetDirty(markup); SceneView.RepaintAll(); }
        public static Vector3 Center(BlockoutMarkup markup, BlockoutMarkup.Position p, float y) =>
            new Vector3(markup.origin.x + (p.centerCell.x + .5f) * markup.step, y, markup.origin.y + (p.centerCell.y + .5f) * markup.step);

        private void CompleteLink(BlockoutMarkup markup, string target)
        {
            if (target == linkSource) { Status = "Выберите другую позицию."; return; }
            var from = markup.positions.FirstOrDefault(p => p.id == linkSource);
            var to = markup.positions.FirstOrDefault(p => p.id == target);
            if (from == null || to == null) { linkSource = null; return; }
            if (!linkPassage)
            {
                var existing = markup.contacts.FirstOrDefault(c => c.positionAId == from.id && c.positionBId == to.id || c.positionAId == to.id && c.positionBId == from.id);
                if (existing == null)
                {
                    Begin(markup, "Связать позиции контактом");
                    existing = BlockoutContactSpec.CanonicalCopy(new BlockoutContactSpec { id = "contact-" + Guid.NewGuid().ToString("N"), positionAId = from.id, positionBId = to.id });
                    markup.contacts.Add(existing);
                    Changed(markup);
                }
                SelectContact(markup, existing.id);
                Status = "Контакт выбранной пары доступен в панели. Задайте пары поз и требования к обоим направлениям.";
            }
            else
            {
                if (!(from.states ?? Array.Empty<BlockoutPositionState>()).Any(s => s != null && s.id == from.protectedStateId)
                    || !(to.states ?? Array.Empty<BlockoutPositionState>()).Any(s => s != null && s.id == to.protectedStateId))
                { Status = "Для концов прохода сначала выберите исходные закрытые позы обеих позиций."; return; }
                Begin(markup, "Связать позиции проходом");
                var route = new BlockoutMarkup.Route { id = "route-" + Guid.NewGuid().ToString("N"), fromPositionId = from.id,
                    toPositionId = to.id, fromStateId = from.protectedStateId, toStateId = to.protectedStateId, widthCells = corridorWidth };
                markup.routes.Add(route);
                MapGrowthMarkupEditing.RepaintRoute(markup, route);
                Changed(markup); Status = "Проход создан с исходными позами. Клетки коридора можно дорисовать или удалить смысловой кистью; Direct задаётся в панели.";
            }
            Finish(); linkSource = null;
        }

        private static void DrawLinks(BlockoutMarkup markup, float y, Func<string, bool, MapGrowthLinkLabel> label)
        {
            foreach (var contact in markup.contacts)
            {
                var a = markup.positions.FirstOrDefault(p => p.id == contact.positionAId);
                var b = markup.positions.FirstOrDefault(p => p.id == contact.positionBId);
                if (a == null || b == null) continue;
                Vector3 start = Center(markup, a, y), end = Center(markup, b, y);
                using (new Handles.DrawingScope(Color.magenta))
                {
                    Handles.DrawDottedLine(start, end, 4);
                    if ((end - start).sqrMagnitude > .000001f)
                    {
                        DrawArrow(Vector3.Lerp(start, end, .35f), Quaternion.LookRotation(end - start).eulerAngles.y, .3f, Color.magenta);
                        DrawArrow(Vector3.Lerp(start, end, .65f), Quaternion.LookRotation(start - end).eulerAngles.y, .3f, Color.magenta);
                    }
                    Handles.Label((start + end) * .5f, "Контакт · замысел");
                    var measured = label?.Invoke(contact.id, true) ?? new MapGrowthLinkLabel("? Измерение не выполнено", Color.gray);
                    using (new Handles.DrawingScope(measured.Color)) Handles.Label((start + end) * .5f + Vector3.up * .12f, measured.Text);
                }
            }
            foreach (var route in markup.routes)
            {
                var a = markup.positions.FirstOrDefault(p => p.id == route.fromPositionId);
                var b = markup.positions.FirstOrDefault(p => p.id == route.toPositionId);
                if (a == null || b == null) continue;
                Vector3 Endpoint(BlockoutMarkup.Position p, string stateId)
                { var state = p.states?.FirstOrDefault(s => s.id == stateId); return Center(markup, p, y) + new Vector3(state?.centerOffset.x ?? 0, 0, state?.centerOffset.y ?? 0); }
                Vector3 start = Endpoint(a, route.fromStateId), end = Endpoint(b, route.toStateId);
                var points = new[] { start }.Concat((route.viaCells ?? new System.Collections.Generic.List<Vector2Int>()).Select(c => new Vector3(markup.origin.x + (c.x + .5f) * markup.step, y, markup.origin.y + (c.y + .5f) * markup.step))).Concat(new[] { end }).ToArray();
                using (new Handles.DrawingScope(Color.cyan))
                {
                    for (int i = 1; i < points.Length; i++) Handles.DrawDottedLine(points[i - 1], points[i], 8);
                    Handles.Label(Vector3.Lerp(start, end, .4f), "Проход · замысел" + (route.requireDirect ? " · Direct" : ""));
                    var measured = label?.Invoke(route.id, false) ?? new MapGrowthLinkLabel("? Измерение не выполнено", Color.gray);
                    using (new Handles.DrawingScope(measured.Color)) Handles.Label(Vector3.Lerp(start, end, .4f) + Vector3.up * .12f, measured.Text);
                }
            }
        }
        private static void DrawArrow(Vector3 point, float yaw, float length, Color color)
        {
            using (new Handles.DrawingScope(color)) Handles.ArrowHandleCap(0, point, Quaternion.Euler(0, yaw, 0), length, EventType.Repaint);
        }
        private static void DrawBody(BlockoutMarkup markup, BlockoutMarkup.Position p, BlockoutPositionState state, Vector3 foot)
        {
            float min = (-(p.sizeCells / 2) - .5f) * markup.step;
            float max = (p.sizeCells - p.sizeCells / 2 - .5f) * markup.step;
            bool inside = state.centerOffset.x >= min && state.centerOffset.x <= max && state.centerOffset.y >= min && state.centerOffset.y <= max;
            var data = markup.bodyProfile == null ? null : markup.bodyProfile.data;
            var body = state.stance == BlockoutStance.Standing ? data?.standing : data?.crouching;
            using (new Handles.DrawingScope(inside ? Color.cyan : Color.red))
            {
                if (!inside) Handles.Label(foot, "Поза вне области позиции");
                if (body == null) { Handles.Label(foot, "Для силуэта задайте профиль тела"); return; }
                Quaternion rotation = Quaternion.Euler(0, p.mainThreatYaw + state.yaw, 0);
                float radius = data.radius;
                Handles.DrawWireDisc(foot, Vector3.up, radius); Handles.DrawWireDisc(foot + Vector3.up * body.eyeOffset.y, Vector3.up, radius);
                foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    Handles.DrawLine(foot + side * radius, foot + side * radius + Vector3.up * body.eyeOffset.y);
                Vector3 eye = foot + rotation * body.eyeOffset, muzzle = foot + rotation * body.muzzleOffset;
                Handles.Label(eye, "Глаз"); Handles.Label(muzzle, "Ствол");
                Handles.DrawWireDisc(eye, Vector3.up, .04f); Handles.DrawWireDisc(muzzle, Vector3.up, .04f);
                DrawArrow(eye, p.mainThreatYaw + state.yaw, .3f, inside ? Color.cyan : Color.red);
            }
        }
    }
}

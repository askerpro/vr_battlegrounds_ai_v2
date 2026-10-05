using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ввод замысла внутри существующего редактора блокаута; ID и игровая геометрия не подменяются UI.</summary>
    [Serializable] public sealed class MapGrowthMarkupPanel
    {
        [SerializeField] private string positionAId, positionBId, contactId;

        public BlockoutMarkup DrawHeader(BlockoutMarkup markup)
        {
            if (markup == null) return null;
            if (markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion)
            {
                EditorGUILayout.HelpBox("Разметка прежней версии. Создайте копию v2; исходный ассет сохранится. Позы после миграции задаются явно.", MessageType.Info);
                if (GUILayout.Button("Создать копию разметки v2…"))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Копия разметки", markup.name + "-v2", "asset", "Сохраняется новый ассет.");
                    if (CanCreateAsset(path))
                    {
                        var copy = BlockoutMarkupMigration.CreateVersion2Copy(markup);
                        try { AssetDatabase.CreateAsset(copy, path); AssetDatabase.SaveAssetIfDirty(copy); return copy; }
                        catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
                    }
                }
                return markup;
            }
            var profile = (MapBodyProfile)EditorGUILayout.ObjectField("Профиль тела", markup.bodyProfile, typeof(MapBodyProfile), false);
            if (profile != markup.bodyProfile) Change(markup, "Выбрать профиль тела", () => markup.bodyProfile = profile);
            if (GUILayout.Button("Создать демонстрационный профиль тела…")) CreateProfile(markup);
            if (markup.bodyProfile != null && markup.bodyProfile.data != null && !markup.bodyProfile.data.calibrated)
                EditorGUILayout.HelpBox("Профиль демонстрационный: радиус и точки тела требуют проверки в шлеме. Измерения не подтверждают полную защиту игрока.", MessageType.Info);
            return markup;
        }

        public void DrawPosition(BlockoutMarkup markup, BlockoutMarkup.Position position, BlockoutPositionHandles handles)
        {
            if (markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) return;
            EditorGUI.BeginChangeCheck();
            string label = EditorGUILayout.TextField("Название позиции", position.displayName ?? "");
            int size = EditorGUILayout.IntSlider("Область, клеток", position.sizeCells, 1, 15);
            if (EditorGUI.EndChangeCheck()) Change(markup, "Изменить область позиции", () => {
                position.displayName = label;
                if (size != position.sizeCells) MapGrowthMarkupEditing.ResizePosition(markup, position, size);
            });
            EditorGUILayout.LabelField("Позы внутри позиции", EditorStyles.boldLabel);
            var states = position.states ?? Array.Empty<BlockoutPositionState>();
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Поза " + (i + 1) + (position.protectedStateId == state.id ? " · исходная закрытая" : ""));
                    EditorGUI.BeginChangeCheck();
                    int stance = EditorGUILayout.Popup("Положение тела", (int)state.stance, new[] { "Стоя", "В приседе" });
                    Vector2 offset = EditorGUILayout.Vector2Field("Смещение в области, м", state.centerOffset);
                    float yaw = EditorGUILayout.FloatField("Поворот от направления позиции, °", state.yaw);
                    if (EditorGUI.EndChangeCheck()) Change(markup, "Изменить позу", () => {
                        state.stance = (BlockoutStance)stance; state.centerOffset = offset; state.yaw = yaw;
                    });
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Править позу на карте")) handles.EditState(markup, state.id);
                        if (GUILayout.Button("Исходная закрытая")) Change(markup, "Выбрать закрытую позу", () => position.protectedStateId = state.id);
                        if (GUILayout.Button("Удалить позу и её связи"))
                        { Change(markup, "Удалить позу", () => MapGrowthMarkupEditing.DeleteState(markup, position.id, state.id)); break; }
                    }
                }
            }
            using (new EditorGUI.DisabledScope(states.Length >= 8))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Добавить выход слева")) AddState(markup, position, BlockoutStance.Standing, -1);
                if (GUILayout.Button("Добавить выход справа")) AddState(markup, position, BlockoutStance.Standing, 1);
            }
            using (new EditorGUI.DisabledScope(states.Length >= 8))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Добавить позу стоя")) AddState(markup, position, BlockoutStance.Standing);
                if (GUILayout.Button("Добавить позу в приседе")) AddState(markup, position, BlockoutStance.Crouching);
            }
        }

        public void DrawRelationships(BlockoutMarkup markup, BlockoutPositionHandles handles = null)
        {
            if (markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) return;
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Контакты между позициями", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Контакт описывает обзор и прострел в обе стороны. Физический проход задаётся отдельно.", EditorStyles.wordWrappedLabel);
            positionAId = PickPosition("Первая позиция", markup, positionAId, 0);
            positionBId = PickPosition("Вторая позиция", markup, positionBId, 1);
            using (new EditorGUI.DisabledScope(positionAId == null || positionBId == null || positionAId == positionBId))
                if (GUILayout.Button("Открыть или создать контакт пары")) SelectContact(markup);
            if (markup.contacts.Count > 0)
            {
                string sceneContact = BlockoutPositionHandles.SelectedContact(markup);
                if (markup.contacts.Any(c => c.id == sceneContact)) contactId = sceneContact;
                var labels = markup.contacts.Select(c => PositionLabel(markup, c.positionAId) + " ↔ " + PositionLabel(markup, c.positionBId)).ToArray();
                int index = Math.Max(0, markup.contacts.FindIndex(c => c.id == contactId));
                index = EditorGUILayout.Popup("Контакт", index, labels); contactId = markup.contacts[index].id;
                BlockoutPositionHandles.SelectContact(markup, contactId);
                DrawContact(markup, index);
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Физические связи", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Клетки с ID маршрута ограничивают допустимый коридор. Для нового пути выберите слой Route и нарисуйте полосу кистью.", EditorStyles.wordWrappedLabel);
            foreach (var route in markup.routes.ToArray())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(route.id);
                    string from = PickPosition("Начало", markup, route.fromPositionId, -1);
                    string to = PickPosition("Конец", markup, route.toPositionId, -1);
                    string fromState = PickState("Поза в начале", markup.positions.FirstOrDefault(p => p.id == from), route.fromStateId);
                    string toState = PickState("Поза в конце", markup.positions.FirstOrDefault(p => p.id == to), route.toStateId);
                    bool direct = EditorGUILayout.Toggle("Требовать прямой проход", route.requireDirect);
                    if (from != route.fromPositionId || to != route.toPositionId || fromState != route.fromStateId || toState != route.toStateId || direct != route.requireDirect)
                        Change(markup, "Изменить физическую связь", () => {
                            route.fromPositionId = from; route.toPositionId = to; route.fromStateId = fromState; route.toStateId = toState; route.requireDirect = direct;
                        });
                    EditorGUILayout.LabelField("Движение двунаправленное; прямой проход проверяется габаритом тела.", EditorStyles.wordWrappedLabel);
                    int width = EditorGUILayout.IntSlider("Ширина перерисовки, клеток", route.widthCells, 1, 15);
                    if (width != route.widthCells) Change(markup, "Изменить ширину кисти прохода", () => route.widthCells = width);
                    EditorGUILayout.LabelField("Промежуточные точки по порядку от начала. После изменения явно перерисуйте полосу или поправьте её кистью.", EditorStyles.wordWrappedMiniLabel);
                    for (int i = 0; i < route.viaCells.Count; i++)
                    {
                        int index = i;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var point = EditorGUILayout.Vector2IntField("Точка " + (i + 1) + ", клетка", route.viaCells[i]);
                            if (point != route.viaCells[i]) Change(markup, "Изменить точку прохода", () => route.viaCells[index] = point);
                            if (GUILayout.Button("×", GUILayout.Width(24))) { Change(markup, "Удалить точку прохода", () => route.viaCells.RemoveAt(index)); break; }
                        }
                    }
                    using (new EditorGUI.DisabledScope(route.viaCells.Count >= 16))
                        if (GUILayout.Button("Добавить промежуточную точку"))
                        {
                            var a = markup.positions.FirstOrDefault(p => p.id == route.fromPositionId);
                            var b = markup.positions.FirstOrDefault(p => p.id == route.toPositionId);
                            Vector2Int point = route.viaCells.Count > 0 ? route.viaCells.Last() + Vector2Int.right
                                : a != null && b != null ? Vector2Int.RoundToInt(((Vector2)a.centerCell + b.centerCell) * .5f) : Vector2Int.zero;
                            Change(markup, "Добавить точку прохода", () => route.viaCells.Add(point));
                        }
                    using (new EditorGUI.DisabledScope(handles == null || route.viaCells.Count == 0))
                        if (GUILayout.Button("Править точки прохода в Scene View")) handles.EditRoute(markup, route.id);
                    bool endpointsValid = markup.positions.Any(p => p.id == route.fromPositionId && (p.states ?? Array.Empty<BlockoutPositionState>()).Any(s => s.id == route.fromStateId))
                        && markup.positions.Any(p => p.id == route.toPositionId && (p.states ?? Array.Empty<BlockoutPositionState>()).Any(s => s.id == route.toStateId));
                    using (new EditorGUI.DisabledScope(!endpointsValid))
                        if (GUILayout.Button("Перерисовать только этот коридор по точкам"))
                            Change(markup, "Перерисовать коридор", () => MapGrowthMarkupEditing.RepaintRoute(markup, route));
                    if (GUILayout.Button("Удалить маршрут и его клетки")) Change(markup, "Удалить маршрут", () => MapGrowthMarkupEditing.DeleteRoute(markup, route.id));
                }
            }
            var validation = MapGrowthValidation.ValidateInput(markup, markup.bodyProfile == null ? null : markup.bodyProfile.data, new MapGrowthSearchParameters());
            if (validation.errors.Count > 0)
                EditorGUILayout.HelpBox("Вход ещё не готов:\n" + string.Join("\n", validation.errors.Take(12).Select(e => e.ownerId + ": " + e.message))
                    + (validation.errors.Count > 12 ? "\n… всего ошибок: " + validation.errors.Count : ""), MessageType.Warning);
            foreach (var warning in validation.warnings.Take(4)) EditorGUILayout.HelpBox(warning.ownerId + ": " + warning.message, MessageType.Warning);
        }

        private void DrawContact(BlockoutMarkup markup, int index)
        {
            var original = markup.contacts[index]; var copy = BlockoutContactSpec.CanonicalCopy(original);
            EditorGUI.BeginChangeCheck();
            copy.displayName = EditorGUILayout.TextField("Название контакта", copy.displayName ?? "");
            EditorGUILayout.LabelField("Замысел контакта"); copy.description = EditorGUILayout.TextArea(copy.description ?? "", GUILayout.MinHeight(42));
            var a = markup.positions.FirstOrDefault(p => p.id == copy.positionAId);
            var b = markup.positions.FirstOrDefault(p => p.id == copy.positionBId);
            foreach (var condition in copy.cases ?? Array.Empty<BlockoutContactCase>())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    condition.stateAId = PickState("Поза · " + PositionLabel(markup, copy.positionAId), a, condition.stateAId);
                    condition.stateBId = PickState("Поза · " + PositionLabel(markup, copy.positionBId), b, condition.stateBId);
                    condition.advantage = (ExpectedAdvantage)EditorGUILayout.Popup("Ожидаемое преимущество", (int)condition.advantage,
                        new[] { "Не задано", PositionLabel(markup, copy.positionAId), PositionLabel(markup, copy.positionBId) });
                    DrawDirection(PositionLabel(markup, copy.positionAId) + " → " + PositionLabel(markup, copy.positionBId), condition.aToB);
                    DrawDirection(PositionLabel(markup, copy.positionBId) + " → " + PositionLabel(markup, copy.positionAId), condition.bToA);
                    if (GUILayout.Button("Удалить это условие")) { copy.cases = copy.cases.Where(c => c != condition).ToArray(); GUI.changed = true; }
                }
            }
            var next = (from sa in a?.states ?? Array.Empty<BlockoutPositionState>() from sb in b?.states ?? Array.Empty<BlockoutPositionState>()
                where !(copy.cases ?? Array.Empty<BlockoutContactCase>()).Any(c => c.stateAId == sa.id && c.stateBId == sb.id)
                select new { sa, sb }).FirstOrDefault();
            using (new EditorGUI.DisabledScope(next == null))
                if (GUILayout.Button("Добавить условия для другой пары поз"))
                {
                    copy.cases = (copy.cases ?? Array.Empty<BlockoutContactCase>()).Concat(new[] { new BlockoutContactCase {
                        id = "case-" + Guid.NewGuid().ToString("N"), stateAId = next.sa.id, stateBId = next.sb.id } }).ToArray(); GUI.changed = true;
                }
            if (EditorGUI.EndChangeCheck()) Change(markup, "Изменить контакт", () => markup.contacts[index] = copy);
            EditorGUILayout.HelpBox("Преимущество — пожелание автора; критерий ещё не откалиброван. Отчёт не будет выдавать его за доказанный баланс.", MessageType.Info);
            if (GUILayout.Button("Удалить контакт")) Change(markup, "Удалить контакт", () => markup.contacts.RemoveAt(index));
        }

        private static void DrawDirection(string label, BlockoutContactDirection direction)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            string[] modes = { "Не задано", "Требуется", "Запрещено" };
            direction.vision = (ContactRequirement)EditorGUILayout.Popup("Обзор", (int)direction.vision, modes);
            direction.shot = (ContactRequirement)EditorGUILayout.Popup("Прострел", (int)direction.shot, modes);
            direction.visibleShotOnly = EditorGUILayout.Toggle("Учитывать только видимый прострел", direction.visibleShotOnly);
            DrawRange("Доля прострела цели", direction.shotShare); DrawRange("Открытость стрелка ответному огню", direction.sourceExposure);
        }

        private static void DrawRange(string label, MapGrowthMetricRange range)
        {
            range.enabled = EditorGUILayout.Toggle(label, range.enabled);
            if (!range.enabled) return;
            using (new EditorGUILayout.HorizontalScope())
            { range.min = EditorGUILayout.FloatField("Минимум", range.min); range.max = EditorGUILayout.FloatField("Максимум", range.max); }
        }

        private void SelectContact(BlockoutMarkup markup)
        {
            var existing = markup.contacts.FirstOrDefault(c => c.positionAId == positionAId && c.positionBId == positionBId || c.positionAId == positionBId && c.positionBId == positionAId);
            if (existing != null) { contactId = existing.id; BlockoutPositionHandles.SelectContact(markup, contactId); return; }
            var contact = BlockoutContactSpec.CanonicalCopy(new BlockoutContactSpec {
                id = "contact-" + Guid.NewGuid().ToString("N"), positionAId = positionAId, positionBId = positionBId });
            Change(markup, "Добавить контакт", () => markup.contacts.Add(contact)); contactId = contact.id;
            BlockoutPositionHandles.SelectContact(markup, contactId);
        }

        private static string PickPosition(string label, BlockoutMarkup markup, string selected, int fallback)
        {
            if (markup.positions.Count == 0) { EditorGUILayout.LabelField(label, "Нет позиций"); return null; }
            if (fallback < 0)
            {
                var labels = new[] { "Выберите позицию" }.Concat(markup.positions.Select(p => PositionLabel(markup, p.id))).ToArray();
                int before = markup.positions.FindIndex(p => p.id == selected) + 1;
                int after = EditorGUILayout.Popup(label, before, labels);
                return after == before ? selected : after == 0 ? null : markup.positions[after - 1].id;
            }
            int index = markup.positions.FindIndex(p => p.id == selected); if (index < 0) index = Math.Min(fallback, markup.positions.Count - 1);
            index = EditorGUILayout.Popup(label, index, markup.positions.Select(p => PositionLabel(markup, p.id)).ToArray()); return markup.positions[index].id;
        }

        private static string PickState(string label, BlockoutMarkup.Position position, string selected)
        {
            var states = position?.states ?? Array.Empty<BlockoutPositionState>();
            var labels = new[] { "Выберите позу" }.Concat(states.Select((s, i) => (s.stance == BlockoutStance.Standing ? "Стоя" : "В приседе") + " · " + (i + 1))).ToArray();
            int index = Array.FindIndex(states, s => s.id == selected) + 1;
            int after = EditorGUILayout.Popup(label, index, labels);
            return after == index ? selected : after == 0 ? null : states[after - 1].id;
        }

        private static string PositionLabel(BlockoutMarkup markup, string id)
        {
            var p = markup.positions.FirstOrDefault(candidate => candidate.id == id);
            return p == null ? "Удалённая позиция" : string.IsNullOrWhiteSpace(p.displayName) ? p.id : p.displayName;
        }

        private static void AddState(BlockoutMarkup markup, BlockoutMarkup.Position position, BlockoutStance stance, float side = 0)
        {
            Change(markup, "Добавить позу", () => {
                var state = new BlockoutPositionState { id = "state-" + Guid.NewGuid().ToString("N"), stance = stance };
                float distance = Mathf.Max(0, (position.sizeCells / 2 - .5f) * markup.step);
                Vector3 lateral = Quaternion.Euler(0, position.mainThreatYaw, 0) * Vector3.right * side * distance;
                state.centerOffset = new Vector2(lateral.x, lateral.z);
                position.states = (position.states ?? Array.Empty<BlockoutPositionState>()).Concat(new[] { state }).ToArray();
                if (string.IsNullOrEmpty(position.protectedStateId)) position.protectedStateId = state.id;
            });
        }

        private static void CreateProfile(BlockoutMarkup markup)
        {
            string path = EditorUtility.SaveFilePanelInProject("Демонстрационный профиль", "MapBodyProfile-demo", "asset", "Точки условные; нужна проверка в шлеме.");
            if (!CanCreateAsset(path)) return;
            var profile = ScriptableObject.CreateInstance<MapBodyProfile>(); profile.data.id = "body-" + Guid.NewGuid().ToString("N");
            profile.data.standing = Pose(1.7f, 1.6f); profile.data.crouching = Pose(1.1f, 1);
            try { AssetDatabase.CreateAsset(profile, path); AssetDatabase.SaveAssetIfDirty(profile); }
            catch { UnityEngine.Object.DestroyImmediate(profile); throw; }
            Change(markup, "Выбрать демонстрационный профиль", () => markup.bodyProfile = profile);
        }

        private static bool CanCreateAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) return true;
            EditorUtility.DisplayDialog("Ассет уже существует", "Выберите другое имя: существующая разметка или профиль не будут заменены.", "OK");
            return false;
        }

        private static MapBodyPoseTemplate Pose(float eye, float muzzle) => new MapBodyPoseTemplate {
            eyeOffset = new Vector3(0, eye, 0), muzzleOffset = new Vector3(0, muzzle, 0),
            body = new[] { new ImpactBodySample { offset = new Vector3(0, eye, 0), weight = 1 },
                new ImpactBodySample { offset = new Vector3(0, eye * .65f, 0), weight = 1 },
                new ImpactBodySample { offset = new Vector3(0, eye * .3f, 0), weight = 1 } } };

        private static void Change(BlockoutMarkup markup, string label, Action mutation)
        { Undo.RegisterCompleteObjectUndo(markup, label); mutation(); markup.needsReevaluation = true; EditorUtility.SetDirty(markup); SceneView.RepaintAll(); }
    }
}

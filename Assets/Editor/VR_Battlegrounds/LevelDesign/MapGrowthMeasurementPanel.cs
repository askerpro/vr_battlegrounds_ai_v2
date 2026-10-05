using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public readonly struct MapGrowthLinkLabel
    {
        public readonly string Text;
        public readonly Color Color;
        public MapGrowthLinkLabel(string text, Color color) { Text = text; Color = color; }
    }
    /// <summary>Результат одного ручного запуска общего ядра. Не является сохранённым сертификатом карты.</summary>
    [InitializeOnLoad]
    public sealed class MapGrowthMeasurementPanel
    {
        private static int revision;
        private int measuredRevision, markupDirtyCount, profileDirtyCount;
        private Scene measuredScene;
        private BlockoutMarkup measuredMarkup;
        private MapBodyProfile measuredProfile;
        private MapGrowthIntentResult intent;
        private MapEvaluationResult report;
        private string problem;
        private bool showAll, showReport;
        static MapGrowthMeasurementPanel() => ObjectChangeEvents.changesPublished += Changed;
        private static void Changed(ref ObjectChangeEventStream changes) { if (changes.length > 0) unchecked { revision++; } }

        private bool Stale(BlockoutMarkup markup, Scene scene, bool matches) => revision != measuredRevision
            || scene.handle != measuredScene.handle || !matches || EditorUtility.GetDirtyCount(markup) != markupDirtyCount
            || markup.bodyProfile != measuredProfile || measuredProfile != null && EditorUtility.GetDirtyCount(measuredProfile) != profileDirtyCount;

        /// <summary>Scene View читает тот же отчёт; второго кэша измерений или автора состояния нет.</summary>
        public MapGrowthLinkLabel LinkLabel(BlockoutMarkup markup, Scene scene, bool matches, string id, bool contact)
        {
            if (intent == null || measuredMarkup != markup) return new MapGrowthLinkLabel("? Измерение не выполнено", Color.gray);
            if (Stale(markup, scene, matches)) return new MapGrowthLinkLabel("↻ Предыдущее измерение устарело", Color.gray);
            var checks = intent.checks.Where(c => contact ? c.sources.Any(s => s.contactId == id) : c.ownerId == id && c.field == "route").ToArray();
            int passed = checks.Count(c => c.status == MapGrowthIntentStatus.Satisfied), failed = checks.Count(c => c.status == MapGrowthIntentStatus.Violated);
            int unknown = checks.Count(c => c.status == MapGrowthIntentStatus.Unmeasured);
            if (checks.Length == 0) return new MapGrowthLinkLabel("? Нет заданных проверок", Color.gray);
            return new MapGrowthLinkLabel("Измерено: ✓ " + passed + " · ! " + failed + " · ? " + unknown,
                failed > 0 ? Color.red : unknown > 0 || !intent.measurementsComplete ? Color.yellow : Color.green);
        }

        public void Draw(BlockoutMarkup markup, Scene scene, bool matches)
        {
            if (markup == null || markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) return;
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Задумано / измерено", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!matches || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
                if (GUILayout.Button("Измерить замысел активной карты")) Measure(markup, scene);
            if (!matches) EditorGUILayout.HelpBox("Для измерения выберите активную карту и соответствующую ей разметку/арену/сетку.", MessageType.Info);
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            if (intent == null || measuredMarkup != markup) return;
            bool stale = Stale(markup, scene, matches);
            EditorGUILayout.HelpBox(stale ? "Есть изменения после запуска. Ниже результат предыдущего измерения; пересчитайте его."
                : "Результат последнего запуска. Полнота расчёта: " + (intent.measurementsComplete ? "полный" : "неполный") + ". Геометрические доли не подтверждают баланс или полную защиту в шлеме.", stale ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.LabelField("Выполнено " + intent.checks.Count(c => c.status == MapGrowthIntentStatus.Satisfied)
                + "; нарушено " + intent.checks.Count(c => c.status == MapGrowthIntentStatus.Violated)
                + "; не измерено " + intent.checks.Count(c => c.status == MapGrowthIntentStatus.Unmeasured));
            showAll = EditorGUILayout.Toggle("Показать все строки", showAll);
            foreach (var check in showAll ? intent.checks : intent.checks.Take(20))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string status = check.status == MapGrowthIntentStatus.Satisfied ? "✓ Выполнено" : check.status == MapGrowthIntentStatus.Violated ? "! Нарушено" : "? Не измерено";
                    EditorGUILayout.LabelField(status + " · " + check.ownerId, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(check.field + ": задумано " + check.intended + "; измерено " + (check.measured.HasValue ? check.measured.Value.ToString("G4") : "—"), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(check.message ?? "", EditorStyles.wordWrappedLabel);
                    if (check.sources.Length > 0) EditorGUILayout.LabelField("Источник: " + string.Join(", ", check.sources.Select(s => s.contactId + "/" + s.caseId)), EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (!showAll && intent.checks.Count > 20) EditorGUILayout.LabelField("Показано 20 из " + intent.checks.Count + " строк; полный расчёт сохранён в окне.");
            EditorGUILayout.LabelField("Измеренных незаданных контактов поз: " + intent.undeclaredContacts.Length);
            if (showAll) foreach (var row in intent.undeclaredContacts)
                EditorGUILayout.LabelField(row.from + "/" + row.fromState + " → " + row.to + "/" + row.toState
                    + ": обзор " + row.visibleShare.ToString("G3") + "; прострел " + row.shotShare.ToString("G3"), EditorStyles.wordWrappedLabel);
            showReport = EditorGUILayout.Foldout(showReport, "Общий отчёт карты", true);
            if (showReport && report != null) EditorGUILayout.TextArea(MapEvaluation.Format(report), GUILayout.MinHeight(150));
        }

        private void Measure(BlockoutMarkup markup, Scene scene)
        {
            problem = null; intent = null; report = null;
            try
            {
                var built = MapGridBuilder.Build(scene);
                if (built.Problems.Count > 0) { problem = string.Join("\n", built.Problems); return; }
                var input = MapGrowthMarkupAdapter.Build(markup, built.Grid);
                if (!input.CanEvaluate) { problem = string.Join("\n", input.validation.errors.Select(e => e.ownerId + ": " + e.message)); return; }
                // GUID сверяет Painter; переименование сцены не должно превращать сохранённую подсказку scenePath во второй идентификатор.
                input.layout.map = scene.name;
                report = MapEvaluationScene.EvaluateLayout(scene, input.layout, built: built);
                intent = MapGrowthIntentEvaluation.Evaluate(input, report);
                measuredMarkup = markup; measuredProfile = markup.bodyProfile;
                measuredRevision = revision; measuredScene = scene;
                markupDirtyCount = EditorUtility.GetDirtyCount(markup);
                profileDirtyCount = measuredProfile == null ? 0 : EditorUtility.GetDirtyCount(measuredProfile);
            }
            catch (Exception exception)
            { problem = "Не удалось измерить замысел: " + exception.Message; GameLog.Debug.Warning(problem, markup); }
        }
    }
}

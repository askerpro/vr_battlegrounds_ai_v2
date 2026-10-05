using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Владелец ручного запуска и изображения; расчёт не записывает рабочую карту.</summary>
    public sealed class MapGrowthCandidatePanel : IDisposable
    {
        private MapGrowthSceneCapture capture;
        private MapGrowthSearchRun run;
        private MapGrowthReport report;
        private Texture2D preview;
        private string previewCandidateId;
        private bool previewIsometric;
        private Action repaint;
        private string problem, previewProblem, applied;
        private bool ticking, isometric, showReport, showReasons, showChecks, stale;
        private int selected = -1;
        private double lastRepaint, lastFreshnessCheck;
        private readonly System.Collections.Generic.HashSet<GameObject> replacement = new System.Collections.Generic.HashSet<GameObject>();
        public bool Running => ticking;
        public MapGrowthReport Report => report;

        public MapGrowthSettings Draw(BlockoutMarkup markup, Scene scene, bool matches, MapGrowthSettings settings, Action repaintWindow)
        {
            EditorGUILayout.HelpBox("Задайте позиции, позы, контакты и проходы во вкладке «Позиции». По умолчанию текущая геометрия фиксирована. Для замены отметьте корни из предыдущих созданных наборов; их дочерние детали тоже будут заменены.", MessageType.Info);
            using (new EditorGUI.DisabledScope(ticking))
            {
                settings = (MapGrowthSettings)EditorGUILayout.ObjectField("Настройки поиска", settings, typeof(MapGrowthSettings), false);
                if (settings == null && GUILayout.Button("Создать настройки поиска")) settings = CreateSettings();
                if (settings != null) DrawSettings(settings);
                int workers = Math.Max(0, EditorPrefs.GetInt(MapGrowthExecutionOptions.WorkerPreference, 0));
                int chosenWorkers = EditorGUILayout.IntField("Фоновые потоки (0 — автоматически)", workers);
                if (chosenWorkers != workers) EditorPrefs.SetInt(MapGrowthExecutionOptions.WorkerPreference, Math.Max(0, chosenWorkers));
            }
            bool unavailable = !matches || markup == null || settings == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;
            if (!unavailable)
            {
                try
                {
                    var available = MapGrowthGeneratedOwnership.Available(scene);
                    replacement.RemoveWhere(g => g == null || !available.Contains(g));
                    using (new EditorGUI.DisabledScope(ticking || capture != null)) foreach (var root in available)
                    {
                        bool old = capture != null ? capture.ReplacedRoots.Contains(root) : replacement.Contains(root);
                        bool chosen = EditorGUILayout.ToggleLeft("Разрешить заменить: " + root.name, old);
                        if (chosen != old) { if (chosen) replacement.Add(root); else replacement.Remove(root); }
                    }
                    if (available.Length > 0) EditorGUILayout.LabelField("Без отметки блок закреплён. Чтобы изменить выбор, закройте результат.", EditorStyles.wordWrappedMiniLabel);
                }
                catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Error); unavailable = true; }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(unavailable || ticking))
                    if (GUILayout.Button("Вырастить варианты")) Start(scene, markup, settings, repaintWindow, replacement.ToArray());
                using (new EditorGUI.DisabledScope(!ticking)) if (GUILayout.Button("Отменить")) Cancel();
                using (new EditorGUI.DisabledScope(ticking || capture == null)) if (GUILayout.Button("Закрыть результат")) Dispose();
            }
            if (!matches) EditorGUILayout.HelpBox("Выберите разметку этой активной карты во вкладке «Позиции».", MessageType.Warning);
            if (ticking)
            {
                if (run == null) EditorGUILayout.LabelField("Захват штатных форм: " + capture.CapturedTemplates + "/" + capture.TemplateCount);
                else EditorGUILayout.LabelField(run.Phase + " · ветка " + (run.BranchId + 1) + " · попытка " + run.AttemptId);
            }
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Error);
            if (report == null) return settings;
            if (!ticking && capture != null && EditorApplication.timeSinceStartup - lastFreshnessCheck > 1)
            { lastFreshnessCheck = EditorApplication.timeSinceStartup; stale = !capture.UsesInput(scene, markup, settings) || !capture.IsCurrent(); }
            EditorGUILayout.LabelField("Найдено " + report.DistinctGeometryCount + "/" + report.TargetCandidates
                + " · попыток " + report.Attempts + "/" + report.AttemptBudget + " · seed " + report.Seed);
            EditorGUILayout.LabelField("Время поиска: " + (report.ElapsedMilliseconds / 1000).ToString("F1") + " с");
            if (report.Performance != null)
            {
                var perf = report.Performance;
                EditorGUILayout.LabelField("Потоков " + perf.Workers + " · фоновых задач " + (run?.PendingPreparations ?? 0) + " · очередь физики " + (run?.QueuedEvaluations ?? 0));
                EditorGUILayout.LabelField("Предложения Σ " + perf.PreparationMilliseconds.ToString("F1") + " мс · маршруты в фоне Σ " + perf.RoutePreparationMilliseconds.ToString("F1") + " мс · native Step Σ " + perf.EvaluationStepMilliseconds.ToString("F1") + " мс · ожидание очереди Σ " + perf.QueueWaitMilliseconds.ToString("F1") + " мс", EditorStyles.wordWrappedMiniLabel);
                string allocations = perf.AllocationCounterAvailable ? ((perf.MainThreadAllocatedBytes + perf.PreparationAllocatedBytes + perf.RouteAllocatedBytes) / 1048576.0).ToString("F1") + " МБ" : "счётчик недоступен";
                string workingSet = perf.WorkingSetAvailable ? (perf.PeakWorkingSetBytes / 1048576.0).ToString("F1") + " МБ" : "счётчик недоступен";
                EditorGUILayout.LabelField("Выделено managed: " + allocations + " · выборочный пик heap " + (perf.PeakManagedBytes / 1048576.0).ToString("F1") + " МБ · process: " + workingSet, EditorStyles.wordWrappedMiniLabel);
                if (perf.SamplingProblem != null) EditorGUILayout.HelpBox("Часть диагностики памяти недоступна: " + perf.SamplingProblem, MessageType.Info);
            }
            if (report.ShortageReason != null) EditorGUILayout.HelpBox(report.ShortageReason, MessageType.Info);
            if (stale || report.Stale) EditorGUILayout.HelpBox("Вход изменился. Это предыдущий отчёт; для применения пересчитайте варианты.", MessageType.Warning);
            if (report.Cancelled) EditorGUILayout.HelpBox("Запуск отменён. Показаны только уже полностью оценённые варианты; поиск не завершён.", MessageType.Warning);
            if (!string.IsNullOrEmpty(report.Problem)) EditorGUILayout.HelpBox(report.Problem, MessageType.Error);
            showReasons = EditorGUILayout.Foldout(showReasons, "Ветки и причины отказов", true);
            if (showReasons) foreach (var branch in report.Branches)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Ветка " + branch.branchId + " · seed " + branch.seed + " · попыток " + branch.attempts
                        + " · отсев " + branch.filtered + " · оценено " + branch.evaluated + " · принято изменений " + branch.accepted, EditorStyles.wordWrappedLabel);
                    if (branch.best != null) EditorGUILayout.LabelField(branch.best.ToString(), EditorStyles.wordWrappedMiniLabel);
                    foreach (var reason in branch.reasons) EditorGUILayout.LabelField(reason.Value + " × " + reason.Key, EditorStyles.wordWrappedLabel);
                }
            }
            if (report.Candidates.Count == 0) return settings;
            for (int i = 0; i < report.Candidates.Count; i++)
                if (GUILayout.Toggle(selected == i, "Вариант " + (i + 1) + " · seed " + report.Candidates[i].Seed + " · новых блоков " + report.Candidates[i].Recipes.Count, "Button") && selected != i) Select(i);
            if (selected < 0 || selected >= report.Candidates.Count) return settings;
            var candidate = report.Candidates[selected];
            bool nextView = EditorGUILayout.Toggle("Изометрия предпросмотра", isometric);
            if (nextView != isometric) { isometric = nextView; RenderSelection(); }
            if (preview != null)
            {
                var rect = GUILayoutUtility.GetAspectRect(1, GUILayout.MaxHeight(420));
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
            if (!string.IsNullOrEmpty(previewProblem)) EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
            EditorGUILayout.LabelField(MapGrowthViolationVector.From(candidate).ToString(), EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("Полное автоматическое измерение не заменяет проверки поз, оружия и защиты тела в Unity/Quest. Новые фактические позиции автоматически не обнаруживаются.", MessageType.Info);
            showChecks = EditorGUILayout.Foldout(showChecks, "Контакты, проходы и непроверенные требования", true);
            if (showChecks && candidate.Intent != null)
            {
                foreach (var check in candidate.Intent.checks)
                {
                    string status = check.kind == MapGrowthIntentCheckKind.ManualAcceptance ? "? Ручная проверка" : check.status == MapGrowthIntentStatus.Satisfied ? "✓ Выполнено" : check.status == MapGrowthIntentStatus.Violated ? "! Нарушено" : "? Не измерено";
                    EditorGUILayout.LabelField(status + " · " + check.ownerId + "/" + check.field + ": " + check.intended
                        + " → " + (check.measured.HasValue ? check.measured.Value.ToString("G4") : "—") + ". " + check.message, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.LabelField("Незаданные контакты между объявленными позами: " + candidate.Intent.undeclaredContacts.Length);
                foreach (var row in candidate.Intent.undeclaredContacts)
                    EditorGUILayout.LabelField(row.from + "/" + row.fromState + " → " + row.to + "/" + row.toState
                        + ": обзор " + row.visibleShare.ToString("G3") + "; прострел " + row.shotShare.ToString("G3"), EditorStyles.wordWrappedMiniLabel);
            }
            foreach (var timing in candidate.TimingsMilliseconds) EditorGUILayout.LabelField(timing.Key + ": " + timing.Value.ToString("F1") + " мс", EditorStyles.miniLabel);
            showReport = EditorGUILayout.Foldout(showReport, "Общий отчёт карты", true);
            if (showReport && candidate.Evaluation != null) EditorGUILayout.TextArea(MapEvaluation.Format(candidate.Evaluation), GUILayout.MinHeight(150));
            using (new EditorGUI.DisabledScope(ticking || stale || report.Stale || unavailable || !capture.UsesInput(scene, markup, settings) || !candidate.AutomaticRequirementsSatisfied))
                if (GUILayout.Button("Применить выбранный вариант · одна операция Undo"))
                {
                    try
                    {
                        if (!capture.UsesInput(scene, markup, settings)) throw new InvalidOperationException("Выбран другой вход; пересчитайте поиск.");
                        var result = MapGrowthCandidateApply.Apply(scene, capture, candidate); applied = "Добавлено блоков: " + result.CreatedObjects.Count + ". Карта не сохранена автоматически."; stale = !capture.IsCurrent();
                    }
                    catch (Exception ex) { Fail(ex); }
                }
            if (!string.IsNullOrEmpty(applied)) EditorGUILayout.HelpBox(applied, MessageType.Info);
            return settings;
        }

        public void Start(Scene scene, BlockoutMarkup markup, MapGrowthSettings settings, Action repaintWindow = null, GameObject[] replacedRoots = null)
        {
            Dispose(); problem = null; previewProblem = null; applied = null; stale = false; repaint = repaintWindow;
            try
            {
                capture = MapGrowthSnapshotBuilder.BeginCapture(scene, markup, settings, replacedRoots);
                ticking = true; EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += Dispose; EditorApplication.playModeStateChanged += PlayStateChanged;
            }
            catch (Exception ex) { Dispose(); Fail(ex); }
        }
        public void Cancel()
        {
            if (run != null) { run.Cancel(); run.Step(); FinishTicking(); }
            else { Dispose(); problem = "Захват отменён; частичного результата нет."; }
            repaint?.Invoke();
        }
        private void Tick()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) { Dispose(); return; }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (run == null)
                {
                    if (!capture.StepCapture()) { RepaintThrottled(); return; }
                    run = new MapGrowthSearchRun(capture, MapGrowthExecutionOptions.FromMachine()); report = run.Report;
                }
                if (run.Step()) FinishTicking();
                RepaintThrottled();
            }
            catch (Exception ex) { Dispose(); Fail(ex); }
        }
        private void FinishTicking()
        {
            ticking = false; EditorApplication.update -= Tick; stale = capture != null && !capture.IsCurrent();
            if (report != null && report.Candidates.Count > 0 && selected < 0) Select(0);
            repaint?.Invoke();
        }
        private void RepaintThrottled()
        { if (EditorApplication.timeSinceStartup - lastRepaint < .15) return; lastRepaint = EditorApplication.timeSinceStartup; repaint?.Invoke(); }
        private void Select(int index) { selected = index; RenderSelection(); }
        private void RenderSelection()
        {
            var candidate = report.Candidates[selected];
            if (preview != null && (previewCandidateId != candidate.CandidateId || previewIsometric != isometric))
            { Object.DestroyImmediate(preview); preview = null; previewCandidateId = null; }
            if (capture == null || !capture.IsCurrent())
            { previewProblem = "Вход изменился. Сохранённый предпросмотр относится к предыдущему варианту; пересчитайте поиск."; return; }
            if (preview != null) Object.DestroyImmediate(preview); preview = null; previewProblem = null;
            try { preview = MapGrowthCandidatePreview.Render(capture, candidate, isometric); previewCandidateId = candidate.CandidateId; previewIsometric = isometric; }
            catch (Exception ex) { previewProblem = ex.Message; }
        }
        private void Fail(Exception ex) { problem = "Выращиватель: " + ex.Message; GameLog.Debug.Warning(problem); repaint?.Invoke(); }
        private void PlayStateChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) Dispose(); }
        public void Dispose()
        {
            ticking = false; EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Dispose; EditorApplication.playModeStateChanged -= PlayStateChanged;
            replacement.Clear();
            try { run?.Dispose(); }
            finally { run = null; capture?.Dispose(); capture = null; if (preview != null) Object.DestroyImmediate(preview); preview = null; report = null; selected = -1; }
        }
        private static MapGrowthSettings CreateSettings()
        {
            string path = EditorUtility.SaveFilePanelInProject("Настройки выращивателя", "MapGrowth", "asset", "Выберите место сохранения настроек поиска.");
            if (string.IsNullOrEmpty(path)) return null;
            var asset = ScriptableObject.CreateInstance<MapGrowthSettings>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssetIfDirty(asset); return asset;
        }
        private static void DrawSettings(MapGrowthSettings settings)
        {
            var serialized = new SerializedObject(settings); serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("profile"), new GUIContent("Профиль измерителя"));
            var search = serialized.FindProperty("search");
            Field("seed", "Исходный seed"); Field("targetCandidates", "Желаемых вариантов"); Field("branchCount", "Независимых веток");
            Field("attemptsPerBranch", "Попыток на ветку"); Field("maximumGeneratedBlocks", "Предел новых блоков");
            var operations = search.FindPropertyRelative("operations");
            operations.intValue = EditorGUILayout.MaskField("Разрешённые изменения", operations.intValue,
                new[] { "Добавить блок", "Удалить новый блок", "Переместить", "Повернуть", "Изменить форму", "Изменить высоту", "Изменить материал", "Изменить щели", "Добавить деталь 0.3×0.3" }) & (int)MapGrowthOperation.All;
            Range("density", "Доля занятого пола"); Range("closureStanding", "Закрытость стоя"); Range("closureCrouching", "Закрытость в приседе");
            serialized.ApplyModifiedProperties();
            void Field(string key, string label) => EditorGUILayout.PropertyField(search.FindPropertyRelative(key), new GUIContent(label));
            void Range(string key, string label)
            {
                var range = search.FindPropertyRelative(key); EditorGUILayout.PropertyField(range.FindPropertyRelative("enabled"), new GUIContent(label));
                if (!range.FindPropertyRelative("enabled").boolValue) return;
                using (new EditorGUILayout.HorizontalScope())
                { EditorGUILayout.PropertyField(range.FindPropertyRelative("min"), new GUIContent("От")); EditorGUILayout.PropertyField(range.FindPropertyRelative("max"), new GUIContent("До")); }
            }
        }
    }
}

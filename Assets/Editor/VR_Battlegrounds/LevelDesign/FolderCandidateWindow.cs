using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Запуск грубого подбора для импортированного пака Assets/env_packs/имя_пака.</summary>
    public sealed class FolderCandidateWindow : EditorWindow
    {
        private const string FolderPreference = "VrBattlegrounds.Catalog.AutoPackFolder";
        private string folder, feedback;
        private bool error;
        private DefaultAsset selectedFolder;
        private FolderCandidateScanner.Session session;
        private FolderCandidateScanner.Report report;

        [MenuItem("Tools/VR Battlegrounds/Level Design/Candidates/Auto Mark Pack")]
        public static void Open()
        {
            var window = GetWindow<FolderCandidateWindow>("Автоподбор LEGO");
            window.minSize = new Vector2(490, 340);
            window.Show();
        }

        private void OnEnable()
        {
            folder = EditorPrefs.GetString(FolderPreference, FolderCandidateScanner.PackRoot + "/");
            selectedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            EditorApplication.update += Tick;
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            session = null; // Закрытие/перекомпиляция отменяет несохранённый проход.
        }

        private void OnGUI()
        {
            GUILayout.Label("Пак → кандидаты для LD_Alphabet", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Папка: Assets/env_packs/<имя пака>. Вложенные папки включаются. Подбор по габаритам, поворотам и простым сборкам; предложения требуют визуальной проверки.", MessageType.Info);
            using (new EditorGUI.DisabledScope(session != null))
            {
                var chosen = (DefaultAsset)EditorGUILayout.ObjectField("Папка пака", selectedFolder, typeof(DefaultAsset), false);
                if (chosen != selectedFolder)
                {
                    selectedFolder = chosen;
                    folder = chosen != null ? AssetDatabase.GetAssetPath(chosen) : FolderCandidateScanner.PackRoot + "/";
                    report = null;
                }
                var typed = EditorGUILayout.TextField("Путь", folder);
                if (typed != folder)
                {
                    folder = typed;
                    selectedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
                    report = null;
                }
                if (GUILayout.Button("Грубая разметка пака"))
                {
                    try
                    {
                        session = FolderCandidateScanner.Begin(folder);
                        EditorPrefs.SetString(FolderPreference, folder);
                        feedback = "Сканирование началось. Прежний каталог сохраняется до завершения.";
                        error = false;
                    }
                    catch (Exception exception) { Fail(exception); }
                }
            }
            if (session != null)
            {
                var rect = GUILayoutUtility.GetRect(1, 23, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, (float)session.Done / session.Count, session.Done + " / " + session.Count);
                GUILayout.Label(session.Current, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Отменить")) { session = null; feedback = "Отменено. Прежние рецепты сохранены."; }
            }
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, error ? MessageType.Error : MessageType.Info);
            using (new EditorGUI.DisabledScope(session != null))
            {
                if (GUILayout.Button("Обновить автоматические кандидаты на стенде"))
                {
                    try
                    {
                        var result = AssetCandidateReviewScene.RefreshAutomaticCandidates();
                        feedback = "Стенд обновлён: " + result.recipes + " автоматических предложений. Ручные кандидаты сохранены.";
                        error = false;
                    }
                    catch (Exception exception) { Fail(exception); }
                }
            }
            if (report != null && File.Exists(report.csvPath) && GUILayout.Button("Показать CSV отчёт"))
                EditorUtility.RevealInFinder(Path.GetFullPath(report.csvPath));
            GUILayout.Label("Исходники и эталоны сохраняются. Повторный запуск заменяет автоматические предложения только этого пака. Закрытие окна отменяет текущий проход.", EditorStyles.wordWrappedMiniLabel);
        }

        private void Tick()
        {
            if (session == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                session = null;
                feedback = "Проход отменён: начался Play Mode, импорт или компиляция. Прежний каталог сохранён.";
                Repaint();
                return;
            }
            try
            {
                if (session.Step())
                {
                    report = session.Save();
                    session = null;
                    feedback = "Просмотрено " + report.scanned + "; измерено " + report.measured + "; пропущено " + report.skipped
                        + "; предложено " + report.matched + " соответствий. Причины пропусков — в JSON отчёте. Теперь обновите стенд для визуальной проверки.";
                    error = false;
                }
            }
            catch (Exception exception) { session = null; Fail(exception); }
            Repaint();
        }
        private void Fail(Exception exception) { feedback = exception.Message; error = true; }
    }
}

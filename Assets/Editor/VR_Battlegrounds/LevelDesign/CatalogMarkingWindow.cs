using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ручная разметка выбранного объекта или составного укрытия эталоном LD_Alphabet.</summary>
    public sealed class CatalogMarkingWindow : EditorWindow
    {
        [SerializeField] private string blockGuid, title = "", notes = "";
        [SerializeField] private int theme;
        private static readonly string[] Themes = { "Индустриальная военная", "Вестерн", "Постапокалипсис", "Другая" };
        private string[] paths = Array.Empty<string>();
        private Vector2 scroll;
        private string feedback, selectionInfo;
        private bool error;

        [MenuItem("Tools/VR Battlegrounds/Level Design/Art Pass/Decorate/Candidates/LEGO Marking", false, 200)]
        public static void Open()
        {
            var window = GetWindow<CatalogMarkingWindow>("Кандидаты LEGO");
            window.minSize = new Vector2(480, 620);
            window.Show();
        }

        [MenuItem("GameObject/VR Battlegrounds/LEGO Candidate", false, 49)]
        private static void OpenForSelection() => Open();

        private void OnEnable()
        {
            Selection.selectionChanged += SelectionChanged;
            EditorApplication.projectChanged += ReloadBlocks;
            Undo.undoRedoPerformed += UndoChanged;
            ReloadBlocks();
            SelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= SelectionChanged;
            EditorApplication.projectChanged -= ReloadBlocks;
            Undo.undoRedoPerformed -= UndoChanged;
        }

        private void UndoChanged()
        {
            var ledger = CatalogMarkingService.Load();
            if (ledger != null) CatalogMarkingService.Save(ledger);
            Repaint();
        }

        private void ReloadBlocks() { paths = CatalogMarkingService.BlockPaths(); Repaint(); }

        private void SelectionChanged()
        {
            var roots = CatalogMarkingService.SelectionRoots(Selection.gameObjects);
            title = string.Join(" + ", roots.Select(g => g.name));
            try
            {
                var size = CatalogMarkingService.BoundsOf(roots).size;
                selectionInfo = roots.Length + " объектов → один кандидат\n" + Size(size);
            }
            catch (Exception e) { selectionInfo = e.Message; }
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Разметка объектов из обзорных сцен", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Выберите объект или несколько частей одного укрытия. Метка связывает их с эталоном; повторное назначение обновит её. Разные эталоны создают разные соответствия.", MessageType.Info);
            if (paths.Length == 0) { EditorGUILayout.HelpBox("Не найдены блоки LD_Alphabet.", MessageType.Error); return; }
            int index = Array.FindIndex(paths, p => AssetDatabase.AssetPathToGUID(p) == blockGuid);
            index = Mathf.Max(0, index);
            index = EditorGUILayout.Popup("Эталон LEGO", index, paths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray());
            blockGuid = AssetDatabase.AssetPathToGUID(paths[index]);
            var block = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
            if (block == null) { ReloadBlocks(); return; }
            GUILayout.Label(AssetCandidateReviewScene.ReferenceCaption(block, block.name), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            GUILayout.Label("Выбрано: " + (selectionInfo ?? "Нет выбора"), EditorStyles.wordWrappedLabel);
            title = EditorGUILayout.TextField("Название кандидата", title);
            theme = EditorGUILayout.Popup("Тема", theme, Themes);
            EditorGUILayout.LabelField("Заметка: поворот, сборка, что подогнать");
            notes = EditorGUILayout.TextArea(notes, GUILayout.MinHeight(42));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Пометить выбранное")) Action(() =>
                {
                    CatalogMarkingService.Mark(Selection.gameObjects, paths[index], title, Themes[theme], notes);
                    feedback = "Метка сохранена. Эталоны и источники не изменены.";
                });
                if (GUILayout.Button("Собрать отмеченные → копии, таблица и стенд")) Action(() =>
                {
                    var catalog = CatalogMarkingService.Export();
                    AssetCandidateReviewScene.RefreshMarkedCandidates();
                    feedback = "Собрано кандидатов: " + catalog.groups.Sum(g => g.candidates.Length) + ". Визуальные копии без коллайдеров; размеры сохранены.";
                });
            }
            bool gizmos = EditorPrefs.GetBool(CatalogMarkingService.GizmoPreference, true);
            bool show = EditorGUILayout.Toggle("Gizmo-метки", gizmos);
            if (show != gizmos) { EditorPrefs.SetBool(CatalogMarkingService.GizmoPreference, show); SceneView.RepaintAll(); }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Следующее демо")) Action(AssetPackDemoGallery.Next);
            if (GUILayout.Button("Таблица")) EditorUtility.RevealInFinder(CatalogMarkingService.TablePath);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, error ? MessageType.Error : MessageType.Info);
            EditorGUILayout.Space();
            var ledger = CatalogMarkingService.Load();
            EditorGUILayout.LabelField("Сохранённые метки: " + (ledger != null ? ledger.entries.Count : 0), EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (ledger != null)
            foreach (var entry in ledger.entries.ToArray())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(entry.title, EditorStyles.boldLabel);
                var path = AssetDatabase.GUIDToAssetPath(entry.blockGuid);
                GUILayout.Label(System.IO.Path.GetFileNameWithoutExtension(path) + " / " + entry.theme, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(entry.notes)) GUILayout.Label(entry.notes, EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Выбрать / изменить")) Action(() =>
                {
                    var objects = CatalogMarkingService.Resolve(entry);
                    if (objects.Any(g => g == null)) throw new InvalidOperationException("Откройте сцену с источником; часть выбранных объектов недоступна.");
                    Selection.objects = objects;
                    title = entry.title; notes = entry.notes; blockGuid = entry.blockGuid;
                    theme = Mathf.Max(0, Array.IndexOf(Themes, entry.theme));
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                });
                if (GUILayout.Button("Убрать метку", GUILayout.Width(110))) Action(() => CatalogMarkingService.Remove(entry));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void Action(System.Action action)
        {
            error = false;
            try { action(); }
            catch (Exception e) { error = true; feedback = e.Message; }
            Repaint();
        }

        private static string Size(Vector3 size) => $"{size.x:0.##} × {size.y:0.##} × {size.z:0.##} м";
    }
}

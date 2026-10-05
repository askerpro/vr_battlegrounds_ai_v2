using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Ручное сведение в Edit Mode: выбор оружия, два маркера, лучи и перспектива глаза.</summary>
    public sealed class ManualSightCalibrationWindow : EditorWindow
    {
        private WeaponInfo[] _weapons = Array.Empty<WeaponInfo>();
        private int _selection;
        private string _message = "";
        private Vector2 _scroll;
        private double _nextRepaint;

        [MenuItem("Tools/VR Battlegrounds/Weapons/Manual Sight Calibration")]
        public static void ShowWindow()
        {
            var window = GetWindow<ManualSightCalibrationWindow>("Ручное сведение");
            window.minSize = new Vector2(390, 430);
        }

        private void OnEnable()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath);
            _weapons = registry != null ? registry.Weapons.Where(w => w != null && w.WeaponPrefab != null).ToArray() : Array.Empty<WeaponInfo>();
            var session = ManualSightCalibrationAuthoring.Current();
            if (session != null && session.Weapon != null) _selection = Mathf.Max(0, Array.IndexOf(_weapons, session.Weapon));
            SceneView.duringSceneGui += DrawRays;
            EditorApplication.update += Refresh;
            Undo.undoRedoPerformed += HandleUndo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawRays;
            EditorApplication.update -= Refresh;
            Undo.undoRedoPerformed -= HandleUndo;
        }

        private void Refresh()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + .2;
            var session = ManualSightCalibrationAuthoring.Current();
            if (!EditorApplication.isPlayingOrWillChangePlaymode && session != null)
            {
                if (session.Instance != null)
                    foreach (var optic in session.Instance.GetComponentsInChildren<WeaponOpticView>(true))
                        if (optic.enabled && optic.gameObject.activeInHierarchy) optic.RefreshView();
                RepaintViews();
            }
        }
        private void RepaintViews() { Repaint(); SceneView.RepaintAll(); }
        private void HandleUndo()
        {
            var session = ManualSightCalibrationAuthoring.Current();
            if (session != null && session.Weapon != null) _selection = Mathf.Max(0, Array.IndexOf(_weapons, session.Weapon));
            RepaintViews();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Ручное сведение механических прицелов", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edit Mode. Зелёный луч — настоящий ShotSource; голубой — от целика через мушку. Двигайте детали обычными инструментами Unity.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                if (GUILayout.Button("Открыть стенд")) Run(() =>
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    var opened = ManualSightCalibrationAuthoring.Open();
                    if (opened.Instance == null && _weapons.Length > 0) ManualSightCalibrationAuthoring.Load(opened, _weapons[_selection]);
                });
                var session = ManualSightCalibrationAuthoring.Current();
                if (_weapons.Length > 0)
                {
                    int next = EditorGUILayout.Popup("Оружие", Mathf.Clamp(_selection, 0, _weapons.Length - 1), _weapons.Select(w => w.DisplayName).ToArray());
                    if (next != _selection)
                    {
                        int previous = _selection;
                        Run(() =>
                        {
                            if (session == null) { if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; session = ManualSightCalibrationAuthoring.Open(); }
                            if (!AllowSwitch(session)) return;
                            ManualSightCalibrationAuthoring.Load(session, _weapons[next], true); _selection = next;
                        });
                        if (_selection != next) _selection = previous;
                    }
                }
                if (session != null && session.Instance != null)
                {
                    EditorGUILayout.LabelField("Загружено", session.Weapon != null ? session.Weapon.DisplayName : session.Instance.name);
                    EditorGUILayout.LabelField("ShotSource", session.Muzzle != null ? session.Muzzle.name + " · тип " + session.ShotTypeIndex : "Не найден");
                    if (session.Instance.GetComponentInChildren<WeaponOpticView>(true) != null)
                        EditorGUILayout.HelpBox("Две точки сводят целик и мушку. Сетка оптики отображает свой текущий профиль; её фокус и stereo проверяются отдельно.", MessageType.Info);
                    EditorGUI.BeginChangeCheck();
                    float distance = EditorGUILayout.FloatField("Сведение, м", session.Distance);
                    if (EditorGUI.EndChangeCheck()) Run(() => ManualSightCalibrationAuthoring.SetDistance(session, distance));
                    EditorGUILayout.LabelField("Допустимо: больше 0 и до 20 м; по умолчанию 15 м.", EditorStyles.wordWrappedMiniLabel);
                    MarkerRow(session, SightAlignmentRole.Rear, "Целик · Rear");
                    MarkerRow(session, SightAlignmentRole.Front, "Мушка · Front");
                    EditorGUILayout.HelpBox("Для создания маркера выберите родительскую деталь оружия в Hierarchy. Empty появится в её начале координат — переместите его в точку фактического наведения. Также можно добавить Empty и компонент вручную.", MessageType.None);
                    if (session.TryIntersection(out _, out var offset, out var reason))
                    {
                        EditorGUILayout.LabelField("На плоскости мишени", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("X: " + offset.x.ToString("+0.00;-0.00;0.00") + " мм   Y: " + offset.y.ToString("+0.00;-0.00;0.00") + " мм");
                    }
                    else EditorGUILayout.HelpBox(reason, MessageType.Warning);
                    if (GUILayout.Button("Вид через прицел")) Run(() => ManualSightCalibrationAuthoring.ViewThroughSights(session));
                    if (GUILayout.Button("Показать оружие целиком")) { Selection.activeGameObject = session.Instance; SceneView.lastActiveSceneView?.FrameSelected(); }
                    if (GUILayout.Button("Показать мишень крупно")) { Selection.activeGameObject = session.Target.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
                    if (GUILayout.Button("Выровнять ShotSource на центр")) Run(() => { Undo.RecordObject(session.Placement, "Разместить оружие на стенде"); ManualSightCalibrationAuthoring.Align(session); });
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Сохранение в проверочную копию", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(session.OutputPath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(36));
                    EditorGUILayout.LabelField(ManualSightCalibrationAuthoring.HasEdits(session) ? "Есть несохранённые правки" : "Правки сохранены / оружие только загружено");
                    if (GUILayout.Button("Сохранить настройку прицела")) Run(() =>
                    {
                        ManualSightCalibrationAuthoring.Save(session);
                        EditorSceneManager.SaveScene(session.gameObject.scene);
                        _message = "Сохранены детали, маркеры и дистанция. Исходное оружие в реестре не заменено.";
                    });
                }
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private bool AllowSwitch(ManualSightCalibrationSession session)
        {
            if (!ManualSightCalibrationAuthoring.HasEdits(session)) return true;
            int choice = EditorUtility.DisplayDialogComplex("Несохранённое сведение", "Перед переключением сохраните настройку или отмените правки текущей рабочей копии.", "Сохранить", "Отмена", "Отменить правки");
            if (choice == 1) return false;
            if (choice == 0) ManualSightCalibrationAuthoring.Save(session);
            return true;
        }

        private void MarkerRow(ManualSightCalibrationSession session, SightAlignmentRole role, string label)
        {
            var marker = session.Instance.GetComponentsInChildren<SightAlignmentMarker>(true).FirstOrDefault(m => m.Role == role);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(label, marker, typeof(SightAlignmentMarker), true);
                if (marker != null)
                { if (GUILayout.Button("Выбрать", GUILayout.Width(80))) Selection.activeGameObject = marker.gameObject; }
                else if (GUILayout.Button("Создать", GUILayout.Width(80))) Run(() => ManualSightCalibrationAuthoring.AddMarker(session, Selection.activeTransform, role));
            }
        }

        private void Run(Action action)
        {
            try { _message = ""; action(); }
            catch (Exception exception) { _message = exception.Message; }
            RepaintViews();
        }

        private void DrawRays(SceneView view)
        {
            var session = ManualSightCalibrationAuthoring.Current();
            if (session == null || session.Muzzle == null || session.Target == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Color colour = Handles.color; var depth = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                var muzzle = session.Muzzle; Vector3 bore = muzzle.position + muzzle.forward * session.Distance;
                Handles.color = Color.green; Handles.DrawLine(muzzle.position, bore, 2); Mark(bore, muzzle.forward, .003f);
                if (session.TryIntersection(out var hit, out _, out _) && session.TryMarkers(out var rear, out var front, out _))
                {
                    Handles.color = Color.cyan; Handles.DrawLine(rear.transform.position, hit, 2);
                    Handles.DrawWireDisc(rear.transform.position, muzzle.forward, .001f);
                    Handles.DrawWireDisc(front.transform.position, muzzle.forward, .001f); Mark(hit, muzzle.forward, .006f);
                }
            }
            finally { Handles.color = colour; Handles.zTest = depth; }
            void Mark(Vector3 point, Vector3 normal, float radius) { Handles.DrawWireDisc(point, normal, radius); }
        }
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Одобрение композиции и независимые ручные оценки позиций.</summary>
    public sealed class ContactCatalogReviewWindow : EditorWindow
    {
        private ContactReviewDatabase _db;
        private ContactCompositionReview _entry;
        private GameObject _prefab;
        private Vector2 _scroll;
        private bool _dirty, _criteria;
        private int _position;
        private string _error, _newCriterion, _output, _measurementHash;
        private PositionImpactResult _measurement;
        private static readonly string[] Statuses = { "Черновик", "Нравится — в каталог", "Отклонить" };
        private static readonly string[] Scores = { "Не оценено", "1 — низко", "2", "3", "4", "5 — высоко" };

        [MenuItem("Tools/VR Battlegrounds/Level Design/Art Pass/Check/Contact Catalog Review", false, 200)]
        public static void Open() => GetWindow<ContactCatalogReviewWindow>("Оценка контактов");
        private static ContactCatalogReviewWindow embedded;
        /// <summary>Общая отрисовка внутри редактора блокаута; скрытый контейнер не показывает второе окно.</summary>
        public static void DrawEmbedded()
        {
            if(embedded==null)
            {
                embedded=CreateInstance<ContactCatalogReviewWindow>();embedded.hideFlags=HideFlags.HideAndDontSave;
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseEmbedded;
                EditorApplication.quitting += ReleaseEmbedded;
            }
            embedded.OnGUI();
        }
        private static void ReleaseEmbedded()
        {
            if(embedded!=null) DestroyImmediate(embedded);
            embedded=null;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseEmbedded;
            EditorApplication.quitting -= ReleaseEmbedded;
        }

        public static void ShowFor(GameObject prefab)
        {
            var window = GetWindow<ContactCatalogReviewWindow>("Оценка контактов");
            window.Select(prefab); window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(530, 520);
            try { _db = ContactCatalogReviews.Load(); _error = null; }
            catch (Exception e) { _error = e.Message; }
        }

        private void OnDisable() { if (_dirty) Save(); }

        private void Save()
        {
            if (_db == null) return;
            try { ContactCatalogReviews.Save(_db); _dirty = false; _error = null; }
            catch (Exception e) { _error = e.Message; GameLog.Error("Не удалось сохранить оценки контактов: " + e.Message); }
        }

        private void Select(GameObject prefab)
        {
            if (prefab == _prefab || _db == null) return;
            if (_dirty)
            {
                Save(); if (_dirty) return;
            }
            try
            {
                if (prefab != null)
                {
                    if (!PrefabUtility.IsPartOfPrefabAsset(prefab)) throw new ArgumentException("Нужен префаб из Project, не экземпляр сцены.");
                    ContactCatalogMeasurement.Markers(prefab);
                }
                _prefab = prefab; _position = 0; _measurement = null;
                _entry = prefab == null ? null : ContactCatalogReviews.Get(_db,
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
                _error = null;
            }
            catch (Exception e) { _error = e.Message; }
        }

        private void OnGUI()
        {
            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (_db == null) return;
            EditorGUILayout.LabelField("Каталог контактных композиций", EditorStyles.boldLabel);
            var selected = (GameObject)EditorGUILayout.ObjectField("Префаб композиции", _prefab, typeof(GameObject), false);
            if (selected != _prefab) Select(selected);
            if (AssetDatabase.IsValidFolder(ContactCatalogReviews.PrefabFolder))
            {
                var assets = AssetDatabase.FindAssets("t:Prefab", new[] { ContactCatalogReviews.PrefabFolder })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
                if (assets.Length != 0)
                {
                    int index = Array.IndexOf(assets, _prefab == null ? "" : AssetDatabase.GetAssetPath(_prefab));
                    int next = EditorGUILayout.Popup("Примеры", index,
                        assets.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray());
                    if (next >= 0 && next != index) Select(AssetDatabase.LoadAssetAtPath<GameObject>(assets[next]));
                }
            }
            if (_entry == null || _prefab == null) return;
            string path = AssetDatabase.GetAssetPath(_prefab);
            string hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool unsaved = stage != null && stage.assetPath == path && stage.scene.isDirty;
            bool stale = ContactCatalogReviews.IsStale(_entry, hash);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Открыть префаб")) AssetDatabase.OpenAsset(_prefab);
            if (GUILayout.Button(_dirty ? "Сохранить оценки *" : "Сохранить оценки")) Save();
            if (GUILayout.Button("Открыть стенд") && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ContactCatalogReviews.LabPath);
            EditorGUILayout.EndHorizontal();
            if (unsaved) EditorGUILayout.HelpBox("В Prefab Mode есть несохранённая геометрия. Сохраните префаб перед измерением/подтверждением.", MessageType.Warning);
            if (stale)
            {
                EditorGUILayout.HelpBox("Префаб или его зависимости изменились. Прежние оценки сохранены, но эта версия пока не входит в подтверждённый каталог.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(unsaved))
                    if (GUILayout.Button("Подтвердить оценки для текущей версии")) { _entry.reviewedHash = hash; _dirty = true; }
            }
            EditorGUILayout.LabelField(ContactCatalogReviews.IsCatalogMember(_entry, hash)
                ? "Статус: в каталоге; неоценённые критерии остаются неизвестными" : "Статус: кандидат / отклонён / требует повторного ревью", EditorStyles.wordWrappedLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            _entry.status = (ContactReviewStatus)EditorGUILayout.Popup("Вся композиция", (int)_entry.status, Statuses);
            EditorGUILayout.LabelField("Комментарий к композиции");
            _entry.notes = EditorGUILayout.TextArea(_entry.notes ?? "", GUILayout.MinHeight(42));
            Transform[] markers;
            try { markers = ContactCatalogMeasurement.Markers(_prefab); }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); EditorGUILayout.EndScrollView(); return; }
            EditorGUILayout.Space();
            _position = GUILayout.SelectionGrid(Mathf.Clamp(_position, 0, markers.Length - 1),
                markers.Select(m => "Позиция " + m.name).ToArray(), markers.Length);
            var review = ContactCatalogReviews.Position(_entry, markers[_position].name);
            EditorGUILayout.HelpBox("1 — низко, 5 — высоко. Высокая защита не означает хороший баланс. Оценивайте из позиции игрока всю группу укрытий.", MessageType.Info);
            foreach (var criterion in _db.criteria)
            {
                var score = ContactCatalogReviews.Score(review, criterion.id);
                score.value = EditorGUILayout.Popup(criterion.title, Mathf.Clamp(score.value, 0, 5), Scores);
            }
            EditorGUILayout.LabelField("Заметки о позиции, угрозах и позах");
            review.notes = EditorGUILayout.TextArea(review.notes ?? "", GUILayout.MinHeight(55));
            _criteria = EditorGUILayout.Foldout(_criteria, "Настроить критерии", true);
            if (_criteria)
            {
                foreach (var criterion in _db.criteria) criterion.title = EditorGUILayout.TextField(criterion.title);
                _newCriterion = EditorGUILayout.TextField("Новый критерий", _newCriterion);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newCriterion)))
                    if (GUILayout.Button("Добавить критерий"))
                    {
                        _db.criteria.Add(new ContactCriterion { id = Guid.NewGuid().ToString("N"), title = _newCriterion.Trim() });
                        _newCriterion = ""; _dirty = true;
                    }
            }
            if (EditorGUI.EndChangeCheck()) _dirty = true;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(unsaved || EditorApplication.isPlaying))
                if (GUILayout.Button("Измерить текущий префаб: позиции и взаимный импакт"))
                {
                    try { _measurement = ContactCatalogMeasurement.Measure(_prefab, out _output); _measurementHash = hash; _error = null; }
                    catch (Exception e) { _measurement = null; _error = e.Message; }
                }
            if (_measurement != null)
            {
                if (_measurementHash != hash) EditorGUILayout.HelpBox("Замер устарел после правки префаба. Измерьте заново.", MessageType.Warning);
                EditorGUILayout.LabelField(_measurement.complete ? "Все запрошенные сочетания поз измерены" : "Измерение неполное — это не нулевые значения", EditorStyles.boldLabel);
                foreach (string problem in _measurement.problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
                EditorGUILayout.HelpBox("Доли образцов тела, не шанс попадания. Связи союзников в 1 против 2 — геометрия, не боевой контакт. Совместный импакт двух противников не равен сумме строк.", MessageType.Info);
                foreach (var pair in _measurement.pairs.Where(p => p.from == review.id))
                    EditorGUILayout.LabelField($"{pair.from}/{pair.fromState} → {pair.to}/{pair.toState}\nОбзор {pair.visibleShare:P0}; огонь {pair.shotShare:P0}; открытость источника {pair.sourceExposure:P0}", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Показать JSON отчёта")) EditorUtility.RevealInFinder(_output);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}

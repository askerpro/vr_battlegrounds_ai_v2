using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Окно аудита: охват, запуск, список находок (серьёзные сверху), подробности и переход к ассету.</summary>
    public sealed class AssetAuditWindow : EditorWindow
    {
        private enum ScopeChoice { Folder, Selection, Scene }
        private enum SortChoice { Severity, Instances, Rule, Asset }

        private static readonly string[] ScopeLabels = { "Папка (пак)", "Выделение + зависимости", "Сцена" };
        private static readonly string[] SortLabels = { "Серьёзность", "Экземпляры", "Правило", "Ассет" };

        [SerializeField] private ScopeChoice _scope = ScopeChoice.Folder;
        [SerializeField] private DefaultAsset _folder;
        [SerializeField] private SceneAsset _scene;
        [SerializeField] private bool _showCritical = true;
        [SerializeField] private bool _showWarning = true;
        [SerializeField] private bool _showInfo = true;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private int _ruleFilter;
        [SerializeField] private SortChoice _sort = SortChoice.Severity;

        private AuditReport _report;
        private AuditFinding _selected;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        public static void Open() => GetWindow<AssetAuditWindow>("Asset Audit").Show();

        public static void ShowReport(AuditReport report)
        {
            AssetAuditWindow window = GetWindow<AssetAuditWindow>("Asset Audit");
            window._report = report;
            window._selected = null;
            window.Show();
        }

        private void OnGUI()
        {
            DrawScope();
            if (_report == null)
            {
                EditorGUILayout.HelpBox("Выберите охват и нажмите «Проверить». Аудитор только читает ассеты.", MessageType.Info);
                return;
            }
            DrawSummary();
            DrawFilters();
            List<AuditFinding> visible = Filtered().ToList();
            DrawList(visible);
            DrawDetails();
        }

        private void DrawScope()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _scope = (ScopeChoice)EditorGUILayout.Popup((int)_scope, ScopeLabels, EditorStyles.toolbarPopup, GUILayout.Width(170));
                switch (_scope)
                {
                    case ScopeChoice.Folder:
                        _folder = (DefaultAsset)EditorGUILayout.ObjectField(_folder, typeof(DefaultAsset), false, GUILayout.MinWidth(200));
                        break;
                    case ScopeChoice.Scene:
                        if (_scene == null)
                            _scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorSceneManager.GetActiveScene().path);
                        _scene = (SceneAsset)EditorGUILayout.ObjectField(_scene, typeof(SceneAsset), false, GUILayout.MinWidth(200));
                        break;
                    default:
                        GUILayout.Label(Selection.assetGUIDs.Length + " выделено", EditorStyles.miniLabel);
                        break;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Проверить", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    RunAudit();
                using (new EditorGUI.DisabledScope(_report == null || string.IsNullOrEmpty(_report.OutputPath)))
                {
                    if (GUILayout.Button("JSON", EditorStyles.toolbarButton, GUILayout.Width(50)))
                        EditorUtility.RevealInFinder(Path.GetFullPath(_report.OutputPath));
                }
            }
        }

        private void RunAudit()
        {
            AuditRequest request;
            switch (_scope)
            {
                case ScopeChoice.Folder:
                    string folder = _folder != null ? AssetDatabase.GetAssetPath(_folder) : null;
                    if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                    {
                        ShowNotification(new GUIContent("Укажите папку"));
                        return;
                    }
                    request = AuditRequest.Folder(folder);
                    break;
                case ScopeChoice.Scene:
                    string scene = _scene != null ? AssetDatabase.GetAssetPath(_scene) : null;
                    if (string.IsNullOrEmpty(scene))
                    {
                        ShowNotification(new GUIContent("Укажите сцену"));
                        return;
                    }
                    request = AuditRequest.Scene(scene);
                    break;
                default:
                    string[] paths = Selection.assetGUIDs.Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                    if (paths.Length == 0)
                    {
                        ShowNotification(new GUIContent("Выделите ассеты в Project"));
                        return;
                    }
                    request = AuditRequest.Assets(paths);
                    break;
            }
            request.ShowProgress = true;
            _report = AssetAuditRunner.Run(request);
            _selected = null;
            GUIUtility.ExitGUI();
        }

        private void DrawSummary()
        {
            string text = _report.Request.Describe() + " — критично " + _report.Count(AuditSeverity.Critical) +
                          ", предупреждений " + _report.Count(AuditSeverity.Warning) + ", инфо " + _report.Count(AuditSeverity.Info) +
                          ", ассетов " + _report.Inventory.TotalAssets;
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            if (_report.Errors.Count > 0)
                EditorGUILayout.HelpBox("Ошибки правил/охвата: " + _report.Errors.Count + ". Первая: " + _report.Errors[0], MessageType.Warning);
        }

        private void DrawFilters()
        {
            string[] rules = new[] { "Все правила" }.Concat(_report.Rules.Select(r => r.Id)).ToArray();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _showCritical = GUILayout.Toggle(_showCritical, "Критично", EditorStyles.toolbarButton);
                _showWarning = GUILayout.Toggle(_showWarning, "Предупр.", EditorStyles.toolbarButton);
                _showInfo = GUILayout.Toggle(_showInfo, "Инфо", EditorStyles.toolbarButton);
                _ruleFilter = Mathf.Clamp(EditorGUILayout.Popup(_ruleFilter, rules, EditorStyles.toolbarPopup, GUILayout.Width(190)), 0, rules.Length - 1);
                _sort = (SortChoice)EditorGUILayout.Popup((int)_sort, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(100));
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            }
        }

        private IEnumerable<AuditFinding> Filtered()
        {
            string rule = _ruleFilter > 0 && _ruleFilter <= _report.Rules.Count ? _report.Rules[_ruleFilter - 1].Id : null;
            IEnumerable<AuditFinding> query = _report.Findings.Where(f =>
                (f.Severity != AuditSeverity.Critical || _showCritical) &&
                (f.Severity != AuditSeverity.Warning || _showWarning) &&
                (f.Severity != AuditSeverity.Info || _showInfo) &&
                (rule == null || f.RuleId == rule) &&
                (string.IsNullOrEmpty(_search) ||
                 f.Asset.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 f.Message.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 f.Items.Any(i => i.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)));
            switch (_sort)
            {
                case SortChoice.Instances:
                    return query.OrderByDescending(f => f.Count).ThenByDescending(f => f.Severity);
                case SortChoice.Rule:
                    return query.OrderBy(f => f.RuleId).ThenByDescending(f => f.Severity);
                case SortChoice.Asset:
                    return query.OrderBy(f => f.Asset).ThenByDescending(f => f.Severity);
                default:
                    return query; // отчёт уже отсортирован по серьёзности
            }
        }

        private void DrawList(List<AuditFinding> visible)
        {
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            foreach (AuditFinding finding in visible)
            {
                Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                if (finding == _selected)
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                var severityRect = new Rect(row.x, row.y, 70, row.height);
                var ruleRect = new Rect(severityRect.xMax, row.y, 190, row.height);
                var countRect = new Rect(ruleRect.xMax, row.y, 45, row.height);
                var textRect = new Rect(countRect.xMax, row.y, row.width - countRect.xMax + row.x, row.height);
                EditorGUI.DrawRect(new Rect(row.x, row.y + 2, 4, row.height - 4), SeverityColor(finding.Severity));
                GUI.Label(new Rect(severityRect.x + 8, row.y, severityRect.width - 8, row.height), SeverityLabel(finding.Severity), EditorStyles.miniBoldLabel);
                GUI.Label(ruleRect, finding.RuleId, EditorStyles.miniLabel);
                GUI.Label(countRect, finding.Count > 1 ? "×" + finding.Count : string.Empty, EditorStyles.miniLabel);
                GUI.Label(textRect, ShortAsset(finding.Asset) + " — " + finding.Message, EditorStyles.miniLabel);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    _selected = finding;
                    _detailScroll = Vector2.zero;
                    if (Event.current.clickCount == 2)
                        Ping(finding.Asset);
                    Event.current.Use();
                    Repaint();
                }
            }
            if (visible.Count == 0)
                EditorGUILayout.LabelField("Нет находок под фильтром.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawDetails()
        {
            if (_selected == null)
                return;
            AuditRule rule = _selected.Rule;
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, EditorStyles.helpBox, GUILayout.Height(Mathf.Min(260, position.height * 0.45f)));
            EditorGUILayout.LabelField(SeverityLabel(_selected.Severity) + " · " + rule.Id + " — " + rule.Title, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(_selected.Asset, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Показать", EditorStyles.miniButton, GUILayout.Width(70)))
                    Ping(_selected.Asset);
            }
            EditorGUILayout.LabelField(_selected.Message, EditorStyles.wordWrappedLabel);
            if (_selected.Metrics.Count > 0)
                EditorGUILayout.LabelField("Метрики: " + string.Join(", ", _selected.Metrics.Select(m => m.Name + "=" + m.Value)), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Почему плохо на Quest", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(rule.WhyBadOnQuest, EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(_selected.FixHint))
            {
                EditorGUILayout.LabelField("Как исправить", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(_selected.FixHint, EditorStyles.wordWrappedMiniLabel);
            }
            AuditFixer fixer = AuditFixerCatalog.ForRule(rule.Id);
            if (fixer != null)
            {
                int count = _report.Findings.Count(f => f.RuleId == rule.Id);
                if (GUILayout.Button("Исправить: " + fixer.Title + " (находок правила: " + count + ")", GUILayout.Height(22)))
                    RunFixer(fixer);
            }
            EditorGUILayout.LabelField("Доказательство", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(rule.Evidence, EditorStyles.wordWrappedMiniLabel);
            if (_selected.Items.Count > 0)
            {
                EditorGUILayout.LabelField("Где (" + _selected.Count + ", показано " + _selected.Items.Count + ")", EditorStyles.miniBoldLabel);
                foreach (string item in _selected.Items)
                {
                    if (GUILayout.Button(item, EditorStyles.linkLabel))
                        Ping(item);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Dry-run по всем находкам правила в отчёте → подтверждение со списком → применение → повторный аудит.</summary>
        private void RunFixer(AuditFixer fixer)
        {
            List<AuditFixAction> plan = fixer.Plan(_report.Findings.Where(f => f.RuleId == fixer.RuleId));
            if (plan.Count == 0)
            {
                ShowNotification(new GUIContent("Нечего исправлять"));
                return;
            }
            const int shown = 15;
            string list = string.Join("\n", plan.Take(shown).Select(a => a.ToString()));
            if (plan.Count > shown)
                list += "\n… и ещё " + (plan.Count - shown);
            if (!EditorUtility.DisplayDialog(fixer.Title, fixer.WhatChanges + "\n\nАссетов: " + plan.Count + "\n\n" + list, "Применить", "Отмена"))
                return;
            fixer.Apply(plan);
            int failed = plan.Count(a => !a.Applied);
            if (failed > 0)
                EditorUtility.DisplayDialog(fixer.Title, "Не применено: " + failed + "\n" + string.Join("\n", plan.Where(a => !a.Applied).Take(shown).Select(a => a.ToString())), "OK");
            AuditRequest again = _report.Request;
            again.ShowProgress = true;
            _report = AssetAuditRunner.Run(again);
            _selected = null;
            GUIUtility.ExitGUI();
        }

        private static void Ping(string pathOrObject)
        {
            if (string.IsNullOrEmpty(pathOrObject))
                return;
            int hash = pathOrObject.IndexOf('#');
            string assetPath = hash >= 0 ? pathOrObject.Substring(0, hash) : pathOrObject;
            Object target = null;
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
            {
                target = hash >= 0
                    ? AssetDatabase.LoadAllAssetsAtPath(assetPath).FirstOrDefault(o => o != null && o.name == pathOrObject.Substring(hash + 1))
                    : null;
                if (target == null)
                    target = AssetDatabase.LoadMainAssetAtPath(assetPath);
            }
            else
            {
                target = GameObject.Find("/" + pathOrObject);
            }
            if (target == null)
                return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private static string ShortAsset(string asset)
        {
            if (string.IsNullOrEmpty(asset))
                return "(сцена)";
            int slash = asset.LastIndexOf('/');
            return slash >= 0 ? asset.Substring(slash + 1) : asset;
        }

        private static string SeverityLabel(AuditSeverity severity) =>
            severity == AuditSeverity.Critical ? "Критично" : severity == AuditSeverity.Warning ? "Предупр." : "Инфо";

        private static Color SeverityColor(AuditSeverity severity) =>
            severity == AuditSeverity.Critical ? new Color(0.9f, 0.25f, 0.2f)
            : severity == AuditSeverity.Warning ? new Color(0.95f, 0.7f, 0.15f)
            : new Color(0.45f, 0.65f, 0.9f);
    }
}

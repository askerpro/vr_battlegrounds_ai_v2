using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>
    /// Меню и batchmode-вход аудита ассетов.
    /// Batchmode: <c>Unity -batchmode -quit -projectPath . -executeMethod
    /// VrBattlegrounds.Editor.Performance.AssetAudit.AssetAuditMenu.RunFromCommandLine
    /// -auditFolder Assets/Art/... [-auditFolder ...] | -auditScene Assets/Scenes/Lobby.unity | -auditAsset path
    /// [-auditOut reports/audit.json] [-auditFailOn critical|warning|none] [-auditDisable rule.id,...]
    /// [-auditThreshold rule.id.Field=value ...]</c>. Код выхода: 0 — нет находок уровня FailOn, 1 — есть, 2 — сбой.
    /// </summary>
    public static class AssetAuditMenu
    {
        private const string Root = "Tools/VR Battlegrounds/Performance/Asset Audit/";

        [MenuItem(Root + "Open Window", priority = 0)]
        private static void OpenWindow() => AssetAuditWindow.Open();

        [MenuItem(Root + "Audit Selection", priority = 20)]
        private static void AuditSelection()
        {
            string[] paths = Selection.assetGUIDs.Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (paths.Length == 0)
            {
                GameLog.Perf.Warning("[AssetAudit] выделите в Project папку или ассеты");
                return;
            }
            AuditRequest request = paths.All(p => AssetDatabase.IsValidFolder(p)) ? AuditRequest.Folder(paths) : AuditRequest.Assets(paths);
            request.ShowProgress = true;
            AssetAuditWindow.ShowReport(AssetAuditRunner.Run(request));
        }

        [MenuItem(Root + "Audit Active Scene", priority = 21)]
        private static void AuditActiveScene()
        {
            string scenePath = EditorSceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath))
            {
                GameLog.Perf.Warning("[AssetAudit] активная сцена не сохранена — нечего проверять");
                return;
            }
            AuditRequest request = AuditRequest.Scene(scenePath);
            request.ShowProgress = true;
            AssetAuditWindow.ShowReport(AssetAuditRunner.Run(request));
        }

        [MenuItem(Root + "Fix Android ASTC in Selected Folders", priority = 40)]
        private static void FixAndroidAstc()
        {
            string[] folders = Selection.assetGUIDs.Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(p => AssetDatabase.IsValidFolder(p)).ToArray();
            if (folders.Length == 0)
            {
                GameLog.Perf.Warning("[AssetAudit] выделите в Project папки паков");
                return;
            }
            const string ruleId = "texture.android-format";
            AuditFixer fixer = AuditFixerCatalog.ForRule(ruleId);
            List<AuditFixAction> plan = AuditFixerCatalog.PlanForFolders(ruleId, folders);
            GameLog.Perf.Info("[AssetAudit] dry-run " + fixer.Title + ": " + plan.Count + "\n" + string.Join("\n", plan.Select(a => a.ToString())));
            if (plan.Count == 0)
                return;
            string list = string.Join("\n", plan.Take(15).Select(a => a.ToString())) + (plan.Count > 15 ? "\n… и ещё " + (plan.Count - 15) : string.Empty);
            if (!EditorUtility.DisplayDialog(fixer.Title, fixer.WhatChanges + "\n\nАссетов: " + plan.Count + "\n\n" + list, "Применить", "Отмена"))
                return;
            fixer.Apply(plan);
        }

        /// <summary>Вход для <c>-executeMethod</c>; аргументы — см. описание класса.</summary>
        public static void RunFromCommandLine()
        {
            int exitCode;
            try
            {
                exitCode = RunWithArguments(Environment.GetCommandLineArgs());
            }
            catch (Exception exception)
            {
                GameLog.Error("[AssetAudit] сбой batchmode: " + exception);
                exitCode = 2;
            }
            if (UnityEngine.Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }

        internal static int RunWithArguments(IReadOnlyList<string> args)
        {
            var folders = new List<string>();
            var assets = new List<string>();
            string scene = null;
            string output = null;
            string failOn = "critical";
            var disabled = new List<string>();
            var thresholds = new Dictionary<string, string>();

            for (int i = 0; i < args.Count - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i])
                {
                    case "-auditFolder": folders.Add(value); break;
                    case "-auditAsset": assets.Add(value); break;
                    case "-auditScene": scene = value; break;
                    case "-auditOut": output = value; break;
                    case "-auditFailOn": failOn = value.ToLowerInvariant(); break;
                    case "-auditDisable": disabled.AddRange(value.Split(',').Select(s => s.Trim())); break;
                    case "-auditThreshold":
                        int eq = value.IndexOf('=');
                        if (eq > 0)
                            thresholds[value.Substring(0, eq)] = value.Substring(eq + 1);
                        break;
                }
            }

            AuditRequest request;
            if (!string.IsNullOrEmpty(scene))
                request = AuditRequest.Scene(scene);
            else if (folders.Count > 0)
                request = AuditRequest.Folder(folders.ToArray());
            else if (assets.Count > 0)
                request = AuditRequest.Assets(assets.ToArray());
            else
            {
                GameLog.Error("[AssetAudit] нужен -auditFolder, -auditAsset или -auditScene");
                return 2;
            }

            request.OutputPath = output;
            foreach (string id in disabled.Where(s => s.Length > 0))
                request.DisabledRules.Add(id);
            foreach (KeyValuePair<string, string> pair in thresholds)
                request.ThresholdOverrides[pair.Key] = pair.Value;

            AuditReport report = AssetAuditRunner.Run(request);
            switch (failOn)
            {
                case "none":
                    return 0;
                case "warning":
                    return report.Count(AuditSeverity.Critical) + report.Count(AuditSeverity.Warning) > 0 ? 1 : 0;
                default:
                    return report.Count(AuditSeverity.Critical) > 0 ? 1 : 0;
            }
        }
    }
}

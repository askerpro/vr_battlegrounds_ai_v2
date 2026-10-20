using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>
    /// Точка входа аудита (окно, меню, MCP execute_code, batchmode). Только читает ассеты: ничего не сохраняет,
    /// не меняет импорт. Сцену, которой нет в редакторе, открывает аддитивно и закрывает без сохранения.
    /// MCP: <c>AssetAuditRunner.RunFolder("Assets/...").Summary()</c> или <c>RunScene("Assets/Scenes/Lobby.unity")</c>.
    /// </summary>
    public static class AssetAuditRunner
    {
        public const string DefaultOutputFolder = "Logs/AssetAudit";

        public static AuditReport RunFolder(params string[] folders) => Run(AuditRequest.Folder(folders));

        public static AuditReport RunScene(string scenePath) => Run(AuditRequest.Scene(scenePath));

        public static AuditReport RunAssets(params string[] assetPaths) => Run(AuditRequest.Assets(assetPaths));

        public static AuditReport Run(AuditRequest request)
        {
            DateTime started = DateTime.UtcNow;
            Stopwatch watch = Stopwatch.StartNew();
            var errors = new List<string>();
            List<AuditRule> rules = AuditRuleCatalog.CreateRules(request, errors);
            AuditInventory inventory = AuditInventory.Collect(request);
            errors.AddRange(inventory.Problems);
            var context = new AuditContext(request) { ScenePath = inventory.ScenePath };
            HashSet<UnityEngine.Object> dirtyBefore = DirtyAssets();

            try
            {
                CheckMaterials(rules, inventory, context, errors, request.ShowProgress);
                CheckTextures(rules, inventory, context, errors, request.ShowProgress);
                CheckShaders(rules, inventory, context, errors);
                CheckGameObjectAssets(rules, inventory, context, errors, request.ShowProgress);
                GuardDirty(dirtyBefore, "проверки ассетов", request, errors);
                if (!string.IsNullOrEmpty(inventory.ScenePath))
                {
                    CheckScene(rules, inventory.ScenePath, context, errors);
                    GuardDirty(dirtyBefore, "открытие сцены " + inventory.ScenePath, request, errors);
                }
                foreach (IAuditFinishCheck rule in rules.OfType<IAuditFinishCheck>())
                    Guard((AuditRule)rule, "итог", errors, () => rule.Finish(context));
            }
            finally
            {
                if (request.ShowProgress)
                    EditorUtility.ClearProgressBar();
            }

            var report = new AuditReport(request, rules, Merge(context.RawFindings), inventory, errors, started,
                watch.Elapsed.TotalSeconds);
            if (request.OutputPath == null || request.OutputPath.Length > 0)
                report.WriteJson(request.OutputPath ?? DefaultOutputPath(request, started));
            GameLog.Perf.Info("[AssetAudit] " + report.Summary(5));
            return report;
        }

        private static void CheckMaterials(List<AuditRule> rules, AuditInventory inventory, AuditContext context,
            List<string> errors, bool progress)
        {
            List<IMaterialAuditCheck> checks = rules.OfType<IMaterialAuditCheck>().ToList();
            int index = 0;
            var seen = new HashSet<Material>();
            foreach (string path in inventory.MaterialSources)
            {
                if (progress)
                    EditorUtility.DisplayProgressBar("Asset Audit", "Материалы: " + path, Fraction(index++, inventory.MaterialSources.Count));
                foreach (Material material in LoadMaterials(path))
                {
                    if (!seen.Add(material))
                        continue;
                    context.RegisterMaterialUse(material, path);
                    foreach (IMaterialAuditCheck check in checks)
                        Guard((AuditRule)check, path, errors, () => check.CheckMaterial(material, path, context));
                }
            }
        }

        private static IEnumerable<Material> LoadMaterials(string path)
        {
            if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                return material != null ? new[] { material } : Array.Empty<Material>();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>();
        }

        private static void CheckTextures(List<AuditRule> rules, AuditInventory inventory, AuditContext context,
            List<string> errors, bool progress)
        {
            List<ITextureAuditCheck> checks = rules.OfType<ITextureAuditCheck>().ToList();
            if (checks.Count == 0)
                return;
            int index = 0;
            foreach (string path in inventory.Textures)
            {
                if (progress)
                    EditorUtility.DisplayProgressBar("Asset Audit", "Текстуры: " + path, Fraction(index++, inventory.Textures.Count));
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (importer == null)
                    continue;
                foreach (ITextureAuditCheck check in checks)
                    Guard((AuditRule)check, path, errors, () => check.CheckTexture(importer, texture, path, context));
            }
        }

        private static void CheckShaders(List<AuditRule> rules, AuditInventory inventory, AuditContext context, List<string> errors)
        {
            List<IShaderAuditCheck> checks = rules.OfType<IShaderAuditCheck>().ToList();
            foreach (string path in inventory.Shaders)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    continue;
                foreach (IShaderAuditCheck check in checks)
                    Guard((AuditRule)check, path, errors, () => check.CheckShader(shader, path, context));
            }
        }

        private static void CheckGameObjectAssets(List<AuditRule> rules, AuditInventory inventory, AuditContext context,
            List<string> errors, bool progress)
        {
            List<IGameObjectAuditCheck> checks = rules.OfType<IGameObjectAuditCheck>().ToList();
            if (checks.Count == 0)
                return;
            int index = 0;
            foreach (string path in inventory.GameObjects)
            {
                if (progress)
                    EditorUtility.DisplayProgressBar("Asset Audit", "Префабы и модели: " + path, Fraction(index++, inventory.GameObjects.Count));
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null)
                    Walk(root.transform, string.Empty, path, false, checks, context, errors);
            }
        }

        private static void CheckScene(List<AuditRule> rules, string scenePath, AuditContext context, List<string> errors)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            try
            {
                List<IGameObjectAuditCheck> objectChecks = rules.OfType<IGameObjectAuditCheck>().ToList();
                foreach (GameObject root in scene.GetRootGameObjects())
                    Walk(root.transform, string.Empty, scenePath, true, objectChecks, context, errors);
                foreach (ISceneAuditCheck check in rules.OfType<ISceneAuditCheck>())
                    Guard((AuditRule)check, scenePath, errors, () => check.CheckScene(scene, scenePath, context));
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Walk(Transform transform, string parentPath, string assetPath, bool inScene,
            List<IGameObjectAuditCheck> checks, AuditContext context, List<string> errors)
        {
            GameObject gameObject = transform.gameObject;
            // В сцене выключенное не рисуется; в префабе проверяем всё — его могут включить.
            if (inScene && !gameObject.activeSelf)
                return;
            string path = parentPath.Length == 0 ? gameObject.name : parentPath + "/" + gameObject.name;
            string prefabPath = inScene ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) : assetPath;
            var source = new AuditObjectSource(assetPath, path, inScene, prefabPath);
            foreach (IGameObjectAuditCheck check in checks)
                Guard((AuditRule)check, assetPath + ":" + path, errors, () => check.CheckGameObject(gameObject, source, context));
            for (int i = 0; i < transform.childCount; i++)
                Walk(transform.GetChild(i), path, assetPath, inScene, checks, context, errors);
        }

        /// <summary>Ассеты (не объекты сцен), помеченные несохранёнными.</summary>
        private static HashSet<UnityEngine.Object> DirtyAssets()
        {
            var dirty = new HashSet<UnityEngine.Object>();
            foreach (UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (asset != null && EditorUtility.IsDirty(asset) && AssetDatabase.Contains(asset))
                    dirty.Add(asset);
            }
            return dirty;
        }

        /// <summary>
        /// Аудитор только читает, но открытие сцены запускает [ExecuteAlways]/OnValidate её скриптов, а загрузка —
        /// апгрейдеры; они могут пометить ассеты несохранёнными (2026-10-20: MI_MiscProps_01.mat после Lobby).
        /// Новые «грязные» ассеты попадают в ошибки отчёта и, по <see cref="AuditRequest.RevertNewlyDirtied"/>,
        /// выгружаются из памяти — следующий доступ перечитает их с диска. Бывшие грязными до прогона не трогаются.
        /// </summary>
        private static void GuardDirty(HashSet<UnityEngine.Object> before, string phase, AuditRequest request, List<string> errors)
        {
            var reverted = new List<string>();
            var kept = new List<string>();
            foreach (UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (asset == null || !EditorUtility.IsDirty(asset) || !AssetDatabase.Contains(asset) ||
                    before.Contains(asset))
                    continue;
                string label = asset.GetType().Name + " " + AuditAssetUtil.AssetLabel(asset);
                bool unloadable = !(asset is GameObject) && !(asset is Component) && !(asset is AssetImporter);
                if (request.RevertNewlyDirtied && unloadable)
                {
                    Resources.UnloadAsset(asset);
                    reverted.Add(label);
                }
                else
                {
                    kept.Add(label);
                }
            }
            if (reverted.Count > 0)
                errors.Add("после фазы «" + phase + "» ассеты стали несохранёнными, выгружены без записи: " + string.Join("; ", reverted));
            if (kept.Count > 0)
                errors.Add("после фазы «" + phase + "» остались несохранённые ассеты (сохранять не нужно, закрыть без записи): " + string.Join("; ", kept));
        }

        private static void Guard(AuditRule rule, string where, List<string> errors, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                errors.Add(rule.Id + " @ " + where + ": " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        /// <summary>Слияние по ключу группы; порядок: серьёзность ↓, число экземпляров ↓, правило, ассет.</summary>
        private static List<AuditFinding> Merge(IReadOnlyList<AuditFinding> raw)
        {
            var merged = new Dictionary<string, AuditFinding>();
            var order = new List<AuditFinding>();
            foreach (AuditFinding finding in raw)
            {
                string key = finding.EffectiveGroupKey;
                if (merged.TryGetValue(key, out AuditFinding existing))
                {
                    existing.MergeFrom(finding);
                    continue;
                }
                merged[key] = finding;
                order.Add(finding);
            }
            return order
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.Count)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ThenBy(f => f.Asset, StringComparer.Ordinal)
                .ToList();
        }

        private static string DefaultOutputPath(AuditRequest request, DateTime started)
        {
            string label = request.Paths.Count > 0 ? Path.GetFileNameWithoutExtension(request.Paths[0]) : "empty";
            if (request.Paths.Count > 1)
                label += "+" + (request.Paths.Count - 1);
            foreach (char invalid in Path.GetInvalidFileNameChars())
                label = label.Replace(invalid, '_');
            return DefaultOutputFolder + "/" + request.Kind.ToString().ToLowerInvariant() + "-" + label + "-" +
                   started.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".json";
        }

        private static float Fraction(int index, int total) => total <= 0 ? 1f : (float)index / total;
    }
}

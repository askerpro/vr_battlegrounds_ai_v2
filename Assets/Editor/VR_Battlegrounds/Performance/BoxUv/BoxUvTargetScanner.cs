using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Performance.AssetAudit;
using VrBattlegrounds.Editor.Performance.AssetAudit.Fixers;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// Находит сабмеши моделей, которые рисуются материалами с box-UV (<see cref="Materials"/>),
    /// и пишет <see cref="BoxUvTargets"/>. Источники: префабы проекта и сцены из списка (по умолчанию Lobby).
    /// Сабмеш, который где-то рисуется другим материалом, не переразворачивается — иначе изменится чужая картинка.
    /// Сцены, ссылающиеся на ту же модель, но не просканированные, попадают в <c>unverifiedScenes</c>.
    /// </summary>
    public static class BoxUvTargetScanner
    {
        public static readonly string[] DefaultScenes = { "Assets/Scenes/Lobby.unity" };

        /// <summary>
        /// Материалы Lobby на трипланарном S_VPWorldAligned, чьи сабмеши переразворачиваются box-UV (UV в метрах; аудит UV
        /// 2026-10-20, reports/uv-audit-20261020). После переразвёртки их переводит на Baked Lit фиксер аудитора
        /// (<see cref="BakedLitMaterialFixer.PlanWorldAligned"/>) — единственный писатель материалов (QPM-SHADER-007).
        /// WornGround (пол шатра) — материал проекта: его пишет LobbyDecorationAuthoring.ApplyGroundMaterials.
        /// </summary>
        public static readonly string[] Materials =
        {
            "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Concrete_01.mat",
            "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Tiles_ObjectSpace.mat",
        };

        [MenuItem("Tools/VR Battlegrounds/Performance/Box UV/Scan (Dry Run)")]
        private static void DryRunMenu() => GameLog.Perf.Info("[BoxUv] " + JsonConvert.SerializeObject(Summary(Scan(DefaultScenes))));

        [MenuItem("Tools/VR Battlegrounds/Performance/Box UV/Scan And Apply")]
        private static void ApplyMenu() => GameLog.Perf.Info("[BoxUv] " + JsonConvert.SerializeObject(Apply(DefaultScenes)));

        /// <summary>Сканировать, записать конфиг, переимпортировать затронутые модели, перевести материалы на Baked Lit.</summary>
        public static Dictionary<string, object> Apply(params string[] scenes)
        {
            BoxUvTargets config = Scan(scenes);
            config.Save();
            AssetDatabase.ImportAsset(BoxUvTargets.FilePath, ImportAssetOptions.ForceUpdate);
            List<string> reimported = BoxUvModelPostprocessor.ReimportTargets();
            Dictionary<string, object> materials = ConvertMaterials();
            Dictionary<string, object> report = Summary(config);
            report["reimportedModels"] = reimported.Count;
            report["materials"] = materials;
            return report;
        }

        /// <summary>
        /// S_VPWorldAligned → Baked Lit через фиксер аудитора: UV box-UV в метрах, поэтому метры на единицу UV = 1,
        /// тайлинг = 1 / _MainTile. Уже переведённые материалы попадают в skipped.
        /// </summary>
        public static Dictionary<string, object> ConvertMaterials()
        {
            var fixer = new BakedLitMaterialFixer();
            List<AuditFixAction> plan = fixer.PlanWorldAligned(Materials.Select(p => new KeyValuePair<string, float>(p, 1f)));
            fixer.Apply(plan);
            return new Dictionary<string, object>
            {
                ["converted"] = plan.Where(a => a.Applied).Select(a => a.ToString()).ToList(),
                ["failures"] = plan.Where(a => !a.Applied).Select(a => a.ToString()).ToList(),
                ["skipped"] = fixer.LastSkipped.ToList(),
                ["passed"] = plan.All(a => a.Applied),
            };
        }

        public static Dictionary<string, object> Summary(BoxUvTargets config) => new Dictionary<string, object>
        {
            ["targets"] = config.Targets.Count,
            ["models"] = config.Targets.Select(t => t.Model).Distinct().Count(),
            ["skipped"] = config.SkippedMeshes,
            ["unverifiedScenes"] = config.UnverifiedScenes,
        };

        public static BoxUvTargets Scan(params string[] scenes)
        {
            var targetMaterials = new HashSet<Material>();
            var config = new BoxUvTargets();
            foreach (string path in Materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new InvalidOperationException("Нет материала " + path);
                targetMaterials.Add(material);
                config.Materials.Add(path);
            }
            config.Scenes.AddRange(scenes);

            // (модель, меш, сабмеш) → материалы, которыми он рисуется.
            var usage = new Dictionary<(string model, string mesh, int sub), HashSet<string>>();
            var skipped = new Dictionary<string, BoxUvTargets.Skipped>();

            string[] materialGuids = config.Materials.Select(AssetDatabase.AssetPathToGUID).ToArray();
            List<string> prefabs = AssetFiles(".prefab");
            foreach (string prefab in FilesContaining(prefabs, materialGuids))
                Collect(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), targetMaterials, usage, skipped, null);
            ScanScenes(scenes, root => Collect(root, targetMaterials, usage, skipped, null));

            // Те же модели в других префабах (с любыми материалами): иначе не увидим чужие материалы на сабмеше.
            var candidateModels = new HashSet<string>(usage.Keys.Select(k => k.model), StringComparer.OrdinalIgnoreCase);
            string[] modelGuids = candidateModels.Select(AssetDatabase.AssetPathToGUID).ToArray();
            foreach (string prefab in FilesContaining(prefabs, modelGuids))
                Collect(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), targetMaterials, usage, skipped, candidateModels);
            ScanScenes(scenes, root => Collect(root, targetMaterials, usage, skipped, candidateModels));
            var scanned = new HashSet<string>(scenes, StringComparer.OrdinalIgnoreCase);
            config.UnverifiedScenes.AddRange(FilesContaining(AssetFiles(".unity"), modelGuids).Where(s => !scanned.Contains(s)));

            var targetNames = new HashSet<string>(targetMaterials.Select(m => m.name), StringComparer.Ordinal);
            foreach (var group in usage.GroupBy(u => (u.Key.model, u.Key.mesh)).OrderBy(g => g.Key.model).ThenBy(g => g.Key.mesh))
            {
                var target = new BoxUvTargets.Target { Model = group.Key.model, Mesh = group.Key.mesh };
                foreach (var slot in group.OrderBy(s => s.Key.sub))
                {
                    var foreign = slot.Value.Where(name => !targetNames.Contains(name)).ToList();
                    bool usesTarget = slot.Value.Count > foreign.Count;
                    if (!usesTarget) continue;
                    if (foreign.Count > 0)
                    {
                        skipped[group.Key.model + "|" + group.Key.mesh + "|" + slot.Key.sub] = new BoxUvTargets.Skipped
                        {
                            Model = group.Key.model, Mesh = group.Key.mesh,
                            Reason = "submesh " + slot.Key.sub + " also drawn with " + string.Join(", ", foreign),
                        };
                        continue;
                    }
                    target.Submeshes.Add(slot.Key.sub);
                }
                if (target.Submeshes.Count > 0) config.Targets.Add(target);
            }
            config.SkippedMeshes.AddRange(skipped.Values.OrderBy(s => s.Model).ThenBy(s => s.Mesh));
            return config;
        }

        private static void Collect(GameObject root, HashSet<Material> targets,
            Dictionary<(string, string, int), HashSet<string>> usage, Dictionary<string, BoxUvTargets.Skipped> skipped,
            HashSet<string> onlyModels)
        {
            if (root == null) return;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Material[] materials = renderer.sharedMaterials;
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (onlyModels != null && !onlyModels.Contains(meshPath)) continue;
                bool hasTarget = materials.Any(m => m != null && targets.Contains(m));
                if (!(AssetImporter.GetAtPath(meshPath) is ModelImporter))
                {
                    if (hasTarget)
                        skipped[meshPath + "|" + mesh.name] = new BoxUvTargets.Skipped
                            { Model = meshPath, Mesh = mesh.name, Reason = "not an imported model (builtin or mesh asset)" };
                    continue;
                }
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    var key = (meshPath, mesh.name, sub);
                    if (!usage.TryGetValue(key, out HashSet<string> names))
                    {
                        if (!hasTarget && onlyModels == null) continue;
                        usage[key] = names = new HashSet<string>();
                    }
                    names.Add(materials[sub] != null ? materials[sub].name : "<none>");
                }
            }
        }

        private static void ScanScenes(IEnumerable<string> scenes, Action<GameObject> visit)
        {
            foreach (string path in scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened = true;
                }
                foreach (GameObject root in scene.GetRootGameObjects()) visit(root);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static List<string> AssetFiles(string extension) =>
            Directory.EnumerateFiles("Assets", "*" + extension, SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).ToList();

        private static IEnumerable<string> FilesContaining(IEnumerable<string> files, string[] guids)
        {
            if (guids.Length == 0) yield break;
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                if (guids.Any(g => text.Contains(g))) yield return file;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Fixers
{
    /// <summary>
    /// Фиксеры объектов сцены: цель — строка <see cref="GlobalObjectId"/> из находки (имена светильников повторяются).
    /// Сцена должна быть открыта; правка идёт через Undo с записью переопределений префаба, затем сцена сохраняется.
    /// </summary>
    internal static class SceneObjectFix
    {
        public static IEnumerable<string> Targets(IEnumerable<AuditFinding> findings) =>
            findings.SelectMany(f => f.FixTargets).Where(t => t.StartsWith("GlobalObjectId_", StringComparison.Ordinal)).Distinct();

        public static T Resolve<T>(string target) where T : UnityEngine.Object =>
            GlobalObjectId.TryParse(target, out GlobalObjectId id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T : null;

        public static string Describe(Component component) =>
            component.gameObject.scene.path + ":" + AuditAssetUtil.HierarchyPath(component.transform);

        public static void Apply<T>(IReadOnlyList<AuditFixAction> actions, string undoName, Func<T, Component> owner, Action<T> change)
            where T : UnityEngine.Object
        {
            var scenes = new HashSet<Scene>();
            foreach (AuditFixAction action in actions)
            {
                T target = Resolve<T>(action.AssetPath);
                if (target == null)
                {
                    action.MarkFailed("объект не найден — откройте сцену");
                    continue;
                }
                Undo.RecordObject(target, undoName);
                change(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                scenes.Add(owner(target).gameObject.scene);
                action.MarkApplied();
            }
            foreach (Scene scene in scenes)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
    }

    /// <summary>Фиксер <c>lighting.realtime-light</c>: только режим источника → Baked; свет нужно перезапечь.</summary>
    public sealed class RealtimeLightBakeFixer : AuditFixer
    {
        public override string RuleId => "lighting.realtime-light";
        public override string Title => "Источники → Baked";
        public override string WhatChanges => "Режим источника света (Realtime/Mixed → Baked). Яркость, цвет и дальность не меняются; после — перезапечь свет.";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            foreach (string target in SceneObjectFix.Targets(findings))
            {
                var light = SceneObjectFix.Resolve<Light>(target);
                if (light != null && light.lightmapBakeType != LightmapBakeType.Baked)
                    actions.Add(new AuditFixAction(target, SceneObjectFix.Describe(light) + ": " + light.lightmapBakeType, "Baked"));
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            SceneObjectFix.Apply<Light>(actions, "Источник → Baked", l => l, l => l.lightmapBakeType = LightmapBakeType.Baked);
            GameLog.Perf.Info("[RealtimeLightBakeFixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count + "; перезапеките свет.");
        }
    }

    /// <summary>Фиксер <c>lighting.dynamic-in-gi</c>: снимает только флаг Contribute GI, остальные Static-флаги остаются.</summary>
    public sealed class DynamicObjectGiFixer : AuditFixer
    {
        public override string RuleId => "lighting.dynamic-in-gi";
        public override string Title => "Снять Contribute GI у подвижных";
        public override string WhatChanges => "Static-флаг Contribute GI объекта (остальные флаги сохраняются); свет объект берёт из проб.";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            foreach (string target in SceneObjectFix.Targets(findings))
            {
                var gameObject = SceneObjectFix.Resolve<GameObject>(target);
                if (gameObject == null)
                    continue;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) != 0)
                    actions.Add(new AuditFixAction(target, SceneObjectFix.Describe(gameObject.transform) + ": " + flags,
                        (flags & ~StaticEditorFlags.ContributeGI).ToString()));
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            SceneObjectFix.Apply<GameObject>(actions, "Снять Contribute GI", g => g.transform, g =>
                GameObjectUtility.SetStaticEditorFlags(g, GameObjectUtility.GetStaticEditorFlags(g) & ~StaticEditorFlags.ContributeGI));
            GameLog.Perf.Info("[DynamicObjectGiFixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count + "; перезапеките свет.");
        }
    }

    /// <summary>
    /// Фиксер <c>mesh.lightmapped-without-uv2</c>. Меш модели (FBX и т.п.) общий для всех карт, поэтому импорт не
    /// меняется (QPM-LIGHT-003): создаётся своя копия меша с UV2 в <c>&lt;папка сцены&gt;/&lt;сцена&gt;/LightmapMeshes/</c>,
    /// и на неё переключаются только MeshFilter этой сцены (коллайдеры остаются на исходном меше). Сгенерированный
    /// <c>.asset</c>-меш проекта развёртывается на месте. Меши пакетов и встроенные не трогаются.
    /// </summary>
    public sealed class LightmapUv2Fixer : AuditFixer
    {
        public const string MeshFolderName = "LightmapMeshes";

        public override string RuleId => "mesh.lightmapped-without-uv2";
        public override string Title => "UV2 для лайтмап";
        public override string WhatChanges =>
            "Меш модели: копия с UV2 в папке сцены, MeshFilter объектов сцены — на копию (импорт модели не меняется). " +
            "Меш .asset: вторая развёртка UV на месте. После — перезапечь свет.";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            foreach (string target in SceneObjectFix.Targets(findings))
            {
                var filter = SceneObjectFix.Resolve<GameObject>(target)?.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    continue;
                string after = IsOwnMeshAsset(path) ? "развёртка UV2 в " + path : CopyPath(filter.gameObject.scene, mesh);
                actions.Add(new AuditFixAction(target, SceneObjectFix.Describe(filter) + ": " + path + "#" + mesh.name, after));
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            var copies = new Dictionary<Mesh, Mesh>();
            var scenes = new HashSet<Scene>();
            foreach (AuditFixAction action in actions)
            {
                var filter = SceneObjectFix.Resolve<GameObject>(action.AssetPath)?.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    action.MarkFailed("нет MeshFilter с мешем — откройте сцену");
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(mesh);
                if (IsOwnMeshAsset(path))
                {
                    if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    {
                        Unwrapping.GenerateSecondaryUVSet(mesh);
                        EditorUtility.SetDirty(mesh);
                        AssetDatabase.SaveAssetIfDirty(mesh);
                    }
                    if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) action.MarkApplied();
                    else action.MarkFailed("UV2 не появилась после развёртки");
                    continue;
                }
                if (!copies.TryGetValue(mesh, out Mesh copy))
                    copies[mesh] = copy = CopyWithUv2(filter.gameObject.scene, mesh);
                Undo.RecordObject(filter, "Меш с UV2 для лайтмап");
                filter.sharedMesh = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                scenes.Add(filter.gameObject.scene);
                action.MarkApplied();
            }
            foreach (Scene scene in scenes)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            GameLog.Perf.Info("[LightmapUv2Fixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count +
                              ", копий мешей " + copies.Count + "; перезапеките свет.");
        }

        private static bool IsOwnMeshAsset(string path) =>
            path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && !(AssetImporter.GetAtPath(path) is ModelImporter);

        public static string CopyPath(Scene scene, Mesh mesh)
        {
            string folder = System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/') + "/" + scene.name + "/" + MeshFolderName;
            string model = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(mesh));
            return folder + "/" + model + "_" + mesh.name + "_UV2.asset";
        }

        private static Mesh CopyWithUv2(Scene scene, Mesh source)
        {
            string path = CopyPath(scene, source);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && existing.HasVertexAttribute(VertexAttribute.TexCoord1))
                return existing;
            string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(parent).Replace('\\', '/'), System.IO.Path.GetFileName(parent));
                AssetDatabase.CreateFolder(parent, MeshFolderName);
            }
            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + "_UV2";
            Unwrapping.GenerateSecondaryUVSet(copy);
            if (existing != null)
            {
                EditorUtility.CopySerialized(copy, existing);
                UnityEngine.Object.DestroyImmediate(copy);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
    }
}

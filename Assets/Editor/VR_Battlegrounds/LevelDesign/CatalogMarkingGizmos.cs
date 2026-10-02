using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Ссылки кэшируются; метки рисуются общим механизмом карточек без наложения текста.</summary>
    [InitializeOnLoad]
    public static class CatalogMarkingGizmos
    {
        private static readonly Dictionary<string, GameObject[]> Objects = new Dictionary<string, GameObject[]>();
        static CatalogMarkingGizmos()
        {
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            EditorSceneManager.sceneOpened += (scene, mode) => Invalidate();
            EditorSceneManager.sceneClosed += scene => Invalidate();
        }

        public static void Invalidate() { Objects.Clear(); SceneView.RepaintAll(); }

        public static IEnumerable<AssetCandidateReviewLabelDrawer.LabelInfo> Labels()
        {
            if (!EditorPrefs.GetBool(CatalogMarkingService.GizmoPreference, true)) yield break;
            var ledger = CatalogMarkingService.Load();
            if (ledger == null) yield break;
            foreach (var entry in ledger.entries)
            {
                GameObject[] roots;
                if (!Objects.TryGetValue(entry.id, out roots)) { roots = CatalogMarkingService.Resolve(entry); Objects[entry.id] = roots; }
                if (roots.Length == 0 || roots.Any(g => g == null) || !roots.Any(g => g.activeInHierarchy)) continue;
                var renderers = roots.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var path = AssetDatabase.GUIDToAssetPath(entry.blockGuid);
                var block = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string blockName = block != null ? block.name + ", " + CatalogMarkingService.Cover(block).ToString().ToLowerInvariant() : "Эталон удалён";
                yield return new AssetCandidateReviewLabelDrawer.LabelInfo {
                    Caption = "✓ " + entry.title + "\n" + blockName + " / " + entry.theme,
                    Color = new Color(.6f, 1, .65f), Position = bounds.center + Vector3.up * bounds.extents.y
                };
            }
        }
    }
}

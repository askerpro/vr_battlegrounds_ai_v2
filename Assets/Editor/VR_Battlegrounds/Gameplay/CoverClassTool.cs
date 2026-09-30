using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Расставляет <see cref="CoverSurface"/> по правилу <see cref="CoverClassRules"/> (суффикс имени
    /// <c>_Hard</c>/<c>_Soft</c>/<c>_Visual</c>) во всех префабах <c>Assets/Prefabs</c> и сценах <c>Assets/Scenes</c>:
    /// ставит недостающие, правит класс, снимает лишние. <see cref="CoverSurface.PenetrationModifier"/> не трогает —
    /// это ручная настройка материала. Идемпотентен. Проверка — <c>CoverClassTests</c>.
    /// </summary>
    public static class CoverClassTool
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] SceneRoots  = { "Assets/Scenes" };

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Apply Cover Classes")]
        private static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var changed   = new List<string>();
            var conflicts = new List<string>();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                GameLog.Error("Apply Cover Classes: в Play Mode разметка не выполняется.");
                return;
            }

            ApplyToPrefabs(changed, conflicts);
            ApplyToScenes(changed, conflicts);
            AssetDatabase.SaveAssets();

            GameLog.WeaponSystem.Info(
                $"Apply Cover Classes: изменено ассетов {changed.Count}\n  {(changed.Count == 0 ? "—" : string.Join("\n  ", changed))}\n" +
                $"Проблемы: {conflicts.Count}\n  {(conflicts.Count == 0 ? "—" : string.Join("\n  ", conflicts))}");
        }

        private static void ApplyToPrefabs(List<string> changed, List<string> conflicts)
        {
            // Базы и вложенные — раньше содержащих: внешний префаб наследует разметку, а не пишет override.
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Prefab", PrefabRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => AssetDatabase.GetDependencies(path, true).Count(d => d.EndsWith(".prefab")))
                .ThenBy(path => path);

            foreach (string path in paths)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = ApplyToHierarchy(contents, path, conflicts);
                    if (count == 0) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    if (saved) changed.Add($"{path} ({count})");
                    else conflicts.Add($"{path}: не сохранён (Missing Script?)");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        private static void ApplyToScenes(List<string> changed, List<string> conflicts)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", SceneRoots))
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                Scene  scene  = SceneManager.GetSceneByPath(path);
                bool   opened = !scene.isLoaded;

                if (!opened && scene.isDirty)
                {
                    conflicts.Add($"{path}: несохранённые правки — пропущена");
                    continue;
                }

                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    int count = scene.GetRootGameObjects().Sum(root => ApplyToHierarchy(root, path, conflicts));
                    if (count == 0) continue;

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed.Add($"{path} ({count})");
                }
                finally
                {
                    if (opened) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static int ApplyToHierarchy(GameObject root, string assetPath, List<string> conflicts)
        {
            int count = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go      = t.gameObject;
                var        surface = go.GetComponent<CoverSurface>();

                if (!CoverClassRules.TryExpectedClass(go, out CoverClass expected))
                {
                    if (surface == null) continue;
                    try
                    {
                        Object.DestroyImmediate(surface, true);
                        count++;
                    }
                    catch (System.InvalidOperationException e)
                    {
                        conflicts.Add($"{assetPath} → {go.name}: лишний CoverSurface не снят ({e.Message}) — снять в базовом префабе");
                    }
                    continue;
                }

                if (surface == null) surface = go.AddComponent<CoverSurface>();
                else if (surface.Class == expected) continue;

                surface.Class = expected;
                if (PrefabUtility.IsPartOfPrefabInstance(surface))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
                EditorUtility.SetDirty(surface);
                count++;
            }

            return count;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Расставляет теги <see cref="GameTags" /> по правилу <see cref="GameTagRules" />
    /// во всех префабах <c>Assets/Prefabs</c> и сценах <c>Assets/Scenes</c>.
    ///
    /// <para>
    /// Инструмент идемпотентен: повторный запуск ничего не меняет. Чужие теги
    /// (<c>MainCamera</c>, <c>EditorOnly</c>) не трогает никогда — если правило требует
    /// игровой тег на объекте с чужим, это попадает в отчёт как конфликт.
    /// </para>
    /// </summary>
    public static class GameTagsTool
    {
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] SceneRoots  = { "Assets/Scenes" };

        public sealed class Result
        {
            public readonly List<string> AddedTags      = new List<string>();
            public readonly List<string> ChangedAssets  = new List<string>();
            public readonly List<string> SkippedScenes  = new List<string>();
            public readonly List<string> Conflicts      = new List<string>();
            public int ChangedObjects;

            public override string ToString() =>
                $"Теги заведены: {Join(AddedTags)}\n" +
                $"Изменено объектов: {ChangedObjects} в {ChangedAssets.Count} ассетах\n{Join(ChangedAssets, "\n  ")}\n" +
                $"Пропущены сцены (несохранённые правки): {Join(SkippedScenes)}\n" +
                $"Конфликты с чужими тегами: {Conflicts.Count}\n{Join(Conflicts, "\n  ")}";

            private static string Join(List<string> items, string sep = ", ") =>
                items.Count == 0 ? "—" : (sep.StartsWith("\n") ? "  " : "") + string.Join(sep, items);
        }

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Apply Game Tags")]
        private static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[GameTagsTool]\n" + Run());
        }

        public static Result Run()
        {
            var result = new Result();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.Conflicts.Add("Play Mode — разметка не выполнялась");
                return result;
            }

            EnsureTags(result);
            ApplyToPrefabs(result);
            ApplyToScenes(result);
            AssetDatabase.SaveAssets();
            return result;
        }

        // ── TagManager ──────────────────────────────────────────────────────

        private static void EnsureTags(Result result)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(TagManagerPath)[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");

            var existing = new HashSet<string>(UnityEditorInternal.InternalEditorUtility.tags);
            foreach (string tag in GameTags.Custom)
            {
                if (existing.Contains(tag)) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
                result.AddedTags.Add(tag);
            }

            if (result.AddedTags.Count > 0) tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Префабы ─────────────────────────────────────────────────────────

        private static void ApplyToPrefabs(Result result)
        {
            // Вложенные префабы и базы вариантов — раньше тех, кто их содержит: тогда внешний
            // префаб наследует уже верный тег, а не пишет себе override с тем же значением.
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Prefab", PrefabRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => AssetDatabase.GetDependencies(path, true).Count(d => d.EndsWith(".prefab")))
                .ThenBy(path => path);

            foreach (string path in paths)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changed = ApplyToHierarchy(contents, path, result);
                    if (changed == 0) continue;

                    // Префаб с Missing Script Unity сохранить откажется — это не правка, а отказ.
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    if (!saved)
                    {
                        result.Conflicts.Add($"{path}: не сохранён (Missing Script?) — {changed} тегов не записано");
                        continue;
                    }

                    result.ChangedObjects += changed;
                    result.ChangedAssets.Add($"{path} ({changed})");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        // ── Сцены ───────────────────────────────────────────────────────────

        private static void ApplyToScenes(Result result)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", SceneRoots))
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                Scene  scene  = SceneManager.GetSceneByPath(path);
                bool   opened = !scene.isLoaded;

                // Открытую сцену с несохранёнными правками не сохраняем за пользователя.
                if (!opened && scene.isDirty)
                {
                    result.SkippedScenes.Add(path);
                    continue;
                }

                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                try
                {
                    int changed = 0;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        changed += ApplyToHierarchy(root, path, result);

                    if (changed == 0) continue;

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    result.ChangedObjects += changed;
                    result.ChangedAssets.Add($"{path} ({changed})");
                }
                finally
                {
                    if (opened) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // ── Общее ───────────────────────────────────────────────────────────

        public static int ApplyToHierarchy(GameObject root, string assetPath, Result result)
        {
            int changed = 0;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go       = t.gameObject;
                string     expected = GameTagRules.ExpectedTag(go);
                string     actual   = go.tag;

                if (actual == expected) continue;
                if (expected == GameTagRules.Untagged && !GameTags.IsGameTag(actual)) continue;

                if (actual != GameTagRules.Untagged && !GameTags.IsGameTag(actual))
                {
                    result.Conflicts.Add($"{assetPath} → {go.name}: стоит '{actual}', правило хочет '{expected}'");
                    continue;
                }

                go.tag = expected;
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                changed++;
            }

            return changed;
        }
    }
}

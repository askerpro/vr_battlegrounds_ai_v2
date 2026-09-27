using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Теги из <see cref="GameTags" /> совпадают с тем, что по компонентам требует
    /// <see cref="GameTagRules" />, во всех префабах и сценах проекта.
    ///
    /// <para>
    /// Тег — копия факта, который уже записан компонентом, а копии расходятся: новое оружие
    /// без тега <c>Weapon</c> молча выпадет из <c>FindGameObjectsWithTag</c>. Тест держит
    /// копию в согласии с оригиналом. Починка — <c>Tools/VR Battlegrounds/Gameplay/Apply Game Tags</c>.
    /// </para>
    /// </summary>
    public class GameTagsTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] SceneRoots  = { "Assets/Scenes" };

        [Test]
        public void GameTags_ExistInTagManager()
        {
            string[] defined = InternalEditorUtility.tags;
            string[] missing = GameTags.All.Where(t => !defined.Contains(t)).ToArray();

            Assert.IsEmpty(missing, "Теги не заведены в TagManager: " + string.Join(", ", missing));
        }

        [Test]
        public void Prefabs_TagsMatchRules()
        {
            var mismatches = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Collect(prefab, path, mismatches);
            }

            Assert.IsEmpty(mismatches, Report(mismatches));
        }

        [Test]
        public void Scenes_TagsMatchRules()
        {
            var mismatches = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", SceneRoots))
            {
                string path  = AssetDatabase.GUIDToAssetPath(guid);
                Scene  scene = EditorSceneManager.OpenPreviewScene(path);

                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        Collect(root, path, mismatches);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(mismatches, Report(mismatches));
        }

        // ── Само правило на синтетических объектах ──────────────────────────

        [Test]
        public void Rule_SolidColliderOutsideEntities_IsEnvironment()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try   { Assert.AreEqual(GameTags.Environment, GameTagRules.ExpectedTag(wall)); }
            finally { Object.DestroyImmediate(wall); }
        }

        [Test]
        public void Rule_TriggerCollider_IsNotEnvironment()
        {
            var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.GetComponent<Collider>().isTrigger = true;
            try   { Assert.AreEqual(GameTagRules.Untagged, GameTagRules.ExpectedTag(volume)); }
            finally { Object.DestroyImmediate(volume); }
        }

        [Test]
        public void Rule_ColliderUnderRigidbody_IsNotEnvironment()
        {
            var item = new GameObject("Item", typeof(Rigidbody));
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.SetParent(item.transform);
            try   { Assert.AreEqual(GameTagRules.Untagged, GameTagRules.ExpectedTag(part)); }
            finally { Object.DestroyImmediate(item); }
        }

        [Test]
        public void Rule_SpawnZoneRoot_IsSpawnZone()
        {
            var zone = new GameObject("Zone", typeof(BoxCollider), typeof(TeamSpawnZone));
            try   { Assert.AreEqual(GameTags.SpawnZone, GameTagRules.ExpectedTag(zone)); }
            finally { Object.DestroyImmediate(zone); }
        }

        // ── Вспомогательное ─────────────────────────────────────────────────

        private static void Collect(GameObject root, string assetPath, List<string> mismatches)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go       = t.gameObject;
                string     expected = GameTagRules.ExpectedTag(go);
                string     actual   = go.tag;

                bool wrong = expected == GameTagRules.Untagged
                    ? GameTags.IsGameTag(actual)
                    : actual != expected;

                if (wrong)
                    mismatches.Add($"{assetPath} → {PathOf(t)}: стоит '{actual}', положен '{expected}'");
            }
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Report(List<string> mismatches)
        {
            const int shown = 40;
            string head = $"Расхождений: {mismatches.Count}. Починка — Tools/VR Battlegrounds/Gameplay/Apply Game Tags.\n";
            string more = mismatches.Count > shown ? $"\n… и ещё {mismatches.Count - shown}" : "";
            return head + string.Join("\n", mismatches.Take(shown)) + more;
        }
    }
}

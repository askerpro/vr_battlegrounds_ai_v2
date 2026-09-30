using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Разметка укрытий (LD-31): <see cref="CoverSurface"/> стоит ровно там, где суффикс имени требует класс
    /// (<see cref="CoverClassRules"/>), с тем же классом — во всех префабах и сценах. Починка —
    /// <c>Tools/VR Battlegrounds/Gameplay/Apply Cover Classes</c>.
    ///
    /// <para>
    /// Плюс смысловая проверка Soft: под разметкой есть сплошной коллайдер, и он не толще
    /// <see cref="WallPenetration.MaxThickness"/> (предел поиска выхода, 90 юнитов CS) по самой тонкой оси — иначе
    /// «Soft» не пробивается никогда и обманывает игрока (LD-28). Реалистичная толщина — LD-30.
    /// </para>
    /// </summary>
    public class CoverClassTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };
        private static readonly string[] SceneRoots  = { "Assets/Scenes" };

        [TestCase("LD_Wall_Mid_Soft", CoverClass.Soft)]
        [TestCase("LD_Wall_Mid_Soft (2)", CoverClass.Soft)]
        [TestCase("Fence_Soft_Long", CoverClass.Soft)]
        [TestCase("LD_Block_Low_Hard", CoverClass.Hard)]
        [TestCase("Bush_Visual", CoverClass.Visual)]
        public void Правило_суффикс_задаёт_класс(string name, CoverClass expected)
        {
            Assert.IsTrue(CoverClassRules.TryParse(name, out CoverClass actual), name);
            Assert.AreEqual(expected, actual, name);
        }

        [TestCase("LD_Wall_Mid")]
        [TestCase("Visual")]
        [TestCase("SoftBox")]
        [TestCase("LD_Softwood")]
        [TestCase("")]
        public void Правило_без_суффикса_класса_нет(string name)
        {
            Assert.IsFalse(CoverClassRules.TryParse(name, out _), $"'{name}' не должно размечаться.");
        }

        [Test]
        public void Префабы_разметка_совпадает_с_именами()
        {
            var problems = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Collect(AssetDatabase.LoadAssetAtPath<GameObject>(path), path, problems);
            }

            Assert.IsEmpty(problems, Report(problems));
        }

        [Test]
        public void Сцены_разметка_совпадает_с_именами()
        {
            var problems = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", SceneRoots))
            {
                string path  = AssetDatabase.GUIDToAssetPath(guid);
                Scene  scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects()) Collect(root, path, problems);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(problems, Report(problems));
        }

        [Test]
        public void Проверка_ловит_Soft_без_компонента_и_толстый_Soft()
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Plank_Soft";
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab_Soft";
            slab.transform.localScale = Vector3.one * (WallPenetration.MaxThickness + 0.2f);
            slab.AddComponent<CoverSurface>().Class = CoverClass.Soft;
            var stray = new GameObject("Crate");
            stray.AddComponent<CoverSurface>();

            try
            {
                var problems = new List<string>();
                Collect(plank, "test", problems);
                Collect(slab, "test", problems);
                Collect(stray, "test", problems);

                Assert.That(problems.Any(p => p.Contains("Plank_Soft") && p.Contains("нет CoverSurface")), Report(problems));
                Assert.That(problems.Any(p => p.Contains("Slab_Soft") && p.Contains("толще")), Report(problems));
                Assert.That(problems.Any(p => p.Contains("Crate") && p.Contains("без суффикса")), Report(problems));
            }
            finally
            {
                Object.DestroyImmediate(plank);
                Object.DestroyImmediate(slab);
                Object.DestroyImmediate(stray);
            }
        }

        // ── Вспомогательное ─────────────────────────────────────────────────

        private static void Collect(GameObject root, string assetPath, List<string> problems)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go      = t.gameObject;
                var        surface = go.GetComponent<CoverSurface>();
                string     where   = $"{assetPath} → {PathOf(t)}";

                if (!CoverClassRules.TryExpectedClass(go, out CoverClass expected))
                {
                    if (surface != null) problems.Add($"{where}: CoverSurface на объекте без суффикса _Hard/_Soft/_Visual");
                    continue;
                }

                if (surface == null)
                {
                    problems.Add($"{where}: в имени {expected}, а нет CoverSurface");
                    continue;
                }

                if (surface.Class != expected)
                    problems.Add($"{where}: в имени {expected}, в CoverSurface {surface.Class}");

                if (expected == CoverClass.Soft) CheckSoftGeometry(go, where, problems);
            }
        }

        private static void CheckSoftGeometry(GameObject go, string where, List<string> problems)
        {
            Collider[] solid = go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
            if (solid.Length == 0)
            {
                problems.Add($"{where}: Soft без сплошного коллайдера — пуле нечего пробивать");
                return;
            }

            foreach (Collider collider in solid)
            {
                float thinnest = Thinnest(collider);
                if (thinnest > WallPenetration.MaxThickness)
                    problems.Add($"{where}/{collider.name}: Soft толще {WallPenetration.MaxThickness} м ({thinnest:F2} м) — не пробьётся никогда (LD-30)");
            }
        }

        /// <summary>Самая тонкая ось коллайдера в мире (по локальному размеру × масштаб — повороты не мешают).</summary>
        private static float Thinnest(Collider collider)
        {
            Vector3 size;
            switch (collider)
            {
                case BoxCollider box:     size = box.size; break;
                case MeshCollider mesh:   size = mesh.sharedMesh != null ? mesh.sharedMesh.bounds.size : Vector3.zero; break;
                case SphereCollider s:    size = Vector3.one * s.radius * 2f; break;
                case CapsuleCollider c:   size = Vector3.one * c.radius * 2f; break;
                default:                  return 0f;
            }

            Vector3 scale = collider.transform.lossyScale;
            return Mathf.Min(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y), Mathf.Abs(size.z * scale.z));
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Report(List<string> problems)
        {
            const int shown = 40;
            string head = $"Расхождений: {problems.Count}. Починка разметки — Tools/VR Battlegrounds/Gameplay/Apply Cover Classes.\n";
            string more = problems.Count > shown ? $"\n… и ещё {problems.Count - shown}" : "";
            return head + string.Join("\n", problems.Take(shown)) + more;
        }
    }
}
